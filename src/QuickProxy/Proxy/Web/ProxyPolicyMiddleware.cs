using QuickProxy.Proxy.Runtime;

namespace QuickProxy.Proxy.Web;

public sealed class ProxyPolicyMiddleware(RequestDelegate next)
{
    private static readonly HashSet<string> AssetExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".js", ".mjs", ".css", ".png", ".jpg", ".jpeg", ".gif", ".svg", ".webp", ".ico", ".woff", ".woff2", ".ttf",
        ".eot", ".map"
    };

    public async Task Invoke(HttpContext context, IProxyHostRuntime runtime)
    {
        var host = runtime.MatchHost(context.Request.Host.Value);
        if (host is null)
        {
            await next(context);
            return;
        }

        if (host.ForceSsl && !context.Request.IsHttps)
        {
            var redirectTarget =
                $"https://{context.Request.Host}{context.Request.PathBase}{context.Request.Path}{context.Request.QueryString}";
            context.Response.Redirect(redirectTarget, true);
            return;
        }

        var upgrade = context.Request.Headers.Upgrade.ToString();
        if (!host.Websockets &&
            string.Equals(upgrade, "websocket", StringComparison.OrdinalIgnoreCase))
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsync("WebSockets disabled for this host.");
            return;
        }

        await next(context);

        if (!host.CacheAssets) return;

        var extension = Path.GetExtension(context.Request.Path.Value ?? string.Empty);
        if (AssetExtensions.Contains(extension))
            context.Response.Headers.CacheControl = "public, max-age=31536000, immutable";
    }
}