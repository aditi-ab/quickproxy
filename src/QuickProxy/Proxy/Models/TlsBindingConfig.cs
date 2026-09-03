namespace QuickProxy.Proxy.Models;

public sealed class TlsBindingConfig
{
    public TlsBindingMode Mode { get; set; } = TlsBindingMode.None;
    public string? PfxPath { get; set; }
    public string? PfxPassword { get; set; }
    public string? PfxPasswordEnvVar { get; set; }
    public string? Thumbprint { get; set; }
    public string StoreName { get; set; } = "My";
    public string StoreLocation { get; set; } = "LocalMachine";
}