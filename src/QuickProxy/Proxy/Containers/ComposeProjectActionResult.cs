namespace QuickProxy.Proxy.Containers;

public sealed class ComposeProjectActionResult
{
    public string Message { get; set; } = string.Empty;
    public string Output { get; set; } = string.Empty;
    public ComposeProjectRuntimeSnapshot Runtime { get; set; } = new();
}