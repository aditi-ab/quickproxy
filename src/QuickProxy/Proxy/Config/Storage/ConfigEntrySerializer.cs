using System.Text.Json;
using QuickProxy.Proxy.Config.Models;

namespace QuickProxy.Proxy.Config.Storage;

internal static class ConfigEntrySerializer
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static string SerializeLabels(IReadOnlyList<ConfigLabel>? labels)
    {
        return JsonSerializer.Serialize(NormalizeLabels(labels), JsonOptions);
    }

    public static List<ConfigLabel> DeserializeLabels(string? labelsJson)
    {
        if (string.IsNullOrWhiteSpace(labelsJson)) return [];

        try
        {
            return NormalizeLabels(JsonSerializer.Deserialize<List<ConfigLabel>>(labelsJson, JsonOptions));
        }
        catch (JsonException)
        {
            return [];
        }
    }

    public static ConfigEntry Normalize(ConfigEntry entry)
    {
        var normalized = new ConfigEntry
        {
            Key = ConfigKeyNormalizer.NormalizeKey(entry.Key),
            EntryType = entry.EntryType,
            PayloadKind = entry.PayloadKind,
            MediaType = NormalizeOptional(entry.MediaType),
            UpdatedAtUtc = entry.UpdatedAtUtc,
            UpdatedBy = entry.UpdatedBy,
            Labels = NormalizeLabels(entry.Labels)
        };

        if (normalized.PayloadKind == ConfigPayloadKind.Binary)
        {
            normalized.BinaryBase64 = NormalizeBinaryBase64(entry.BinaryBase64);
            normalized.Value = string.Empty;
            normalized.EncryptedBinaryBase64 = NormalizeOptional(entry.EncryptedBinaryBase64);
            normalized.EncryptedValue = null;
            normalized.EncryptedLabels = NormalizeOptional(entry.EncryptedLabels);
            if (string.IsNullOrWhiteSpace(normalized.MediaType)) normalized.MediaType = "application/octet-stream";
        }
        else
        {
            normalized.Value = entry.Value ?? string.Empty;
            normalized.BinaryBase64 = null;
            normalized.EncryptedValue = NormalizeOptional(entry.EncryptedValue);
            normalized.EncryptedBinaryBase64 = null;
            normalized.EncryptedLabels = NormalizeOptional(entry.EncryptedLabels);
            if (string.IsNullOrWhiteSpace(normalized.MediaType)) normalized.MediaType = "text/plain";
        }

        return normalized;
    }

    public static List<ConfigLabel> NormalizeLabels(IReadOnlyList<ConfigLabel>? labels)
    {
        if (labels is null || labels.Count == 0) return [];

        return labels
            .Where(label => label is not null)
            .Select(label => new ConfigLabel
            {
                Key = (label.Key ?? string.Empty).Trim(),
                Value = label.Value ?? string.Empty
            })
            .Where(label => !string.IsNullOrWhiteSpace(label.Key))
            .ToList();
    }

    public static string NormalizeBinaryBase64(string? binaryBase64)
    {
        if (string.IsNullOrWhiteSpace(binaryBase64)) return string.Empty;

        return Convert.ToBase64String(Convert.FromBase64String(binaryBase64));
    }

    public static string? NormalizeOptional(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }
}