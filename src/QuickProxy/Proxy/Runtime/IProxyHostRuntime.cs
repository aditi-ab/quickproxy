using QuickProxy.Proxy.Models;

namespace QuickProxy.Proxy.Runtime;

public interface IProxyHostRuntime
{
    IReadOnlyList<ProxyHostConfig> GetHosts();
    IReadOnlyList<ProxyHostConfig> GetStoredHosts();
    IReadOnlyList<AdminProxyHostDto> GetAdminHosts();
    AdminProxyHostDto? GetAdminHost(string id);
    ProxyHostConfig? GetHost(string id);
    ProxyHostConfig? MatchHost(string? hostHeader);
    ProxyRouteConfig? MatchRoute(ProxyHostConfig host, string path);
    bool TryReload();
}