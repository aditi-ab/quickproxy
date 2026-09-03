using QuickProxy.Proxy.Models;
using QuickProxy.Proxy.Runtime;
using QuickProxy.Shared.Web;

namespace QuickProxy.Proxy.Web;

public sealed class UnknownHostFallbackMiddleware(RequestDelegate next)
{
    public async Task Invoke(HttpContext context, IProxyHostRuntime runtime, IFallbackSettingsCache settingsCache,
        FallbackPageResponder responder)
    {
        if (InternalApiPaths.IsInternalApi(context.Request.Path))
        {
            await next(context);
            return;
        }

        var matchedHost = runtime.MatchHost(context.Request.Host.Value);
        if (matchedHost is not null)
        {
            await next(context);
            return;
        }

        var settings = settingsCache.Get();
        if (!settings.Enabled)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        if (settings.Mode == FallbackResponseMode.Redirect)
        {
            context.Response.StatusCode = settings.StatusCode;
            context.Response.Headers.Location = settings.RedirectUrl;
            return;
        }

        await responder.WriteUnknownHostAsync(context, settings);
    }
}