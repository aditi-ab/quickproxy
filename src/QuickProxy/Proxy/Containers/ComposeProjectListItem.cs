namespace QuickProxy.Proxy.Containers;

public sealed class ComposeProjectListItem
{
    public ComposeProject Project { get; set; } = new();
    public ComposeProjectRuntimeSnapshot Runtime { get; set; } = new();
}