namespace QuickProxy.Proxy.Containers;

public sealed record ContainerShellClientMessage(string Type, string? Data, int? Cols, int? Rows);

public sealed record ContainerShellServerMessage(string Type, string? Data = null, string? Message = null);