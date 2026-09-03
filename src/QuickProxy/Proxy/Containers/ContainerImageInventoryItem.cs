namespace QuickProxy.Proxy.Containers;

public sealed class ContainerImageInventoryItem
{
    public string Id { get; set; } = string.Empty;
    public List<string> RepoTags { get; set; } = [];
    public List<string> RepoDigests { get; set; } = [];
    public DateTimeOffset CreatedUtc { get; set; }
    public long SizeBytes { get; set; }
    public long SharedSizeBytes { get; set; }
    public long VirtualSizeBytes { get; set; }
    public int Containers { get; set; }
    public Dictionary<string, string> Labels { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}