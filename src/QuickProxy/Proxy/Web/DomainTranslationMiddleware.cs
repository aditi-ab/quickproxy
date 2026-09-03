using System.Net;
using QuickProxy.Proxy.Runtime;
using Yarp.ReverseProxy.Forwarder;

namespace QuickProxy.Proxy.Web;

public sealed class DomainTranslationMiddleware(
    RequestDelegate next,
    IHttpForwarder forwarder,
    IDomainTranslationRuntime domainTranslationRuntime,
    IProxyHostRuntime proxyHostRuntime,
    ILogger<DomainTranslationMiddleware> logger)
{
    private static readonly HttpMessageInvoker ForwarderHttpClient = CreateInvoker();

    private static readonly ForwarderRequestConfig ForwarderRequestConfig = new()
    {
        ActivityTimeout = TimeSpan.FromSeconds(100),
        Version = HttpVersion.Version20,
        VersionPolicy = HttpVersionPolicy.RequestVersionOrLower
    };

    public async Task Invoke(HttpContext context)
    {
        if (proxyHostRuntime.MatchHost(context.Request.Host.Value) is not null)
        {
            await next(context);
            return;
        }

        var rule = domainTranslationRuntime.MatchRule(context.Request.Host.Value);
        if (rule is null)
        {
            await next(context);
            return;
        }

        var incomingHost = context.Request.Host.Value;
        if (string.IsNullOrWhiteSpace(incomingHost))
        {
            await next(context);
            return;
        }

        var translatedHost = domainTranslationRuntime.TranslateHost(incomingHost, rule);
        var destinationPrefix = BuildDestinationPrefix(context.Request.Scheme, translatedHost);
        var transformer = new DomainTranslationTransformer(rule.RewriteHostHeader, translatedHost);
        var error = await forwarder.SendAsync(context, destinationPrefix, ForwarderHttpClient, ForwarderRequestConfig,
            transformer);
        if (error == ForwarderError.None) return;

        var errorFeature = context.GetForwarderErrorFeature();
        logger.LogError(errorFeature?.Exception,
            "Domain translation forward failed for rule '{RuleId}' and host '{Host}'.", rule.Id,
            context.Request.Host.Value);
        if (!context.Response.HasStarted) context.Response.StatusCode = StatusCodes.Status502BadGateway;
    }

    private static string BuildDestinationPrefix(string scheme, string translatedHost)
    {
        return $"{scheme}://{translatedHost}";
    }

    private static HttpMessageInvoker CreateInvoker()
    {
        var handler = new SocketsHttpHandler
        {
            UseProxy = false,
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.None,
            UseCookies = false,
            EnableMultipleHttp2Connections = true
        };

        return new HttpMessageInvoker(handler);
    }

    private sealed class DomainTranslationTransformer(bool rewriteHostHeader, string translatedHost) : HttpTransformer
    {
        public override async ValueTask TransformRequestAsync(HttpContext httpContext, HttpRequestMessage proxyRequest,
            string destinationPrefix, CancellationToken cancellationToken)
        {
            await base.TransformRequestAsync(httpContext, proxyRequest, destinationPrefix, cancellationToken);
            proxyRequest.Headers.Host = rewriteHostHeader
                ? translatedHost
                : httpContext.Request.Host.Value;
            proxyRequest.Headers.Remove("X-Forwarded-Proto");
            proxyRequest.Headers.Remove("X-Forwarded-Host");
            proxyRequest.Headers.Remove("X-Forwarded-For");
            proxyRequest.Headers.TryAddWithoutValidation("X-Forwarded-Proto", httpContext.Request.Scheme);
            proxyRequest.Headers.TryAddWithoutValidation("X-Forwarded-Host", httpContext.Request.Host.Value);
            if (httpContext.Connection.RemoteIpAddress is not null)
                proxyRequest.Headers.TryAddWithoutValidation("X-Forwarded-For",
                    httpContext.Connection.RemoteIpAddress.ToString());
        }
    }
}