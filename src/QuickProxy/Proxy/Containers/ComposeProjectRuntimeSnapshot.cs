namespace QuickProxy.Proxy.Containers;

public sealed class ComposeProjectRuntimeSnapshot
{
    public string ProjectName { get; set; } = string.Empty;
    public string Status { get; set; } = "draft";
    public int ServiceCount { get; set; }
    public int ContainerCount { get; set; }
    public List<ComposeProjectServiceRuntime> Services { get; set; } = [];
    public List<ComposeProjectContainerRuntime> Containers { get; set; } = [];
    public string? LastCommandOutput { get; set; }
}

public sealed class ComposeProjectServiceRuntime
{
    public string Name { get; set; } = string.Empty;
    public int ContainerCount { get; set; }
    public int RunningCount { get; set; }
    public List<string> ContainerNames { get; set; } = [];
}

public sealed class ComposeProjectContainerRuntime
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Service { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
}