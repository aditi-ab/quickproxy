using QuickProxy.Proxy.Containers;
using QuickProxy.Proxy.Models;

namespace QuickProxy.Proxy.Provisioning;

public sealed class ProvisioningDocument
{
    public List<ProvisionedAuthProviderEntry> AuthProviders { get; set; } = [];
    public List<DomainTranslationRule> DomainTranslations { get; set; } = [];
    public List<ProxyHostConfig> ProxyHosts { get; set; } = [];
    public List<ProxyHostConfig> AutomaticTemplates { get; set; } = [];
    public List<ProvisionedCertificateEntry> Certificates { get; set; } = [];
    public List<ProvisionedContainerDefaultsSetEntry> ContainerDefaultSets { get; set; } = [];
}

public sealed class ProvisionedCertificateEntry
{
    public string Id { get; set; } = string.Empty;
    public CertificateConfigMode Mode { get; set; } = CertificateConfigMode.Files;
    public string? PfxPassword { get; set; }
    public string? PfxPasswordEnvVar { get; set; }
    public string? Thumbprint { get; set; }
    public string StoreName { get; set; } = "My";
    public string StoreLocation { get; set; } = "LocalMachine";
    public List<string> IssuerMatchDomains { get; set; } = [];
    public bool? IssuerEnabled { get; set; }
    public IssuerCaSourceMode? IssuerCaSource { get; set; }
    public string? CaCertificatePath { get; set; }
    public string? CaPrivateKeyPath { get; set; }
    public string? CaPfxPath { get; set; }
    public string? CaPfxPassword { get; set; }
    public string? CaPfxPasswordEnvVar { get; set; }
    public string? CaStoreThumbprint { get; set; }
    public string? CaStoreName { get; set; }
    public string? CaStoreLocation { get; set; }
    public ProvisionedCertificateFiles Files { get; set; } = new();
}

public sealed class ProvisionedCertificateFiles
{
    public string? CertificatePemBase64 { get; set; }
    public string? KeyPemBase64 { get; set; }
    public string? IntermediatePemBase64 { get; set; }
    public string? PfxBase64 { get; set; }
    public string? CaCertificatePemBase64 { get; set; }
    public string? CaKeyPemBase64 { get; set; }
    public string? CaPfxBase64 { get; set; }
}

public sealed class ProvisionedContainerDefaultsSetEntry
{
    public string Id { get; set; } = string.Empty;
    public List<ContainerKeyValuePair> Labels { get; set; } = [];
    public List<ContainerKeyValuePair> EnvVars { get; set; } = [];
    public List<ContainerMountBindingRequest> MountBindings { get; set; } = [];
    public List<ContainerHostMappingRequest> HostMappings { get; set; } = [];
    public List<ContainerNetworkAliasRequest> NetworkAliases { get; set; } = [];
}

public sealed class ProvisionedAuthProviderEntry
{
    public string Id { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public bool AllowAutoAccess { get; set; } = true;
    public AuthProviderType Type { get; set; } = AuthProviderType.Ldap;
    public ProvisionedLdapAuthProviderSettings Ldap { get; set; } = new();
    public ProvisionedOidcAuthProviderSettings Oidc { get; set; } = new();
}

public sealed class ProvisionedLdapAuthProviderSettings
{
    public string Server { get; set; } = string.Empty;
    public int Port { get; set; } = 389;
    public bool UseSsl { get; set; }
    public string BindDn { get; set; } = string.Empty;
    public string BindPassword { get; set; } = string.Empty;
    public string BaseDn { get; set; } = string.Empty;
    public string UserFilter { get; set; } = "(mail={email})";
    public string EmailAttribute { get; set; } = "mail";
    public string FullNameAttribute { get; set; } = "displayName";
}

public sealed class ProvisionedOidcAuthProviderSettings
{
    public string Authority { get; set; } = string.Empty;
    public string MetadataUrl { get; set; } = string.Empty;
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string Scopes { get; set; } = "openid profile email";
    public string EmailClaim { get; set; } = "email";
    public string NameClaim { get; set; } = "name";
    public string SubjectClaim { get; set; } = "sub";
    public bool UsePkce { get; set; } = true;
}