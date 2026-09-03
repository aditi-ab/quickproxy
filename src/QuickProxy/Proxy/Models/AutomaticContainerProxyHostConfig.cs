namespace QuickProxy.Proxy.Models;

public sealed class AutomaticContainerProxyHostConfig
{
    public List<AutomaticContainerLabelSelector> LabelSelectors { get; set; } = [];
    public List<string> DomainTemplates { get; set; } = [];
}