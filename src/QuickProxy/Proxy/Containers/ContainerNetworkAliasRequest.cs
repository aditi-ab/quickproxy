namespace QuickProxy.Proxy.Containers;

public sealed class ContainerNetworkAliasRequest
{
    public string Network { get; set; } = string.Empty;
    public string Alias { get; set; } = string.Empty;
}