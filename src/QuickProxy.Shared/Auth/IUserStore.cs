namespace QuickProxy.Shared.Auth;

public interface IUserStore
{
    IReadOnlyList<AdminUserRecord> List();
    AdminUserRecord? GetByEmail(string email);
    bool Exists(string email);
    bool AnyUsers();
    void Upsert(AdminUserRecord user);
    bool Delete(string email);
}