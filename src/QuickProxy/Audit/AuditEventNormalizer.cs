namespace QuickProxy.Audit;

internal static class AuditEventNormalizer
{
    public static AuditEvent Normalize(AuditEvent auditEvent)
    {
        auditEvent.Id = string.IsNullOrWhiteSpace(auditEvent.Id) ? Guid.NewGuid().ToString("N") : auditEvent.Id.Trim();
        auditEvent.Module = NormalizeFilter(auditEvent.Module) ?? string.Empty;
        auditEvent.Action = NormalizeFilter(auditEvent.Action) ?? string.Empty;
        auditEvent.TargetType = NormalizeOptional(auditEvent.TargetType);
        auditEvent.TargetId = NormalizeOptional(auditEvent.TargetId);
        auditEvent.Source = NormalizeFilter(auditEvent.Source) ?? "admin-api";
        auditEvent.Outcome = NormalizeFilter(auditEvent.Outcome) ?? "success";
        auditEvent.CorrelationId = NormalizeOptional(auditEvent.CorrelationId);
        auditEvent.Error = NormalizeOptional(auditEvent.Error);
        auditEvent.Actor ??= new AuditActor();
        auditEvent.Actor.Type = NormalizeFilter(auditEvent.Actor.Type) ?? "user";
        auditEvent.Actor.Id = NormalizeOptional(auditEvent.Actor.Id);
        auditEvent.Actor.DisplayName = NormalizeOptional(auditEvent.Actor.DisplayName);
        return auditEvent;
    }

    public static AuditEventListItem ToListItem(AuditEvent auditEvent)
    {
        return new AuditEventListItem
        {
            Id = auditEvent.Id,
            OccurredAtUtc = auditEvent.OccurredAtUtc,
            Module = auditEvent.Module,
            Action = auditEvent.Action,
            TargetType = auditEvent.TargetType,
            TargetId = auditEvent.TargetId,
            Actor = auditEvent.Actor,
            Source = auditEvent.Source,
            Outcome = auditEvent.Outcome,
            StatusCode = auditEvent.StatusCode,
            CorrelationId = auditEvent.CorrelationId,
            Error = auditEvent.Error,
            Summary = auditEvent.Changes?.Summary
        };
    }

    private static string? NormalizeFilter(string? value)
    {
        return NormalizeOptional(value)?.ToLowerInvariant();
    }

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}