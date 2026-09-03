using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using QuickProxy.Proxy.Models;

namespace QuickProxy.Proxy.Storage.Db;

public sealed class DbAuthProviderStore(IDbContextFactory<QuickProxyDbContext> factory) : IAuthProviderStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    static DbAuthProviderStore()
    {
        JsonOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
    }

    public IReadOnlyList<AuthProviderConfig> List()
    {
        using var db = factory.CreateDbContext();
        EnsureSchema(db);

        return db.AuthProviders.AsNoTracking()
            .OrderBy(x => x.Id)
            .ToArray()
            .Select(x => JsonSerializer.Deserialize<AuthProviderConfig>(x.Json, JsonOptions))
            .Where(x => x is not null)
            .Cast<AuthProviderConfig>()
            .OrderBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public AuthProviderConfig? Get(string id)
    {
        using var db = factory.CreateDbContext();
        EnsureSchema(db);

        var entity = db.AuthProviders.AsNoTracking().FirstOrDefault(x => x.Id == id);
        return entity is null ? null : JsonSerializer.Deserialize<AuthProviderConfig>(entity.Json, JsonOptions);
    }

    public bool Exists(string id)
    {
        using var db = factory.CreateDbContext();
        EnsureSchema(db);
        return db.AuthProviders.Any(x => x.Id == id);
    }

    public void Upsert(AuthProviderConfig provider)
    {
        using var db = factory.CreateDbContext();
        EnsureSchema(db);

        var json = JsonSerializer.Serialize(provider, JsonOptions);
        var existing = db.AuthProviders.FirstOrDefault(x => x.Id == provider.Id);
        if (existing is null)
            db.AuthProviders.Add(new AuthProviderConfigEntity
            {
                Id = provider.Id,
                Json = json
            });
        else
            existing.Json = json;

        db.SaveChanges();
    }

    public bool Delete(string id)
    {
        using var db = factory.CreateDbContext();
        EnsureSchema(db);

        var entity = db.AuthProviders.FirstOrDefault(x => x.Id == id);
        if (entity is null) return false;

        db.AuthProviders.Remove(entity);
        db.SaveChanges();
        return true;
    }

    private static void EnsureSchema(QuickProxyDbContext db)
    {
        db.Database.OpenConnection();
        try
        {
            if (db.Database.IsSqlite())
            {
                db.Database.ExecuteSqlRaw("""
                                          CREATE TABLE IF NOT EXISTS AuthProviderConfigs (
                                              Id TEXT NOT NULL PRIMARY KEY,
                                              Json TEXT NOT NULL
                                          )
                                          """);
                return;
            }

            db.Database.ExecuteSqlRaw("""
                                      IF OBJECT_ID(N'AuthProviderConfigs', N'U') IS NULL
                                      BEGIN
                                          CREATE TABLE [AuthProviderConfigs] (
                                              [Id] nvarchar(200) NOT NULL PRIMARY KEY,
                                              [Json] nvarchar(max) NOT NULL
                                          );
                                      END
                                      """);
        }
        finally
        {
            db.Database.CloseConnection();
        }
    }
}