using System.Security.Claims;
using System.Text;
using System.Text.Json;
using QuickProxy.Shared.Web;

namespace QuickProxy.Audit;

public sealed class AuditLoggingMiddleware(RequestDelegate next, ILogger<AuditLoggingMiddleware> logger)
{
    private static readonly HashSet<string> MutatingMethods = new(StringComparer.OrdinalIgnoreCase)
    {
        HttpMethods.Post,
        HttpMethods.Put,
        HttpMethods.Patch,
        HttpMethods.Delete
    };

    private static readonly HashSet<string> SensitiveFieldNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "password",
        "bindPassword",
        "clientSecret",
        "secret",
        "privateKey",
        "privateKeyPem",
        "privateKeyBase64",
        "pfxBase64",
        "encryptedValue",
        "encryptedBinaryBase64",
        "binaryBase64",
        "value",
        "composeYaml",
        "content",
        "yaml",
        "pem"
    };

    public async Task InvokeAsync(HttpContext context, IAuditStore auditStore)
    {
        if (!ShouldAudit(context))
        {
            await next(context);
            return;
        }

        var requestSummary = await ReadRequestSummaryAsync(context);
        var actor = ResolveActor(context.User);
        var correlationId = context.TraceIdentifier;

        try
        {
            await next(context);
            AppendAuditEvent(
                auditStore,
                context,
                actor,
                correlationId,
                requestSummary,
                context.Response.StatusCode >= 400 ? "failure" : "success",
                context.Response.StatusCode >= 400
                    ? $"Request failed with status {context.Response.StatusCode}."
                    : null);
        }
        catch (Exception ex)
        {
            AppendAuditEvent(auditStore, context, actor, correlationId, requestSummary, "failure", ex.Message);
            logger.LogDebug(ex, "Audit captured failing admin mutation for {Method} {Path}.", context.Request.Method,
                context.Request.Path);
            throw;
        }
    }

    private static bool ShouldAudit(HttpContext context)
    {
        if (context.WebSockets.IsWebSocketRequest) return false;

        if (!MutatingMethods.Contains(context.Request.Method)) return false;

        if (!InternalApiPaths.IsInternalAdminApi(context.Request.Path)) return false;

        if (context.Request.Path.StartsWithSegments($"{InternalApiPaths.AdminRoot}/audit",
                StringComparison.OrdinalIgnoreCase)) return false;

        if (context.Request.Path.StartsWithSegments($"{InternalApiPaths.AdminRoot}/auth",
                StringComparison.OrdinalIgnoreCase)) return false;

        return context.User.Identity?.IsAuthenticated == true;
    }

    private static async Task<AuditChangeSet?> ReadRequestSummaryAsync(HttpContext context)
    {
        if (context.Request.ContentLength is <= 0) return BuildRouteSummary(context, null);

        if (context.Request.ContentType is null
            || !context.Request.ContentType.Contains("json", StringComparison.OrdinalIgnoreCase))
            return BuildRouteSummary(context, null);

        context.Request.EnableBuffering();
        using var reader = new StreamReader(context.Request.Body, Encoding.UTF8, false, leaveOpen: true);
        var raw = await reader.ReadToEndAsync();
        context.Request.Body.Position = 0;

        if (string.IsNullOrWhiteSpace(raw)) return BuildRouteSummary(context, null);

        try
        {
            using var document = JsonDocument.Parse(raw);
            return BuildRouteSummary(context, document.RootElement);
        }
        catch
        {
            return BuildRouteSummary(context, new
            {
                kind = "opaque",
                length = raw.Length
            });
        }
    }

    private static AuditChangeSet BuildRouteSummary(HttpContext context, object? parsedBody)
    {
        var fields = new List<AuditFieldChange>
        {
            new()
            {
                Path = "request.method",
                After = context.Request.Method,
                Kind = "request"
            },
            new()
            {
                Path = "request.path",
                After = context.Request.Path.Value,
                Kind = "request"
            }
        };

        if (context.Request.Query.Count > 0)
            fields.AddRange(context.Request.Query
                .OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
                .Select(x => new AuditFieldChange
                {
                    Path = $"query.{x.Key}",
                    After = Truncate(string.Join(", ", x.Value.Where(value => value is not null))),
                    Kind = "request"
                }));

        switch (parsedBody)
        {
            case JsonElement element when element.ValueKind == JsonValueKind.Object:
                fields.AddRange(ToFieldChanges("body", element));
                break;
            case not null:
                fields.Add(new AuditFieldChange
                {
                    Path = "body",
                    After = Truncate(JsonSerializer.Serialize(parsedBody)),
                    Kind = "request"
                });
                break;
        }

        return new AuditChangeSet
        {
            Summary = $"{context.Request.Method} {context.Request.Path.Value}",
            Fields = fields
        };
    }

    private static IEnumerable<AuditFieldChange> ToFieldChanges(string path, JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object) yield break;

        foreach (var property in element.EnumerateObject().OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
        {
            var childPath = $"{path}.{property.Name}";
            if (property.Value.ValueKind == JsonValueKind.Object)
            {
                foreach (var child in ToFieldChanges(childPath, property.Value)) yield return child;

                continue;
            }

            if (property.Value.ValueKind == JsonValueKind.Array)
            {
                yield return new AuditFieldChange
                {
                    Path = childPath,
                    After = property.Value.GetArrayLength().ToString(),
                    Kind = "collection"
                };
                continue;
            }

            yield return new AuditFieldChange
            {
                Path = childPath,
                After = SanitizeValue(property.Name, property.Value),
                Kind = SensitiveFieldNames.Contains(property.Name) ? "redacted" : "request"
            };
        }
    }

    private static string? SanitizeValue(string propertyName, JsonElement value)
    {
        if (SensitiveFieldNames.Contains(propertyName)) return RedactedSummary(value);

        return value.ValueKind switch
        {
            JsonValueKind.String => Truncate(value.GetString()),
            JsonValueKind.Number => value.ToString(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Null => null,
            _ => Truncate(value.ToString())
        };
    }

    private static string RedactedSummary(JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.Array => $"redacted ({value.GetArrayLength()} items)",
            JsonValueKind.String => $"redacted ({value.GetString()?.Length ?? 0} chars)",
            JsonValueKind.Null => "redacted",
            _ => "redacted"
        };
    }

    private static string? Truncate(string? value)
    {
        var trimmed = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        if (trimmed is null) return null;

        return trimmed.Length <= 160 ? trimmed : $"{trimmed[..160]}... ({trimmed.Length} chars)";
    }

    private static AuditActor ResolveActor(ClaimsPrincipal principal)
    {
        var email = principal.FindFirstValue(ClaimTypes.Email) ?? principal.FindFirstValue(ClaimTypes.NameIdentifier);
        var name = principal.FindFirstValue(ClaimTypes.Name);

        return new AuditActor
        {
            Type = "user",
            Id = string.IsNullOrWhiteSpace(email) ? null : email.Trim(),
            DisplayName = string.IsNullOrWhiteSpace(name) ? email?.Trim() : name.Trim()
        };
    }

    private static void AppendAuditEvent(
        IAuditStore auditStore,
        HttpContext context,
        AuditActor actor,
        string correlationId,
        AuditChangeSet? changes,
        string outcome,
        string? error)
    {
        var module = ResolveModule(context.Request.Path);
        auditStore.Append(new AuditEvent
        {
            Module = module,
            Action = ResolveAction(context.Request.Method, context.Request.Path),
            TargetType = ResolveTargetType(context.Request.Path),
            TargetId = ResolveTargetId(context),
            Actor = actor,
            Source = "admin-api",
            Outcome = outcome,
            StatusCode = context.Response.StatusCode,
            CorrelationId = correlationId,
            Error = error,
            Changes = changes
        });
    }

    private static string ResolveModule(PathString path)
    {
        var remainder = path.Value?[InternalApiPaths.AdminRoot.Length..].Trim('/') ?? string.Empty;
        var segments = remainder.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0) return "admin";

        var firstSegment = segments[0];
        return firstSegment switch
        {
            "configs" => "key-values",
            "auth-providers" => "users",
            _ => string.IsNullOrWhiteSpace(firstSegment) ? "admin" : firstSegment
        };
    }

    private static string ResolveAction(string method, PathString path)
    {
        var normalizedMethod = method.Trim().ToUpperInvariant();
        var remainder = path.Value?[InternalApiPaths.AdminRoot.Length..].Trim('/') ?? string.Empty;
        if (string.IsNullOrWhiteSpace(remainder)) return normalizedMethod.ToLowerInvariant();

        var segments = remainder.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var tail = segments[^1].ToLowerInvariant();
        if (normalizedMethod == HttpMethods.Post.ToUpperInvariant() && tail is "restore" or "reprovision" or "validate"
                or "deploy" or "pull" or "start" or "stop" or "restart" or "down") return tail;

        return normalizedMethod switch
        {
            "POST" => "create",
            "PUT" => "update",
            "PATCH" => "update",
            "DELETE" => "delete",
            _ => normalizedMethod.ToLowerInvariant()
        };
    }

    private static string? ResolveTargetType(PathString path)
    {
        var remainder = path.Value?[InternalApiPaths.AdminRoot.Length..].Trim('/') ?? string.Empty;
        var segments = remainder.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0) return null;

        return segments[0] switch
        {
            "configs" => "config",
            "users" => "user",
            "auth-providers" => "auth-provider",
            "proxy-hosts" => "proxy-host",
            "containers" => segments.Length > 1 &&
                            string.Equals(segments[1], "projects", StringComparison.OrdinalIgnoreCase)
                ? "compose-project"
                : "container",
            _ => segments[0].Trim().TrimEnd('s')
        };
    }

    private static string? ResolveTargetId(HttpContext context)
    {
        foreach (var key in new[] { "id", "email", "name", "key", "path", "providerId", "revisionId" })
            if (context.Request.RouteValues.TryGetValue(key, out var value) && value is not null)
            {
                var normalized = value.ToString()?.Trim();
                if (!string.IsNullOrWhiteSpace(normalized)) return normalized;
            }

        return null;
    }
}