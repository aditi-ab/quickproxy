namespace QuickProxy.Configuration;

public sealed class ProvisioningSettings
{
    public bool Enabled { get; set; }
    public string? FilePath { get; set; }
    public string? Url { get; set; }
    public int TimeoutSeconds { get; set; } = 30;
}