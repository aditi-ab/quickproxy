namespace QuickProxy.Proxy.Containers;

public sealed class ContainerInventorySnapshot
{
    public ContainerInventoryStatus Status { get; set; } = new();
    public IReadOnlyList<ContainerInventoryItem> Containers { get; set; } = [];
}