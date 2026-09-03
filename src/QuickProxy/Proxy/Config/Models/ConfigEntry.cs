namespace QuickProxy.Proxy.Config.Models;

public sealed class ConfigEntry
{
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string? BinaryBase64 { get; set; }
    public string? EncryptedValue { get; set; }
    public string? EncryptedBinaryBase64 { get; set; }
    public string? EncryptedLabels { get; set; }
    public string? MediaType { get; set; }
    public ConfigEntryType EntryType { get; set; } = ConfigEntryType.Data;
    public ConfigPayloadKind PayloadKind { get; set; } = ConfigPayloadKind.Text;
    public List<ConfigLabel> Labels { get; set; } = [];
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public string? UpdatedBy { get; set; }
}