using System.Text.Json;
using Microsoft.Extensions.Primitives;
using QuickProxy.Proxy.Models;
using QuickProxy.Proxy.Runtime;

namespace QuickProxy.Proxy.Web;

public sealed class ProxyDebugLoggingMiddleware(
    RequestDelegate next,
    ILogger<ProxyDebugLoggingMiddleware> logger)
{
    public const string ProxyDebugSnapshotItemKey = "QuickProxy.ProxyDebugSnapshot";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static readonly HashSet<string> RequestHeaderNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Host",
        "Origin",
        "Referer",
        "User-Agent",
        "Accept",
        "Upgrade",
        "Connection",
        "Forwarded",
        "X-Forwarded-For",
        "X-Forwarded-Host",
        "X-Forwarded-Proto",
        "X-Forwarded-Prefix",
        "X-Original-Host",
        "X-Original-Proto",
        "X-Original-For"
    };

    private static readonly HashSet<string> ResponseHeaderNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Location",
        "Server",
        "Via",
        "Alt-Svc",
        "Access-Control-Allow-Origin",
        "Access-Control-Allow-Headers",
        "Access-Control-Allow-Methods",
        "Access-Control-Expose-Headers",
        "Access-Control-Allow-Credentials",
        "Access-Control-Max-Age"
    };

    public async Task Invoke(HttpContext context, IProxyHostRuntime runtime, IFallbackSettingsCache settingsCache)
    {
        var matchedHost = runtime.MatchHost(context.Request.Host.Value);
        if (matchedHost is null)
        {
            await next(context);
            return;
        }

        var settings = settingsCache.Get();
        if (!settings.ProxyDebugLoggingEnabled)
        {
            await next(context);
            return;
        }

        var matchedRoute = runtime.MatchRoute(matchedHost, context.Request.Path.Value ?? "/");
        if (matchedRoute is null)
        {
            await next(context);
            return;
        }

        var snapshot = new ProxyDebugSnapshot();
        context.Items[ProxyDebugSnapshotItemKey] = snapshot;

        await next(context);

        var payload = new
        {
            TraceId = context.TraceIdentifier,
            MatchedHostId = matchedHost.Id,
            MatchedRoutePath = matchedRoute.Path,
            Request = new
            {
                context.Request.Method,
                context.Request.Scheme,
                Host = context.Request.Host.Value,
                Path = context.Request.Path.Value,
                QueryString = context.Request.QueryString.Value,
                context.Request.IsHttps,
                matchedRoute.PreserveOriginalHostHeader,
                matchedRoute.SendForwardedHeaders,
                matchedRoute.IgnoreBadCertificates,
                UpstreamMode = matchedRoute.UpstreamMode.ToString(),
                Upstream = new
                {
                    Scheme = matchedRoute.UpstreamMode == ProxyHostUpstreamMode.Container
                        ? matchedRoute.Container.Scheme
                        : matchedRoute.Upstream.Scheme,
                    Host = matchedRoute.UpstreamMode == ProxyHostUpstreamMode.Container
                        ? matchedRoute.Container.ContainerName
                        : matchedRoute.Upstream.Host,
                    Port = matchedRoute.UpstreamMode == ProxyHostUpstreamMode.Container
                        ? matchedRoute.Container.Port
                        : matchedRoute.Upstream.Port,
                    PortResolutionMode = matchedRoute.UpstreamMode == ProxyHostUpstreamMode.Container
                        ? matchedRoute.Container.PortResolutionMode.ToString()
                        : null,
                    NetworkName = matchedRoute.UpstreamMode == ProxyHostUpstreamMode.Container
                        ? matchedRoute.Container.NetworkName
                        : null
                },
                Headers = FilterHeaders(context.Request.Headers, RequestHeaderNames)
            },
            ProxyRequest = new
            {
                snapshot.DestinationPrefix,
                Version = snapshot.OutboundRequestVersion,
                Headers = FilterHeaders(snapshot.OutboundRequestHeaders, RequestHeaderNames)
            },
            Response = new
            {
                context.Response.StatusCode,
                Headers = FilterHeaders(context.Response.Headers, ResponseHeaderNames),
                UpstreamVersion = snapshot.UpstreamResponseVersion,
                UpstreamHeaders = FilterHeaders(snapshot.UpstreamResponseHeaders, ResponseHeaderNames)
            }
        };

        logger.LogInformation("Proxy debug {ProxyDebugJson}", JsonSerializer.Serialize(payload, JsonOptions));
    }

    private static Dictionary<string, string[]> FilterHeaders(IHeaderDictionary headers, HashSet<string> allowedNames)
    {
        var result = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

        foreach (var header in headers)
        {
            if (!allowedNames.Contains(header.Key)) continue;

            result[header.Key] = ToArray(header.Value);
        }

        return result;
    }

    private static Dictionary<string, string[]> FilterHeaders(
        IReadOnlyDictionary<string, string[]>? headers,
        HashSet<string> allowedNames)
    {
        var result = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        if (headers is null) return result;

        foreach (var header in headers)
        {
            if (!allowedNames.Contains(header.Key)) continue;

            result[header.Key] = header.Value;
        }

        return result;
    }

    private static string[] ToArray(StringValues values)
    {
        return values.Count == 0 ? [] : values.Select(x => x ?? string.Empty).ToArray();
    }
}