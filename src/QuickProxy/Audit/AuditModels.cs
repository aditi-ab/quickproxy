using System.Text.Json.Serialization;

namespace QuickProxy.Audit;

public sealed class AuditEvent
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public DateTimeOffset OccurredAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public string Module { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string? TargetType { get; set; }
    public string? TargetId { get; set; }
    public AuditActor Actor { get; set; } = new();
    public string Source { get; set; } = "admin-api";
    public string Outcome { get; set; } = "success";
    public int? StatusCode { get; set; }
    public string? CorrelationId { get; set; }
    public string? Error { get; set; }
    public AuditChangeSet? Changes { get; set; }
}

public sealed class AuditActor
{
    public string Type { get; set; } = "user";
    public string? Id { get; set; }
    public string? DisplayName { get; set; }
}

public sealed class AuditChangeSet
{
    public string? Summary { get; set; }
    public List<AuditFieldChange> Fields { get; set; } = [];
}

public sealed class AuditFieldChange
{
    public string Path { get; set; } = string.Empty;
    public string? Before { get; set; }
    public string? After { get; set; }
    public string Kind { get; set; } = "value";
}

public sealed class AuditQuery
{
    public string? Module { get; set; }
    public string? Action { get; set; }
    public string? Actor { get; set; }
    public string? Target { get; set; }
    public string? Outcome { get; set; }
    public DateTimeOffset? FromUtc { get; set; }
    public DateTimeOffset? ToUtc { get; set; }
    public int Limit { get; set; } = 200;
    public int Offset { get; set; }
}

public sealed class AuditEventListItem
{
    public string Id { get; set; } = string.Empty;
    public DateTimeOffset OccurredAtUtc { get; set; }
    public string Module { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string? TargetType { get; set; }
    public string? TargetId { get; set; }
    public AuditActor Actor { get; set; } = new();
    public string Source { get; set; } = "admin-api";
    public string Outcome { get; set; } = "success";
    public int? StatusCode { get; set; }
    public string? CorrelationId { get; set; }
    public string? Error { get; set; }
    public string? Summary { get; set; }
}

public sealed class AuditListResponse
{
    public int Total { get; set; }
    public IReadOnlyList<AuditEventListItem> Items { get; set; } = [];
}

[JsonSerializable(typeof(AuditEvent))]
[JsonSerializable(typeof(List<AuditEvent>))]
internal partial class AuditJsonContext : JsonSerializerContext
{
}