namespace QuickProxy.Proxy.Containers;

public interface IContainerInventory
{
    IReadOnlyList<ContainerInventoryItem> ListContainers();
    ContainerInventoryItem? GetContainer(string name);
    ContainerInventoryStatus GetStatus();
    ContainerInventorySnapshot GetSnapshot();
}