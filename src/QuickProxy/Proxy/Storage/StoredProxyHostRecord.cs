namespace QuickProxy.Proxy.Storage;

public sealed class StoredProxyHostRecord
{
    public required string HostId { get; init; }
    public required string StorageLocation { get; init; }
    public required string Json { get; init; }
}