namespace QuickProxy.Proxy.Models;

public sealed class AdminProxyHostDto
{
    public string Id { get; set; } = string.Empty;
    public ProxyHostMode Mode { get; set; } = ProxyHostMode.Manual;
    public bool Enabled { get; set; } = true;
    public List<string> DomainNames { get; set; } = [];
    public AutomaticContainerProxyHostConfig AutomaticContainer { get; set; } = new();
    public bool ForceSsl { get; set; }
    public bool CacheAssets { get; set; }
    public bool Websockets { get; set; } = true;
    public string? CertificateId { get; set; }
    public List<ProxyRouteConfig> Routes { get; set; } = [];
    public TlsBindingConfig Tls { get; set; } = new();
    public ProxyHostRuntimeMetadata Runtime { get; set; } = new();
}