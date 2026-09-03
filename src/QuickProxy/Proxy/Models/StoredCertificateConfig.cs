namespace QuickProxy.Proxy.Models;

public sealed class StoredCertificateConfig
{
    public string Id { get; set; } = string.Empty;
    public CertificateConfigMode Mode { get; set; } = CertificateConfigMode.Files;
    public string? PfxPassword { get; set; }
    public string? PfxPasswordEnvVar { get; set; }
    public string? Thumbprint { get; set; }
    public string StoreName { get; set; } = "My";
    public string StoreLocation { get; set; } = "LocalMachine";
    public List<string> IssuerMatchDomains { get; set; } = [];
    public bool IssuerEnabled { get; set; } = true;
    public IssuerCaSourceMode IssuerCaSource { get; set; } = IssuerCaSourceMode.UploadPem;
    public string? IssuerCaCertPath { get; set; }
    public string? IssuerCaKeyPath { get; set; }
    public string? IssuerCaPfxPath { get; set; }
    public string? IssuerCaPfxPassword { get; set; }
    public string? IssuerCaPfxPasswordEnvVar { get; set; }
    public string? IssuerCaThumbprint { get; set; }
    public string IssuerCaStoreName { get; set; } = "My";
    public string IssuerCaStoreLocation { get; set; } = "LocalMachine";

    public bool HasCertificateFile { get; set; }
    public bool HasKeyFile { get; set; }
    public bool HasIntermediateFile { get; set; }
    public bool HasPfxFile { get; set; }

    // Computed metadata returned by API for UI display.
    public List<string> DomainNames { get; set; } = [];
    public string Provider { get; set; } = string.Empty;
    public DateTimeOffset? ExpiresAtUtc { get; set; }
    public bool InUse { get; set; }
    public int InUseCount { get; set; }
}