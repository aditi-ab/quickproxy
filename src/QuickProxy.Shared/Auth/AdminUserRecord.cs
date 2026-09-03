namespace QuickProxy.Shared.Auth;

public sealed class AdminUserRecord
{
    public string Email { get; set; } = string.Empty;
    public string? FullName { get; set; }
    public bool Enabled { get; set; } = true;
    public string PasswordHash { get; set; } = string.Empty;
    public List<AdminUserExternalIdentity> ExternalIdentities { get; set; } = [];
}

public sealed class AdminUserExternalIdentity
{
    public string ProviderId { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
}