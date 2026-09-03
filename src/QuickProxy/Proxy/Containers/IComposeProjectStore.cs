namespace QuickProxy.Proxy.Containers;

public interface IComposeProjectStore
{
    IReadOnlyList<ComposeProject> List();
    ComposeProject? Get(string id);
    ComposeProject Upsert(ComposeProject project);
    bool Delete(string id);
}