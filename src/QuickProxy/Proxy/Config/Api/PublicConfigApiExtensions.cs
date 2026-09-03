using QuickProxy.Proxy.Config.Models;
using QuickProxy.Proxy.Config.Storage;
using QuickProxy.Proxy.Runtime;
using QuickProxy.Shared.Web;

namespace QuickProxy.Proxy.Config.Api;

public static class PublicConfigApiExtensions
{
    public static IEndpointRouteBuilder MapPublicConfigApi(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup($"{InternalApiPaths.Root}/config");
        var exportGroup = app.MapGroup($"{InternalApiPaths.Root}/config-export");

        group.MapGet("",
            async (HttpRequest request, string? prefix, IConfigReadService store,
                IConfigEncryptionService encryptionService, CancellationToken cancellationToken) =>
            {
                var normalizedPrefix = ConfigKeyNormalizer.NormalizePrefix(prefix);
                var entries = string.IsNullOrWhiteSpace(normalizedPrefix)
                    ? await store.ListAsync(cancellationToken: cancellationToken)
                    : await store.ListAsync(normalizedPrefix, cancellationToken);

                var decryptRequested = request.Query.ContainsKey("decrypt");
                return Results.Ok(entries.Select(entry =>
                    ToPublicConfigMetadata(entry, decryptRequested, encryptionService)));
            });

        group.MapGet("/{*key}", async (HttpRequest request, string key, string? raw, string? recurse, string? template,
            IConfigReadService store, IConfigEncryptionService encryptionService,
            IHostTemplateValueProvider templateValueProvider, CancellationToken cancellationToken) =>
        {
            var rawRequested = IsQueryFlagEnabled(raw);
            var recurseRequested = IsQueryFlagEnabled(recurse);
            var templateRequested = IsQueryFlagEnabled(template);
            var decryptRequested = request.Query.ContainsKey("decrypt");
            var normalizedKey = ConfigKeyNormalizer.NormalizeKey(key);
            if (string.IsNullOrWhiteSpace(normalizedKey))
                return Results.BadRequest(new { code = "validation_error", message = "key is required." });

            if (rawRequested && recurseRequested)
                return Results.BadRequest(new
                    { code = "validation_error", message = "raw and recurse cannot be combined." });

            if (recurseRequested)
            {
                var prefix = ConfigKeyNormalizer.NormalizePrefix(normalizedKey);
                var entries = await store.ListAsync(cancellationToken: cancellationToken);
                var matchingEntries = entries
                    .Where(x => string.Equals(x.Key, normalizedKey, StringComparison.OrdinalIgnoreCase) ||
                                x.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    .ToArray();

                return Results.Ok(matchingEntries.Select(entry =>
                    ToPublicConfigMetadata(entry, decryptRequested, encryptionService)));
            }

            var entry = await store.GetAsync(normalizedKey, cancellationToken);
            if (entry is null)
                return Results.NotFound(
                    new { code = "not_found", message = $"Config '{normalizedKey}' was not found." });

            if (rawRequested)
                return ToRawResult(entry, decryptRequested, templateRequested, encryptionService,
                    templateValueProvider);

            return Results.Ok(ToPublicConfigMetadata(entry, decryptRequested, encryptionService));
        });

        exportGroup.MapGet("",
            async (HttpRequest request, string? prefix, IConfigReadService store,
                IConfigEncryptionService encryptionService, CancellationToken cancellationToken) =>
            {
                var normalizedPrefix = ConfigKeyNormalizer.NormalizePrefix(prefix);
                var entries = string.IsNullOrWhiteSpace(normalizedPrefix)
                    ? await store.ListAsync(cancellationToken: cancellationToken)
                    : await store.ListAsync(normalizedPrefix, cancellationToken);

                var decryptRequested = request.Query.ContainsKey("decrypt");
                return Results.Ok(entries.Select(entry =>
                    ToPublicConfigEntry(entry, decryptRequested, encryptionService)));
            });

        exportGroup.MapGet("/{*key}",
            async (HttpRequest request, string key, string? recurse, IConfigReadService store,
                IConfigEncryptionService encryptionService, CancellationToken cancellationToken) =>
            {
                var recurseRequested = IsQueryFlagEnabled(recurse);
                var decryptRequested = request.Query.ContainsKey("decrypt");
                var normalizedKey = ConfigKeyNormalizer.NormalizeKey(key);
                if (string.IsNullOrWhiteSpace(normalizedKey))
                    return Results.BadRequest(new { code = "validation_error", message = "key is required." });

                if (recurseRequested)
                {
                    var prefix = ConfigKeyNormalizer.NormalizePrefix(normalizedKey);
                    var entries = await store.ListAsync(cancellationToken: cancellationToken);
                    var matchingEntries = entries
                        .Where(x => string.Equals(x.Key, normalizedKey, StringComparison.OrdinalIgnoreCase) ||
                                    x.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                        .ToArray();

                    return Results.Ok(matchingEntries.Select(entry =>
                        ToPublicConfigEntry(entry, decryptRequested, encryptionService)));
                }

                var entry = await store.GetAsync(normalizedKey, cancellationToken);
                if (entry is null)
                    return Results.NotFound(new
                        { code = "not_found", message = $"Config '{normalizedKey}' was not found." });

                return Results.Ok(ToPublicConfigEntry(entry, decryptRequested, encryptionService));
            });

        return app;
    }

    private static IResult ToRawResult(MergedConfigEntry entry, bool decryptRequested, bool templateRequested,
        IConfigEncryptionService encryptionService, IHostTemplateValueProvider templateValueProvider)
    {
        if (entry.EntryType == ConfigEntryType.Secret && !decryptRequested)
        {
            var encrypted = entry.PayloadKind == ConfigPayloadKind.Binary
                ? entry.EncryptedBinaryBase64 ?? string.Empty
                : entry.EncryptedValue ?? string.Empty;
            return Results.Text(encrypted, "text/plain; charset=utf-8");
        }

        if (entry.PayloadKind == ConfigPayloadKind.Binary)
        {
            var binaryBase64 = entry.EntryType == ConfigEntryType.Secret
                ? encryptionService.DecryptBinaryBase64(entry.EncryptedBinaryBase64 ?? string.Empty)
                : entry.BinaryBase64 ?? string.Empty;
            var bytes = string.IsNullOrWhiteSpace(binaryBase64) ? [] : Convert.FromBase64String(binaryBase64);
            return Results.File(bytes, entry.MediaType ?? "application/octet-stream", GetDownloadName(entry.Key));
        }

        var value = entry.EntryType == ConfigEntryType.Secret
            ? encryptionService.DecryptString(entry.EncryptedValue ?? string.Empty)
            : entry.Value ?? string.Empty;
        if (templateRequested) value = templateValueProvider.ReplaceKvPlaceholders(value);

        return Results.Text(value, $"{entry.MediaType ?? "text/plain"}; charset=utf-8");
    }

    private static PublicConfigEntry ToPublicConfigEntry(MergedConfigEntry entry, bool decryptRequested,
        IConfigEncryptionService encryptionService)
    {
        var labels = entry.EntryType == ConfigEntryType.Secret && decryptRequested
            ? encryptionService.DecryptLabels(entry.EncryptedLabels)
            : entry.Labels;

        if (entry.EntryType == ConfigEntryType.Secret && decryptRequested)
            return new PublicConfigEntry(
                entry.Key,
                GetLastSegment(entry.Key),
                entry.PayloadKind == ConfigPayloadKind.Text
                    ? encryptionService.DecryptString(entry.EncryptedValue ?? string.Empty)
                    : string.Empty,
                entry.PayloadKind == ConfigPayloadKind.Binary
                    ? encryptionService.DecryptBinaryBase64(entry.EncryptedBinaryBase64 ?? string.Empty)
                    : null,
                null,
                null,
                null,
                entry.MediaType,
                entry.EntryType,
                entry.PayloadKind,
                labels,
                true,
                entry.UpdatedAtUtc);

        return new PublicConfigEntry(
            entry.Key,
            GetLastSegment(entry.Key),
            entry.EntryType == ConfigEntryType.Secret ? string.Empty : entry.Value,
            entry.EntryType == ConfigEntryType.Secret ? null : entry.BinaryBase64,
            entry.EncryptedValue,
            entry.EncryptedBinaryBase64,
            entry.EncryptedLabels,
            entry.MediaType,
            entry.EntryType,
            entry.PayloadKind,
            labels,
            entry.EntryType != ConfigEntryType.Secret,
            entry.UpdatedAtUtc);
    }

    private static PublicConfigMetadataEntry ToPublicConfigMetadata(MergedConfigEntry entry, bool decryptRequested,
        IConfigEncryptionService encryptionService)
    {
        var labels = entry.EntryType == ConfigEntryType.Secret && decryptRequested
            ? encryptionService.DecryptLabels(entry.EncryptedLabels)
            : entry.Labels;

        return new PublicConfigMetadataEntry(
            entry.Key,
            GetLastSegment(entry.Key),
            entry.MediaType,
            entry.EntryType,
            entry.PayloadKind,
            labels,
            entry.EntryType != ConfigEntryType.Secret || decryptRequested,
            entry.UpdatedAtUtc);
    }

    private static string GetLastSegment(string key)
    {
        var normalized = ConfigKeyNormalizer.NormalizeKey(key);
        if (string.IsNullOrWhiteSpace(normalized)) return string.Empty;

        var index = normalized.LastIndexOf('/');
        return index < 0 ? normalized : normalized[(index + 1)..];
    }

    private static string GetDownloadName(string key)
    {
        var name = GetLastSegment(key);
        return string.IsNullOrWhiteSpace(name) ? "config.bin" : name;
    }

    private static bool IsQueryFlagEnabled(string? value)
    {
        if (value is null) return false;

        return string.IsNullOrWhiteSpace(value) ||
               value.Equals("true", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("1", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("yes", StringComparison.OrdinalIgnoreCase);
    }
}

public sealed record PublicConfigEntry(
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
    List<ConfigLabel> Labels,
    bool IsRevealed,
    DateTimeOffset UpdatedAtUtc);

public sealed record PublicConfigMetadataEntry(
    string Key,
    string Name,
    string? MediaType,
    ConfigEntryType EntryType,
    ConfigPayloadKind PayloadKind,
    List<ConfigLabel> Labels,
    bool IsRevealed,
    DateTimeOffset UpdatedAtUtc);