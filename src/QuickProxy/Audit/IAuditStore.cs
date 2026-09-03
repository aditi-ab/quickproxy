namespace QuickProxy.Audit;

public interface IAuditStore
{
    void Append(AuditEvent auditEvent);
    AuditListResponse List(AuditQuery query);
    AuditEvent? Get(string id);
}