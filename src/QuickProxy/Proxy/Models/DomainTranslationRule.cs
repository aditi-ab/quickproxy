namespace QuickProxy.Proxy.Models;

public sealed class DomainTranslationRule
{
    public string Id { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public string SourceDomain { get; set; } = string.Empty;
    public string TargetDomain { get; set; } = string.Empty;
    public string? CertificateId { get; set; }
    public bool RewriteHostHeader { get; set; } = true;
}