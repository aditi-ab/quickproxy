using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using QuickProxy.Proxy.Models;

namespace QuickProxy.Proxy.Storage.Db;

public sealed class DbDomainTranslationStore(IDbContextFactory<QuickProxyDbContext> factory) : IDomainTranslationStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    static DbDomainTranslationStore()
    {
        JsonOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
    }

    public string DataDirectory => "database://domain-translations";

    public IReadOnlyList<DomainTranslationRule> List()
    {
        using var db = factory.CreateDbContext();
        return db.DomainTranslations.AsNoTracking()
            .OrderBy(x => x.Id)
            .Select(x => x.Json)
            .AsEnumerable()
            .Select(x => JsonSerializer.Deserialize<DomainTranslationRule>(x, JsonOptions))
            .Where(x => x is not null)
            .Cast<DomainTranslationRule>()
            .ToArray();
    }

    public DomainTranslationRule? Get(string id)
    {
        using var db = factory.CreateDbContext();
        var entity = db.DomainTranslations.AsNoTracking().FirstOrDefault(x => x.Id == id);
        return entity is null ? null : JsonSerializer.Deserialize<DomainTranslationRule>(entity.Json, JsonOptions);
    }

    public bool Exists(string id)
    {
        using var db = factory.CreateDbContext();
        return db.DomainTranslations.Any(x => x.Id == id);
    }

    public void Upsert(DomainTranslationRule rule)
    {
        using var db = factory.CreateDbContext();
        var json = JsonSerializer.Serialize(rule, JsonOptions);
        var existing = db.DomainTranslations.FirstOrDefault(x => x.Id == rule.Id);
        if (existing is null)
            db.DomainTranslations.Add(new DomainTranslationRuleEntity { Id = rule.Id, Json = json });
        else
            existing.Json = json;

        db.SaveChanges();
    }

    public bool Delete(string id)
    {
        using var db = factory.CreateDbContext();
        var entity = db.DomainTranslations.FirstOrDefault(x => x.Id == id);
        if (entity is null) return false;

        db.DomainTranslations.Remove(entity);
        db.SaveChanges();
        return true;
    }
}