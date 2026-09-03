namespace QuickProxy.Proxy.Models;

public sealed class ContainerUpstreamTarget
{
    public string ContainerName { get; set; } = string.Empty;
    public string Scheme { get; set; } = "http";
    public int Port { get; set; }
    public ContainerPortResolutionMode PortResolutionMode { get; set; } = ContainerPortResolutionMode.Container;
    public string? NetworkName { get; set; }
}