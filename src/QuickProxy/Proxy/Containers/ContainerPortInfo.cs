namespace QuickProxy.Proxy.Containers;

public sealed class ContainerPortInfo
{
    public int ContainerPort { get; set; }
    public string Protocol { get; set; } = "tcp";
    public bool IsExposed { get; set; }
    public List<int> PublishedPorts { get; set; } = [];
    public List<PublishedPortBinding> PublishedBindings { get; set; } = [];
}