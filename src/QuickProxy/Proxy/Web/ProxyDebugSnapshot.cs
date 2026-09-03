namespace QuickProxy.Proxy.Web;

public sealed class ProxyDebugSnapshot
{
    public string? DestinationPrefix { get; set; }
    public string? OutboundRequestVersion { get; set; }
    public Dictionary<string, string[]>? OutboundRequestHeaders { get; set; }
    public string? UpstreamResponseVersion { get; set; }
    public Dictionary<string, string[]>? UpstreamResponseHeaders { get; set; }
}