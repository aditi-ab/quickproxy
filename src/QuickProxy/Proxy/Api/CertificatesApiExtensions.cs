using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.RegularExpressions;
using QuickProxy.Proxy.Models;
using QuickProxy.Proxy.Runtime;
using QuickProxy.Proxy.Storage;
using QuickProxy.Shared.Web;

namespace QuickProxy.Proxy.Api;

public static partial class CertificatesApiExtensions
{
    [GeneratedRegex("^[a-z0-9]+(?:-[a-z0-9]+)*$")]
    private static partial Regex CertificateIdRegex();

    public static IEndpointRouteBuilder MapCertificatesApi(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup($"{InternalApiPaths.AdminRoot}/certificates").RequireAuthorization();

        group.MapGet("/",
            (ICertificateStore store, IProxyHostRuntime runtime, IDomainTranslationRuntime domainTranslationRuntime) =>
            {
                return Results.Ok(EnrichForResponse(store.List(), store, runtime, domainTranslationRuntime));
            });

        group.MapGet("/{id}",
            (string id, ICertificateStore store, IProxyHostRuntime runtime,
                IDomainTranslationRuntime domainTranslationRuntime) =>
            {
                var config = store.Get(id);
                if (config is null) return NotFound(id);

                var enriched = EnrichForResponse([config], store, runtime, domainTranslationRuntime).FirstOrDefault();
                return Results.Ok(enriched);
            });

        group.MapPut("/{id}",
            (string id, StoredCertificateConfig config, ICertificateStore store, ICertificateRuntimeCache cache) =>
            {
                config.Id = id;
                var errors = Validate(config);
                if (errors.Count > 0) return Validation(errors);

                store.Upsert(NormalizeForStorage(config));
                cache.Invalidate(id);
                return Results.Ok(store.Get(id));
            });

        group.MapDelete("/{id}", (string id, ICertificateStore store, ICertificateRuntimeCache cache) =>
        {
            if (!store.Delete(id)) return NotFound(id);

            cache.Invalidate(id);
            return Results.NoContent();
        });

        group.MapPost("/{id}/files", async (
            string id,
            HttpRequest request,
            ICertificateStore store,
            ICertificateRuntimeCache cache) =>
        {
            var config = store.Get(id);
            if (config is null) return NotFound(id);

            if (!request.HasFormContentType) return Validation(["Request must be multipart/form-data."]);

            var form = await request.ReadFormAsync();
            var files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            await AddIfPresentAsync(form.Files.GetFile("certificateFile"), "certificate.pem", files);
            await AddIfPresentAsync(form.Files.GetFile("keyFile"), "key.pem", files);
            await AddIfPresentAsync(form.Files.GetFile("intermediateFile"), "intermediate.pem", files);
            await AddIfPresentAsync(form.Files.GetFile("pfxFile"), "certificate.pfx", files);
            await AddIfPresentAsync(form.Files.GetFile("caCertificateFile"), "ca-certificate.pem", files);
            await AddIfPresentAsync(form.Files.GetFile("caKeyFile"), "ca-key.pem", files);
            await AddIfPresentAsync(form.Files.GetFile("caPfxFile"), "ca-certificate.pfx", files);
            store.SaveFiles(id, files);
            cache.Invalidate(id);

            return Results.Ok(store.Get(id));
        });

        return app;
    }

    public static IEndpointRouteBuilder MapPublicCertificatesApi(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup($"{InternalApiPaths.Root}/certificates");

        group.MapGet("/development", (DevelopmentCertificateAccessor developmentCertificateAccessor) =>
        {
            var bytes = developmentCertificateAccessor.GetFallbackPfxBytes();
            return Results.File(bytes, "application/x-pkcs12", DevelopmentCertificateAccessor.FileName);
        });

        return app;
    }

