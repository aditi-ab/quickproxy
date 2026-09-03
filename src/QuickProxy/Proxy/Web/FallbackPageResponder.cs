using System.Reflection;
using Microsoft.Extensions.FileProviders;
using QuickProxy.Proxy.Models;
using QuickProxy.Shared.Web;

namespace QuickProxy.Proxy.Web;

public sealed class FallbackPageResponder(IHostEnvironment environment)
{
    private readonly IFileProvider? _embeddedFallbackProvider =
        Assembly.GetExecutingAssembly().CreateEmbeddedClientFallbackFileProvider();

    public Task WriteUnknownHostAsync(HttpContext context, FallbackSettings settings)
    {
        return WriteConfiguredResponseAsync(
            context,
            settings.Mode,
            settings.StatusCode,
            settings.HtmlFilePath,
            settings.ContentType,
            settings.RedirectUrl,
            "not-found.html",
            "Not Found");
    }

    public Task WriteBadGatewayAsync(HttpContext context, FallbackSettings settings)
    {
        return WriteConfiguredResponseAsync(
            context,
            settings.BadGatewayMode,
            StatusCodes.Status502BadGateway,
            settings.BadGatewayHtmlFilePath,
            settings.BadGatewayContentType,
            null,
            "bad-gateway.html",
            "Bad Gateway");
    }

    public Task WriteGatewayTimeoutAsync(HttpContext context, FallbackSettings settings)
    {
        return WriteConfiguredResponseAsync(
            context,
            settings.GatewayTimeoutMode,
            StatusCodes.Status504GatewayTimeout,
            settings.GatewayTimeoutHtmlFilePath,
            settings.GatewayTimeoutContentType,
            null,
            "gateway-timeout.html",
            "Gateway Timeout");
    }

    private async Task WriteConfiguredResponseAsync(
        HttpContext context,
        FallbackResponseMode mode,
        int statusCode,
        string? htmlFilePath,
        string? contentType,
        string? redirectUrl,
        string embeddedFileName,
        string fallbackText)
    {
        context.Response.StatusCode = statusCode;

        if (mode == FallbackResponseMode.Redirect)
        {
            if (!string.IsNullOrWhiteSpace(redirectUrl)) context.Response.Headers.Location = redirectUrl;

            return;
        }

        if (mode == FallbackResponseMode.StatusCode) return;

        if (!context.Response.HasStarted)
            context.Response.ContentType = string.IsNullOrWhiteSpace(contentType)
                ? "text/html; charset=utf-8"
                : contentType;

        var file = mode switch
        {
            FallbackResponseMode.Default => _embeddedFallbackProvider?.GetFileInfo(embeddedFileName),
            FallbackResponseMode.HtmlFile => ResolvePhysicalFile(htmlFilePath),
            _ => null
        };

        if (file is not null && file.Exists)
        {
            await using var stream = file.CreateReadStream();
            await stream.CopyToAsync(context.Response.Body);
            return;
        }

        if (!context.Response.HasStarted) context.Response.ContentType = "text/plain; charset=utf-8";

        await context.Response.WriteAsync(fallbackText);
    }

    private IFileInfo? ResolvePhysicalFile(string? htmlFilePath)
    {
        if (string.IsNullOrWhiteSpace(htmlFilePath)) return null;

        var fullPath = Path.IsPathRooted(htmlFilePath)
            ? htmlFilePath
            : Path.Combine(environment.ContentRootPath, htmlFilePath);

        if (!File.Exists(fullPath)) return null;

        var directoryPath = Path.GetDirectoryName(fullPath);
        var fileName = Path.GetFileName(fullPath);

        if (string.IsNullOrWhiteSpace(directoryPath) || string.IsNullOrWhiteSpace(fileName)) return null;

        var provider = new PhysicalFileProvider(directoryPath);
        return provider.GetFileInfo(fileName);
    }
}