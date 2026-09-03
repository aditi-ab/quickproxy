using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace QuickProxy.Shared.Web;

public static class AdminUiExtensions
{
    public static void UseLocalhostAdminGuard(this IApplicationBuilder app)
    {
        app.Use(async (context, next) =>
        {
            if (InternalApiPaths.IsInternalApi(context.Request.Path) &&
                !InternalApiPaths.IsInternalAdminApi(context.Request.Path))
            {
                await next();
                return;
            }

            if (context.Connection.RemoteIpAddress is { } remoteIp && IPAddress.IsLoopback(remoteIp))
            {
                await next();
                return;
            }

            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(new
            {
                code = "forbidden",
                message = "Admin is only accessible from localhost."
            });
        });
    }
}
