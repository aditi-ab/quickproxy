using QuickProxy.Proxy.Models;
using QuickProxy.Proxy.Runtime;
using QuickProxy.Proxy.Storage;
using QuickProxy.Shared.Web;

namespace QuickProxy.Proxy.Api;

public static class FallbackSettingsApiExtensions
{
    public static IEndpointRouteBuilder MapFallbackSettingsApi(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup($"{InternalApiPaths.AdminRoot}/fallback-settings").RequireAuthorization();

        group.MapGet("/", (IFallbackSettingsCache cache) => { return Results.Ok(cache.Get()); });

        group.MapPut("/", (FallbackSettings settings, IFallbackSettingsStore store, IFallbackSettingsCache cache) =>
        {
            var errors = Validate(settings);
            if (errors.Count > 0)
                return Results.BadRequest(new
                {
                    code = "validation_error",
                    message = "Validation failed.",
                    details = errors
                });

            store.Write(settings);
            cache.Update(settings);
            return Results.Ok(settings);
        });

        return app;
    }

    private static List<string> Validate(FallbackSettings settings)
    {
        var errors = new List<string>();

        if (settings.StatusCode is < 100 or > 599) errors.Add("statusCode must be between 100 and 599.");

        if (settings.Mode == FallbackResponseMode.HtmlFile && string.IsNullOrWhiteSpace(settings.HtmlFilePath))
            errors.Add("htmlFilePath is required when mode is 'htmlFile'.");

        if (settings.Mode == FallbackResponseMode.Redirect)
        {
            if (settings.StatusCode is < 300 or > 399)
                errors.Add("statusCode must be in 300-399 when mode is 'redirect'.");

            if (string.IsNullOrWhiteSpace(settings.RedirectUrl))
                errors.Add("redirectUrl is required when mode is 'redirect'.");
            else if (!Uri.TryCreate(settings.RedirectUrl, UriKind.Absolute, out _))
                errors.Add("redirectUrl must be an absolute URL.");
        }

        if (settings.BadGatewayMode == FallbackResponseMode.Redirect)
            errors.Add("badGatewayMode cannot be 'redirect'.");

        if (settings.BadGatewayMode == FallbackResponseMode.HtmlFile &&
            string.IsNullOrWhiteSpace(settings.BadGatewayHtmlFilePath))
            errors.Add("badGatewayHtmlFilePath is required when badGatewayMode is 'htmlFile'.");

        if (settings.GatewayTimeoutMode == FallbackResponseMode.Redirect)
            errors.Add("gatewayTimeoutMode cannot be 'redirect'.");

        if (settings.GatewayTimeoutMode == FallbackResponseMode.HtmlFile &&
            string.IsNullOrWhiteSpace(settings.GatewayTimeoutHtmlFilePath))
            errors.Add("gatewayTimeoutHtmlFilePath is required when gatewayTimeoutMode is 'htmlFile'.");

        return errors;
    }
}