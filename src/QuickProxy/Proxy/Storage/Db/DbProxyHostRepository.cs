using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using QuickProxy.Proxy.Models;

namespace QuickProxy.Proxy.Storage.Db;

public sealed class DbProxyHostRepository(IDbContextFactory<QuickProxyDbContext> factory) : IProxyHostRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    static DbProxyHostRepository()
    {
        JsonOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
    }

    public string DataDirectory => "database://proxy-hosts";

    public bool IsReservedFile(string hostId)
    {
        return false;
    }

    public IReadOnlyList<StoredProxyHostRecord> ReadAll()
    {
        using var db = factory.CreateDbContext();
        return db.Hosts.AsNoTracking()
            .OrderBy(x => x.Id)
            .Select(x => new StoredProxyHostRecord
            {
                HostId = x.Id,
                StorageLocation = $"database://proxy-hosts/{x.Id}",
                Json = x.Json
            })
            .ToArray();
    }

    public ProxyHostConfig? ReadById(string id)
    {
        using var db = factory.CreateDbContext();
        var entity = db.Hosts.AsNoTracking().FirstOrDefault(x => x.Id == id);
        return entity is null ? null : JsonSerializer.Deserialize<ProxyHostConfig>(entity.Json, JsonOptions);
    }

    public bool Exists(string id)
    {
        using var db = factory.CreateDbContext();
        return db.Hosts.Any(x => x.Id == id);
    }

    public void Write(ProxyHostConfig config)
    {
        using var db = factory.CreateDbContext();
        var json = JsonSerializer.Serialize(config, JsonOptions);
        var existing = db.Hosts.FirstOrDefault(x => x.Id == config.Id);
        if (existing is null)
            db.Hosts.Add(new HostConfigEntity { Id = config.Id, Json = json });
        else
            existing.Json = json;

        db.SaveChanges();
    }

    public bool Delete(string id)
    {
        using var db = factory.CreateDbContext();
        var entity = db.Hosts.FirstOrDefault(x => x.Id == id);
        if (entity is null) return false;

        db.Hosts.Remove(entity);
        db.SaveChanges();
        return true;
    }
}