namespace QuickProxy.Proxy.Containers;

public sealed class ContainerMountBindingRequest
{
    public string HostPath { get; set; } = string.Empty;
    public string ContainerPath { get; set; } = string.Empty;
    public bool ReadOnly { get; set; }
}