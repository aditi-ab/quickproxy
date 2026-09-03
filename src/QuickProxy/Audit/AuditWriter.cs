namespace QuickProxy.Audit;

public static class AuditWriter
{
    public static void WriteSystemEvent(
        IAuditStore auditStore,
        string module,
        string action,
        string? targetType,
        string? targetId,
        string outcome,
        string? summary,
        string? error = null)
    {
        auditStore.Append(new AuditEvent
        {
            Module = module,
            Action = action,
            TargetType = targetType,
            TargetId = targetId,
            Actor = new AuditActor
            {
                Type = "system",
                Id = "provisioning",
                DisplayName = "Provisioning"
            },
            Source = "system",
            Outcome = outcome,
            Error = error,
            Changes = string.IsNullOrWhiteSpace(summary)
                ? null
                : new AuditChangeSet
                {
                    Summary = summary
                }
        });
    }
}