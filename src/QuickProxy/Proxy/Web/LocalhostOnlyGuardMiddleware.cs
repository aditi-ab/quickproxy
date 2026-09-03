using System.Net;
using QuickProxy.Shared.Web;

namespace QuickProxy.Proxy.Web;

public sealed class LocalhostOnlyGuardMiddleware(RequestDelegate next)
{
    public async Task Invoke(HttpContext context)
    {
        if (!InternalApiPaths.IsInternalAdminApi(context.Request.Path))
        {
            await next(context);
            return;
        }

        var remoteIp = context.Connection.RemoteIpAddress;
        if (remoteIp is not null && IPAddress.IsLoopback(remoteIp))
        {
            await next(context);
            return;
        }

        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        await context.Response.WriteAsJsonAsync(new
        {
            code = "forbidden",
            message = "Admin API is only accessible from localhost."
        });
    }
}