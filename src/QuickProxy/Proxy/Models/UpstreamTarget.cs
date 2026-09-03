namespace QuickProxy.Proxy.Models;

public sealed class UpstreamTarget
{
    public string Scheme { get; set; } = "http";
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; }
}