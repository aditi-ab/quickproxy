using QuickProxy.Proxy.Models;

namespace QuickProxy.Proxy.Storage;

public interface IProxyHostRepository
{
    string DataDirectory { get; }
    bool IsReservedFile(string hostId);
    IReadOnlyList<StoredProxyHostRecord> ReadAll();
    ProxyHostConfig? ReadById(string id);
    bool Exists(string id);
    void Write(ProxyHostConfig config);
    bool Delete(string id);
}