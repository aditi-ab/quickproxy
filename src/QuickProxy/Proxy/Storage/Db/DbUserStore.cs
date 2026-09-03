using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using QuickProxy.Shared.Auth;

namespace QuickProxy.Proxy.Storage.Db;

public sealed class DbUserStore(IDbContextFactory<QuickProxyDbContext> factory) : IUserStore
{
    public IReadOnlyList<AdminUserRecord> List()
    {
        using var db = factory.CreateDbContext();
        EnsureSchema(db);
        return db.Users.AsNoTracking()
            .OrderBy(x => x.Email)
            .Select(ToModel)
            .ToArray();
    }

    public AdminUserRecord? GetByEmail(string email)
    {
        using var db = factory.CreateDbContext();
        EnsureSchema(db);
        var entity = db.Users.AsNoTracking().FirstOrDefault(x => x.Email == email);
        return entity is null ? null : ToModel(entity);
    }

    public bool AnyUsers()
    {
        using var db = factory.CreateDbContext();
        EnsureSchema(db);
        return db.Users.Any();
    }

    public bool Exists(string email)
    {
        using var db = factory.CreateDbContext();
        EnsureSchema(db);
        return db.Users.Any(x => x.Email == email);
    }

    public void Upsert(AdminUserRecord user)
    {
        using var db = factory.CreateDbContext();
        EnsureSchema(db);
        var existing = db.Users.FirstOrDefault(x => x.Email == user.Email);
        if (existing is null)
        {
            db.Users.Add(ToEntity(user));
        }
        else
        {
            existing.FullName = user.FullName;
            existing.Enabled = user.Enabled;
            existing.PasswordHash = user.PasswordHash;
            existing.ExternalIdentitiesJson = SerializeExternalIdentities(user.ExternalIdentities);
        }

        db.SaveChanges();
    }

    public bool Delete(string email)
    {
        using var db = factory.CreateDbContext();
        EnsureSchema(db);
        var existing = db.Users.FirstOrDefault(x => x.Email == email);
        if (existing is null) return false;

        db.Users.Remove(existing);
        db.SaveChanges();
        return true;
    }

    private static AdminUserRecord ToModel(UserEntity entity)
    {
        return new AdminUserRecord
        {
            Email = entity.Email,
            FullName = entity.FullName,
            Enabled = entity.Enabled,
            PasswordHash = entity.PasswordHash,
            ExternalIdentities = DeserializeExternalIdentities(entity.ExternalIdentitiesJson)
        };
    }

    private static UserEntity ToEntity(AdminUserRecord model)
    {
        return new UserEntity
        {
            Email = model.Email,
            FullName = model.FullName,
            Enabled = model.Enabled,
            PasswordHash = model.PasswordHash,
            ExternalIdentitiesJson = SerializeExternalIdentities(model.ExternalIdentities)
        };
    }

    private static string SerializeExternalIdentities(IReadOnlyList<AdminUserExternalIdentity>? identities)
    {
        return JsonSerializer.Serialize(identities ?? []);
    }

    private static List<AdminUserExternalIdentity> DeserializeExternalIdentities(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];

        return JsonSerializer.Deserialize<List<AdminUserExternalIdentity>>(json) ?? [];
    }

    private static void EnsureSchema(QuickProxyDbContext db)
    {
        db.Database.OpenConnection();
        try
        {
            if (db.Database.IsSqlite())
            {
                db.Database.ExecuteSqlRaw("ALTER TABLE Users ADD COLUMN ExternalIdentitiesJson TEXT NULL");
                return;
            }

            db.Database.ExecuteSqlRaw("""
                                      IF COL_LENGTH('Users', 'ExternalIdentitiesJson') IS NULL
                                      BEGIN
                                          ALTER TABLE [Users] ADD [ExternalIdentitiesJson] nvarchar(max) NULL;
                                      END
                                      """);
        }
        catch
        {
            // Column already exists or provider-specific no-op.
        }
        finally
        {
            db.Database.CloseConnection();
        }
    }
}