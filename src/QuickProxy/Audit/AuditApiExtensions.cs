using QuickProxy.Shared.Web;

namespace QuickProxy.Audit;

public static class AuditApiExtensions
{
    public static IEndpointRouteBuilder MapAuditApi(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup($"{InternalApiPaths.AdminRoot}/audit").RequireAuthorization();

        group.MapGet("", (
            string? module,
            string? action,
            string? actor,
            string? target,
            string? outcome,
            DateTimeOffset? fromUtc,
            DateTimeOffset? toUtc,
            int? limit,
            int? offset,
            IAuditStore store) =>
        {
            var query = new AuditQuery
            {
                Module = module,
                Action = action,
                Actor = actor,
                Target = target,
                Outcome = outcome,
                FromUtc = fromUtc,
                ToUtc = toUtc,
                Limit = limit ?? 200,
                Offset = offset ?? 0
            };

            return Results.Ok(store.List(query));
        });

        group.MapGet("/{id}", (string id, IAuditStore store) =>
        {
            var normalizedId = (id ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(normalizedId))
                return Results.BadRequest(new
                {
                    code = "validation_error",
                    message = "Audit event id is required."
                });

            var auditEvent = store.Get(normalizedId);
            return auditEvent is null
                ? Results.NotFound(new
                {
                    code = "not_found",
                    message = $"Audit event '{normalizedId}' was not found."
                })
                : Results.Ok(auditEvent);
        });

        return app;
    }
}