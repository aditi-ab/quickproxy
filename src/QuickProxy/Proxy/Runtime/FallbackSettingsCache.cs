using QuickProxy.Proxy.Models;
using QuickProxy.Proxy.Storage;

namespace QuickProxy.Proxy.Runtime;

public interface IFallbackSettingsCache
{
    FallbackSettings Get();
    void Update(FallbackSettings settings);
    void Invalidate();
}

public sealed class FallbackSettingsCache(IFallbackSettingsStore store) : IFallbackSettingsCache
{
    private readonly TimeSpan _cacheTtl = TimeSpan.FromSeconds(30);
    private readonly object _sync = new();
    private CacheEntry? _entry;

    public FallbackSettings Get()
    {
        var now = DateTimeOffset.UtcNow;
        var current = _entry;
        if (current is not null && now - current.UpdatedAtUtc <= _cacheTtl) return Clone(current.Value);

        lock (_sync)
        {
            current = _entry;
            if (current is not null && now - current.UpdatedAtUtc <= _cacheTtl) return Clone(current.Value);

            var loaded = store.Read();
            _entry = new CacheEntry(Clone(loaded), now);
            return loaded;
        }
    }

    public void Update(FallbackSettings settings)
    {
        var clone = Clone(settings);
        _entry = new CacheEntry(clone, DateTimeOffset.UtcNow);
    }

    public void Invalidate()
    {
        _entry = null;
    }

    private static FallbackSettings Clone(FallbackSettings value)
    {
        return new FallbackSettings
        {
            Enabled = value.Enabled,
            StatusCode = value.StatusCode,
            Mode = value.Mode,
            HtmlFilePath = value.HtmlFilePath,
            RedirectUrl = value.RedirectUrl,
            ContentType = value.ContentType,
            ProxyDebugLoggingEnabled = value.ProxyDebugLoggingEnabled
        };
    }

    private sealed record CacheEntry(FallbackSettings Value, DateTimeOffset UpdatedAtUtc);
}