    private static IReadOnlyList<StoredCertificateConfig> EnrichForResponse(
        IReadOnlyList<StoredCertificateConfig> configs,
        ICertificateStore store,
        IProxyHostRuntime? runtime,
        IDomainTranslationRuntime? domainTranslationRuntime)
    {
        var hosts = runtime?.GetHosts() ?? [];
        var domainTranslations = domainTranslationRuntime?.GetRules() ?? [];

        foreach (var config in configs)
        {
            var hostUsage = hosts.Count(x =>
                string.Equals(x.CertificateId, config.Id, StringComparison.OrdinalIgnoreCase));
            var translationUsage = domainTranslations.Count(x =>
                string.Equals(x.CertificateId, config.Id, StringComparison.OrdinalIgnoreCase));
            config.InUse = hostUsage + translationUsage > 0;
            config.InUseCount = hostUsage + translationUsage;

            config.Provider = config.Mode switch
            {
                CertificateConfigMode.Files => "Files",
                CertificateConfigMode.Pfx => "PFX",
                CertificateConfigMode.Thumbprint => "Windows Store",
                CertificateConfigMode.Issuer => "Issuer",
                _ => "Unknown"
            };

            var cert = TryLoadCertificate(config, store);
            if (cert is not null)
            {
                config.ExpiresAtUtc = cert.NotAfter;
                config.DomainNames = ExtractDomainNames(cert);
            }
            else
            {
                config.ExpiresAtUtc = null;
                config.DomainNames = [];
            }
        }

        return configs;
    }

    private static StoredCertificateConfig NormalizeForStorage(StoredCertificateConfig config)
    {
        var matchDomains = config.IssuerMatchDomains
            .Select(x => x?.Trim().ToLowerInvariant() ?? string.Empty)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new StoredCertificateConfig
        {
            Id = config.Id,
            Mode = config.Mode,
            PfxPassword = config.PfxPassword,
            PfxPasswordEnvVar = config.PfxPasswordEnvVar,
            Thumbprint = config.Thumbprint,
            StoreName = config.StoreName,
            StoreLocation = config.StoreLocation,
            IssuerMatchDomains = matchDomains,
            IssuerEnabled = config.IssuerEnabled,
            IssuerCaSource = config.IssuerCaSource,
            IssuerCaCertPath = config.IssuerCaCertPath?.Trim(),
            IssuerCaKeyPath = config.IssuerCaKeyPath?.Trim(),
            IssuerCaPfxPath = config.IssuerCaPfxPath?.Trim(),
            IssuerCaPfxPassword = config.IssuerCaPfxPassword,
            IssuerCaPfxPasswordEnvVar = config.IssuerCaPfxPasswordEnvVar?.Trim(),
            IssuerCaThumbprint = config.IssuerCaThumbprint?.Trim(),
            IssuerCaStoreName = string.IsNullOrWhiteSpace(config.IssuerCaStoreName)
                ? "My"
                : config.IssuerCaStoreName.Trim(),
            IssuerCaStoreLocation = string.IsNullOrWhiteSpace(config.IssuerCaStoreLocation)
                ? "LocalMachine"
                : config.IssuerCaStoreLocation.Trim()
        };
    }

    private static X509Certificate2? TryLoadCertificate(StoredCertificateConfig config, ICertificateStore store)
    {
        try
        {
            return config.Mode switch
            {
                CertificateConfigMode.Files => LoadFromFiles(config, store),
                CertificateConfigMode.Pfx => LoadFromPfx(config, store),
                CertificateConfigMode.Thumbprint => LoadFromStore(config),
                CertificateConfigMode.Issuer => null,
                _ => null
            };
        }
        catch
        {
            return null;
        }
    }

    private static X509Certificate2? LoadFromFiles(StoredCertificateConfig config, ICertificateStore store)
    {
        var certBytes = store.GetFile(config.Id, "certificate.pem");
        if (certBytes is null || certBytes.Length == 0) return null;

        var certPem = Encoding.UTF8.GetString(certBytes);
        return X509Certificate2.CreateFromPem(certPem);
    }

    private static X509Certificate2? LoadFromPfx(StoredCertificateConfig config, ICertificateStore store)
    {
        var pfxBytes = store.GetFile(config.Id, "certificate.pfx");
        if (pfxBytes is null || pfxBytes.Length == 0) return null;

        var password = config.PfxPassword;
        if (string.IsNullOrWhiteSpace(password) && !string.IsNullOrWhiteSpace(config.PfxPasswordEnvVar))
            password = Environment.GetEnvironmentVariable(config.PfxPasswordEnvVar);

        return X509CertificateLoader.LoadPkcs12(pfxBytes, password);
    }

