namespace QuickProxy.Audit;

public sealed class NoOpAuditStore : IAuditStore
{
    public void Append(AuditEvent auditEvent)
    {
    }

    public AuditEvent? Get(string id)
    {
        return null;
    }

    public AuditListResponse List(AuditQuery query)
    {
        return new AuditListResponse();
    }
}