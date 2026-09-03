namespace QuickProxy.Proxy.Containers;

public sealed class ContainerPublishedPortRequest
{
    public int ContainerPort { get; set; }
    public int HostPort { get; set; }
    public string Protocol { get; set; } = "tcp";
    public string? HostIp { get; set; }
}