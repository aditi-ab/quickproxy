using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.RegularExpressions;
using QuickProxy.Proxy.Models;
using QuickProxy.Proxy.Storage;

namespace QuickProxy.Proxy.Runtime;

public sealed partial class IssuedCertificateService(
    IHostEnvironment environment,
    ICertificateStore certificateStore,
    ILogger<IssuedCertificateService> logger) : IIssuedCertificateService
{
    private const string ServerAuthOid = "1.3.6.1.5.5.7.3.1";

    public string? EnsureForHost(ProxyHostConfig host)
    {
        return EnsureForHost(host, null);
    }

    public string? EnsureForHost(ProxyHostConfig host, string? issuerCertificateId)
    {
        if (host.DomainNames.Count == 0) return null;

        var domains = host.DomainNames
            .Select(x => x?.Trim().ToLowerInvariant() ?? string.Empty)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (domains.Length == 0) return null;

        var issuer = SelectIssuer(domains, issuerCertificateId);
        if (issuer is null) return null;

        var issuedId = BuildIssuedId(host.Id);
        if (certificateStore.Exists(issuedId)) return issuedId;

        var issuedConfig = new StoredCertificateConfig
        {
            Id = issuedId,
            Mode = CertificateConfigMode.Files
        };

        var files = CreateIssuedFiles(issuer, domains);
        if (files.Count == 0) return null;

        certificateStore.Upsert(issuedConfig);
        certificateStore.SaveFiles(issuedId, files);
        return issuedId;
    }

    public string? EnsureForHostName(string hostName)
    {
        var normalizedHostName = (hostName ?? string.Empty).Trim().Trim('.').ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(normalizedHostName)) return null;

        var issuer = SelectIssuer([normalizedHostName], null);
        if (issuer is null) return null;

        var issuedId = BuildIssuedHostNameId(normalizedHostName);
        if (certificateStore.Exists(issuedId)) return issuedId;

        var issuedConfig = new StoredCertificateConfig
        {
            Id = issuedId,
            Mode = CertificateConfigMode.Files
        };

        var files = CreateIssuedFiles(issuer, [normalizedHostName]);
        if (files.Count == 0) return null;

        certificateStore.Upsert(issuedConfig);
        certificateStore.SaveFiles(issuedId, files);
        return issuedId;
    }

    public string? EnsureForDomainTranslation(DomainTranslationRule rule)
    {
        var sourceDomain = (rule.SourceDomain ?? string.Empty).Trim().Trim('.').ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(sourceDomain)) return null;

        var domains = new[]
        {
            sourceDomain,
            $"*.{sourceDomain}"
        };

        var issuer = SelectIssuer(domains, rule.CertificateId);
        if (issuer is null) return null;

        var issuedId = BuildIssuedDomainTranslationId(rule.Id);
        if (certificateStore.Exists(issuedId)) return issuedId;

        var issuedConfig = new StoredCertificateConfig
        {
            Id = issuedId,
            Mode = CertificateConfigMode.Files
        };

        var files = CreateIssuedFiles(issuer, domains);
        if (files.Count == 0) return null;

        certificateStore.Upsert(issuedConfig);
        certificateStore.SaveFiles(issuedId, files);
        return issuedId;
    }

    public bool DeleteForHost(string hostId)
    {
        if (string.IsNullOrWhiteSpace(hostId)) return false;

        var issuedId = BuildIssuedId(hostId);
        var deleted = certificateStore.Delete(issuedId);
        if (!deleted) return false;

        logger.LogInformation(
            "Deleted stale issued certificate '{CertificateId}' for removed generated host '{HostId}'.", issuedId,
            hostId);
        return true;
    }

    public bool DeleteForDomainTranslation(string ruleId)
    {
        if (string.IsNullOrWhiteSpace(ruleId)) return false;

        var issuedId = BuildIssuedDomainTranslationId(ruleId);
        var deleted = certificateStore.Delete(issuedId);
        if (!deleted) return false;

        logger.LogInformation(
            "Deleted stale issued certificate '{CertificateId}' for removed domain translation '{RuleId}'.", issuedId,
            ruleId);
        return true;
    }

    [GeneratedRegex("[^a-z0-9-]+", RegexOptions.Compiled)]
    private static partial Regex NonKebabCharsRegex();

    private StoredCertificateConfig? SelectIssuer(IReadOnlyList<string> domains, string? preferredIssuerId)
    {
        if (!string.IsNullOrWhiteSpace(preferredIssuerId))
        {
            var preferred = certificateStore.Get(preferredIssuerId);
            if (preferred?.Mode == CertificateConfigMode.Issuer && preferred.IssuerEnabled) return preferred;
        }

        var issuers = certificateStore.List()
            .Where(x => x.Mode == CertificateConfigMode.Issuer && x.IssuerEnabled)
            .OrderBy(x => x.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        StoredCertificateConfig? best = null;
        var bestScore = -1;

        foreach (var issuer in issuers)
        {
            var rules = issuer.IssuerMatchDomains
                .Select(x => x?.Trim().ToLowerInvariant() ?? string.Empty)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase);

            foreach (var rule in rules)
            {
                if (!domains.Any(domain => DomainMatches(domain, rule))) continue;

                var score = rule.Length;
                if (score > bestScore)
                {
                    best = issuer;
                    bestScore = score;
                }
            }
        }

        return best;
    }

    private static bool DomainMatches(string domain, string rule)
    {
        return string.Equals(domain, rule, StringComparison.OrdinalIgnoreCase) ||
               domain.EndsWith($".{rule}", StringComparison.OrdinalIgnoreCase);
    }

    private Dictionary<string, byte[]> CreateIssuedFiles(StoredCertificateConfig issuer, IReadOnlyList<string> domains)
    {
        try
        {
            using var caCertificate = LoadIssuerCertificate(issuer);
            if (!caCertificate.HasPrivateKey)
            {
                logger.LogWarning("Issuer certificate '{IssuerId}' has no private key.", issuer.Id);
                return [];
            }

            using var privateKey = RSA.Create(2048);
            var request = new CertificateRequest(
                $"CN={domains[0]}",
                privateKey,
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1);

            var san = new SubjectAlternativeNameBuilder();
            foreach (var domain in domains) san.AddDnsName(domain);

            request.CertificateExtensions.Add(san.Build());
            request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
            request.CertificateExtensions.Add(new X509KeyUsageExtension(
                X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment,
                true));
            request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
                [new Oid(ServerAuthOid)],
                true));
            request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));

            var notBefore = DateTimeOffset.UtcNow.AddMinutes(-5);
            var maxNotAfter = DateTimeOffset.UtcNow.AddDays(90);
            var issuerNotAfter = new DateTimeOffset(caCertificate.NotAfter.ToUniversalTime());
            var notAfter = issuerNotAfter < maxNotAfter ? issuerNotAfter : maxNotAfter;
            if (notAfter <= notBefore)
            {
                logger.LogWarning("Issuer certificate '{IssuerId}' is expired or not valid for issuing.", issuer.Id);
                return [];
            }

            var serial = RandomNumberGenerator.GetBytes(16);
            using var generated = request.Create(caCertificate, notBefore, notAfter, serial);
            using var withKey = generated.CopyWithPrivateKey(privateKey);
            var certPem = withKey.ExportCertificatePem();
            var keyPem = privateKey.ExportPkcs8PrivateKeyPem();

            return new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase)
            {
                ["certificate.pem"] = Encoding.UTF8.GetBytes(certPem),
                ["key.pem"] = Encoding.UTF8.GetBytes(keyPem)
            };
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed issuing certificate from issuer '{IssuerId}'.", issuer.Id);
            return [];
        }
    }

    private X509Certificate2 LoadIssuerCertificate(StoredCertificateConfig issuer)
    {
        return issuer.IssuerCaSource switch
        {
            IssuerCaSourceMode.PathPem => LoadPathPemIssuer(issuer),
            IssuerCaSourceMode.PathPfx => LoadPathPfxIssuer(issuer),
            IssuerCaSourceMode.UploadPem => LoadUploadPemIssuer(issuer),
            IssuerCaSourceMode.UploadPfx => LoadUploadPfxIssuer(issuer),
            IssuerCaSourceMode.StoreThumbprint => LoadStoreThumbprintIssuer(issuer),
            _ => throw new InvalidOperationException($"Unsupported issuer CA source '{issuer.IssuerCaSource}'.")
        };
    }

    private X509Certificate2 LoadPathPemIssuer(StoredCertificateConfig issuer)
    {
        var certPath = ResolvePath(issuer.IssuerCaCertPath);
        var keyPath = ResolvePath(issuer.IssuerCaKeyPath);
        if (!File.Exists(certPath) || !File.Exists(keyPath))
            throw new FileNotFoundException($"Issuer CA PEM path not found: cert='{certPath}', key='{keyPath}'.");

        var certPem = File.ReadAllText(certPath);
        var keyPem = File.ReadAllText(keyPath);
        return X509Certificate2.CreateFromPem(certPem, keyPem);
    }

    private X509Certificate2 LoadPathPfxIssuer(StoredCertificateConfig issuer)
    {
        var pfxPath = ResolvePath(issuer.IssuerCaPfxPath);
        if (!File.Exists(pfxPath)) throw new FileNotFoundException($"Issuer CA PFX path not found: '{pfxPath}'.");

        var password = issuer.IssuerCaPfxPassword;
        if (string.IsNullOrWhiteSpace(password) && !string.IsNullOrWhiteSpace(issuer.IssuerCaPfxPasswordEnvVar))
            password = Environment.GetEnvironmentVariable(issuer.IssuerCaPfxPasswordEnvVar);

        return X509CertificateLoader.LoadPkcs12FromFile(
            pfxPath,
            password,
            X509KeyStorageFlags.DefaultKeySet | X509KeyStorageFlags.Exportable);
    }

    private X509Certificate2 LoadUploadPemIssuer(StoredCertificateConfig issuer)
    {
        var certBytes = certificateStore.GetFile(issuer.Id, "ca-certificate.pem");
        var keyBytes = certificateStore.GetFile(issuer.Id, "ca-key.pem");
        if (certBytes is null || keyBytes is null)
            throw new InvalidOperationException($"Issuer '{issuer.Id}' missing uploaded CA PEM files.");

        return X509Certificate2.CreateFromPem(
            Encoding.UTF8.GetString(certBytes),
            Encoding.UTF8.GetString(keyBytes));
    }

    private X509Certificate2 LoadUploadPfxIssuer(StoredCertificateConfig issuer)
    {
        var pfxBytes = certificateStore.GetFile(issuer.Id, "ca-certificate.pfx");
        if (pfxBytes is null || pfxBytes.Length == 0)
            throw new InvalidOperationException($"Issuer '{issuer.Id}' missing uploaded CA PFX.");

        var password = issuer.IssuerCaPfxPassword;
        if (string.IsNullOrWhiteSpace(password) && !string.IsNullOrWhiteSpace(issuer.IssuerCaPfxPasswordEnvVar))
            password = Environment.GetEnvironmentVariable(issuer.IssuerCaPfxPasswordEnvVar);

        return X509CertificateLoader.LoadPkcs12(
            pfxBytes,
            password,
            X509KeyStorageFlags.DefaultKeySet | X509KeyStorageFlags.Exportable);
    }

    private static X509Certificate2 LoadStoreThumbprintIssuer(StoredCertificateConfig issuer)
    {
        if (string.IsNullOrWhiteSpace(issuer.IssuerCaThumbprint))
            throw new InvalidOperationException("issuerCaThumbprint is required for storeThumbprint source.");

        if (!Enum.TryParse<StoreName>(issuer.IssuerCaStoreName, true, out var storeName)) storeName = StoreName.My;

        if (!Enum.TryParse<StoreLocation>(issuer.IssuerCaStoreLocation, true, out var storeLocation))
            storeLocation = StoreLocation.LocalMachine;

        using var store = new X509Store(storeName, storeLocation);
        store.Open(OpenFlags.ReadOnly);
        var normalizedThumbprint = issuer.IssuerCaThumbprint.Replace(" ", string.Empty, StringComparison.Ordinal);
        var certs = store.Certificates.Find(X509FindType.FindByThumbprint, normalizedThumbprint, false);
        if (certs.Count == 0)
            throw new InvalidOperationException($"Issuer thumbprint '{issuer.IssuerCaThumbprint}' not found in store.");

        return certs[0];
    }

    private string ResolvePath(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;

        return Path.IsPathRooted(value)
            ? value
            : Path.Combine(environment.ContentRootPath, value);
    }

    private static string BuildIssuedId(string hostId)
    {
        var normalizedHostId = NonKebabCharsRegex().Replace(hostId.ToLowerInvariant(), "-").Trim('-');
        normalizedHostId = Regex.Replace(normalizedHostId, "-{2,}", "-");
        if (string.IsNullOrWhiteSpace(normalizedHostId)) normalizedHostId = "host";

        return $"issued-{normalizedHostId}";
    }

    private static string BuildIssuedDomainTranslationId(string ruleId)
    {
        var normalizedRuleId = NonKebabCharsRegex().Replace(ruleId.ToLowerInvariant(), "-").Trim('-');
        normalizedRuleId = Regex.Replace(normalizedRuleId, "-{2,}", "-");
        if (string.IsNullOrWhiteSpace(normalizedRuleId)) normalizedRuleId = "translation";

        return $"issued-translation-{normalizedRuleId}";
    }

    private static string BuildIssuedHostNameId(string hostName)
    {
        var normalizedHostName = NonKebabCharsRegex().Replace(hostName.ToLowerInvariant(), "-").Trim('-');
        normalizedHostName = Regex.Replace(normalizedHostName, "-{2,}", "-");
        if (string.IsNullOrWhiteSpace(normalizedHostName)) normalizedHostName = "admin";

        return $"issued-admin-{normalizedHostName}";
    }
}