namespace QuickProxy.Proxy.Containers;

public sealed class ContainerHostMappingRequest
{
    public string Hostname { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
}