namespace QuickProxy.Proxy.Runtime;

public interface ICertificateRuntimeCache
{
    void Invalidate(string id);
    void InvalidateAll();
}