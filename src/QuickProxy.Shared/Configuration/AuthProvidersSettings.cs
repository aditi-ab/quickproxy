namespace QuickProxy.Shared.Configuration;

public sealed class AuthProvidersSettings
{
    public LdapAuthSettings Ldap { get; set; } = new();
    public EntraAuthSettings Entra { get; set; } = new();
}

public sealed class LdapAuthSettings
{
    public bool Enabled { get; set; }
    public string Server { get; set; } = string.Empty;
    public int Port { get; set; } = 389;
    public bool UseSsl { get; set; }
    public string Domain { get; set; } = string.Empty;
    public string BindIdentityPattern { get; set; } = string.Empty;
}

public sealed class EntraAuthSettings
{
    public bool Enabled { get; set; }
    public string TenantId { get; set; } = string.Empty;
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string Scope { get; set; } = "openid profile email";
}