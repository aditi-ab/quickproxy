namespace QuickProxy.Proxy.Containers;

public sealed class ComposeProject
{
    public string Id { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string Status { get; set; } = "draft";
    public string ComposeYaml { get; set; } = string.Empty;
    public string WorkspacePath { get; set; } = string.Empty;
    public List<ComposeManagedFile> ManagedFiles { get; set; } = [];
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public DateTimeOffset? LastDeployAtUtc { get; set; }
    public string? LastError { get; set; }
}