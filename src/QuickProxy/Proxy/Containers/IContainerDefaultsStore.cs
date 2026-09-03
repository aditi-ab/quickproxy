namespace QuickProxy.Proxy.Containers;

public interface IContainerDefaultsStore
{
    IReadOnlyList<ContainerDefaultsSet> List();
    ContainerDefaultsSet? Get(string id);
    ContainerDefaultsSet Upsert(ContainerDefaultsSet set);
    bool Delete(string id);
}