using QuickProxy.Proxy.Models;

namespace QuickProxy.Proxy.Storage;

public interface IDomainTranslationStore
{
    string DataDirectory { get; }
    IReadOnlyList<DomainTranslationRule> List();
    DomainTranslationRule? Get(string id);
    bool Exists(string id);
    void Upsert(DomainTranslationRule rule);
    bool Delete(string id);
}