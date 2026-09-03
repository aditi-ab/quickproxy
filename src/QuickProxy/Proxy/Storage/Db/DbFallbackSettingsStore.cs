using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using QuickProxy.Proxy.Models;

namespace QuickProxy.Proxy.Storage.Db;

public sealed class DbFallbackSettingsStore(IDbContextFactory<QuickProxyDbContext> factory) : IFallbackSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    static DbFallbackSettingsStore()
    {
        JsonOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
    }

    public string SettingsDirectory => "database://settings";

    public FallbackSettings Read()
    {
        using var db = factory.CreateDbContext();
        var entity = db.FallbackSettings.FirstOrDefault(x => x.Id == 1);
        if (entity is null)
        {
            var defaults = new FallbackSettings();
            Write(defaults);
            return defaults;
        }

        return JsonSerializer.Deserialize<FallbackSettings>(entity.Json, JsonOptions) ?? new FallbackSettings();
    }

    public void Write(FallbackSettings settings)
    {
        using var db = factory.CreateDbContext();
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        var entity = db.FallbackSettings.FirstOrDefault(x => x.Id == 1);
        if (entity is null)
            db.FallbackSettings.Add(new FallbackSettingsEntity { Id = 1, Json = json });
        else
            entity.Json = json;

        db.SaveChanges();
    }
}