using QuickProxy.Proxy.Models;

namespace QuickProxy.Proxy.Runtime;

public interface IIssuedCertificateService
{
    string? EnsureForHost(ProxyHostConfig host);
    string? EnsureForHost(ProxyHostConfig host, string? issuerCertificateId);
    string? EnsureForHostName(string hostName);
    string? EnsureForDomainTranslation(DomainTranslationRule rule);
    bool DeleteForHost(string hostId);
    bool DeleteForDomainTranslation(string ruleId);
}