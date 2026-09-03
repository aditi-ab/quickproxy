namespace QuickProxy.Proxy.Containers;

public sealed record ContainerRuntimeEvent(string Action, string? ContainerId, string? ContainerName);