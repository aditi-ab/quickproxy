using System.Net;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using QuickProxy.Proxy.Storage.Db;
using QuickProxy.Shared.Configuration;

namespace QuickProxy.Proxy.Runtime;

public sealed class AdminCertificateAccessor(
    IHostEnvironment environment,
    IConfiguration configuration,
    ListenSettings settings,
    IApplicationDataStore applicationDataStore)
{
    public const string DefaultGeneratedPassword = "quickproxy-admin";
    public const string QuickProxyFallbackCertificateOid = "1.3.6.1.4.1.55555.1.1";
    private const string DefaultSubjectName = "CN=QuickProxy SSL";

    public X509Certificate2 LoadOrCreate()
    {
        var configuredPath = ResolveConfiguredPath(environment, settings.AdminCertificate.Path);
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            if (!File.Exists(configuredPath))
                throw new InvalidOperationException($"Admin certificate file '{configuredPath}' was not found.");

            return X509CertificateLoader.LoadPkcs12FromFile(
                configuredPath,
                ResolveConfiguredPassword(settings.AdminCertificate));
        }

        var pfxBytes = applicationDataStore.GetOrCreate("admin-fallback-certificate",
            () => CreateSelfSignedPfx(configuration, DefaultGeneratedPassword));
        return X509CertificateLoader.LoadPkcs12(pfxBytes, DefaultGeneratedPassword);
    }

    internal static byte[] CreateSelfSignedPfx(IConfiguration configuration, string password)
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(
            DefaultSubjectName,
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(
            X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment,
            false));

        var enhancedKeyUsage = new OidCollection
        {
            new Oid("1.3.6.1.5.5.7.3.1")
        };
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(enhancedKeyUsage, false));
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));
        request.CertificateExtensions.Add(new X509Extension(
            new Oid(QuickProxyFallbackCertificateOid, "QuickProxy Fallback Admin Certificate"),
            [0x05, 0x00],
            false));

        var sanBuilder = new SubjectAlternativeNameBuilder();
        sanBuilder.AddDnsName("localhost");
        sanBuilder.AddIpAddress(IPAddress.Loopback);
        sanBuilder.AddIpAddress(IPAddress.IPv6Loopback);
        foreach (var dnsName in ResolveCandidateDnsNames(configuration)) sanBuilder.AddDnsName(dnsName);

        request.CertificateExtensions.Add(sanBuilder.Build());

        using var generatedCertificate = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddYears(5));

        return generatedCertificate.Export(X509ContentType.Pkcs12, password);
    }

    public static bool IsTrustedFallbackCertificate(X509Certificate2? certificate, SslPolicyErrors sslPolicyErrors)
    {
        if (certificate is null) return false;

        if (sslPolicyErrors == SslPolicyErrors.None) return true;

        var allowedErrors = SslPolicyErrors.RemoteCertificateChainErrors |
                            SslPolicyErrors.RemoteCertificateNameMismatch;
        if ((sslPolicyErrors & ~allowedErrors) != SslPolicyErrors.None) return false;

        var now = DateTimeOffset.UtcNow;
        if (now < certificate.NotBefore || now > certificate.NotAfter) return false;

        if (!string.Equals(certificate.Subject, certificate.Issuer, StringComparison.OrdinalIgnoreCase)) return false;

        if (IsLegacyQuickProxyFallbackCertificate(certificate)) return true;

        return certificate.Extensions
            .OfType<X509Extension>()
            .Any(extension =>
                string.Equals(extension.Oid?.Value, QuickProxyFallbackCertificateOid, StringComparison.Ordinal));
    }

    private static IEnumerable<string> ResolveCandidateDnsNames(IConfiguration configuration)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        AddName(names, Environment.MachineName);
        AddName(names, Dns.GetHostName());
        AddName(names, Environment.GetEnvironmentVariable("SERVERNAME"));
        AddName(names, Environment.GetEnvironmentVariable("TemplateValues__Server__Name"));
        AddName(names, configuration["TemplateValues:Server:Name"]);

        try
        {
            var hostEntry = Dns.GetHostEntry(Dns.GetHostName());
            AddName(names, hostEntry.HostName);
            foreach (var alias in hostEntry.Aliases) AddName(names, alias);
        }
        catch
        {
            // Best-effort only.
        }

        names.Remove("localhost");
        return names;
    }

    private static void AddName(ISet<string> names, string? candidate)
    {
        var normalized = candidate?.Trim().Trim('.').ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(normalized)) return;

        names.Add(normalized);
    }

    private static bool IsLegacyQuickProxyFallbackCertificate(X509Certificate2 certificate)
    {
        if (!string.Equals(certificate.Subject, DefaultSubjectName, StringComparison.OrdinalIgnoreCase)) return false;

        if (!CertificateHasServerAuthenticationUsage(certificate)) return false;

        return CertificateHasSubjectAlternativeName(certificate, "localhost");
    }

    private static bool CertificateHasServerAuthenticationUsage(X509Certificate2 certificate)
    {
        foreach (var extension in certificate.Extensions.OfType<X509EnhancedKeyUsageExtension>())
        foreach (var oid in extension.EnhancedKeyUsages)
            if (string.Equals(oid.Value, "1.3.6.1.5.5.7.3.1", StringComparison.Ordinal))
                return true;

        return false;
    }

    private static bool CertificateHasSubjectAlternativeName(X509Certificate2 certificate, string dnsName)
    {
        return certificate.Extensions
            .OfType<X509Extension>()
            .Where(extension => string.Equals(extension.Oid?.Value, "2.5.29.17", StringComparison.Ordinal))
            .Any(extension => extension.Format(true)
                .Split([Environment.NewLine], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Any(line => string.Equals(line, $"DNS Name={dnsName}", StringComparison.OrdinalIgnoreCase)));
    }

    private static string? ResolveConfiguredPath(IHostEnvironment environment, string? configuredPath)
    {
        if (string.IsNullOrWhiteSpace(configuredPath)) return null;

        return Path.IsPathRooted(configuredPath)
            ? configuredPath
            : Path.Combine(environment.ContentRootPath, configuredPath);
    }

    private static string? ResolveConfiguredPassword(AdminCertificateSettings settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.Password)) return settings.Password;

        if (!string.IsNullOrWhiteSpace(settings.PasswordEnvVar))
            return Environment.GetEnvironmentVariable(settings.PasswordEnvVar);

        return null;
    }
}