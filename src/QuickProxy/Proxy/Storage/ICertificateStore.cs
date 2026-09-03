using QuickProxy.Proxy.Models;

namespace QuickProxy.Proxy.Storage;

public interface ICertificateStore
{
    string CertificatesRootDirectory { get; }
    IReadOnlyList<StoredCertificateConfig> List();
    StoredCertificateConfig? Get(string id);
    bool Exists(string id);
    void Upsert(StoredCertificateConfig config);
    bool Delete(string id);
    byte[]? GetFile(string id, string fileName);
    void SaveFiles(string id, IReadOnlyDictionary<string, byte[]> files);
}