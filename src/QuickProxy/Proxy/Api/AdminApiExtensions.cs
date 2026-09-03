using QuickProxy.Proxy.Models;
using QuickProxy.Proxy.Runtime;
using QuickProxy.Proxy.Storage;
using QuickProxy.Proxy.Validation;
using QuickProxy.Shared.Configuration;
using QuickProxy.Shared.Web;

namespace QuickProxy.Proxy.Api;

public static class AdminApiExtensions
{
    public static IEndpointRouteBuilder MapAdminApi(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup($"{InternalApiPaths.AdminRoot}/proxy-hosts").RequireAuthorization();

        group.MapGet("/link-settings", (ListenSettings settings) =>
        {
            return Results.Ok(new
            {
                httpPort = settings.HttpPort,
                httpsPort = settings.HttpsPort
            });
        });

        group.MapGet("/", (IProxyHostRuntime runtime) => { return Results.Ok(runtime.GetAdminHosts()); });

        group.MapGet("/{id}", (string id, IProxyHostRuntime runtime) =>
        {
            var host = runtime.GetAdminHost(id);
            return host is null ? NotFound(id) : Results.Ok(host);
        });

        group.MapPost("/", (
            ProxyHostConfig host,
            IProxyHostRepository repository,
            IProxyHostRuntime runtime,
            IIssuedCertificateService issuedCertificateService) =>
        {
            if (repository.Exists(host.Id)) return Conflict("duplicate_id", $"Host '{host.Id}' already exists.");

            var validationErrors = ValidateForWrite(host, host.Id, runtime.GetStoredHosts(), null);
            if (validationErrors.Count > 0) return Validation(validationErrors);

            ApplyIssuedCertificateBinding(host, issuedCertificateService);
            repository.Write(host);
            runtime.TryReload();
            return Results.Created($"{InternalApiPaths.AdminRoot}/proxy-hosts/{host.Id}", host);
        });

        group.MapPut("/{id}", (
            string id,
            ProxyHostConfig host,
            IProxyHostRepository repository,
            IProxyHostRuntime runtime,
            IIssuedCertificateService issuedCertificateService) =>
        {
            var existing = runtime.GetAdminHost(id);
            if (existing?.Runtime.ReadOnly == true)
                return Conflict("read_only", $"Host '{id}' is generated at runtime and cannot be edited.");

            host.Id = id;
            var validationErrors = ValidateForWrite(host, id, runtime.GetStoredHosts(), id);
            if (validationErrors.Count > 0) return Validation(validationErrors);

            ApplyIssuedCertificateBinding(host, issuedCertificateService);
            repository.Write(host);
            runtime.TryReload();
            return Results.Ok(host);
        });

        group.MapDelete("/{id}", (string id, IProxyHostRepository repository, IProxyHostRuntime runtime) =>
        {
            var existing = runtime.GetAdminHost(id);
            if (existing?.Runtime.ReadOnly == true)
                return Conflict("read_only", $"Host '{id}' is generated at runtime and cannot be deleted.");

            if (!repository.Delete(id)) return NotFound(id);

            runtime.TryReload();
            return Results.NoContent();
        });

        return app;
    }

    private static IResult NotFound(string key)
    {
        return Results.NotFound(new
        {
            code = "not_found",
            message = $"Resource '{key}' was not found."
        });
    }

    private static IResult Validation(List<string> details)
    {
        return Results.BadRequest(new
        {
            code = "validation_error",
            message = "Validation failed.",
            details
        });
    }

    private static IResult Conflict(string code, string message)
    {
        return Results.Conflict(new
        {
            code,
            message
        });
    }

    private static void ApplyIssuedCertificateBinding(ProxyHostConfig host,
        IIssuedCertificateService issuedCertificateService)
    {
        if (!string.IsNullOrWhiteSpace(host.CertificateId) &&
            !host.CertificateId.StartsWith("issued-", StringComparison.OrdinalIgnoreCase))
            return;

        var issuedId = issuedCertificateService.EnsureForHost(host);
        if (!string.IsNullOrWhiteSpace(issuedId)) host.CertificateId = issuedId;
    }

    private static List<string> ValidateForWrite(
        ProxyHostConfig candidate,
        string expectedId,
        IReadOnlyList<ProxyHostConfig> currentHosts,
        string? replaceId)
    {
        var single = ProxyHostValidator.ValidateSingle(candidate, expectedId);
        var errors = new List<string>(single.Errors);

        var all = currentHosts
            .Where(x => replaceId is null || !string.Equals(x.Id, replaceId, StringComparison.OrdinalIgnoreCase))
            .ToList();
        all.Add(candidate);
        errors.AddRange(ProxyHostValidator.ValidateAcrossHosts(all));

        return errors;
    }
}