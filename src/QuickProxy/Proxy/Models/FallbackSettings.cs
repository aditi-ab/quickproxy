namespace QuickProxy.Proxy.Models;

public sealed class FallbackSettings
{
    public bool Enabled { get; set; } = true;
    public int StatusCode { get; set; } = StatusCodes.Status404NotFound;
    public FallbackResponseMode Mode { get; set; } = FallbackResponseMode.Default;
    public string HtmlFilePath { get; set; } = "ClientFallback/not-found.html";
    public string RedirectUrl { get; set; } = string.Empty;
    public string ContentType { get; set; } = "text/html; charset=utf-8";
    public FallbackResponseMode BadGatewayMode { get; set; } = FallbackResponseMode.Default;
    public string BadGatewayHtmlFilePath { get; set; } = "ClientFallback/bad-gateway.html";
    public string BadGatewayContentType { get; set; } = "text/html; charset=utf-8";
    public FallbackResponseMode GatewayTimeoutMode { get; set; } = FallbackResponseMode.Default;
    public string GatewayTimeoutHtmlFilePath { get; set; } = "ClientFallback/gateway-timeout.html";
    public string GatewayTimeoutContentType { get; set; } = "text/html; charset=utf-8";
    public bool ProxyDebugLoggingEnabled { get; set; }
}