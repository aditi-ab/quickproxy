using QuickProxy.Proxy.Models;

namespace QuickProxy.Proxy.Storage;

public interface IFallbackSettingsStore
{
    string SettingsDirectory { get; }
    FallbackSettings Read();
    void Write(FallbackSettings settings);
}