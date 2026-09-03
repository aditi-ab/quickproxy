using System.Collections.Concurrent;
using System.Formats.Asn1;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using QuickProxy.Proxy.Models;
using QuickProxy.Proxy.Storage;

namespace QuickProxy.Proxy.Runtime;

public sealed class TlsCertificateSelector(
    IHostEnvironment environment,
    IProxyHostRuntime runtime,
    IDomainTranslationRuntime domainTranslationRuntime,
    ICertificateStore certificateStore,
    IIssuedCertificateService issuedCertificateService,
    DevelopmentCertificateAccessor developmentCertificateAccessor,
    ILogger<TlsCertificateSelector> logger) : ICertificateRuntimeCache
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

    public void Invalidate(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return;

        _certificateCache.TryRemove(id, out _);
    }

    public void InvalidateAll()
    {
        _certificateCache.Clear();
    }

    public X509Certificate2? Select(string? serverName)
    {
        if (string.IsNullOrWhiteSpace(serverName)) return null;

        var host = runtime.MatchHost(serverName);
        if (host is null) return SelectForDomainTranslation(serverName);

        try
        {
            if (!string.IsNullOrWhiteSpace(host.CertificateId))
            {
                var certificate = LoadFromStoredConfig(host, host.CertificateId);
                if (IsUsableForTls(certificate, host.Id)) return certificate;
            }

            if (host.Tls.Mode != TlsBindingMode.None)
            {
                var fallback = host.Tls.Mode switch
                {
                    TlsBindingMode.Pfx => LoadPfx(host),
                    TlsBindingMode.Thumbprint => LoadByThumbprint(host),
                    _ => null
                };

                if (IsUsableForTls(fallback, host.Id)) return fallback;
            }

            var developmentCertificate = LoadAspNetDevelopmentCertificate(serverName, host.Id);
            if (IsUsableForTls(developmentCertificate, host.Id)) return developmentCertificate;

            var fileFallbackCertificate = LoadDevCertificateFallback(host.Id);
            if (IsUsableForTls(fileFallbackCertificate, host.Id)) return fileFallbackCertificate;

            return null;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to resolve certificate for host '{HostId}'.", host.Id);
            return null;
        }
    }

    private X509Certificate2? SelectForDomainTranslation(string serverName)
    {
        var rule = domainTranslationRuntime.MatchRule(serverName);
        if (rule is null) return null;

        try
        {
            if (!string.IsNullOrWhiteSpace(rule.CertificateId))
            {
                var certificate = LoadFromStoredConfig(
                    rule.Id,
                    rule.CertificateId,
                    () => issuedCertificateService.EnsureForDomainTranslation(rule));
                if (IsUsableForTls(certificate, rule.Id)) return certificate;
            }

            var developmentCertificate = LoadAspNetDevelopmentCertificate(serverName, rule.Id);
            if (IsUsableForTls(developmentCertificate, rule.Id)) return developmentCertificate;

            var fileFallbackCertificate = LoadDevCertificateFallback(rule.Id);
            if (IsUsableForTls(fileFallbackCertificate, rule.Id)) return fileFallbackCertificate;

            return null;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to resolve certificate for domain translation '{RuleId}'.", rule.Id);
            return null;
        }
    }

    private X509Certificate2? LoadFromStoredConfig(ProxyHostConfig host, string? certificateId)
    {
        return LoadFromStoredConfig(
            host.Id,
            certificateId,
            () => issuedCertificateService.EnsureForHost(host, certificateId));
    }

    private X509Certificate2? LoadFromStoredConfig(
        string ownerId,
        string? certificateId,
        Func<string?> issueFromIssuer)
    {
        if (string.IsNullOrWhiteSpace(certificateId)) return null;

        var effectiveCertificateId = certificateId.Trim();
        var configuredCertificate = certificateStore.Get(effectiveCertificateId);
        if (configuredCertificate?.Mode == CertificateConfigMode.Issuer)
        {
            var issuedId = issueFromIssuer();
            if (string.IsNullOrWhiteSpace(issuedId))
            {
                logger.LogWarning(
                    "Issuer-backed certificate '{CertificateId}' could not issue a certificate for '{OwnerId}'.",
                    certificateId, ownerId);
                return null;
            }

            effectiveCertificateId = issuedId;
            configuredCertificate = certificateStore.Get(effectiveCertificateId);
        }

        var now = DateTimeOffset.UtcNow;
        if (_certificateCache.TryGetValue(effectiveCertificateId, out var cached) &&
            now - cached.UpdatedAtUtc <= _cacheTtl)
            return cached.Certificate;

        var config = configuredCertificate ?? certificateStore.Get(effectiveCertificateId);
        if (config is null)
        {
            logger.LogWarning("Certificate config '{CertificateId}' was not found for '{OwnerId}'.",
                effectiveCertificateId, ownerId);
            _certificateCache[effectiveCertificateId] = new CertificateCacheEntry(null, now);
            return null;
        }

        var certificate = config.Mode switch
        {
            CertificateConfigMode.Files => LoadFromPemFiles(config),
            CertificateConfigMode.Pfx => LoadFromStoredPfx(config),
            CertificateConfigMode.Thumbprint => LoadByThumbprint(config.Thumbprint, config.StoreName,
                config.StoreLocation, ownerId),
            CertificateConfigMode.Issuer => null,
            _ => null
        };

        _certificateCache[effectiveCertificateId] = new CertificateCacheEntry(certificate, now);
        return certificate;
    }

    private X509Certificate2? LoadFromPemFiles(StoredCertificateConfig config)
    {
        var certificateBytes = certificateStore.GetFile(config.Id, "certificate.pem");
        var keyBytes = certificateStore.GetFile(config.Id, "key.pem");
        if (certificateBytes is null || keyBytes is null || certificateBytes.Length == 0 || keyBytes.Length == 0)
        {
            logger.LogWarning("PEM files missing for certificate config '{CertificateId}'.", config.Id);
            return null;
        }

        try
        {
            // On Windows, returning a raw PEM-loaded cert can pass validation but still fail TLS handshake.
            // Rehydrate through PKCS#12 in-memory to ensure Kestrel receives a fully usable cert+private key.
            var certificatePem = Encoding.UTF8.GetString(certificateBytes);
            var keyPem = Encoding.UTF8.GetString(keyBytes);
            using var pemCertificate = X509Certificate2.CreateFromPem(certificatePem, keyPem);
            var pfxBytes = pemCertificate.Export(X509ContentType.Pkcs12);
            return X509CertificateLoader.LoadPkcs12(
                pfxBytes,
                string.Empty,
                X509KeyStorageFlags.DefaultKeySet | X509KeyStorageFlags.Exportable);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed loading PEM certificate for certificate config '{CertificateId}'.", config.Id);
            return null;
        }
    }

    private X509Certificate2? LoadFromStoredPfx(StoredCertificateConfig config)
    {
        var pfxBytes = certificateStore.GetFile(config.Id, "certificate.pfx");
        if (pfxBytes is null || pfxBytes.Length == 0)
        {
            logger.LogWarning("PFX file missing for certificate config '{CertificateId}'.", config.Id);
            return null;
        }

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
            logger.LogError(ex, "Failed loading PFX for certificate config '{CertificateId}'.", config.Id);
            return null;
        }
    }

    private X509Certificate2? LoadPfx(ProxyHostConfig host)
    {
        var pfxPath = host.Tls.PfxPath;
        if (string.IsNullOrWhiteSpace(pfxPath))
        {
            logger.LogWarning("Host '{HostId}' uses PFX mode but pfxPath is empty.", host.Id);
            return null;
        }

        var fullPath = Path.IsPathRooted(pfxPath) ? pfxPath : Path.Combine(environment.ContentRootPath, pfxPath);
        if (!File.Exists(fullPath))
        {
            logger.LogWarning("PFX file not found for host '{HostId}': {Path}", host.Id, fullPath);
            return null;
        }

        var password = host.Tls.PfxPassword;
        if (string.IsNullOrWhiteSpace(password) && !string.IsNullOrWhiteSpace(host.Tls.PfxPasswordEnvVar))
            password = Environment.GetEnvironmentVariable(host.Tls.PfxPasswordEnvVar);

        return X509CertificateLoader.LoadPkcs12FromFile(
            fullPath,
            password);
    }

    private X509Certificate2? LoadAspNetDevelopmentCertificate(string serverName, string hostId)
    {
        var cacheKey = $"aspnet-dev-cert:{serverName}";
        var now = DateTimeOffset.UtcNow;
        if (_certificateCache.TryGetValue(cacheKey, out var cached) &&
            now - cached.UpdatedAtUtc <= _cacheTtl)
            return cached.Certificate;

        var bestMatch = FindAspNetDevelopmentCertificate(serverName, hostId);
        _certificateCache[cacheKey] = new CertificateCacheEntry(bestMatch, now);
        return bestMatch;
    }

    private X509Certificate2? LoadDevCertificateFallback(string hostId)
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
            logger.LogDebug(
                "Using the database-backed development certificate for host '{HostId}'. Thumbprint='{Thumbprint}'.",
                hostId,
                certificate.Thumbprint);
            return certificate;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed loading the database-backed development certificate for host '{HostId}'.",
                hostId);
            _certificateCache[cacheKey] = new CertificateCacheEntry(null, now);
            return null;
        }
    }

    private X509Certificate2? LoadByThumbprint(ProxyHostConfig host)
    {
        return LoadByThumbprint(host.Tls.Thumbprint, host.Tls.StoreName, host.Tls.StoreLocation, host.Id);
    }

    private X509Certificate2? LoadByThumbprint(string? thumbprint, string storeNameValue, string storeLocationValue,
        string hostId)
    {
        if (string.IsNullOrWhiteSpace(thumbprint))
        {
            logger.LogWarning("Host '{HostId}' uses thumbprint mode but thumbprint is empty.", hostId);
            return null;
        }

        if (!Enum.TryParse<StoreName>(storeNameValue, true, out var storeName)) storeName = StoreName.My;

        if (!Enum.TryParse<StoreLocation>(storeLocationValue, true, out var storeLocation))
            storeLocation = StoreLocation.LocalMachine;

        using var store = new X509Store(storeName, storeLocation);
        store.Open(OpenFlags.ReadOnly);
        var normalized = thumbprint.Replace(" ", string.Empty, StringComparison.Ordinal);
        var certs = store.Certificates.Find(X509FindType.FindByThumbprint, normalized, false);
        if (certs.Count == 0)
        {
            logger.LogWarning("Certificate thumbprint not found for host '{HostId}'.", hostId);
            return null;
        }

        return certs[0];
    }

    private X509Certificate2? FindAspNetDevelopmentCertificate(string serverName, string hostId)
    {
        X509Certificate2? bestMatch = null;
        foreach (var (storeLocation, storeName) in DevelopmentCertificateStores)
        {
            using var store = new X509Store(storeName, storeLocation);
            store.Open(OpenFlags.ReadOnly);

            foreach (var certificate in store.Certificates)
            {
                if (!LooksLikeAspNetDevelopmentCertificate(certificate)) continue;

                if (!IsUsableForTls(certificate, hostId)) continue;

                if (!CertificateMatchesHostName(certificate, serverName)) continue;

                if (bestMatch is null || certificate.NotAfter.ToUniversalTime() > bestMatch.NotAfter.ToUniversalTime())
                    bestMatch = certificate;
            }
        }

        if (bestMatch is not null)
            logger.LogDebug(
                "Using built-in ASP.NET development certificate for host '{HostId}'. Subject='{Subject}', Thumbprint='{Thumbprint}'.",
                hostId,
                bestMatch.Subject,
                bestMatch.Thumbprint);

        return bestMatch;
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
        var normalizedHost = hostName.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(normalizedHost)) return false;

        foreach (var pattern in GetCertificateDnsNames(certificate))
            if (DnsPatternMatches(pattern, normalizedHost))
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

    private bool IsUsableForTls(X509Certificate2? certificate, string hostId)
    {
        if (certificate is null) return false;

        if (!certificate.HasPrivateKey)
        {
            logger.LogError(
                "Certificate selected for host '{HostId}' has no private key. Subject='{Subject}', Thumbprint='{Thumbprint}'.",
                hostId, certificate.Subject, certificate.Thumbprint);
            return false;
        }

        if (certificate.NotAfter.ToUniversalTime() <= DateTime.UtcNow)
        {
            logger.LogError(
                "Certificate selected for host '{HostId}' is expired at '{NotAfterUtc}'. Subject='{Subject}', Thumbprint='{Thumbprint}'.",
                hostId, certificate.NotAfter.ToUniversalTime(), certificate.Subject, certificate.Thumbprint);
            return false;
        }

        return true;
    }

    private sealed record CertificateCacheEntry(X509Certificate2? Certificate, DateTimeOffset UpdatedAtUtc);
}