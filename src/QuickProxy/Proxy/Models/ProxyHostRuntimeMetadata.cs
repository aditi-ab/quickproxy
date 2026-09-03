namespace QuickProxy.Proxy.Models;

public sealed class ProxyHostRuntimeMetadata
{
    public bool ReadOnly { get; set; }
    public bool IsGenerated { get; set; }
    public string? SourceTemplateId { get; set; }
    public string? MatchedContainerId { get; set; }
    public string? MatchedContainerName { get; set; }
    public string? MatchedComposeService { get; set; }
    public int ActiveMatchCount { get; set; }
}