    private static X509Certificate2? LoadFromStore(StoredCertificateConfig config)
    {
        if (string.IsNullOrWhiteSpace(config.Thumbprint)) return null;

        if (!Enum.TryParse<StoreName>(config.StoreName, true, out var storeName)) storeName = StoreName.My;

        if (!Enum.TryParse<StoreLocation>(config.StoreLocation, true, out var storeLocation))
            storeLocation = StoreLocation.LocalMachine;

        using var store = new X509Store(storeName, storeLocation);
        store.Open(OpenFlags.ReadOnly);
        var normalized = config.Thumbprint.Replace(" ", string.Empty, StringComparison.Ordinal);
        var certs = store.Certificates.Find(X509FindType.FindByThumbprint, normalized, false);
        return certs.Count > 0 ? certs[0] : null;
    }

    private static List<string> ExtractDomainNames(X509Certificate2 cert)
    {
        var values = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var sanExtension = cert.Extensions["2.5.29.17"];
        if (sanExtension is not null)
        {
            var formatted = sanExtension.Format(false);
            foreach (var part in formatted.Split(',',
                         StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                if (part.StartsWith("DNS Name=", StringComparison.OrdinalIgnoreCase))
                    values.Add(part["DNS Name=".Length..].Trim());
                else if (part.StartsWith("DNS:", StringComparison.OrdinalIgnoreCase))
                    values.Add(part["DNS:".Length..].Trim());
        }

        if (values.Count == 0)
        {
            var dns = cert.GetNameInfo(X509NameType.DnsName, false);
            if (!string.IsNullOrWhiteSpace(dns)) values.Add(dns);
        }

        return values.Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
    }

    private static async Task AddIfPresentAsync(IFormFile? file, string fileName,
        IDictionary<string, byte[]> destination)
    {
        if (file is null || file.Length == 0) return;

        await using var stream = file.OpenReadStream();
        using var memory = new MemoryStream();
        await stream.CopyToAsync(memory);
        destination[fileName] = memory.ToArray();
    }

    private static List<string> Validate(StoredCertificateConfig config)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(config.Id) || !CertificateIdRegex().IsMatch(config.Id))
            errors.Add("id is required and must be lowercase kebab-case.");

        if (config.Mode == CertificateConfigMode.Thumbprint && string.IsNullOrWhiteSpace(config.Thumbprint))
            errors.Add("thumbprint is required when mode is 'thumbprint'.");

        if (config.Mode == CertificateConfigMode.Issuer)
        {
            if (config.IssuerMatchDomains.Count == 0)
                errors.Add("issuerMatchDomains must contain at least one domain when mode is 'issuer'.");

            var normalized = config.IssuerMatchDomains
                .Select(x => x?.Trim().ToLowerInvariant() ?? string.Empty)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (normalized.Length == 0) errors.Add("issuerMatchDomains contains no valid domains.");

            if (config.IssuerCaSource is IssuerCaSourceMode.PathPem &&
                (string.IsNullOrWhiteSpace(config.IssuerCaCertPath) ||
                 string.IsNullOrWhiteSpace(config.IssuerCaKeyPath)))
                errors.Add("issuerCaCertPath and issuerCaKeyPath are required when issuerCaSource is 'pathPem'.");

            if (config.IssuerCaSource is IssuerCaSourceMode.PathPfx &&
                string.IsNullOrWhiteSpace(config.IssuerCaPfxPath))
                errors.Add("issuerCaPfxPath is required when issuerCaSource is 'pathPfx'.");

            if (config.IssuerCaSource is IssuerCaSourceMode.StoreThumbprint &&
                string.IsNullOrWhiteSpace(config.IssuerCaThumbprint))
                errors.Add("issuerCaThumbprint is required when issuerCaSource is 'storeThumbprint'.");
        }

        return errors;
    }

    private static IResult Validation(List<string> details)
    {
        return Results.BadRequest(new
        {
            code = "validation_error",
            message = "Validation failed.",
            details
        });
    }

    private static IResult NotFound(string id)
    {
        return Results.NotFound(new
        {
            code = "not_found",
            message = $"Certificate '{id}' was not found."
        });
    }
}