using QuickProxy.Proxy.Models;

namespace QuickProxy.Proxy.Runtime;

public interface IDomainTranslationRuntime
{
    IReadOnlyList<DomainTranslationRule> GetRules();
    DomainTranslationRule? GetRule(string id);
    DomainTranslationRule? MatchRule(string? hostHeader);
    string TranslateHost(string host, DomainTranslationRule rule);
    bool TryReload();
}