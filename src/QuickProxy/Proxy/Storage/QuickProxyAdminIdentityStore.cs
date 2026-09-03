using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using Aditify.Identity;
using Microsoft.EntityFrameworkCore;
using QuickProxy.Audit;
using QuickProxy.Proxy.Storage.Db;

namespace QuickProxy.Proxy.Storage;

public sealed class QuickProxyAdminIdentityStore(IDbContextFactory<QuickProxyDbContext> factory) : IAdminIdentityStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public async Task<IReadOnlyList<AdminIdentityUser>> ListUsersAsync(CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var documents = await db.AdminIdentityUsers.AsNoTracking()
            .OrderBy(user => user.NormalizedUsername)
            .Select(user => user.Json)
            .ToArrayAsync(cancellationToken);
        return documents.Select(DeserializeUser).ToArray();
    }

    public async Task<AdminIdentityUser?> FindUserAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var document = await db.AdminIdentityUsers.AsNoTracking()
            .Where(user => user.Id == id)
            .Select(user => user.Json)
            .SingleOrDefaultAsync(cancellationToken);
        return document is null ? null : DeserializeUser(document);
    }

    public async Task<AdminIdentityUser?> FindUserByUsernameAsync(string normalizedUsername,
        CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var normalized = normalizedUsername.Trim().ToUpperInvariant();
        var document = await db.AdminIdentityUsers.AsNoTracking()
            .Where(user => user.NormalizedUsername == normalized)
            .Select(user => user.Json)
            .SingleOrDefaultAsync(cancellationToken);
        return document is null ? null : DeserializeUser(document);
    }

    public async Task SaveUserAsync(AdminIdentityUser user, CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var entity = await db.AdminIdentityUsers.SingleOrDefaultAsync(candidate => candidate.Id == user.Id,
            cancellationToken);
        var normalizedUsername = user.NormalizedUsername.Trim().ToUpperInvariant();
        var json = JsonSerializer.Serialize(user, JsonOptions);
        if (entity is null)
        {
            db.AdminIdentityUsers.Add(new AdminIdentityUserEntity
            {
                Id = user.Id,
                NormalizedUsername = normalizedUsername,
                Json = json
            });
        }
        else
        {
            entity.NormalizedUsername = normalizedUsername;
            entity.Json = json;
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteUserAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var entity = await db.AdminIdentityUsers.SingleOrDefaultAsync(user => user.Id == id, cancellationToken);
        if (entity is null) return;
        db.AdminIdentityUsers.Remove(entity);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<AdminIdentityProvider>> ListProvidersAsync(CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var documents = await db.AdminIdentityProviders.AsNoTracking()
            .OrderBy(provider => provider.Id)
            .Select(provider => provider.Json)
            .ToArrayAsync(cancellationToken);
        return documents.Select(DeserializeProvider).ToArray();
    }

    public async Task<AdminIdentityProvider?> FindProviderAsync(string id, CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var normalizedId = id.Trim().ToLowerInvariant();
        var document = await db.AdminIdentityProviders.AsNoTracking()
            .Where(provider => provider.Id == normalizedId)
            .Select(provider => provider.Json)
            .SingleOrDefaultAsync(cancellationToken);
        return document is null ? null : DeserializeProvider(document);
    }

    public async Task SaveProviderAsync(AdminIdentityProvider provider, CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var normalizedId = provider.Id.Trim().ToLowerInvariant();
        var entity = await db.AdminIdentityProviders.SingleOrDefaultAsync(candidate => candidate.Id == normalizedId,
            cancellationToken);
        var json = JsonSerializer.Serialize(provider, JsonOptions);
        if (entity is null)
            db.AdminIdentityProviders.Add(new AdminIdentityProviderEntity { Id = normalizedId, Json = json });
        else
            entity.Json = json;

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteProviderAsync(string id, CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var normalizedId = id.Trim().ToLowerInvariant();
        var entity = await db.AdminIdentityProviders.SingleOrDefaultAsync(provider => provider.Id == normalizedId,
            cancellationToken);
        if (entity is null) return;
        db.AdminIdentityProviders.Remove(entity);
        await db.SaveChangesAsync(cancellationToken);
    }

    private static AdminIdentityUser DeserializeUser(string json)
    {
        return JsonSerializer.Deserialize<AdminIdentityUser>(json, JsonOptions)
               ?? throw new InvalidOperationException("Stored administrator identity data is invalid.");
    }

    private static AdminIdentityProvider DeserializeProvider(string json)
    {
        return JsonSerializer.Deserialize<AdminIdentityProvider>(json, JsonOptions)
               ?? throw new InvalidOperationException("Stored identity provider data is invalid.");
    }
}

public static class QuickProxyRoles
{
    public const string Reader = "Reader";
    public const string Operator = "Operator";
    public const string Administrator = "Administrator";
}

public sealed class QuickProxyRoleCatalog : IProductRoleCatalog
{
    public IReadOnlyList<string> Roles { get; } =
        [QuickProxyRoles.Reader, QuickProxyRoles.Operator, QuickProxyRoles.Administrator];
}

public sealed class QuickProxyIdentityAuditSink(IAuditStore store) : IAdminIdentityAuditSink
{
    public Task WriteAsync(string action, string target, string outcome, ClaimsPrincipal? actor,
        CancellationToken cancellationToken)
    {
        store.Append(new AuditEvent
        {
            Module = "users",
            Action = action,
            TargetType = "identity",
            TargetId = target,
            Outcome = outcome.ToLowerInvariant(),
            Actor = new AuditActor
            {
                Id = actor?.FindFirstValue(ClaimTypes.NameIdentifier),
                DisplayName = actor?.Identity?.Name,
                Type = actor?.Identity?.IsAuthenticated == true ? "user" : "system"
            }
        });
        return Task.CompletedTask;
    }
}