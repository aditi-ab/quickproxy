using QuickProxy.Shared.Configuration;

namespace QuickProxy.Configuration;

public class AppModuleSettings
{
    public bool Enabled { get; set; } = true;
    public StorageSettings Storage { get; set; } = new();
}

public sealed class RemoteConfigStoreSettings
{
    public bool Enabled { get; set; }
    public string Url { get; set; } = string.Empty;
    public int TimeoutSeconds { get; set; } = 30;
}

public sealed class ConfigSecretsSettings
{
    public string EncryptionKey { get; set; } = string.Empty;
}

public sealed class AppConfigModuleSettings : AppModuleSettings
{
    public RemoteConfigStoreSettings Remote { get; set; } = new();
    public ConfigSecretsSettings Secrets { get; set; } = new();
}

public sealed class AppModulesConfiguration
{
    public AppModuleSettings Proxy { get; init; } = new();
    public AppConfigModuleSettings Config { get; init; } = new();
    public AppModuleSettings Audit { get; init; } = new();
}
