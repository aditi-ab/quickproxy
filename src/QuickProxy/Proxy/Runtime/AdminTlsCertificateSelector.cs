using System.Collections.Concurrent;
using System.Formats.Asn1;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using QuickProxy.Proxy.Models;
using QuickProxy.Proxy.Storage;

namespace QuickProxy.Proxy.Runtime;

public sealed class AdminTlsCertificateSelector(
    IIssuedCertificateService issuedCertificateService,
    ICertificateStore certificateStore,
    DevelopmentCertificateAccessor developmentCertificateAccessor,
    ILogger<AdminTlsCertificateSelector> logger)
{
    private const string SubjectAlternativeNameOid = "2.5.29.17";
    private const string AspNetDevCertificateFriendlyName = "ASP.NET Core HTTPS development certificate";

    private static readonly (StoreLocation Location, StoreName Name)[] DevelopmentCertificateStores =
    [
        (StoreLocation.CurrentUser, StoreName.My),
        (StoreLocation.LocalMachine, StoreName.My)
    ];

    private readonly TimeSpan _cacheTtl = TimeSpan.FromMinutes(5);

    private readonly ConcurrentDictionary<string, CertificateCacheEntry> _certificateCache =
        new(StringComparer.OrdinalIgnoreCase);

    public X509Certificate2 Select(string? serverName, X509Certificate2 fallbackCertificate)
    {
        if (string.IsNullOrWhiteSpace(serverName))
        {
            LogSelection("fallback-no-sni", serverName, fallbackCertificate);
            return fallbackCertificate;
        }

        var normalizedServerName = serverName.Trim().Trim('.').ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(normalizedServerName))
        {
            LogSelection("fallback-empty-sni", serverName, fallbackCertificate);
            return fallbackCertificate;
        }

        try
        {
            if (string.Equals(normalizedServerName, "localhost", StringComparison.OrdinalIgnoreCase))
            {
                var forcedLocalhostCertificate = LoadAspNetDevelopmentCertificate(normalizedServerName);
                if (IsUsableForTls(forcedLocalhostCertificate, normalizedServerName) &&
                    CertificateMatchesHostName(forcedLocalhostCertificate!, normalizedServerName))
                {
                    LogSelection("forced-localhost-aspnet-dev-cert", normalizedServerName, forcedLocalhostCertificate!);
                    return forcedLocalhostCertificate!;
                }

                logger.LogWarning(
                    "Forced localhost ASP.NET development certificate was not available or usable. ServerName='{ServerName}'. Falling back to normal admin certificate resolution.",
                    normalizedServerName);
            }

            var developmentCertificate = LoadAspNetDevelopmentCertificate(normalizedServerName);
            if (IsUsableForTls(developmentCertificate, normalizedServerName) &&
                CertificateMatchesHostName(developmentCertificate!, normalizedServerName))
            {
                LogSelection("aspnet-dev-cert", normalizedServerName, developmentCertificate!);
                return developmentCertificate!;
            }

            var fileFallbackCertificate = LoadDevCertificateFallback();
            if (IsUsableForTls(fileFallbackCertificate, normalizedServerName) &&
                CertificateMatchesHostName(fileFallbackCertificate!, normalizedServerName))
            {
                LogSelection("file-dev-cert", normalizedServerName, fileFallbackCertificate!);
                return fileFallbackCertificate!;
            }

            var issuedCertificate = LoadIssuedCertificate(normalizedServerName);
            if (IsUsableForTls(issuedCertificate, normalizedServerName) &&
                CertificateMatchesHostName(issuedCertificate!, normalizedServerName))
            {
                LogSelection("issued-cert", normalizedServerName, issuedCertificate!);
                return issuedCertificate!;
            }

            LogSelection("fallback-admin-cert", normalizedServerName, fallbackCertificate);
            return fallbackCertificate;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to resolve admin certificate for '{ServerName}'.", normalizedServerName);
            LogSelection("fallback-admin-cert-after-error", normalizedServerName, fallbackCertificate);
            return fallbackCertificate;
        }
    }

    private void LogSelection(string source, string? serverName, X509Certificate2 certificate)
    {
        logger.LogInformation(
            "Admin TLS selected certificate. Source='{Source}', ServerName='{ServerName}', Subject='{Subject}', Thumbprint='{Thumbprint}', NotAfterUtc='{NotAfterUtc}'.",
            source,
            serverName ?? string.Empty,
            certificate.Subject,
            certificate.Thumbprint,
            certificate.NotAfter.ToUniversalTime());
    }

    private X509Certificate2? LoadIssuedCertificate(string serverName)
    {
        var issuedId = issuedCertificateService.EnsureForHostName(serverName);
        if (string.IsNullOrWhiteSpace(issuedId)) return null;

        var cacheKey = $"admin-issued:{issuedId}";
        var now = DateTimeOffset.UtcNow;
        if (_certificateCache.TryGetValue(cacheKey, out var cached) &&
            now - cached.UpdatedAtUtc <= _cacheTtl)
            return cached.Certificate;

        var config = certificateStore.Get(issuedId);
        if (config is null)
        {
            _certificateCache[cacheKey] = new CertificateCacheEntry(null, now);
            return null;
        }

        var certificate = config.Mode switch
        {
            CertificateConfigMode.Files => LoadFromPemFiles(config),
            CertificateConfigMode.Pfx => LoadFromStoredPfx(config),
            CertificateConfigMode.Thumbprint => LoadByThumbprint(config.Thumbprint, config.StoreName,
                config.StoreLocation, serverName),
            _ => null
        };

        _certificateCache[cacheKey] = new CertificateCacheEntry(certificate, now);
        return certificate;
    }

    private X509Certificate2? LoadAspNetDevelopmentCertificate(string serverName)
    {
        var cacheKey = $"aspnet-dev-cert:{serverName}";
        var now = DateTimeOffset.UtcNow;
        if (_certificateCache.TryGetValue(cacheKey, out var cached) &&
            now - cached.UpdatedAtUtc <= _cacheTtl)
            return cached.Certificate;

        X509Certificate2? bestMatch = null;
        foreach (var (storeLocation, storeName) in DevelopmentCertificateStores)
        {
            using var store = new X509Store(storeName, storeLocation);
            store.Open(OpenFlags.ReadOnly);

            foreach (var certificate in store.Certificates)
            {
                if (!LooksLikeAspNetDevelopmentCertificate(certificate)) continue;

                if (!IsUsableForTls(certificate, serverName)) continue;

                if (!CertificateMatchesHostName(certificate, serverName)) continue;

                if (bestMatch is null || certificate.NotAfter.ToUniversalTime() > bestMatch.NotAfter.ToUniversalTime())
                    bestMatch = certificate;
            }
        }

        _certificateCache[cacheKey] = new CertificateCacheEntry(bestMatch, now);
        return bestMatch;
    }

    private X509Certificate2? LoadDevCertificateFallback()
    {
        const string cacheKey = "dev-cert-database";
        var now = DateTimeOffset.UtcNow;
        if (_certificateCache.TryGetValue(cacheKey, out var cached) &&
            now - cached.UpdatedAtUtc <= _cacheTtl)
            return cached.Certificate;

        try
        {
            var certificate = developmentCertificateAccessor.LoadCertificate();
            _certificateCache[cacheKey] = new CertificateCacheEntry(certificate, now);
            return certificate;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed loading the database-backed development certificate.");
            _certificateCache[cacheKey] = new CertificateCacheEntry(null, now);
            return null;
        }
    }

    private X509Certificate2? LoadFromPemFiles(StoredCertificateConfig config)
    {
        var certificateBytes = certificateStore.GetFile(config.Id, "certificate.pem");
        var keyBytes = certificateStore.GetFile(config.Id, "key.pem");
        if (certificateBytes is null || keyBytes is null || certificateBytes.Length == 0 ||
            keyBytes.Length == 0) return null;

        try
        {
            using var pemCertificate = X509Certificate2.CreateFromPem(
                Encoding.UTF8.GetString(certificateBytes),
                Encoding.UTF8.GetString(keyBytes));
            var pfxBytes = pemCertificate.Export(X509ContentType.Pkcs12);
            return X509CertificateLoader.LoadPkcs12(
                pfxBytes,
                string.Empty,
                X509KeyStorageFlags.DefaultKeySet | X509KeyStorageFlags.Exportable);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed loading admin PEM certificate for certificate config '{CertificateId}'.",
                config.Id);
            return null;
        }
    }

    private X509Certificate2? LoadFromStoredPfx(StoredCertificateConfig config)
    {
        var pfxBytes = certificateStore.GetFile(config.Id, "certificate.pfx");
        if (pfxBytes is null || pfxBytes.Length == 0) return null;

        var password = config.PfxPassword;
        if (string.IsNullOrWhiteSpace(password) && !string.IsNullOrWhiteSpace(config.PfxPasswordEnvVar))
            password = Environment.GetEnvironmentVariable(config.PfxPasswordEnvVar);

        try
        {
            return X509CertificateLoader.LoadPkcs12(
                pfxBytes,
                password);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed loading admin PFX certificate for certificate config '{CertificateId}'.",
                config.Id);
            return null;
        }
    }

    private X509Certificate2? LoadByThumbprint(string? thumbprint, string storeNameValue, string storeLocationValue,
        string serverName)
    {
        if (string.IsNullOrWhiteSpace(thumbprint)) return null;

        if (!Enum.TryParse<StoreName>(storeNameValue, true, out var storeName)) storeName = StoreName.My;

        if (!Enum.TryParse<StoreLocation>(storeLocationValue, true, out var storeLocation))
            storeLocation = StoreLocation.LocalMachine;

        using var store = new X509Store(storeName, storeLocation);
        store.Open(OpenFlags.ReadOnly);
        var normalized = thumbprint.Replace(" ", string.Empty, StringComparison.Ordinal);
        var certs = store.Certificates.Find(X509FindType.FindByThumbprint, normalized, false);
        if (certs.Count == 0)
        {
            logger.LogWarning("Admin certificate thumbprint not found for '{ServerName}'.", serverName);
            return null;
        }

        return certs[0];
    }

    private static bool LooksLikeAspNetDevelopmentCertificate(X509Certificate2 certificate)
    {
        if (!string.Equals(certificate.FriendlyName, AspNetDevCertificateFriendlyName,
                StringComparison.OrdinalIgnoreCase)) return false;

        return string.Equals(certificate.GetNameInfo(X509NameType.SimpleName, false), "localhost",
            StringComparison.OrdinalIgnoreCase);
    }

    private static bool CertificateMatchesHostName(X509Certificate2 certificate, string hostName)
    {
        foreach (var pattern in GetCertificateDnsNames(certificate))
            if (DnsPatternMatches(pattern, hostName))
                return true;

        return false;
    }

    private static IEnumerable<string> GetCertificateDnsNames(X509Certificate2 certificate)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var extension in certificate.Extensions)
        {
            if (!string.Equals(extension.Oid?.Value, SubjectAlternativeNameOid, StringComparison.Ordinal)) continue;

            var reader = new AsnReader(extension.RawData, AsnEncodingRules.DER);
            var sequence = reader.ReadSequence();
            while (sequence.HasData)
            {
                var tag = sequence.PeekTag();
                if (tag.HasSameClassAndValue(new Asn1Tag(TagClass.ContextSpecific, 2)))
                {
                    var dnsName = sequence.ReadCharacterString(UniversalTagNumber.IA5String,
                        new Asn1Tag(TagClass.ContextSpecific, 2));
                    if (!string.IsNullOrWhiteSpace(dnsName)) names.Add(dnsName.Trim().ToLowerInvariant());
                }
                else
                {
                    sequence.ReadEncodedValue();
                }
            }

            break;
        }

        if (names.Count == 0)
        {
            var subjectName = certificate.GetNameInfo(X509NameType.DnsName, false);
            if (!string.IsNullOrWhiteSpace(subjectName)) names.Add(subjectName.Trim().ToLowerInvariant());
        }

        return names;
    }

    private static bool DnsPatternMatches(string pattern, string hostName)
    {
        if (string.Equals(pattern, hostName, StringComparison.OrdinalIgnoreCase)) return true;

        if (!pattern.StartsWith("*.", StringComparison.Ordinal)) return false;

        var suffix = pattern[1..];
        if (!hostName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) return false;

        var prefix = hostName[..^suffix.Length];
        return !string.IsNullOrWhiteSpace(prefix) && !prefix.Contains('.');
    }

    private bool IsUsableForTls(X509Certificate2? certificate, string serverName)
    {
        if (certificate is null) return false;

        if (!certificate.HasPrivateKey)
        {
            logger.LogError(
                "Admin certificate selected for '{ServerName}' has no private key. Subject='{Subject}', Thumbprint='{Thumbprint}'.",
                serverName, certificate.Subject, certificate.Thumbprint);
            return false;
        }

        if (certificate.NotAfter.ToUniversalTime() <= DateTime.UtcNow)
        {
            logger.LogError(
                "Admin certificate selected for '{ServerName}' is expired at '{NotAfterUtc}'. Subject='{Subject}', Thumbprint='{Thumbprint}'.",
                serverName, certificate.NotAfter.ToUniversalTime(), certificate.Subject, certificate.Thumbprint);
            return false;
        }

        return true;
    }

    private sealed record CertificateCacheEntry(X509Certificate2? Certificate, DateTimeOffset UpdatedAtUtc);
}