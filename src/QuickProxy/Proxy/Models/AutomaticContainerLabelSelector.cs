namespace QuickProxy.Proxy.Models;

public sealed class AutomaticContainerLabelSelector
{
    public string Key { get; set; } = string.Empty;
    public string? ValuePattern { get; set; }
    public List<string> ValuePatterns { get; set; } = [];
}