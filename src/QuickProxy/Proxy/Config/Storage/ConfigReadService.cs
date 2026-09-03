using System.Net;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Text.Json.Serialization;
using QuickProxy.Configuration;
using QuickProxy.Proxy.Config.Models;
using QuickProxy.Proxy.Runtime;
using QuickProxy.Shared.Web;

namespace QuickProxy.Proxy.Config.Storage;

public interface IConfigReadService
{
    Task<IReadOnlyList<MergedConfigEntry>> ListAsync(string? prefix = null,
        CancellationToken cancellationToken = default);

    Task<MergedConfigEntry?> GetAsync(string key, CancellationToken cancellationToken = default);
}

internal sealed class ConfigReadService(
    ILocalConfigStore localStore,
    IRemoteConfigStore remoteStore) : IConfigReadService
{
    public async Task<IReadOnlyList<MergedConfigEntry>> ListAsync(string? prefix = null,
        CancellationToken cancellationToken = default)
    {
        var normalizedPrefix = ConfigKeyNormalizer.NormalizePrefix(prefix);
        var localEntries = localStore.List(normalizedPrefix);
        var remoteEntries = await remoteStore.ListAsync(normalizedPrefix, cancellationToken);

        var merged = new Dictionary<string, MergedConfigEntry>(StringComparer.OrdinalIgnoreCase);

        foreach (var remoteEntry in remoteEntries)
        {
            var normalizedKey = ConfigKeyNormalizer.NormalizeKey(remoteEntry.Key);
            if (string.IsNullOrWhiteSpace(normalizedKey)) continue;

            merged[normalizedKey] = new MergedConfigEntry
            {
                Key = normalizedKey,
                Value = remoteEntry.Value,
                BinaryBase64 = remoteEntry.BinaryBase64,
                EncryptedValue = remoteEntry.EncryptedValue,
                EncryptedBinaryBase64 = remoteEntry.EncryptedBinaryBase64,
                EncryptedLabels = remoteEntry.EncryptedLabels,
                MediaType = remoteEntry.MediaType,
                EntryType = remoteEntry.EntryType,
                PayloadKind = remoteEntry.PayloadKind,
                Labels = remoteEntry.Labels,
                UpdatedAtUtc = remoteEntry.UpdatedAtUtc,
                UpdatedBy = remoteEntry.UpdatedBy,
                Source = "remote",
                ReadOnly = true
            };
        }

        foreach (var localEntry in localEntries)
        {
            var normalizedKey = ConfigKeyNormalizer.NormalizeKey(localEntry.Key);
            if (string.IsNullOrWhiteSpace(normalizedKey)) continue;

            var hasRemoteMatch = merged.ContainsKey(normalizedKey);
            merged[normalizedKey] = new MergedConfigEntry
            {
                Key = normalizedKey,
                Value = localEntry.Value,
                BinaryBase64 = localEntry.BinaryBase64,
                EncryptedValue = localEntry.EncryptedValue,
                EncryptedBinaryBase64 = localEntry.EncryptedBinaryBase64,
                EncryptedLabels = localEntry.EncryptedLabels,
                MediaType = localEntry.MediaType,
                EntryType = localEntry.EntryType,
                PayloadKind = localEntry.PayloadKind,
                Labels = localEntry.Labels,
                UpdatedAtUtc = localEntry.UpdatedAtUtc,
                UpdatedBy = localEntry.UpdatedBy,
                Source = "local",
                ReadOnly = false,
                HasLocalOverride = hasRemoteMatch
            };
        }

        return merged.Values
            .OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public async Task<MergedConfigEntry?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        var normalizedKey = ConfigKeyNormalizer.NormalizeKey(key);
        if (string.IsNullOrWhiteSpace(normalizedKey)) return null;

        var localEntry = localStore.Get(normalizedKey);
        var remoteEntry = await remoteStore.GetAsync(normalizedKey, cancellationToken);

        if (localEntry is not null)
            return new MergedConfigEntry
            {
                Key = normalizedKey,
                Value = localEntry.Value,
                BinaryBase64 = localEntry.BinaryBase64,
                EncryptedValue = localEntry.EncryptedValue,
                EncryptedBinaryBase64 = localEntry.EncryptedBinaryBase64,
                EncryptedLabels = localEntry.EncryptedLabels,
                MediaType = localEntry.MediaType,
                EntryType = localEntry.EntryType,
                PayloadKind = localEntry.PayloadKind,
                Labels = localEntry.Labels,
                UpdatedAtUtc = localEntry.UpdatedAtUtc,
                UpdatedBy = localEntry.UpdatedBy,
                Source = "local",
                ReadOnly = false,
                HasLocalOverride = remoteEntry is not null
            };

        if (remoteEntry is null) return null;

        return new MergedConfigEntry
        {
            Key = normalizedKey,
            Value = remoteEntry.Value,
            BinaryBase64 = remoteEntry.BinaryBase64,
            EncryptedValue = remoteEntry.EncryptedValue,
            EncryptedBinaryBase64 = remoteEntry.EncryptedBinaryBase64,
            EncryptedLabels = remoteEntry.EncryptedLabels,
            MediaType = remoteEntry.MediaType,
            EntryType = remoteEntry.EntryType,
            PayloadKind = remoteEntry.PayloadKind,
            Labels = remoteEntry.Labels,
            UpdatedAtUtc = remoteEntry.UpdatedAtUtc,
            UpdatedBy = remoteEntry.UpdatedBy,
            Source = "remote",
            ReadOnly = true
        };
    }
}

