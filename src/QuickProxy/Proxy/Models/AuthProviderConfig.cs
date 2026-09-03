namespace QuickProxy.Proxy.Models;

public enum AuthProviderType
{
    Ldap,
    Oidc
}

public sealed class AuthProviderConfig
{
    public string Id { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public bool AllowAutoAccess { get; set; } = true;
    public AuthProviderType Type { get; set; } = AuthProviderType.Ldap;
    public LdapAuthProviderConfig Ldap { get; set; } = new();
    public OidcAuthProviderConfig Oidc { get; set; } = new();
}

public sealed class LdapAuthProviderConfig
{
    public string Server { get; set; } = string.Empty;
    public int Port { get; set; } = 389;
    public bool UseSsl { get; set; }
    public string BindDn { get; set; } = string.Empty;
    public string EncryptedBindPassword { get; set; } = string.Empty;
    public string BaseDn { get; set; } = string.Empty;
    public string UserFilter { get; set; } = "(mail={email})";
    public string EmailAttribute { get; set; } = "mail";
    public string FullNameAttribute { get; set; } = "displayName";
}

public sealed class OidcAuthProviderConfig
{
    public string Authority { get; set; } = string.Empty;
    public string MetadataUrl { get; set; } = string.Empty;
    public string ClientId { get; set; } = string.Empty;
    public string EncryptedClientSecret { get; set; } = string.Empty;
    public string Scopes { get; set; } = "openid profile email";
    public string EmailClaim { get; set; } = "email";
    public string NameClaim { get; set; } = "name";
    public string SubjectClaim { get; set; } = "sub";
    public bool UsePkce { get; set; } = true;
}