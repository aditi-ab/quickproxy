using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using QuickProxy.Proxy.Storage.Db;

namespace QuickProxy.Proxy.Containers;

public sealed class DbComposeProjectStore(IDbContextFactory<QuickProxyDbContext> factory, IHostEnvironment environment)
    : IComposeProjectStore
{
    public IReadOnlyList<ComposeProject> List()
    {
        using var db = factory.CreateDbContext();
        EnsureTableExists(db);
        return ReadModel(db).Projects
            .OrderBy(x => x.Id, StringComparer.OrdinalIgnoreCase)
            .Select(project => ComposeProjectStorageHelper.PrepareRuntimeProject(project, environment))
            .ToArray();
    }

    public ComposeProject? Get(string id)
    {
        var normalizedId = ComposeProjectStorageHelper.NormalizeId(id);
        if (string.IsNullOrWhiteSpace(normalizedId)) return null;

        using var db = factory.CreateDbContext();
        EnsureTableExists(db);
        var project = ReadModel(db).Projects
            .FirstOrDefault(x => ComposeProjectStorageHelper.MatchesProjectId(x, normalizedId));
        return project is null ? null : ComposeProjectStorageHelper.PrepareRuntimeProject(project, environment);
    }

    public ComposeProject Upsert(ComposeProject project)
    {
        using var db = factory.CreateDbContext();
        EnsureTableExists(db);
        var model = ReadModel(db);
        var existing = model.Projects.FirstOrDefault(x => ComposeProjectStorageHelper.MatchesProjectId(x, project.Id));
        var normalized = ComposeProjectStorageHelper.NormalizeProject(project, environment, DateTimeOffset.UtcNow);
        normalized.CreatedAtUtc = existing?.CreatedAtUtc ?? normalized.CreatedAtUtc;
        normalized.LastDeployAtUtc = project.LastDeployAtUtc ?? existing?.LastDeployAtUtc;
        normalized.LastError = project.LastError ?? existing?.LastError;

        if (!string.IsNullOrWhiteSpace(existing?.Slug) &&
            !string.Equals(existing.Slug, normalized.Slug, StringComparison.OrdinalIgnoreCase))
            ComposeProjectStorageHelper.DeleteWorkspace(environment, existing.Slug);

        ComposeProjectStorageHelper.PersistWorkspace(normalized);

        var index = model.Projects.FindIndex(x => ComposeProjectStorageHelper.MatchesProjectId(x, normalized.Id));
        if (index >= 0)
            model.Projects[index] = normalized;
        else
            model.Projects.Add(normalized);

        WriteModel(db, model);
        return ComposeProjectStorageHelper.Clone(normalized);
    }

    public bool Delete(string id)
    {
        using var db = factory.CreateDbContext();
        EnsureTableExists(db);
        var model = ReadModel(db);
        var normalizedId = ComposeProjectStorageHelper.NormalizeId(id);
        var existing =
            model.Projects.FirstOrDefault(x => ComposeProjectStorageHelper.MatchesProjectId(x, normalizedId));
        if (existing is null) return false;

        model.Projects.Remove(existing);
        WriteModel(db, model);
        ComposeProjectStorageHelper.DeleteWorkspace(environment, existing.Slug);
        return true;
    }

    private static ComposeProjectsStoreDocument ReadModel(QuickProxyDbContext db)
    {
        var entity = db.ComposeProjectsSettings.AsNoTracking().FirstOrDefault(x => x.Id == 1);
        if (entity is null || string.IsNullOrWhiteSpace(entity.Json)) return new ComposeProjectsStoreDocument();

        return JsonSerializer.Deserialize<ComposeProjectsStoreDocument>(entity.Json,
                   ComposeProjectStorageHelper.JsonOptions)
               ?? new ComposeProjectsStoreDocument();
    }

    private static void WriteModel(QuickProxyDbContext db, ComposeProjectsStoreDocument model)
    {
        var json = JsonSerializer.Serialize(model, ComposeProjectStorageHelper.JsonOptions);
        var entity = db.ComposeProjectsSettings.FirstOrDefault(x => x.Id == 1);
        if (entity is null)
            db.ComposeProjectsSettings.Add(new ComposeProjectsSettingsEntity { Id = 1, Json = json });
        else
            entity.Json = json;

        db.SaveChanges();
    }

    private static void EnsureTableExists(QuickProxyDbContext db)
    {
        if (db.Database.IsSqlite())
        {
            db.Database.ExecuteSqlRaw("""
                                      CREATE TABLE IF NOT EXISTS ComposeProjectsSettings (
                                          Id INTEGER NOT NULL PRIMARY KEY,
                                          Json TEXT NOT NULL
                                      );
                                      """);
            return;
        }

        if (db.Database.IsSqlServer())
            db.Database.ExecuteSqlRaw("""
                                      IF OBJECT_ID(N'[ComposeProjectsSettings]', N'U') IS NULL
                                      BEGIN
                                          CREATE TABLE [ComposeProjectsSettings] (
                                              [Id] int NOT NULL PRIMARY KEY,
                                              [Json] nvarchar(max) NOT NULL
                                          );
                                      END
                                      """);
    }

    private sealed class ComposeProjectsStoreDocument
    {
        public List<ComposeProject> Projects { get; set; } = [];
    }
}