namespace QuickProxy.Proxy.Containers;

public sealed class ContainerImageUpdateInfo
{
    public string Status { get; set; } = "unknown";
    public bool UpdateAvailable { get; set; }
    public string? Source { get; set; }
    public string? LocalDigest { get; set; }
    public string? RemoteDigest { get; set; }
    public string? Error { get; set; }
    public DateTimeOffset? CheckedAtUtc { get; set; }
    public DateTimeOffset? RemoteCreatedUtc { get; set; }
    public string? RemoteArchitecture { get; set; }
    public string? RemoteOs { get; set; }
    public Dictionary<string, string> RemoteLabels { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}