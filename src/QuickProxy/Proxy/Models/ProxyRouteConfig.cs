namespace QuickProxy.Proxy.Models;

public sealed class ProxyRouteConfig
{
    public string Path { get; set; } = "/";
    public ProxyRouteRewriteMode RewriteMode { get; set; } = ProxyRouteRewriteMode.Preserve;
    public string? RewriteTargetPath { get; set; }
    public bool PreserveOriginalHostHeader { get; set; } = true;
    public bool SendForwardedHeaders { get; set; } = true;
    public bool IgnoreBadCertificates { get; set; }
    public ProxyHostUpstreamMode UpstreamMode { get; set; } = ProxyHostUpstreamMode.Manual;
    public UpstreamTarget Upstream { get; set; } = new();
    public ContainerUpstreamTarget Container { get; set; } = new();
}