internal interface IRemoteConfigStore
{
    Task<IReadOnlyList<ConfigEntry>> ListAsync(string? prefix, CancellationToken cancellationToken);
    Task<ConfigEntry?> GetAsync(string key, CancellationToken cancellationToken);
}

internal sealed class RemoteConfigStore(
    AppModulesConfiguration settings,
    ILogger<RemoteConfigStore> logger) : IRemoteConfigStore
{
    private static readonly JsonSerializerOptions RemoteJsonOptions = CreateRemoteJsonOptions();
    private readonly RemoteConfigStoreSettings _settings = settings.Config.Remote;

    public async Task<IReadOnlyList<ConfigEntry>> ListAsync(string? prefix, CancellationToken cancellationToken)
    {
        if (!IsEnabled()) return [];

        try
        {
            using var client = CreateClient();
            var response = await client.GetAsync(BuildListUri(prefix), cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Remote config list failed with status {StatusCode}.", response.StatusCode);
                return [];
            }

            var payload =
                await response.Content.ReadFromJsonAsync<List<RemotePublicConfigEntry>>(RemoteJsonOptions,
                    cancellationToken) ?? [];
            return payload
                .Select(ToModel)
                .Where(x => !string.IsNullOrWhiteSpace(x.Key))
                .OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch (Exception ex)
        {
            if (ex is OperationCanceledException && cancellationToken.IsCancellationRequested) throw;

            logger.LogWarning(ex, "Remote config list failed. Falling back to local-only results.");
            return [];
        }
    }

    public async Task<ConfigEntry?> GetAsync(string key, CancellationToken cancellationToken)
    {
        if (!IsEnabled()) return null;

        var normalizedKey = ConfigKeyNormalizer.NormalizeKey(key);
        if (string.IsNullOrWhiteSpace(normalizedKey)) return null;

        try
        {
            using var client = CreateClient();
            var response = await client.GetAsync(BuildItemUri(normalizedKey), cancellationToken);
            if (response.StatusCode == HttpStatusCode.NotFound) return null;

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Remote config get for '{Key}' failed with status {StatusCode}.", normalizedKey,
                    response.StatusCode);
                return null;
            }

            var payload =
                await response.Content.ReadFromJsonAsync<RemotePublicConfigEntry>(RemoteJsonOptions, cancellationToken);
            return payload is null ? null : ToModel(payload);
        }
        catch (Exception ex)
        {
            if (ex is OperationCanceledException && cancellationToken.IsCancellationRequested) throw;

            logger.LogWarning(ex, "Remote config get for '{Key}' failed. Falling back to local-only results.",
                normalizedKey);
            return null;
        }
    }

    private bool IsEnabled()
    {
        return _settings.Enabled && !string.IsNullOrWhiteSpace(_settings.Url);
    }

    private HttpClient CreateClient()
    {
        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (_, certificate, _, sslPolicyErrors) =>
            {
                if (sslPolicyErrors == SslPolicyErrors.None) return true;

                if (certificate is not X509Certificate2 x509Certificate)
                    x509Certificate = certificate is null ? null! : new X509Certificate2(certificate);

                return AdminCertificateAccessor.IsTrustedFallbackCertificate(x509Certificate, sslPolicyErrors);
            }
        };

        var client = new HttpClient(handler, true);
        client.Timeout = TimeSpan.FromSeconds(Math.Max(1, _settings.TimeoutSeconds));
        return client;
    }

    private Uri BuildListUri(string? prefix)
    {
        var uriBuilder = new UriBuilder(GetBaseUri());
        var normalizedPrefix = ConfigKeyNormalizer.NormalizePrefix(prefix);
        var queryParts = new List<string>();
        if (!string.IsNullOrWhiteSpace(normalizedPrefix))
            queryParts.Add($"prefix={Uri.EscapeDataString(normalizedPrefix)}");

        uriBuilder.Query = string.Join("&", queryParts);

        return uriBuilder.Uri;
    }

    private Uri BuildItemUri(string key)
    {
        var encodedKey = string.Join("/",
            ConfigKeyNormalizer.NormalizeKey(key)
                .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(Uri.EscapeDataString));

        return new Uri(GetBaseUri(), encodedKey);
    }

    private Uri GetBaseUri()
    {
        var configured = _settings.Url.Trim();
        if (!Uri.TryCreate(configured, UriKind.Absolute, out var uri))
            throw new InvalidOperationException("Config:Remote:Url must be an absolute URL.");

        var path = uri.AbsolutePath.TrimEnd('/');
        if (path.EndsWith($"{InternalApiPaths.Root}/config", StringComparison.OrdinalIgnoreCase))
        {
            var builder = new UriBuilder(uri)
            {
                Path = $"{path[..^"/config".Length]}/config-export".Replace("//", "/")
            };
            uri = builder.Uri;
        }
        else if (!path.EndsWith($"{InternalApiPaths.Root}/config-export", StringComparison.OrdinalIgnoreCase))
        {
            var builder = new UriBuilder(uri)
            {
                Path = $"{path}{InternalApiPaths.Root}/config-export".Replace("//", "/")
            };
            uri = builder.Uri;
        }

        var normalizedPath = uri.AbsolutePath.EndsWith("/", StringComparison.Ordinal)
            ? uri.AbsolutePath
            : $"{uri.AbsolutePath}/";

        return new UriBuilder(uri) { Path = normalizedPath, Query = string.Empty }.Uri;
    }

    private static ConfigEntry ToModel(RemotePublicConfigEntry entry)
    {
        return new ConfigEntry
        {
            Key = ConfigKeyNormalizer.NormalizeKey(entry.Key),
            Value = entry.Value ?? string.Empty,
            BinaryBase64 = entry.BinaryBase64,
            EncryptedValue = entry.EncryptedValue,
            EncryptedBinaryBase64 = entry.EncryptedBinaryBase64,
            EncryptedLabels = entry.EncryptedLabels,
            MediaType = entry.MediaType,
            EntryType = entry.EntryType,
            PayloadKind = entry.PayloadKind,
            Labels = entry.Labels ?? [],
            UpdatedAtUtc = entry.UpdatedAtUtc,
            UpdatedBy = null
        };
    }

    private static JsonSerializerOptions CreateRemoteJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }

    private sealed record RemotePublicConfigEntry(
        string Key,
        string Name,
        string? Value,
        string? BinaryBase64,
        string? EncryptedValue,
        string? EncryptedBinaryBase64,
        string? EncryptedLabels,
        string? MediaType,
        ConfigEntryType EntryType,
        ConfigPayloadKind PayloadKind,
        List<ConfigLabel>? Labels,
        DateTimeOffset UpdatedAtUtc);
}