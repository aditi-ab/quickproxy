namespace QuickProxy.Proxy.Containers;

public sealed class ContainerDefaultsSet
{
    public string Id { get; set; } = string.Empty;
    public List<ContainerKeyValuePair> Labels { get; set; } = [];
    public List<ContainerKeyValuePair> EnvVars { get; set; } = [];
    public List<ContainerMountBindingRequest> MountBindings { get; set; } = [];
    public List<ContainerHostMappingRequest> HostMappings { get; set; } = [];
    public List<ContainerNetworkAliasRequest> NetworkAliases { get; set; } = [];
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}