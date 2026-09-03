using QuickProxy.Proxy.Models;

namespace QuickProxy.Proxy.Storage;

public interface IAuthProviderStore
{
    IReadOnlyList<AuthProviderConfig> List();
    AuthProviderConfig? Get(string id);
    bool Exists(string id);
    void Upsert(AuthProviderConfig provider);
    bool Delete(string id);
}