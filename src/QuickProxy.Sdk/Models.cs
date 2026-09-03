using System.Net;

namespace QuickProxy.Sdk;

public enum QuickProxyConfigEntryType
{
    Data = 0,
    Secret = 1
}

public enum QuickProxyConfigPayloadKind
{
    Text = 0,
    Binary = 1
}

public sealed class QuickProxyConfigLabel
{
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}

public class QuickProxyConfigMetadata
{
    public string Key { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? MediaType { get; set; }
    public QuickProxyConfigEntryType EntryType { get; set; } = QuickProxyConfigEntryType.Data;
    public QuickProxyConfigPayloadKind PayloadKind { get; set; } = QuickProxyConfigPayloadKind.Text;
    public List<QuickProxyConfigLabel> Labels { get; set; } = [];
    public bool IsRevealed { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}

public sealed class QuickProxyConfigEntry : QuickProxyConfigMetadata
{
    public string? Value { get; set; }
    public string? BinaryBase64 { get; set; }
    public string? EncryptedValue { get; set; }
    public string? EncryptedBinaryBase64 { get; set; }
    public string? EncryptedLabels { get; set; }
}

public sealed class QuickProxyClientException : Exception
{
    public QuickProxyClientException(HttpStatusCode statusCode, string message)
        : base(message)
    {
        StatusCode = statusCode;
    }

    public HttpStatusCode StatusCode { get; }
}