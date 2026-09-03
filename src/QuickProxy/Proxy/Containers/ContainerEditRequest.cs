using System.Text.Json.Serialization;

namespace QuickProxy.Proxy.Containers;

public sealed class ContainerEditRequest
{
    public string Name { get; set; } = string.Empty;
    public string Image { get; set; } = string.Empty;
    public List<ContainerKeyValuePair> Labels { get; set; } = [];
    public List<ContainerKeyValuePair> EnvVars { get; set; } = [];
    public List<ContainerMountBindingRequest> MountBindings { get; set; } = [];
    public List<ContainerHostMappingRequest> HostMappings { get; set; } = [];
    public List<ContainerNetworkAliasRequest> NetworkAliases { get; set; } = [];
    public string RestartPolicy { get; set; } = "no";
    public List<ContainerPublishedPortRequest> PublishedPorts { get; set; } = [];

    [JsonIgnore] public List<string> InternalDnsServers { get; set; } = [];

    [JsonIgnore] public List<string> InternalDnsServersToRemove { get; set; } = [];
}