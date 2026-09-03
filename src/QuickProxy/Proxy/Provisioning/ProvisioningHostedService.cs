using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using QuickProxy.Audit;
using QuickProxy.Configuration;
using QuickProxy.Proxy.Config.Storage;
using QuickProxy.Proxy.Containers;
using QuickProxy.Proxy.Models;
using QuickProxy.Proxy.Runtime;
using QuickProxy.Proxy.Storage;
using QuickProxy.Proxy.Validation;

namespace QuickProxy.Proxy.Provisioning;

public sealed partial class ProvisioningHostedService(
    IHostApplicationLifetime applicationLifetime,
    IHostEnvironment environment,
    IHttpClientFactory httpClientFactory,
    IOptions<ProvisioningSettings> settings,
    IHostTemplateValueProvider hostTemplateValueProvider,
    IProxyHostRepository proxyHostRepository,
    IAuthProviderStore authProviderStore,
    IDomainTranslationStore domainTranslationStore,
    IContainerDefaultsStore containerDefaultsStore,
    ICertificateStore certificateStore,
    IConfigEncryptionService configEncryptionService,
    ICertificateRuntimeCache certificateRuntimeCache,
    IProxyHostRuntime proxyHostRuntime,
    IDomainTranslationRuntime domainTranslationRuntime,
    IIssuedCertificateService issuedCertificateService,
    IAuditStore auditStore,
    ILogger<ProvisioningHostedService> logger) : BackgroundService
{
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();
    private readonly SemaphoreSlim _runLock = new(1, 1);

    [GeneratedRegex("^[a-z0-9]+(?:-[a-z0-9]+)*$")]
    private static partial Regex CertificateIdRegex();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await WaitForApplicationStartedAsync(stoppingToken);

        try
        {
            await RunNowAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            AuditWriter.WriteSystemEvent(
                auditStore,
                "system",
                "provision",
                "provisioning",
                null,
                "failure",
                "Provisioning run failed during startup.",
                ex.Message);
            logger.LogError(ex, "Provisioning failed.");
        }
    }

    private async Task RunProvisioningAsync(ProvisioningSettings config, bool overwriteExisting,
        CancellationToken cancellationToken)
    {
        var filePath = config.FilePath?.Trim();
        var url = config.Url?.Trim();

        if (!string.IsNullOrWhiteSpace(filePath) && !string.IsNullOrWhiteSpace(url))
        {
            logger.LogError(
                "Provisioning is invalid. Configure only one of Provisioning:FilePath or Provisioning:Url.");
            return;
        }

        if (string.IsNullOrWhiteSpace(filePath) && string.IsNullOrWhiteSpace(url))
        {
            logger.LogWarning(
                "Provisioning is enabled but no Provisioning:FilePath or Provisioning:Url is configured.");
            return;
        }

        var json = !string.IsNullOrWhiteSpace(filePath)
            ? await LoadFromFileAsync(filePath, cancellationToken)
            : await LoadFromUrlAsync(url!, config.TimeoutSeconds, cancellationToken);

        if (string.IsNullOrWhiteSpace(json))
        {
            logger.LogWarning("Provisioning source returned no content.");
            return;
        }

        json = ExpandTemplates(json, hostTemplateValueProvider);

        ProvisioningDocument? document;
        try
        {
            document = JsonSerializer.Deserialize<ProvisioningDocument>(json, JsonOptions);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Provisioning payload could not be parsed.");
            return;
        }

        if (document is null)
        {
            logger.LogWarning("Provisioning payload was empty after deserialization.");
            return;
        }

        var importedAuthProviderCount = ImportAuthProviders(document.AuthProviders, overwriteExisting);
        var importedTranslationCount = ImportDomainTranslations(document.DomainTranslations, overwriteExisting);
        var importedManualHostCount = ImportProxyHosts(document.ProxyHosts, overwriteExisting);
        var importedTemplateCount = ImportAutomaticTemplates(document.AutomaticTemplates, overwriteExisting);
        var importedDefaultSetsCount = ImportContainerDefaultSets(document.ContainerDefaultSets, overwriteExisting);
        var importedCertificateIds = ImportCertificates(document.Certificates, overwriteExisting);

        foreach (var certificateId in importedCertificateIds) certificateRuntimeCache.Invalidate(certificateId);

        if (importedTranslationCount > 0 || importedManualHostCount > 0 || importedTemplateCount > 0 ||
            importedCertificateIds.Count > 0)
        {
            proxyHostRuntime.TryReload();
            domainTranslationRuntime.TryReload();
            certificateRuntimeCache.InvalidateAll();
        }

        logger.LogInformation(
            "Provisioning completed. Imported {AuthProviderCount} auth providers, {TranslationCount} domain translations, {ManualHostCount} manual proxy hosts, {TemplateCount} proxy host templates, {DefaultSetCount} container default sets, and {CertificateCount} certificates.",
            importedAuthProviderCount,
            importedTranslationCount,
            importedManualHostCount,
            importedTemplateCount,
            importedDefaultSetsCount,
            importedCertificateIds.Count);
        AuditWriter.WriteSystemEvent(
            auditStore,
            "system",
            "provision",
            "provisioning",
            null,
            "success",
            $"Provisioning completed: authProviders={importedAuthProviderCount}, domainTranslations={importedTranslationCount}, proxyHosts={importedManualHostCount}, templates={importedTemplateCount}, containerDefaults={importedDefaultSetsCount}, certificates={importedCertificateIds.Count}.");
    }

    private int ImportAuthProviders(IEnumerable<ProvisionedAuthProviderEntry> providers, bool overwriteExisting)
    {
        var importedCount = 0;
        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var provider in providers)
        {
            var id = (provider?.Id ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(id))
            {
                logger.LogWarning("Skipping provisioned auth provider with empty id.");
                continue;
            }

            if (!seenIds.Add(id))
            {
                logger.LogWarning(
                    "Skipping duplicate provisioned auth provider id '{ProviderId}' in the provisioning document.", id);
                continue;
            }

            if (!overwriteExisting && authProviderStore.Exists(id))
            {
                logger.LogInformation("Skipping provisioned auth provider '{ProviderId}' because it already exists.",
                    id);
                continue;
            }

            var errors = ValidateProvisionedAuthProvider(provider);
            if (errors.Count > 0)
            {
                logger.LogWarning("Skipping provisioned auth provider '{ProviderId}' because it is invalid: {Errors}",
                    id, string.Join("; ", errors));
                continue;
            }

            authProviderStore.Upsert(ToStoredAuthProvider(provider));
            importedCount++;
        }

        return importedCount;
    }

    private int ImportDomainTranslations(IEnumerable<DomainTranslationRule> rules, bool overwriteExisting)
    {
        var importedCount = 0;
        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var currentRules = domainTranslationRuntime.GetRules().ToList();

        foreach (var rule in rules)
        {
            if (rule is null) continue;

            var normalizedId = (rule.Id ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(normalizedId))
            {
                logger.LogWarning("Skipping provisioned domain translation with empty id.");
                continue;
            }

            rule.Id = normalizedId;

            if (!seenIds.Add(normalizedId))
            {
                logger.LogWarning(
                    "Skipping duplicate provisioned domain translation id '{RuleId}' in the provisioning document.",
                    normalizedId);
                continue;
            }

            if (!overwriteExisting && domainTranslationStore.Exists(normalizedId))
            {
                logger.LogInformation("Skipping provisioned domain translation '{RuleId}' because it already exists.",
                    normalizedId);
                continue;
            }

            var validationErrors = ValidateDomainTranslationForProvisioning(rule, currentRules, certificateStore,
                overwriteExisting ? normalizedId : null);
            if (validationErrors.Count > 0)
            {
                logger.LogWarning(
                    "Skipping provisioned domain translation '{RuleId}' because it is invalid: {Errors}",
                    normalizedId,
                    string.Join("; ", validationErrors));
                continue;
            }

            if (overwriteExisting) issuedCertificateService.DeleteForDomainTranslation(normalizedId);

            domainTranslationStore.Upsert(NormalizeProvisionedDomainTranslation(rule));
            currentRules.RemoveAll(x => string.Equals(x.Id, normalizedId, StringComparison.OrdinalIgnoreCase));
            currentRules.Add(rule);
            importedCount++;
        }

        return importedCount;
    }

    private int ImportContainerDefaultSets(IEnumerable<ProvisionedContainerDefaultsSetEntry> sets,
        bool overwriteExisting)
    {
        var importedCount = 0;
        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var set in sets)
        {
            if (set is null) continue;

            var normalizedId = NormalizeSetId(set.Id);
            if (string.IsNullOrWhiteSpace(normalizedId))
            {
                logger.LogWarning("Skipping provisioned container default set with empty id.");
                continue;
            }

            if (!seenIds.Add(normalizedId))
            {
                logger.LogWarning(
                    "Skipping duplicate provisioned container default set id '{SetId}' in the provisioning document.",
                    normalizedId);
                continue;
            }

            if (!overwriteExisting && containerDefaultsStore.Get(normalizedId) is not null)
            {
                logger.LogInformation("Skipping provisioned container default set '{SetId}' because it already exists.",
                    normalizedId);
                continue;
            }

            var labels = NormalizePairs(set.Labels, true, "labels", out var labelsError);
            if (!string.IsNullOrWhiteSpace(labelsError))
            {
                logger.LogWarning(
                    "Skipping provisioned container default set '{SetId}' because labels are invalid: {Error}",
                    normalizedId, labelsError);
                continue;
            }

            var envVars = NormalizePairs(set.EnvVars, false, "envVars", out var envError);
            if (!string.IsNullOrWhiteSpace(envError))
            {
                logger.LogWarning(
                    "Skipping provisioned container default set '{SetId}' because envVars are invalid: {Error}",
                    normalizedId, envError);
                continue;
            }

            var mountBindings = NormalizeMountBindings(set.MountBindings, out var mountBindingsError);
            if (!string.IsNullOrWhiteSpace(mountBindingsError))
            {
                logger.LogWarning(
                    "Skipping provisioned container default set '{SetId}' because mountBindings are invalid: {Error}",
                    normalizedId, mountBindingsError);
                continue;
            }

            var hostMappings = NormalizeHostMappings(set.HostMappings, out var hostMappingsError);
            if (!string.IsNullOrWhiteSpace(hostMappingsError))
            {
                logger.LogWarning(
                    "Skipping provisioned container default set '{SetId}' because hostMappings are invalid: {Error}",
                    normalizedId, hostMappingsError);
                continue;
            }

            var networkAliases = NormalizeNetworkAliases(set.NetworkAliases, out var networkAliasesError);
            if (!string.IsNullOrWhiteSpace(networkAliasesError))
            {
                logger.LogWarning(
                    "Skipping provisioned container default set '{SetId}' because networkAliases are invalid: {Error}",
                    normalizedId, networkAliasesError);
                continue;
            }

            containerDefaultsStore.Upsert(new ContainerDefaultsSet
            {
                Id = normalizedId,
                Labels = labels,
                EnvVars = envVars,
                MountBindings = mountBindings,
                HostMappings = hostMappings,
                NetworkAliases = networkAliases
            });
            importedCount++;
        }

        return importedCount;
    }

    private int ImportProxyHosts(IEnumerable<ProxyHostConfig> hosts, bool overwriteExisting)
    {
        return ImportProxyHostEntries(
            hosts,
            overwriteExisting,
            ProxyHostMode.Manual,
            "Skipping provisioned manual proxy host with empty id.",
            "Skipping duplicate provisioned manual proxy host id '{HostId}' in the provisioning document.",
            "Skipping provisioned manual proxy host '{HostId}' because it already exists.",
            "Skipping provisioned host '{HostId}' because only manual mode is supported in proxyHosts.",
            "Skipping provisioned manual proxy host '{HostId}' because it is invalid: {Errors}");
    }

    private int ImportAutomaticTemplates(IEnumerable<ProxyHostConfig> templates, bool overwriteExisting)
    {
        return ImportProxyHostEntries(
            templates,
            overwriteExisting,
            ProxyHostMode.AutomaticContainer,
            "Skipping provisioned automatic template with empty id.",
            "Skipping duplicate provisioned automatic template id '{HostId}' in the provisioning document.",
            "Skipping provisioned automatic template '{HostId}' because it already exists.",
            "Skipping provisioned host '{HostId}' because only automaticContainer mode is supported by provisioning.",
            "Skipping provisioned automatic template '{HostId}' because it is invalid: {Errors}");
    }

    private int ImportProxyHostEntries(
        IEnumerable<ProxyHostConfig> hosts,
        bool overwriteExisting,
        ProxyHostMode expectedMode,
        string emptyIdMessage,
        string duplicateIdMessage,
        string skipExistingMessage,
        string wrongModeMessage,
        string invalidMessage)
    {
        var importedCount = 0;
        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var currentHosts = proxyHostRuntime.GetStoredHosts()
            .Where(x => overwriteExisting || !seenIds.Contains(x.Id))
            .ToList();

        foreach (var host in hosts)
        {
            if (host is null) continue;

            if (string.IsNullOrWhiteSpace(host.Id))
            {
                logger.LogWarning(emptyIdMessage);
                continue;
            }

            var hostId = host.Id.Trim();
            host.Id = hostId;

            if (!seenIds.Add(hostId))
            {
                logger.LogWarning(duplicateIdMessage, hostId);
                continue;
            }

            if (!overwriteExisting && proxyHostRepository.Exists(hostId))
            {
                logger.LogInformation(skipExistingMessage, hostId);
                continue;
            }

            if (host.Mode != expectedMode)
            {
                logger.LogWarning(wrongModeMessage, hostId);
                continue;
            }

            var validationErrors =
                ValidateProvisionedHost(host, hostId, currentHosts, overwriteExisting ? hostId : null);
            if (validationErrors.Count > 0)
            {
                logger.LogWarning(invalidMessage, hostId, string.Join("; ", validationErrors));
                continue;
            }

            proxyHostRepository.Write(host);
            currentHosts.RemoveAll(x => string.Equals(x.Id, hostId, StringComparison.OrdinalIgnoreCase));
            currentHosts.Add(host);
            importedCount++;
        }

        return importedCount;
    }

    private static List<string> ValidateProvisionedHost(
        ProxyHostConfig candidate,
        string expectedId,
        IReadOnlyList<ProxyHostConfig> currentHosts,
        string? replaceId)
    {
        var single = ProxyHostValidator.ValidateSingle(candidate, expectedId);
        var errors = new List<string>(single.Errors);

        var all = currentHosts
            .Where(x => replaceId is null || !string.Equals(x.Id, replaceId, StringComparison.OrdinalIgnoreCase))
            .ToList();
        all.Add(candidate);
        errors.AddRange(ProxyHostValidator.ValidateAcrossHosts(all));

        return errors;
    }

    private static DomainTranslationRule NormalizeProvisionedDomainTranslation(DomainTranslationRule rule)
    {
        return new DomainTranslationRule
        {
            Id = (rule.Id ?? string.Empty).Trim(),
            Enabled = rule.Enabled,
            SourceDomain = NormalizeProvisionedDomain(rule.SourceDomain),
            TargetDomain = NormalizeProvisionedDomain(rule.TargetDomain),
            CertificateId = string.IsNullOrWhiteSpace(rule.CertificateId) ? null : rule.CertificateId.Trim(),
            RewriteHostHeader = rule.RewriteHostHeader
        };
    }

    private static List<string> ValidateDomainTranslationForProvisioning(
        DomainTranslationRule candidate,
        IReadOnlyList<DomainTranslationRule> currentRules,
        ICertificateStore certificateStore,
        string? replaceId)
    {
        var errors = new List<string>();
        candidate.SourceDomain = NormalizeProvisionedDomain(candidate.SourceDomain);
        candidate.TargetDomain = NormalizeProvisionedDomain(candidate.TargetDomain);

        if (string.IsNullOrWhiteSpace(candidate.Id) ||
            !Regex.IsMatch(candidate.Id, "^[a-z0-9]+(?:-[a-z0-9]+)*$", RegexOptions.CultureInvariant))
            errors.Add("id must be lowercase kebab-case.");

        if (!IsValidProvisioningDomain(candidate.SourceDomain))
            errors.Add("sourceDomain must be a valid hostname without scheme, path, or port.");

        if (!IsValidProvisioningDomain(candidate.TargetDomain))
            errors.Add("targetDomain must be a valid hostname without scheme, path, or port.");

        var duplicate = currentRules.FirstOrDefault(x =>
            x.Enabled &&
            candidate.Enabled &&
            (replaceId is null || !string.Equals(x.Id, replaceId, StringComparison.OrdinalIgnoreCase)) &&
            string.Equals(x.SourceDomain, candidate.SourceDomain, StringComparison.OrdinalIgnoreCase));
        if (duplicate is not null)
            errors.Add($"sourceDomain '{candidate.SourceDomain}' is already used by '{duplicate.Id}'.");

        if (!string.IsNullOrWhiteSpace(candidate.CertificateId) &&
            !certificateStore.Exists(candidate.CertificateId.Trim()))
            errors.Add($"certificateId '{candidate.CertificateId}' was not found.");

        return errors;
    }

    private static string NormalizeProvisionedDomain(string? value)
    {
        return (value ?? string.Empty).Trim().Trim('.').ToLowerInvariant();
    }

    private static bool IsValidProvisioningDomain(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Contains('/') || value.Contains(':')) return false;

        var parts = value.Split('.', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length > 0 && parts.All(part =>
            part.Length > 0 &&
            Regex.IsMatch(part, "^[a-zA-Z0-9-]+$", RegexOptions.CultureInvariant) &&
            !part.StartsWith('-') &&
            !part.EndsWith('-'));
    }

    private List<string> ImportCertificates(IEnumerable<ProvisionedCertificateEntry> certificates,
        bool overwriteExisting)
    {
        var importedIds = new List<string>();
        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var certificate in certificates)
        {
            if (certificate is null) continue;

            if (string.IsNullOrWhiteSpace(certificate.Id))
            {
                logger.LogWarning("Skipping provisioned certificate with empty id.");
                continue;
            }

            if (!seenIds.Add(certificate.Id))
            {
                logger.LogWarning(
                    "Skipping duplicate provisioned certificate id '{CertificateId}' in the provisioning document.",
                    certificate.Id);
                continue;
            }

            if (!overwriteExisting && certificateStore.Exists(certificate.Id))
            {
                logger.LogInformation("Skipping provisioned certificate '{CertificateId}' because it already exists.",
                    certificate.Id);
                continue;
            }

            var errors = ValidateCertificate(certificate);
            if (errors.Count > 0)
            {
                logger.LogWarning(
                    "Skipping provisioned certificate '{CertificateId}' because it is invalid: {Errors}",
                    certificate.Id,
                    string.Join("; ", errors));
                continue;
            }

            certificateStore.Upsert(ToStoredCertificateConfig(certificate));
            var files = DecodeFiles(certificate);
            certificateStore.SaveFiles(certificate.Id, files);
            importedIds.Add(certificate.Id);
        }

        return importedIds;
    }

    private static List<string> ValidateCertificate(ProvisionedCertificateEntry certificate)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(certificate.Id) || !CertificateIdRegex().IsMatch(certificate.Id))
            errors.Add("id must be lowercase kebab-case.");

        if (certificate.Mode is not (CertificateConfigMode.Files or CertificateConfigMode.Pfx
            or CertificateConfigMode.Thumbprint or CertificateConfigMode.Issuer)) errors.Add("mode is invalid.");

        if (certificate.Mode == CertificateConfigMode.Thumbprint && string.IsNullOrWhiteSpace(certificate.Thumbprint))
            errors.Add("thumbprint is required when mode is 'thumbprint'.");

        if (certificate.Mode == CertificateConfigMode.Issuer)
        {
            var matchDomains = certificate.IssuerMatchDomains
                .Select(x => x?.Trim().ToLowerInvariant() ?? string.Empty)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (matchDomains.Length == 0) errors.Add("issuerMatchDomains is required when mode is 'issuer'.");

            var issuerSource = certificate.IssuerCaSource;
            if (!issuerSource.HasValue)
            {
                var sourceHintCount = 0;
                if (!string.IsNullOrWhiteSpace(certificate.Files.CaPfxBase64)) sourceHintCount++;
                if (!string.IsNullOrWhiteSpace(certificate.Files.CaCertificatePemBase64) ||
                    !string.IsNullOrWhiteSpace(certificate.Files.CaKeyPemBase64))
                    sourceHintCount++;
                if (!string.IsNullOrWhiteSpace(certificate.CaPfxPath)) sourceHintCount++;
                if (!string.IsNullOrWhiteSpace(certificate.CaStoreThumbprint)) sourceHintCount++;
                if (!string.IsNullOrWhiteSpace(certificate.CaCertificatePath) ||
                    !string.IsNullOrWhiteSpace(certificate.CaPrivateKeyPath))
                    sourceHintCount++;
                if (sourceHintCount > 1) errors.Add("issuer CA source is ambiguous. Set issuerCaSource explicitly.");

                if (!string.IsNullOrWhiteSpace(certificate.Files.CaPfxBase64))
                    issuerSource = IssuerCaSourceMode.UploadPfx;
                else if (!string.IsNullOrWhiteSpace(certificate.Files.CaCertificatePemBase64) ||
                         !string.IsNullOrWhiteSpace(certificate.Files.CaKeyPemBase64))
                    issuerSource = IssuerCaSourceMode.UploadPem;
                else if (!string.IsNullOrWhiteSpace(certificate.CaPfxPath))
                    issuerSource = IssuerCaSourceMode.PathPfx;
                else if (!string.IsNullOrWhiteSpace(certificate.CaStoreThumbprint))
                    issuerSource = IssuerCaSourceMode.StoreThumbprint;
                else if (!string.IsNullOrWhiteSpace(certificate.CaCertificatePath) ||
                         !string.IsNullOrWhiteSpace(certificate.CaPrivateKeyPath))
                    issuerSource = IssuerCaSourceMode.PathPem;
            }

            if (!issuerSource.HasValue)
                errors.Add("issuerCaSource is required when mode is 'issuer'.");
            else if (issuerSource.Value == IssuerCaSourceMode.PathPem &&
                     (string.IsNullOrWhiteSpace(certificate.CaCertificatePath) ||
                      string.IsNullOrWhiteSpace(certificate.CaPrivateKeyPath)))
                errors.Add("caCertificatePath and caPrivateKeyPath are required when issuerCaSource is 'pathPem'.");
            else if (issuerSource.Value == IssuerCaSourceMode.PathPfx &&
                     string.IsNullOrWhiteSpace(certificate.CaPfxPath))
                errors.Add("caPfxPath is required when issuerCaSource is 'pathPfx'.");
            else if (issuerSource.Value == IssuerCaSourceMode.StoreThumbprint &&
                     string.IsNullOrWhiteSpace(certificate.CaStoreThumbprint))
                errors.Add("caStoreThumbprint is required when issuerCaSource is 'storeThumbprint'.");
            else if (issuerSource.Value == IssuerCaSourceMode.UploadPem &&
                     (string.IsNullOrWhiteSpace(certificate.Files.CaCertificatePemBase64) ||
                      string.IsNullOrWhiteSpace(certificate.Files.CaKeyPemBase64)))
                errors.Add(
                    "caCertificatePemBase64 and caKeyPemBase64 are required when issuerCaSource is 'uploadPem'.");
            else if (issuerSource.Value == IssuerCaSourceMode.UploadPfx &&
                     string.IsNullOrWhiteSpace(certificate.Files.CaPfxBase64))
                errors.Add("caPfxBase64 is required when issuerCaSource is 'uploadPfx'.");
        }

        TryDecodeBase64(certificate.Files.CertificatePemBase64, "certificatePemBase64", errors);
        TryDecodeBase64(certificate.Files.KeyPemBase64, "keyPemBase64", errors);
        TryDecodeBase64(certificate.Files.IntermediatePemBase64, "intermediatePemBase64", errors);
        TryDecodeBase64(certificate.Files.PfxBase64, "pfxBase64", errors);
        TryDecodeBase64(certificate.Files.CaCertificatePemBase64, "caCertificatePemBase64", errors);
        TryDecodeBase64(certificate.Files.CaKeyPemBase64, "caKeyPemBase64", errors);
        TryDecodeBase64(certificate.Files.CaPfxBase64, "caPfxBase64", errors);

        return errors;
    }

    private static void TryDecodeBase64(string? value, string name, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(value)) return;

        try
        {
            _ = Convert.FromBase64String(value);
        }
        catch (FormatException)
        {
            errors.Add($"{name} is not valid base64.");
        }
    }

    private static StoredCertificateConfig ToStoredCertificateConfig(ProvisionedCertificateEntry certificate)
    {
        return new StoredCertificateConfig
        {
            Id = certificate.Id,
            Mode = certificate.Mode,
            PfxPassword = certificate.PfxPassword,
            PfxPasswordEnvVar = certificate.PfxPasswordEnvVar,
            Thumbprint = certificate.Thumbprint,
            StoreName = certificate.StoreName,
            StoreLocation = certificate.StoreLocation,
            IssuerEnabled = certificate.IssuerEnabled ?? true,
            IssuerMatchDomains = certificate.IssuerMatchDomains
                .Select(x => x?.Trim().ToLowerInvariant() ?? string.Empty)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList(),
            IssuerCaSource = certificate.IssuerCaSource ?? InferIssuerSource(certificate),
            IssuerCaCertPath = certificate.CaCertificatePath,
            IssuerCaKeyPath = certificate.CaPrivateKeyPath,
            IssuerCaPfxPath = certificate.CaPfxPath,
            IssuerCaPfxPassword = certificate.CaPfxPassword,
            IssuerCaPfxPasswordEnvVar = certificate.CaPfxPasswordEnvVar,
            IssuerCaThumbprint = certificate.CaStoreThumbprint,
            IssuerCaStoreName = string.IsNullOrWhiteSpace(certificate.CaStoreName) ? "My" : certificate.CaStoreName,
            IssuerCaStoreLocation = string.IsNullOrWhiteSpace(certificate.CaStoreLocation)
                ? "LocalMachine"
                : certificate.CaStoreLocation
        };
    }

    private static IssuerCaSourceMode InferIssuerSource(ProvisionedCertificateEntry certificate)
    {
        if (!string.IsNullOrWhiteSpace(certificate.Files.CaPfxBase64)) return IssuerCaSourceMode.UploadPfx;

        if (!string.IsNullOrWhiteSpace(certificate.Files.CaCertificatePemBase64) ||
            !string.IsNullOrWhiteSpace(certificate.Files.CaKeyPemBase64))
            return IssuerCaSourceMode.UploadPem;

        if (!string.IsNullOrWhiteSpace(certificate.CaPfxPath)) return IssuerCaSourceMode.PathPfx;

        if (!string.IsNullOrWhiteSpace(certificate.CaStoreThumbprint)) return IssuerCaSourceMode.StoreThumbprint;

        return IssuerCaSourceMode.PathPem;
    }

    private static Dictionary<string, byte[]> DecodeFiles(ProvisionedCertificateEntry certificate)
    {
        var files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);

        AddDecodedFile(certificate.Files.CertificatePemBase64, "certificate.pem", files);
        AddDecodedFile(certificate.Files.KeyPemBase64, "key.pem", files);
        AddDecodedFile(certificate.Files.IntermediatePemBase64, "intermediate.pem", files);
        AddDecodedFile(certificate.Files.PfxBase64, "certificate.pfx", files);
        AddDecodedFile(certificate.Files.CaCertificatePemBase64, "ca-certificate.pem", files);
        AddDecodedFile(certificate.Files.CaKeyPemBase64, "ca-key.pem", files);
        AddDecodedFile(certificate.Files.CaPfxBase64, "ca-certificate.pfx", files);

        return files;
    }

    private static void AddDecodedFile(string? value, string fileName, IDictionary<string, byte[]> files)
    {
        if (string.IsNullOrWhiteSpace(value)) return;

        files[fileName] = Convert.FromBase64String(value);
    }

    private async Task<string> LoadFromFileAsync(string configuredPath, CancellationToken cancellationToken)
    {
        var path = Path.IsPathRooted(configuredPath)
            ? configuredPath
            : Path.Combine(environment.ContentRootPath, configuredPath);

        if (!File.Exists(path))
        {
            logger.LogError("Provisioning file '{ProvisioningFilePath}' was not found.", path);
            return string.Empty;
        }

        return await File.ReadAllTextAsync(path, cancellationToken);
    }

    private async Task<string> LoadFromUrlAsync(string url, int timeoutSeconds, CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, timeoutSeconds)));

        var client = httpClientFactory.CreateClient();
        using var response = await client.GetAsync(url, timeoutCts.Token);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(timeoutCts.Token);
    }

    private async Task WaitForApplicationStartedAsync(CancellationToken cancellationToken)
    {
        if (applicationLifetime.ApplicationStarted.IsCancellationRequested) return;

        var startedTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = applicationLifetime.ApplicationStarted.Register(() => startedTcs.TrySetResult());
        using var cancellation = cancellationToken.Register(() => startedTcs.TrySetCanceled(cancellationToken));
        await startedTcs.Task;
    }

    private static string ExpandTemplates(string json, IHostTemplateValueProvider templateValueProvider)
    {
        JsonNode? root;
        try
        {
            root = JsonNode.Parse(json);
        }
        catch
        {
            return json;
        }

        if (root is null) return json;

        ReplaceStringValuesRecursive(root, templateValueProvider);
        return root.ToJsonString();
    }

    private static void ReplaceStringValuesRecursive(JsonNode node, IHostTemplateValueProvider templateValueProvider)
    {
        if (node is JsonObject obj)
        {
            foreach (var property in obj.ToList())
            {
                if (property.Value is null) continue;

                if (property.Value is JsonValue value && value.TryGetValue<string>(out var stringValue) &&
                    stringValue is not null)
                {
                    obj[property.Key] = templateValueProvider.ReplacePlaceholders(stringValue);
                    continue;
                }

                ReplaceStringValuesRecursive(property.Value, templateValueProvider);
            }

            return;
        }

        if (node is JsonArray array)
            for (var i = 0; i < array.Count; i++)
            {
                var item = array[i];
                if (item is null) continue;

                if (item is JsonValue value && value.TryGetValue<string>(out var stringValue) &&
                    stringValue is not null)
                {
                    array[i] = templateValueProvider.ReplacePlaceholders(stringValue);
                    continue;
                }

                ReplaceStringValuesRecursive(item, templateValueProvider);
            }
    }

    public async Task RunNowAsync(CancellationToken cancellationToken, bool overwriteExisting = false)
    {
        await _runLock.WaitAsync(cancellationToken);
        try
        {
            var config = settings.Value;
            if (!config.Enabled)
            {
                logger.LogInformation("Provisioning run skipped because Provisioning:Enabled is false.");
                return;
            }

            await RunProvisioningAsync(config, overwriteExisting, cancellationToken);
        }
        finally
        {
            _runLock.Release();
        }
    }

    private List<string> ValidateProvisionedAuthProvider(ProvisionedAuthProviderEntry provider)
    {
        var errors = new List<string>();
        var id = (provider.Id ?? string.Empty).Trim();
        if (!Regex.IsMatch(id, "^[a-z0-9]+(?:-[a-z0-9]+)*$", RegexOptions.CultureInvariant))
            errors.Add("id must be lowercase kebab-case.");

        if (provider.Type == AuthProviderType.Ldap)
        {
            if (string.IsNullOrWhiteSpace(provider.Ldap.Server)) errors.Add("ldap.server is required.");

            if (string.IsNullOrWhiteSpace(provider.Ldap.BaseDn)) errors.Add("ldap.baseDn is required.");
        }

        if (provider.Type == AuthProviderType.Oidc)
        {
            if (string.IsNullOrWhiteSpace(NormalizeOidcDiscoveryEndpoint(provider.Oidc.MetadataUrl,
                    provider.Oidc.Authority))) errors.Add("oidc.metadataUrl is required.");

            if (string.IsNullOrWhiteSpace(provider.Oidc.ClientId)) errors.Add("oidc.clientId is required.");
        }

        return errors;
    }

    private AuthProviderConfig ToStoredAuthProvider(ProvisionedAuthProviderEntry provider)
    {
        var normalizedOidcMetadataUrl =
            NormalizeOidcDiscoveryEndpoint(provider.Oidc.MetadataUrl, provider.Oidc.Authority);

        return new AuthProviderConfig
        {
            Id = (provider.Id ?? string.Empty).Trim(),
            DisplayName = string.IsNullOrWhiteSpace(provider.DisplayName)
                ? (provider.Id ?? string.Empty).Trim()
                : provider.DisplayName.Trim(),
            Enabled = provider.Enabled,
            AllowAutoAccess = provider.AllowAutoAccess,
            Type = provider.Type,
            Ldap = new LdapAuthProviderConfig
            {
                Server = provider.Ldap.Server?.Trim() ?? string.Empty,
                Port = provider.Ldap.Port <= 0 ? 389 : provider.Ldap.Port,
                UseSsl = provider.Ldap.UseSsl,
                BindDn = provider.Ldap.BindDn?.Trim() ?? string.Empty,
                EncryptedBindPassword = string.IsNullOrWhiteSpace(provider.Ldap.BindPassword)
                    ? string.Empty
                    : configEncryptionService.EncryptString(provider.Ldap.BindPassword),
                BaseDn = provider.Ldap.BaseDn?.Trim() ?? string.Empty,
                UserFilter = string.IsNullOrWhiteSpace(provider.Ldap.UserFilter)
                    ? "(mail={email})"
                    : provider.Ldap.UserFilter.Trim(),
                EmailAttribute = string.IsNullOrWhiteSpace(provider.Ldap.EmailAttribute)
                    ? "mail"
                    : provider.Ldap.EmailAttribute.Trim(),
                FullNameAttribute = string.IsNullOrWhiteSpace(provider.Ldap.FullNameAttribute)
                    ? "displayName"
                    : provider.Ldap.FullNameAttribute.Trim()
            },
            Oidc = new OidcAuthProviderConfig
            {
                Authority = string.Empty,
                MetadataUrl = normalizedOidcMetadataUrl,
                ClientId = provider.Oidc.ClientId?.Trim() ?? string.Empty,
                EncryptedClientSecret = string.IsNullOrWhiteSpace(provider.Oidc.ClientSecret)
                    ? string.Empty
                    : configEncryptionService.EncryptString(provider.Oidc.ClientSecret),
                Scopes = string.IsNullOrWhiteSpace(provider.Oidc.Scopes)
                    ? "openid profile email"
                    : provider.Oidc.Scopes.Trim(),
                EmailClaim = string.IsNullOrWhiteSpace(provider.Oidc.EmailClaim)
                    ? "email"
                    : provider.Oidc.EmailClaim.Trim(),
                NameClaim =
                    string.IsNullOrWhiteSpace(provider.Oidc.NameClaim) ? "name" : provider.Oidc.NameClaim.Trim(),
                SubjectClaim = string.IsNullOrWhiteSpace(provider.Oidc.SubjectClaim)
                    ? "sub"
                    : provider.Oidc.SubjectClaim.Trim(),
                UsePkce = provider.Oidc.UsePkce
            }
        };
    }

    private static string NormalizeOidcDiscoveryEndpoint(string? metadataUrl, string? authority)
    {
        var normalizedMetadataUrl = metadataUrl?.Trim() ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(normalizedMetadataUrl)) return normalizedMetadataUrl;

        var normalizedAuthority = authority?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalizedAuthority)) return string.Empty;

        return normalizedAuthority.TrimEnd('/') + "/.well-known/openid-configuration";
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }

    private static string NormalizeSetId(string? value)
    {
        return (value ?? string.Empty).Trim();
    }

    private static List<ContainerKeyValuePair> NormalizePairs(
        IReadOnlyList<ContainerKeyValuePair>? source,
        bool disallowQuickProxyInternalKeys,
        string fieldName,
        out string? error)
    {
        error = null;
        var result = new List<ContainerKeyValuePair>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var pair in source ?? [])
        {
            var key = (pair.Key ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(key))
            {
                error = $"{fieldName} must not contain empty keys.";
                return [];
            }

            if (disallowQuickProxyInternalKeys &&
                key.StartsWith("quickproxy.internal.", StringComparison.OrdinalIgnoreCase))
            {
                error = $"{fieldName} key '{key}' is reserved.";
                return [];
            }

            if (!seen.Add(key))
            {
                error = $"{fieldName} contains duplicate key '{key}'.";
                return [];
            }

            result.Add(new ContainerKeyValuePair
            {
                Key = key,
                Value = pair.Value ?? string.Empty
            });
        }

        return result;
    }

    private static List<ContainerMountBindingRequest> NormalizeMountBindings(
        IReadOnlyList<ContainerMountBindingRequest>? source,
        out string? error)
    {
        error = null;
        var result = new List<ContainerMountBindingRequest>();
        var seenContainerPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var binding in source ?? [])
        {
            var hostPath = (binding.HostPath ?? string.Empty).Trim();
            var containerPath = (binding.ContainerPath ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(hostPath))
            {
                error = "mountBindings must not contain empty hostPath values.";
                return [];
            }

            if (string.IsNullOrWhiteSpace(containerPath))
            {
                error = "mountBindings must not contain empty containerPath values.";
                return [];
            }

            if (!seenContainerPaths.Add(containerPath))
            {
                error = $"mountBindings contains duplicate containerPath '{containerPath}'.";
                return [];
            }

            result.Add(new ContainerMountBindingRequest
            {
                HostPath = hostPath,
                ContainerPath = containerPath,
                ReadOnly = binding.ReadOnly
            });
        }

        return result;
    }

    private static List<ContainerNetworkAliasRequest> NormalizeNetworkAliases(
        IReadOnlyList<ContainerNetworkAliasRequest>? source,
        out string? error)
    {
        error = null;
        var result = new List<ContainerNetworkAliasRequest>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var alias in source ?? [])
        {
            var network = (alias.Network ?? string.Empty).Trim();
            var value = (alias.Alias ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(network) || string.IsNullOrWhiteSpace(value)) continue;

            var dedupeKey = $"{network}\u001f{value}";
            if (!seen.Add(dedupeKey))
            {
                error = $"networkAliases contains duplicate alias '{value}' for network '{network}'.";
                return [];
            }

            result.Add(new ContainerNetworkAliasRequest
            {
                Network = network,
                Alias = value
            });
        }

        return result;
    }

    private static List<ContainerHostMappingRequest> NormalizeHostMappings(
        IReadOnlyList<ContainerHostMappingRequest>? source,
        out string? error)
    {
        error = null;
        var result = new List<ContainerHostMappingRequest>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var mapping in source ?? [])
        {
            var hostname = (mapping.Hostname ?? string.Empty).Trim();
            var address = (mapping.Address ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(hostname) || string.IsNullOrWhiteSpace(address)) continue;

            if (!seen.Add(hostname))
            {
                error = $"hostMappings contains duplicate hostname '{hostname}'.";
                return [];
            }

            result.Add(new ContainerHostMappingRequest
            {
                Hostname = hostname,
                Address = address
            });
        }

        return result;
    }
}