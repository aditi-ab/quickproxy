using QuickProxy.Proxy.Config.Models;

namespace QuickProxy.Proxy.Config.Storage;

public interface IConfigStore
{
    IReadOnlyList<ConfigEntry> List(string? prefix = null);
    ConfigEntry? Get(string key);
}

public interface ILocalConfigStore : IConfigStore
{
    void Upsert(ConfigEntry entry);
    void ReplaceAll(IReadOnlyList<ConfigEntry> entries);
    void ReplaceAll(IReadOnlyList<ConfigEntry> entries, IReadOnlyList<ConfigEntryRevision> revisions);
    bool DeleteExact(string key);
    bool Delete(string key);
    IReadOnlyList<ConfigEntryRevision> ListAllRevisions();
    IReadOnlyList<ConfigEntryRevision> ListRevisions(string key);
    ConfigEntryRevision? GetRevision(string key, string revisionId);
    ConfigEntry? RestoreRevision(string key, string revisionId, DateTimeOffset restoredAtUtc, string? restoredBy);
    void MoveRevisionHistory(IReadOnlyDictionary<string, string> keyMap, DateTimeOffset movedAtUtc, string? movedBy);
}