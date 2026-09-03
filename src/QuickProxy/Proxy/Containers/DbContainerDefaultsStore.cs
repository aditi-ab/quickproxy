using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using QuickProxy.Proxy.Storage.Db;

namespace QuickProxy.Proxy.Containers;

public sealed class DbContainerDefaultsStore(IDbContextFactory<QuickProxyDbContext> factory) : IContainerDefaultsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public IReadOnlyList<ContainerDefaultsSet> List()
    {
        using var db = factory.CreateDbContext();
        EnsureTableExists(db);
        return ReadModel(db).Sets
            .OrderBy(x => x.Id, StringComparer.OrdinalIgnoreCase)
            .Select(Clone)
            .ToArray();
    }

    public ContainerDefaultsSet? Get(string id)
    {
        var normalizedId = NormalizeId(id);
        if (string.IsNullOrWhiteSpace(normalizedId)) return null;

        using var db = factory.CreateDbContext();
        EnsureTableExists(db);
        var set = ReadModel(db).Sets
            .FirstOrDefault(x => string.Equals(x.Id, normalizedId, StringComparison.OrdinalIgnoreCase));
        return set is null ? null : Clone(set);
    }

    public ContainerDefaultsSet Upsert(ContainerDefaultsSet set)
    {
        using var db = factory.CreateDbContext();
        EnsureTableExists(db);
        var model = ReadModel(db);

        var normalized = Clone(set);
        normalized.Id = NormalizeId(normalized.Id);
        normalized.UpdatedAtUtc = DateTimeOffset.UtcNow;

        var index = model.Sets.FindIndex(x => string.Equals(x.Id, normalized.Id, StringComparison.OrdinalIgnoreCase));
        if (index >= 0)
            model.Sets[index] = normalized;
        else
            model.Sets.Add(normalized);

        WriteModel(db, model);
        return Clone(normalized);
    }

    public bool Delete(string id)
    {
        using var db = factory.CreateDbContext();
        EnsureTableExists(db);
        var model = ReadModel(db);
        var normalizedId = NormalizeId(id);
        if (string.IsNullOrWhiteSpace(normalizedId)) return false;

        var removed = model.Sets.RemoveAll(x => string.Equals(x.Id, normalizedId, StringComparison.OrdinalIgnoreCase));
        if (removed <= 0) return false;

        WriteModel(db, model);
        return true;
    }

    private static ContainerDefaultsStoreDocument ReadModel(QuickProxyDbContext db)
    {
        var entity = db.ContainerDefaultsSettings.AsNoTracking().FirstOrDefault(x => x.Id == 1);
        if (entity is null || string.IsNullOrWhiteSpace(entity.Json)) return new ContainerDefaultsStoreDocument();

        return JsonSerializer.Deserialize<ContainerDefaultsStoreDocument>(entity.Json, JsonOptions)
               ?? new ContainerDefaultsStoreDocument();
    }

    private static void WriteModel(QuickProxyDbContext db, ContainerDefaultsStoreDocument model)
    {
        var json = JsonSerializer.Serialize(model, JsonOptions);
        var entity = db.ContainerDefaultsSettings.FirstOrDefault(x => x.Id == 1);
        if (entity is null)
            db.ContainerDefaultsSettings.Add(new ContainerDefaultsSettingsEntity { Id = 1, Json = json });
        else
            entity.Json = json;

        db.SaveChanges();
    }

    private static void EnsureTableExists(QuickProxyDbContext db)
    {
        if (db.Database.IsSqlite())
        {
            db.Database.ExecuteSqlRaw("""
                                      CREATE TABLE IF NOT EXISTS ContainerDefaultsSettings (
                                          Id INTEGER NOT NULL PRIMARY KEY,
                                          Json TEXT NOT NULL
                                      );
                                      """);
            return;
        }

        if (db.Database.IsSqlServer())
            db.Database.ExecuteSqlRaw("""
                                      IF OBJECT_ID(N'[ContainerDefaultsSettings]', N'U') IS NULL
                                      BEGIN
                                          CREATE TABLE [ContainerDefaultsSettings] (
                                              [Id] int NOT NULL PRIMARY KEY,
                                              [Json] nvarchar(max) NOT NULL
                                          );
                                      END
                                      """);
    }

    private static string NormalizeId(string? value)
    {
        return (value ?? string.Empty).Trim();
    }

    private static ContainerDefaultsSet Clone(ContainerDefaultsSet value)
    {
        return new ContainerDefaultsSet
        {
            Id = value.Id,
            Labels = (value.Labels ?? []).Select(x => new ContainerKeyValuePair { Key = x.Key, Value = x.Value })
                .ToList(),
            EnvVars = (value.EnvVars ?? []).Select(x => new ContainerKeyValuePair { Key = x.Key, Value = x.Value })
                .ToList(),
            MountBindings = (value.MountBindings ?? []).Select(x => new ContainerMountBindingRequest
            {
                HostPath = x.HostPath,
                ContainerPath = x.ContainerPath,
                ReadOnly = x.ReadOnly
            }).ToList(),
            HostMappings = (value.HostMappings ?? []).Select(x => new ContainerHostMappingRequest
            {
                Hostname = x.Hostname,
                Address = x.Address
            }).ToList(),
            NetworkAliases = (value.NetworkAliases ?? []).Select(x => new ContainerNetworkAliasRequest
            {
                Network = x.Network,
                Alias = x.Alias
            }).ToList(),
            UpdatedAtUtc = value.UpdatedAtUtc
        };
    }

    private sealed class ContainerDefaultsStoreDocument
    {
        public List<ContainerDefaultsSet> Sets { get; set; } = [];
    }
}