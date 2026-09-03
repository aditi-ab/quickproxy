using System.Net;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using QuickProxy.Proxy.Api;
using QuickProxy.Proxy.Config.Models;
using QuickProxy.Proxy.Config.Storage;
using QuickProxy.Shared.Auth;
using QuickProxy.Shared.Web;

namespace QuickProxy.Proxy.Config.Api;

public static class ConfigsApiExtensions
{
    private static readonly JsonSerializerOptions RemoteJsonOptions = CreateRemoteJsonOptions();

    public static IEndpointRouteBuilder MapConfigsApi(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup($"{InternalApiPaths.AdminRoot}/configs").RequireAuthorization();

        group.MapGet("", async (string? prefix, IConfigReadService store, CancellationToken cancellationToken) =>
        {
            var entries = await store.ListAsync(prefix, cancellationToken);
            return Results.Ok(entries.Select(ToListEntrySummary));
        });

        group.MapGet("/tree", async (string? path, IConfigReadService store, CancellationToken cancellationToken) =>
        {
            var normalizedPath = ConfigKeyNormalizer.NormalizePrefix(path);
            var entries = string.IsNullOrWhiteSpace(normalizedPath)
                ? await store.ListAsync(cancellationToken: cancellationToken)
                : await store.ListAsync(normalizedPath, cancellationToken);
            return Results.Ok(BuildTree(entries, normalizedPath));
        });

        group.MapGet("/tree/{*path}",
            async (string path, IConfigReadService store, CancellationToken cancellationToken) =>
            {
                var normalizedPath = ConfigKeyNormalizer.NormalizePrefix(path);
                var entries = string.IsNullOrWhiteSpace(normalizedPath)
                    ? await store.ListAsync(cancellationToken: cancellationToken)
                    : await store.ListAsync(normalizedPath, cancellationToken);
                return Results.Ok(BuildTree(entries, normalizedPath));
            });

        group.MapGet("/export", (ILocalConfigStore store) =>
        {
            var entries = store.List()
                .Select(ToBackupEntry)
                .Where(entry => !string.IsNullOrWhiteSpace(entry.Key))
                .OrderBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var revisions = store.ListAllRevisions()
                .Select(ToBackupRevision)
                .Where(revision => !string.IsNullOrWhiteSpace(revision.Key))
                .OrderByDescending(revision => revision.CapturedAtUtc)
                .ThenByDescending(revision => revision.RevisionId, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            return Results.Ok(new ConfigBackupDocument(3, DateTimeOffset.UtcNow, entries, revisions));
        });

        group.MapPost("/restore",
            (ConfigBackupDocument document, ILocalConfigStore store, ClaimsPrincipal principal, IUserStore users) =>
            {
                if (document.FormatVersion is not (1 or 2 or 3))
                    return Validation([$"Unsupported backup formatVersion '{document.FormatVersion}'."]);

                var currentUser = ResolveCurrentUser(principal, users);
                var normalizedEntries =
                    ValidateAndNormalizeBackupEntries(document.Entries, currentUser?.Email, out var details);
                var normalizedRevisions = ValidateAndNormalizeBackupRevisions(document.Revisions, currentUser?.Email,
                    out var revisionDetails);
                details.AddRange(revisionDetails);
                if (details.Count > 0) return Validation(details);

                if (document.FormatVersion >= 3)
                    store.ReplaceAll(normalizedEntries, normalizedRevisions);
                else
                    store.ReplaceAll(normalizedEntries);

                return Results.Ok(new { restored = normalizedEntries.Count });
            });

        group.MapPost("/import-remote", async (
            ImportRemoteConfigsRequest request,
            IHttpClientFactory httpClientFactory,
            ILocalConfigStore store,
            ClaimsPrincipal principal,
            IUserStore users,
            CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(request.Url)) return Validation(["url is required."]);

            var currentUser = ResolveCurrentUser(principal, users);

            try
            {
                var remoteEntries = await ReadRemotePublicEntriesAsync(httpClientFactory, request.Url,
                    currentUser?.Email, cancellationToken);
                store.ReplaceAll(remoteEntries);
                return Results.Ok(new { imported = remoteEntries.Count });
            }
            catch (RemoteConfigImportValidationException ex)
            {
                return Validation([ex.Message]);
            }
            catch (Exception ex)
            {
                if (ex is OperationCanceledException && cancellationToken.IsCancellationRequested) throw;

                return Results.BadRequest(new
                {
                    code = "remote_import_failed",
                    message = "Remote import failed.",
                    details = new[] { ex.Message }
                });
            }
        });

        group.MapGet("/reveal/{*key}", async (
            string key,
            string? source,
            IConfigReadService readService,
            ILocalConfigStore localStore,
            IRemoteConfigStore remoteStore,
            IConfigEncryptionService encryptionService,
            CancellationToken cancellationToken) =>
        {
            var normalizedKey = ConfigKeyNormalizer.NormalizeKey(key);
            if (string.IsNullOrWhiteSpace(normalizedKey)) return Validation(["key is required."]);

            var sourceName = (source ?? string.Empty).Trim().ToLowerInvariant();
            if (sourceName is not ("local" or "remote" or ""))
                return Validation(["source must be 'local' or 'remote' when provided."]);

            var entry = sourceName switch
            {
                "local" => localStore.Get(normalizedKey),
                "remote" => await remoteStore.GetAsync(normalizedKey, cancellationToken),
                _ => await readService.GetAsync(normalizedKey, cancellationToken) is { } merged
                    ? ToConfigEntry(merged)
                    : null
            };

            return entry is null
                ? Results.NotFound(new { code = "not_found", message = $"Config '{normalizedKey}' was not found." })
                : Results.Ok(RevealVariant(entry, encryptionService));
        });

        group.MapGet("/revisions/{*key}", (string key, ILocalConfigStore store) =>
        {
            var normalizedKey = ConfigKeyNormalizer.NormalizeKey(key);
            if (string.IsNullOrWhiteSpace(normalizedKey)) return Validation(["key is required."]);

            return Results.Ok(store.ListRevisions(normalizedKey).Select(ToRevisionSummary));
        });

        group.MapGet("/revisions/item/{revisionId}/{*key}", (
            string key,
            string revisionId,
            bool? reveal,
            ILocalConfigStore store,
            IConfigEncryptionService encryptionService) =>
        {
            var normalizedKey = ConfigKeyNormalizer.NormalizeKey(key);
            if (string.IsNullOrWhiteSpace(normalizedKey)) return Validation(["key is required."]);

            var revision = store.GetRevision(normalizedKey, revisionId);
            if (revision is null)
                return Results.NotFound(new
                    { code = "not_found", message = $"Revision '{revisionId}' was not found for '{normalizedKey}'." });

            var snapshot = reveal == true
                ? RevealVariant(ToConfigEntry(revision), encryptionService)
                : ToVariant(ToConfigEntry(revision));
            return Results.Ok(new ConfigEntryRevisionDetails
            {
                RevisionId = revision.RevisionId,
                Key = revision.Key,
                CapturedAtUtc = revision.CapturedAtUtc,
                CapturedBy = revision.CapturedBy,
                Action = revision.Action,
                Snapshot = snapshot
            });
        });

        group.MapPost("/revisions/restore/{revisionId}/{*key}", (
            string key,
            string revisionId,
            ILocalConfigStore store,
            ClaimsPrincipal principal,
            IUserStore users) =>
        {
            var normalizedKey = ConfigKeyNormalizer.NormalizeKey(key);
            if (string.IsNullOrWhiteSpace(normalizedKey)) return Validation(["key is required."]);

            var currentUser = ResolveCurrentUser(principal, users);
            var restored = store.RestoreRevision(normalizedKey, revisionId, DateTimeOffset.UtcNow, currentUser?.Email);
            return restored is null
                ? Results.NotFound(new
                    { code = "not_found", message = $"Revision '{revisionId}' was not found for '{normalizedKey}'." })
                : Results.Ok(restored);
        });

        group.MapGet("/{*key}", async (
            string key,
            IConfigReadService store,
            ILocalConfigStore localStore,
            IRemoteConfigStore remoteStore,
            CancellationToken cancellationToken) =>
        {
            var normalizedKey = ConfigKeyNormalizer.NormalizeKey(key);
            var entry = await store.GetAsync(normalizedKey, cancellationToken);
            return entry is null
                ? Results.NotFound(new { code = "not_found", message = $"Config '{normalizedKey}' was not found." })
                : Results.Ok(
                    await ToEntryDetailsAsync(normalizedKey, entry, localStore, remoteStore, cancellationToken));
        });

        group.MapPut("/{*key}",
            (string key, UpsertConfigRequest request, ILocalConfigStore store, ClaimsPrincipal principal,
                IUserStore users) =>
            {
                var normalizedKey = ConfigKeyNormalizer.NormalizeKey(key);
                if (string.IsNullOrWhiteSpace(normalizedKey)) return Validation(["key is required."]);

                var currentUser = ResolveCurrentUser(principal, users);
                var entry = new ConfigEntry
                {
                    Key = normalizedKey,
                    Value = request.Value ?? string.Empty,
                    BinaryBase64 = request.BinaryBase64,
                    MediaType = request.MediaType,
                    EntryType = request.EntryType,
                    PayloadKind = request.PayloadKind,
                    Labels = request.Labels ?? [],
                    UpdatedAtUtc = DateTimeOffset.UtcNow,
                    UpdatedBy = currentUser?.Email
                };

                store.Upsert(entry);
                return Results.Ok(entry);
            });

        group.MapPost("/create-override/{*key}", async (
            string key,
            IConfigReadService readService,
            ILocalConfigStore localStore,
            ClaimsPrincipal principal,
            IUserStore users,
            CancellationToken cancellationToken) =>
        {
            var normalizedKey = ConfigKeyNormalizer.NormalizeKey(key);
            if (string.IsNullOrWhiteSpace(normalizedKey)) return Validation(["key is required."]);

            var localEntry = localStore.Get(normalizedKey);
            if (localEntry is not null) return Results.Ok(ToMergedEntry(localEntry, false));

            var entry = await readService.GetAsync(normalizedKey, cancellationToken);
            if (entry is null)
                return Results.NotFound(
                    new { code = "not_found", message = $"Config '{normalizedKey}' was not found." });

            var currentUser = ResolveCurrentUser(principal, users);
            var createdEntry = ToConfigEntry(entry);
            createdEntry.Key = normalizedKey;
            createdEntry.UpdatedAtUtc = DateTimeOffset.UtcNow;
            createdEntry.UpdatedBy = currentUser?.Email;

            localStore.Upsert(createdEntry);
            return Results.Ok(ToMergedEntry(createdEntry, true));
        });

        group.MapDelete("/{*key}",
            async (string key, ILocalConfigStore store, IConfigReadService readService,
                CancellationToken cancellationToken) =>
            {
                var normalizedKey = ConfigKeyNormalizer.NormalizeKey(key);
                var existing = await readService.GetAsync(normalizedKey, cancellationToken);
                if (existing is null)
                    return Results.NotFound(new
                        { code = "not_found", message = $"Config '{normalizedKey}' was not found." });

                if (existing.ReadOnly) return ReadOnlyValidation(normalizedKey);

                return store.Delete(normalizedKey)
                    ? Results.NoContent()
                    : Results.NotFound(new
                        { code = "not_found", message = $"Config '{normalizedKey}' was not found." });
            });

        group.MapPost("/rename-key", RenameKey);
        group.MapPost("/rename-folder", RenameFolder);
        group.MapPost("/move", MoveEntries);
        group.MapPost("/copy", CopyEntries);

        return app;
    }

    private static async Task<IResult> RenameKey(
        RenameKeyRequest request,
        IConfigReadService readService,
        ILocalConfigStore store,
        ClaimsPrincipal principal,
        IUserStore users,
        CancellationToken cancellationToken)
    {
        var fromKey = ConfigKeyNormalizer.NormalizeKey(request.FromKey);
        var toKey = ConfigKeyNormalizer.NormalizeKey(request.ToKey);
        if (string.IsNullOrWhiteSpace(fromKey)) return Validation(["fromKey is required."]);

        if (string.IsNullOrWhiteSpace(toKey)) return Validation(["toKey is required."]);

        if (string.Equals(fromKey, toKey, StringComparison.OrdinalIgnoreCase)) return Results.Ok(new { renamed = 0 });

        var allLocalEntries = store.List();
        var localEntryMap = allLocalEntries.ToDictionary(entry => ConfigKeyNormalizer.NormalizeKey(entry.Key),
            StringComparer.OrdinalIgnoreCase);
        if (!localEntryMap.TryGetValue(fromKey, out var sourceEntry))
            return Results.NotFound(new { code = "not_found", message = $"Config '{fromKey}' was not found." });

        var mergedSourceEntry = await readService.GetAsync(fromKey, cancellationToken);
        if (mergedSourceEntry?.ReadOnly == true) return ReadOnlyValidation(fromKey);

        var hasChildren = allLocalEntries.Any(entry =>
            ConfigKeyNormalizer.NormalizeKey(entry.Key).StartsWith($"{fromKey}/", StringComparison.OrdinalIgnoreCase));
        if (hasChildren)
            return Validation([
                $"Cannot rename '{fromKey}' as a key because it has child entries. Rename the folder instead."
            ]);

        if (await readService.GetAsync(toKey, cancellationToken) is not null)
            return Validation([$"Rename would overwrite existing key '{toKey}'."]);

        var currentUser = ResolveCurrentUser(principal, users);
        store.MoveRevisionHistory(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [fromKey] = toKey
        }, DateTimeOffset.UtcNow, currentUser?.Email);

        return Results.Ok(new { renamed = 1 });
    }

    private static async Task<IResult> RenameFolder(
        RenameFolderRequest request,
        IConfigReadService readService,
        ILocalConfigStore store,
        ClaimsPrincipal principal,
        IUserStore users,
        CancellationToken cancellationToken)
    {
        var fromPrefix = ConfigKeyNormalizer.NormalizePrefix(request.FromPath);
        var toPrefix = ConfigKeyNormalizer.NormalizePrefix(request.ToPath);
        if (string.IsNullOrWhiteSpace(fromPrefix)) return Validation(["fromPath is required."]);

        if (string.IsNullOrWhiteSpace(toPrefix)) return Validation(["toPath is required."]);

        if (string.Equals(fromPrefix, toPrefix, StringComparison.OrdinalIgnoreCase))
            return Results.Ok(new { renamed = 0 });

        if (toPrefix.StartsWith(fromPrefix, StringComparison.OrdinalIgnoreCase))
            return Validation(["Cannot rename a folder into its own descendant path."]);

        var allLocalEntries = store.List();
        var affectedEntries = allLocalEntries
            .Where(entry => entry.Key.StartsWith(fromPrefix, StringComparison.OrdinalIgnoreCase)).ToList();
        if (affectedEntries.Count == 0)
        {
            var anyMergedEntries = (await readService.ListAsync(fromPrefix, cancellationToken))
                .Any(entry => entry.Key.StartsWith(fromPrefix, StringComparison.OrdinalIgnoreCase));
            return anyMergedEntries
                ? ReadOnlyValidation(fromPrefix.TrimEnd('/'))
                : Results.NotFound(new { code = "not_found", message = $"Folder '{fromPrefix}' was not found." });
        }

        var sourceKeys =
            new HashSet<string>(affectedEntries.Select(entry => ConfigKeyNormalizer.NormalizeKey(entry.Key)),
                StringComparer.OrdinalIgnoreCase);
        var renameMap = new Dictionary<string, ConfigEntry>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in affectedEntries)
        {
            var suffix = entry.Key[fromPrefix.Length..];
            var renamedKey = ConfigKeyNormalizer.NormalizeKey($"{toPrefix}{suffix}");
            if (renameMap.ContainsKey(renamedKey))
                return Validation([$"Rename would create duplicate key '{renamedKey}'."]);

            var updated = ToConfigEntry(entry);
            updated.Key = renamedKey;
            renameMap[renamedKey] = updated;
        }

        var mergedEntries = await readService.ListAsync(cancellationToken: cancellationToken);
        foreach (var entry in mergedEntries)
        {
            var normalizedKey = ConfigKeyNormalizer.NormalizeKey(entry.Key);
            if (renameMap.ContainsKey(normalizedKey) && !sourceKeys.Contains(normalizedKey))
                return Validation([$"Rename would overwrite existing key '{entry.Key}'."]);
        }

        var currentUser = ResolveCurrentUser(principal, users);
        var revisionMoveMap = affectedEntries.ToDictionary(
            entry => ConfigKeyNormalizer.NormalizeKey(entry.Key),
            entry =>
            {
                var suffix = entry.Key[fromPrefix.Length..];
                return ConfigKeyNormalizer.NormalizeKey($"{toPrefix}{suffix}");
            },
            StringComparer.OrdinalIgnoreCase);
        store.MoveRevisionHistory(revisionMoveMap, DateTimeOffset.UtcNow, currentUser?.Email);

        return Results.Ok(new { renamed = affectedEntries.Count });
    }

    private static async Task<IResult> MoveEntries(
        MoveConfigsRequest request,
        IConfigReadService readService,
        ILocalConfigStore store,
        ClaimsPrincipal principal,
        IUserStore users,
        CancellationToken cancellationToken)
    {
        var requestedPaths = request.Keys ?? [];
        if (requestedPaths.Count == 0) return Validation(["keys is required."]);

        var normalizedPaths = requestedPaths
            .Select(ConfigKeyNormalizer.NormalizeKey)
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (normalizedPaths.Count == 0) return Validation(["keys must contain at least one valid key."]);

        var targetPrefix = ConfigKeyNormalizer.NormalizePrefix(request.TargetFolder);
        var preserveSourceNames = request.PreserveSourceNames;
        var allEntries = store.List();
        var entryMap = allEntries.ToDictionary(entry => ConfigKeyNormalizer.NormalizeKey(entry.Key),
            StringComparer.OrdinalIgnoreCase);
        var folderPrefixes = normalizedPaths
            .Where(path => allEntries.Any(entry =>
                ConfigKeyNormalizer.NormalizeKey(entry.Key).StartsWith($"{path}/", StringComparison.OrdinalIgnoreCase)))
            .ToList();

        foreach (var path in normalizedPaths)
        {
            var mergedEntry = await readService.GetAsync(path, cancellationToken);
            var isFolder = folderPrefixes.Contains(path, StringComparer.OrdinalIgnoreCase);
            if (mergedEntry?.ReadOnly == true) return ReadOnlyValidation(path);

            if (isFolder)
            {
                var prefix = $"{path}/";
                var hasReadOnlyChildren = (await readService.ListAsync(prefix, cancellationToken))
                    .Any(entry => entry.ReadOnly);
                if (hasReadOnlyChildren) return ReadOnlyValidation(path);
            }
        }

        var missingPaths = normalizedPaths
            .Where(path =>
                !entryMap.ContainsKey(path) && !folderPrefixes.Contains(path, StringComparer.OrdinalIgnoreCase))
            .ToList();
        if (missingPaths.Count > 0)
            return Results.NotFound(new
            {
                code = "not_found",
                message = "One or more keys were not found.",
                details = missingPaths.Select(path => $"Config '{path}' was not found.").ToList()
            });

        var sourceEntries = new List<ConfigEntry>();
        foreach (var path in normalizedPaths)
        {
            if (folderPrefixes.Contains(path, StringComparer.OrdinalIgnoreCase))
            {
                var prefix = $"{path}/";
                sourceEntries.AddRange(allEntries.Where(entry =>
                {
                    var normalizedKey = ConfigKeyNormalizer.NormalizeKey(entry.Key);
                    return normalizedKey.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
                }));
                continue;
            }

            sourceEntries.Add(entryMap[path]);
        }

        sourceEntries = sourceEntries
            .GroupBy(entry => ConfigKeyNormalizer.NormalizeKey(entry.Key), StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();
        var sourceKeys = new HashSet<string>(sourceEntries.Select(entry => ConfigKeyNormalizer.NormalizeKey(entry.Key)),
            StringComparer.OrdinalIgnoreCase);
        var moveMap = new Dictionary<string, ConfigEntry>(StringComparer.OrdinalIgnoreCase);

        foreach (var sourceEntry in sourceEntries)
        {
            var sourceKey = ConfigKeyNormalizer.NormalizeKey(sourceEntry.Key);
            var containingFolder = folderPrefixes
                .Where(prefix => sourceKey.StartsWith($"{prefix}/", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(prefix => prefix.Length)
                .FirstOrDefault();

            var movedKey = BuildRelocatedKey(sourceKey, containingFolder, targetPrefix, preserveSourceNames);
            if (string.IsNullOrWhiteSpace(movedKey))
                return Validation([$"Invalid moved key for source '{sourceKey}'."]);

            if (moveMap.ContainsKey(movedKey)) return Validation([$"Move would create duplicate key '{movedKey}'."]);

            var updated = ToConfigEntry(sourceEntry);
            updated.Key = movedKey;
            moveMap[movedKey] = updated;
        }

        var mergedEntries = await readService.ListAsync(cancellationToken: cancellationToken);
        foreach (var entry in mergedEntries)
        {
            var normalizedExisting = ConfigKeyNormalizer.NormalizeKey(entry.Key);
            if (moveMap.ContainsKey(normalizedExisting) && !sourceKeys.Contains(normalizedExisting))
                return Validation([$"Move would overwrite existing key '{entry.Key}'."]);
        }

        var currentUser = ResolveCurrentUser(principal, users);
        var moveRevisionMap = sourceEntries.ToDictionary(
            entry => ConfigKeyNormalizer.NormalizeKey(entry.Key),
            entry =>
            {
                var sourceKey = ConfigKeyNormalizer.NormalizeKey(entry.Key);
                var containingFolder = folderPrefixes
                    .Where(prefix => sourceKey.StartsWith($"{prefix}/", StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(prefix => prefix.Length)
                    .FirstOrDefault();

                return BuildRelocatedKey(sourceKey, containingFolder, targetPrefix, preserveSourceNames);
            },
            StringComparer.OrdinalIgnoreCase);
        store.MoveRevisionHistory(moveRevisionMap, DateTimeOffset.UtcNow, currentUser?.Email);

        return Results.Ok(new { moved = sourceEntries.Count });
    }

    private static async Task<IResult> CopyEntries(
        MoveConfigsRequest request,
        IConfigReadService readService,
        ILocalConfigStore store,
        ClaimsPrincipal principal,
        IUserStore users,
        CancellationToken cancellationToken)
    {
        var requestedPaths = request.Keys ?? [];
        if (requestedPaths.Count == 0) return Validation(["keys is required."]);

        var normalizedPaths = requestedPaths
            .Select(ConfigKeyNormalizer.NormalizeKey)
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (normalizedPaths.Count == 0) return Validation(["keys must contain at least one valid key."]);

        var targetPrefix = ConfigKeyNormalizer.NormalizePrefix(request.TargetFolder);
        var preserveSourceNames = request.PreserveSourceNames;
        var allEntries = store.List();
        var entryMap = allEntries.ToDictionary(entry => ConfigKeyNormalizer.NormalizeKey(entry.Key),
            StringComparer.OrdinalIgnoreCase);
        var folderPrefixes = normalizedPaths
            .Where(path => allEntries.Any(entry =>
                ConfigKeyNormalizer.NormalizeKey(entry.Key).StartsWith($"{path}/", StringComparison.OrdinalIgnoreCase)))
            .ToList();

        foreach (var path in normalizedPaths)
        {
            var mergedEntry = await readService.GetAsync(path, cancellationToken);
            var isFolder = folderPrefixes.Contains(path, StringComparer.OrdinalIgnoreCase);
            if (mergedEntry?.ReadOnly == true) return ReadOnlyValidation(path);

            if (isFolder)
            {
                var prefix = $"{path}/";
                var hasReadOnlyChildren = (await readService.ListAsync(prefix, cancellationToken))
                    .Any(entry => entry.ReadOnly);
                if (hasReadOnlyChildren) return ReadOnlyValidation(path);
            }
        }

        var missingPaths = normalizedPaths
            .Where(path =>
                !entryMap.ContainsKey(path) && !folderPrefixes.Contains(path, StringComparer.OrdinalIgnoreCase))
            .ToList();
        if (missingPaths.Count > 0)
            return Results.NotFound(new
            {
                code = "not_found",
                message = "One or more keys were not found.",
                details = missingPaths.Select(path => $"Config '{path}' was not found.").ToList()
            });

        var sourceEntries = new List<ConfigEntry>();
        foreach (var path in normalizedPaths)
        {
            if (folderPrefixes.Contains(path, StringComparer.OrdinalIgnoreCase))
            {
                var prefix = $"{path}/";
                sourceEntries.AddRange(allEntries.Where(entry =>
                {
                    var normalizedKey = ConfigKeyNormalizer.NormalizeKey(entry.Key);
                    return normalizedKey.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
                }));
                continue;
            }

            sourceEntries.Add(entryMap[path]);
        }

        sourceEntries = sourceEntries
            .GroupBy(entry => ConfigKeyNormalizer.NormalizeKey(entry.Key), StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();
        var copyMap = new Dictionary<string, ConfigEntry>(StringComparer.OrdinalIgnoreCase);
        var currentUser = ResolveCurrentUser(principal, users);

        foreach (var sourceEntry in sourceEntries)
        {
            var sourceKey = ConfigKeyNormalizer.NormalizeKey(sourceEntry.Key);
            var containingFolder = folderPrefixes
                .Where(prefix => sourceKey.StartsWith($"{prefix}/", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(prefix => prefix.Length)
                .FirstOrDefault();

            var copiedKey = BuildRelocatedKey(sourceKey, containingFolder, targetPrefix, preserveSourceNames);
            if (string.IsNullOrWhiteSpace(copiedKey))
                return Validation([$"Invalid copied key for source '{sourceKey}'."]);

            if (copyMap.ContainsKey(copiedKey)) return Validation([$"Copy would create duplicate key '{copiedKey}'."]);

            var copied = ToConfigEntry(sourceEntry);
            copied.Key = copiedKey;
            copied.UpdatedAtUtc = DateTimeOffset.UtcNow;
            copied.UpdatedBy = currentUser?.Email;
            copyMap[copiedKey] = copied;
        }

        var mergedEntries = await readService.ListAsync(cancellationToken: cancellationToken);
        foreach (var entry in mergedEntries)
        {
            var normalizedExisting = ConfigKeyNormalizer.NormalizeKey(entry.Key);
            if (copyMap.ContainsKey(normalizedExisting))
                return Validation([$"Copy would overwrite existing key '{entry.Key}'."]);
        }

        foreach (var copied in copyMap.Values) store.Upsert(copied);

        return Results.Ok(new { copied = copyMap.Count });
    }

    private static string GetLeafName(string key)
    {
        var normalized = ConfigKeyNormalizer.NormalizeKey(key);
        var slashIndex = normalized.LastIndexOf('/');
        return slashIndex < 0 ? normalized : normalized[(slashIndex + 1)..];
    }

    private static string BuildRelocatedKey(string sourceKey, string? selectedFolder, string targetPrefix,
        bool preserveSourceName)
    {
        if (selectedFolder is null) return ConfigKeyNormalizer.NormalizeKey($"{targetPrefix}{GetLeafName(sourceKey)}");

        var normalizedFolder = ConfigKeyNormalizer.NormalizeKey(selectedFolder);
        if (string.IsNullOrWhiteSpace(normalizedFolder)) return ConfigKeyNormalizer.NormalizeKey(sourceKey);

        if (preserveSourceName)
        {
            var parentPrefix = ConfigKeyNormalizer.NormalizeKey(GetParentPath(normalizedFolder));
            var preservedRelativePath = string.IsNullOrWhiteSpace(parentPrefix)
                ? sourceKey
                : sourceKey[(parentPrefix.Length + 1)..];

            return string.IsNullOrWhiteSpace(targetPrefix)
                ? ConfigKeyNormalizer.NormalizeKey(preservedRelativePath)
                : ConfigKeyNormalizer.NormalizeKey($"{targetPrefix}/{preservedRelativePath}");
        }

        if (string.IsNullOrWhiteSpace(targetPrefix)) return sourceKey[(normalizedFolder.Length + 1)..];

        var relativePath = sourceKey[(normalizedFolder.Length + 1)..];
        return string.IsNullOrWhiteSpace(relativePath)
            ? ConfigKeyNormalizer.NormalizeKey(targetPrefix)
            : ConfigKeyNormalizer.NormalizeKey($"{targetPrefix}/{relativePath}");
    }

    private static string GetParentPath(string key)
    {
        var normalized = ConfigKeyNormalizer.NormalizeKey(key);
        var slashIndex = normalized.LastIndexOf('/');
        return slashIndex < 0 ? string.Empty : normalized[..slashIndex];
    }

    private static AuthApiExtensions.AdminUserResponse? ResolveCurrentUser(ClaimsPrincipal principal, IUserStore users)
    {
        if (principal.Identity?.IsAuthenticated != true) return null;

        var email = principal.FindFirstValue(ClaimTypes.Email)
                    ?? principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(email)) return null;

        var user = users.GetByEmail(email);
        return user is null || !user.Enabled
            ? null
            : new AuthApiExtensions.AdminUserResponse(user.Email, user.FullName, user.Enabled, "local", null,
                !string.IsNullOrWhiteSpace(user.PasswordHash), user.ExternalIdentities.Count);
    }

    private static IResult Validation(List<string> details)
    {
        return Results.BadRequest(new
        {
            code = "validation_error",
            message = "Validation failed.",
            details
        });
    }

    private static IResult ReadOnlyValidation(string key)
    {
        return Results.BadRequest(new
        {
            code = "read_only_remote",
            message = $"Config '{key}' is read-only because it comes from the remote master store."
        });
    }

    private static ConfigBackupEntry ToBackupEntry(ConfigEntry entry)
    {
        var normalized = ConfigEntrySerializer.Normalize(entry);
        return new ConfigBackupEntry(
            normalized.Key,
            normalized.EntryType,
            normalized.PayloadKind,
            normalized.Value,
            normalized.BinaryBase64,
            normalized.EncryptedValue,
            normalized.EncryptedBinaryBase64,
            normalized.EncryptedLabels,
            normalized.MediaType,
            normalized.Labels);
    }

    private static ConfigBackupRevision ToBackupRevision(ConfigEntryRevision revision)
    {
        var normalized = ConfigEntrySerializer.Normalize(new ConfigEntry
        {
            Key = revision.Key,
            Value = revision.Value,
            BinaryBase64 = revision.BinaryBase64,
            EncryptedValue = revision.EncryptedValue,
            EncryptedBinaryBase64 = revision.EncryptedBinaryBase64,
            EncryptedLabels = revision.EncryptedLabels,
            MediaType = revision.MediaType,
            EntryType = revision.EntryType,
            PayloadKind = revision.PayloadKind,
            Labels = revision.Labels,
            UpdatedAtUtc = revision.UpdatedAtUtc,
            UpdatedBy = revision.UpdatedBy
        });

        return new ConfigBackupRevision(
            string.IsNullOrWhiteSpace(revision.RevisionId) ? Guid.NewGuid().ToString("N") : revision.RevisionId.Trim(),
            normalized.Key,
            normalized.EntryType,
            normalized.PayloadKind,
            normalized.Value,
            normalized.BinaryBase64,
            normalized.EncryptedValue,
            normalized.EncryptedBinaryBase64,
            normalized.EncryptedLabels,
            normalized.MediaType,
            normalized.Labels,
            normalized.UpdatedAtUtc,
            normalized.UpdatedBy,
            revision.CapturedAtUtc,
            revision.CapturedBy,
            string.IsNullOrWhiteSpace(revision.Action) ? "update" : revision.Action.Trim());
    }

    private static List<ConfigEntry> ValidateAndNormalizeBackupEntries(
        IReadOnlyList<ConfigBackupEntry>? entries,
        string? updatedBy,
        out List<string> details)
    {
        details = [];
        if (entries is null)
        {
            details.Add("entries is required.");
            return [];
        }

        var utcNow = DateTimeOffset.UtcNow;
        var normalizedEntries = new List<ConfigEntry>(entries.Count);
        var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            var normalizedKey = ConfigKeyNormalizer.NormalizeKey(entry.Key);
            if (string.IsNullOrWhiteSpace(normalizedKey))
            {
                details.Add($"entries[{i}].key is required.");
                continue;
            }

            if (!seenKeys.Add(normalizedKey))
            {
                details.Add($"Duplicate key '{normalizedKey}' was provided.");
                continue;
            }

            try
            {
                normalizedEntries.Add(ConfigEntrySerializer.Normalize(new ConfigEntry
                {
                    Key = normalizedKey,
                    EntryType = entry.EntryType,
                    PayloadKind = entry.PayloadKind,
                    Value = entry.Value ?? string.Empty,
                    BinaryBase64 = entry.BinaryBase64,
                    EncryptedValue = entry.EncryptedValue,
                    EncryptedBinaryBase64 = entry.EncryptedBinaryBase64,
                    EncryptedLabels = entry.EncryptedLabels,
                    MediaType = entry.MediaType,
                    Labels = entry.Labels ?? [],
                    UpdatedAtUtc = utcNow,
                    UpdatedBy = updatedBy
                }));
            }
            catch (Exception ex)
            {
                details.Add($"entries[{i}] is invalid: {ex.Message}");
            }
        }

        return normalizedEntries;
    }

    private static List<ConfigEntryRevision> ValidateAndNormalizeBackupRevisions(
        IReadOnlyList<ConfigBackupRevision>? revisions,
        string? updatedBy,
        out List<string> details)
    {
        details = [];
        if (revisions is null) return [];

        var normalizedRevisions = new List<ConfigEntryRevision>(revisions.Count);
        var seenRevisionIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < revisions.Count; i++)
        {
            var revision = revisions[i];
            var normalizedKey = ConfigKeyNormalizer.NormalizeKey(revision.Key);
            if (string.IsNullOrWhiteSpace(normalizedKey))
            {
                details.Add($"revisions[{i}].key is required.");
                continue;
            }

            var revisionId = string.IsNullOrWhiteSpace(revision.RevisionId)
                ? Guid.NewGuid().ToString("N")
                : revision.RevisionId.Trim();
            if (!seenRevisionIds.Add(revisionId))
            {
                details.Add($"Duplicate revisionId '{revisionId}' was provided.");
                continue;
            }

            try
            {
                var normalizedEntry = ConfigEntrySerializer.Normalize(new ConfigEntry
                {
                    Key = normalizedKey,
                    EntryType = revision.EntryType,
                    PayloadKind = revision.PayloadKind,
                    Value = revision.Value ?? string.Empty,
                    BinaryBase64 = revision.BinaryBase64,
                    EncryptedValue = revision.EncryptedValue,
                    EncryptedBinaryBase64 = revision.EncryptedBinaryBase64,
                    EncryptedLabels = revision.EncryptedLabels,
                    MediaType = revision.MediaType,
                    Labels = revision.Labels ?? [],
                    UpdatedAtUtc = revision.UpdatedAtUtc,
                    UpdatedBy = revision.UpdatedBy ?? updatedBy
                });

                normalizedRevisions.Add(new ConfigEntryRevision
                {
                    RevisionId = revisionId,
                    Key = normalizedEntry.Key,
                    EntryType = normalizedEntry.EntryType,
                    PayloadKind = normalizedEntry.PayloadKind,
                    Value = normalizedEntry.Value,
                    BinaryBase64 = normalizedEntry.BinaryBase64,
                    EncryptedValue = normalizedEntry.EncryptedValue,
                    EncryptedBinaryBase64 = normalizedEntry.EncryptedBinaryBase64,
                    EncryptedLabels = normalizedEntry.EncryptedLabels,
                    MediaType = normalizedEntry.MediaType,
                    Labels = normalizedEntry.Labels,
                    UpdatedAtUtc = normalizedEntry.UpdatedAtUtc,
                    UpdatedBy = normalizedEntry.UpdatedBy,
                    CapturedAtUtc = revision.CapturedAtUtc,
                    CapturedBy = revision.CapturedBy ?? updatedBy,
                    Action = string.IsNullOrWhiteSpace(revision.Action) ? "update" : revision.Action.Trim()
                });
            }
            catch (Exception ex)
            {
                details.Add($"revisions[{i}] is invalid: {ex.Message}");
            }
        }

        return normalizedRevisions;
    }

    private static async Task<List<ConfigEntry>> ReadRemotePublicEntriesAsync(
        IHttpClientFactory httpClientFactory,
        string sourceUrl,
        string? updatedBy,
        CancellationToken cancellationToken)
    {
        using var client = httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(30);

        var endpoint = BuildRemoteImportUri(sourceUrl);
        var response = await client.GetAsync(endpoint, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
            throw new RemoteConfigImportValidationException($"Remote endpoint '{endpoint}' was not found.");

        if (!response.IsSuccessStatusCode)
            throw new RemoteConfigImportValidationException(
                $"Remote endpoint '{endpoint}' returned status {(int)response.StatusCode}.");

        var payload =
            await response.Content.ReadFromJsonAsync<List<RemotePublicConfigEntry>>(RemoteJsonOptions,
                cancellationToken) ?? [];
        if (payload.Count == 0)
            throw new RemoteConfigImportValidationException("Remote source did not return any key/value entries.");

        var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var utcNow = DateTimeOffset.UtcNow;
        var entries = new List<ConfigEntry>(payload.Count);

        foreach (var item in payload)
        {
            var normalizedKey = ConfigKeyNormalizer.NormalizeKey(item.Key);
            if (string.IsNullOrWhiteSpace(normalizedKey)) continue;

            if (!seenKeys.Add(normalizedKey))
                throw new RemoteConfigImportValidationException(
                    $"Remote source returned duplicate key '{normalizedKey}'.");

            entries.Add(ConfigEntrySerializer.Normalize(new ConfigEntry
            {
                Key = normalizedKey,
                Value = item.Value ?? string.Empty,
                BinaryBase64 = item.BinaryBase64,
                EncryptedValue = item.EncryptedValue,
                EncryptedBinaryBase64 = item.EncryptedBinaryBase64,
                EncryptedLabels = item.EncryptedLabels,
                MediaType = item.MediaType,
                EntryType = item.EntryType,
                PayloadKind = item.PayloadKind,
                Labels = item.Labels ?? [],
                UpdatedAtUtc = utcNow,
                UpdatedBy = updatedBy
            }));
        }

        if (entries.Count == 0)
            throw new RemoteConfigImportValidationException(
                "Remote source did not contain any valid key/value entries.");

        return entries;
    }

    private static Uri BuildRemoteImportUri(string sourceUrl)
    {
        const string adminConfigsSuffix = "/internal-api/admin/configs";

        var trimmed = sourceUrl?.Trim() ?? string.Empty;
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
            throw new RemoteConfigImportValidationException("url must be an absolute URL.");

        var builder = new UriBuilder(uri) { Query = string.Empty };
        var normalizedPath = (builder.Path ?? string.Empty).TrimEnd('/');
        if (normalizedPath.EndsWith($"{InternalApiPaths.Root}/config", StringComparison.OrdinalIgnoreCase))
            builder.Path = $"{normalizedPath[..^"/config".Length]}/config-export";
        else if (normalizedPath.EndsWith($"{InternalApiPaths.Root}/config-export", StringComparison.OrdinalIgnoreCase))
            builder.Path = normalizedPath;
        else if (normalizedPath.EndsWith($"{InternalApiPaths.AdminRoot}/configs", StringComparison.OrdinalIgnoreCase))
            builder.Path = normalizedPath[..^adminConfigsSuffix.Length] + $"{InternalApiPaths.Root}/config-export";
        else
            builder.Path = $"{normalizedPath}{InternalApiPaths.Root}/config-export".Replace("//", "/");

        return builder.Uri;
    }

    private static JsonSerializerOptions CreateRemoteJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }

    private static MergedConfigEntry ToMergedEntry(ConfigEntry entry, bool hasLocalOverride)
    {
        return new MergedConfigEntry
        {
            Key = ConfigKeyNormalizer.NormalizeKey(entry.Key),
            Value = entry.Value,
            BinaryBase64 = entry.BinaryBase64,
            EncryptedValue = entry.EncryptedValue,
            EncryptedBinaryBase64 = entry.EncryptedBinaryBase64,
            EncryptedLabels = entry.EncryptedLabels,
            MediaType = entry.MediaType,
            EntryType = entry.EntryType,
            PayloadKind = entry.PayloadKind,
            Labels = entry.Labels,
            UpdatedAtUtc = entry.UpdatedAtUtc,
            UpdatedBy = entry.UpdatedBy,
            Source = "local",
            ReadOnly = false,
            HasLocalOverride = hasLocalOverride
        };
    }

    private static MergedConfigEntry ToListEntrySummary(MergedConfigEntry entry)
    {
        return new MergedConfigEntry
        {
            Key = entry.Key,
            Value = string.Empty,
            BinaryBase64 = null,
            EncryptedValue = null,
            EncryptedBinaryBase64 = null,
            EncryptedLabels = null,
            MediaType = entry.MediaType,
            EntryType = entry.EntryType,
            PayloadKind = entry.PayloadKind,
            Labels = entry.Labels,
            UpdatedAtUtc = entry.UpdatedAtUtc,
            UpdatedBy = entry.UpdatedBy,
            Source = entry.Source,
            ReadOnly = entry.ReadOnly,
            HasLocalOverride = entry.HasLocalOverride
        };
    }

    private static ConfigEntry ToConfigEntry(ConfigEntry entry)
    {
        return ConfigEntrySerializer.Normalize(new ConfigEntry
        {
            Key = entry.Key,
            Value = entry.Value,
            BinaryBase64 = entry.BinaryBase64,
            EncryptedValue = entry.EncryptedValue,
            EncryptedBinaryBase64 = entry.EncryptedBinaryBase64,
            EncryptedLabels = entry.EncryptedLabels,
            MediaType = entry.MediaType,
            EntryType = entry.EntryType,
            PayloadKind = entry.PayloadKind,
            Labels = entry.Labels,
            UpdatedAtUtc = entry.UpdatedAtUtc,
            UpdatedBy = entry.UpdatedBy
        });
    }

    private static ConfigEntry ToConfigEntry(ConfigEntryRevision entry)
    {
        return ConfigEntrySerializer.Normalize(new ConfigEntry
        {
            Key = entry.Key,
            Value = entry.Value,
            BinaryBase64 = entry.BinaryBase64,
            EncryptedValue = entry.EncryptedValue,
            EncryptedBinaryBase64 = entry.EncryptedBinaryBase64,
            EncryptedLabels = entry.EncryptedLabels,
            MediaType = entry.MediaType,
            EntryType = entry.EntryType,
            PayloadKind = entry.PayloadKind,
            Labels = entry.Labels,
            UpdatedAtUtc = entry.UpdatedAtUtc,
            UpdatedBy = entry.UpdatedBy
        });
    }

    private static ConfigEntry ToConfigEntry(MergedConfigEntry entry)
    {
        return ConfigEntrySerializer.Normalize(new ConfigEntry
        {
            Key = entry.Key,
            Value = entry.Value,
            BinaryBase64 = entry.BinaryBase64,
            EncryptedValue = entry.EncryptedValue,
            EncryptedBinaryBase64 = entry.EncryptedBinaryBase64,
            EncryptedLabels = entry.EncryptedLabels,
            MediaType = entry.MediaType,
            EntryType = entry.EntryType,
            PayloadKind = entry.PayloadKind,
            Labels = entry.Labels,
            UpdatedAtUtc = entry.UpdatedAtUtc,
            UpdatedBy = entry.UpdatedBy
        });
    }

    private static async Task<ConfigEntryDetails> ToEntryDetailsAsync(
        string key,
        MergedConfigEntry mergedEntry,
        ILocalConfigStore localStore,
        IRemoteConfigStore remoteStore,
        CancellationToken cancellationToken)
    {
        var localEntry = localStore.Get(key);
        var remoteEntry = await remoteStore.GetAsync(key, cancellationToken);

        return new ConfigEntryDetails
        {
            Key = mergedEntry.Key,
            Value = mergedEntry.Value,
            BinaryBase64 = mergedEntry.BinaryBase64,
            EncryptedValue = mergedEntry.EncryptedValue,
            EncryptedBinaryBase64 = mergedEntry.EncryptedBinaryBase64,
            EncryptedLabels = mergedEntry.EncryptedLabels,
            MediaType = mergedEntry.MediaType,
            EntryType = mergedEntry.EntryType,
            PayloadKind = mergedEntry.PayloadKind,
            Labels = mergedEntry.Labels,
            UpdatedAtUtc = mergedEntry.UpdatedAtUtc,
            UpdatedBy = mergedEntry.UpdatedBy,
            Source = mergedEntry.Source,
            ReadOnly = mergedEntry.ReadOnly,
            HasLocalOverride = mergedEntry.HasLocalOverride,
            Local = localEntry is null ? null : ToVariant(localEntry),
            Remote = remoteEntry is null ? null : ToVariant(remoteEntry)
        };
    }

    private static ConfigEntryVariant ToVariant(ConfigEntry entry)
    {
        return new ConfigEntryVariant
        {
            Value = entry.Value,
            BinaryBase64 = entry.BinaryBase64,
            EncryptedValue = entry.EncryptedValue,
            EncryptedBinaryBase64 = entry.EncryptedBinaryBase64,
            EncryptedLabels = entry.EncryptedLabels,
            MediaType = entry.MediaType,
            EntryType = entry.EntryType,
            PayloadKind = entry.PayloadKind,
            Labels = entry.Labels,
            UpdatedAtUtc = entry.UpdatedAtUtc,
            UpdatedBy = entry.UpdatedBy
        };
    }

    private static ConfigEntryRevisionSummary ToRevisionSummary(ConfigEntryRevision revision)
    {
        return new ConfigEntryRevisionSummary(
            revision.RevisionId,
            revision.Key,
            revision.EntryType,
            revision.PayloadKind,
            revision.MediaType,
            revision.UpdatedAtUtc,
            revision.UpdatedBy,
            revision.CapturedAtUtc,
            revision.CapturedBy,
            revision.Action);
    }

    private static ConfigEntryVariant RevealVariant(ConfigEntry entry, IConfigEncryptionService encryptionService)
    {
        var revealed = ToVariant(entry);
        if (entry.EntryType != ConfigEntryType.Secret)
        {
            revealed.IsRevealed = true;
            return revealed;
        }

        revealed.Labels = encryptionService.DecryptLabels(entry.EncryptedLabels);
        if (entry.PayloadKind == ConfigPayloadKind.Binary)
            revealed.BinaryBase64 = encryptionService.DecryptBinaryBase64(entry.EncryptedBinaryBase64 ?? string.Empty);
        else
            revealed.Value = encryptionService.DecryptString(entry.EncryptedValue ?? string.Empty);

        revealed.IsRevealed = true;
        return revealed;
    }

    private static List<ConfigTreeNode> BuildTree(IReadOnlyList<MergedConfigEntry> entries, string prefix)
    {
        var root = new TreeNode();

        foreach (var entry in entries)
        {
            if (!string.IsNullOrWhiteSpace(prefix) &&
                !entry.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;

            var relativeKey = string.IsNullOrWhiteSpace(prefix) ? entry.Key : entry.Key[prefix.Length..];
            if (string.IsNullOrWhiteSpace(relativeKey)) continue;

            AddEntry(root, relativeKey, entry);
        }

        return ToNodes(root, prefix);
    }

    private static void AddEntry(TreeNode root, string relativeKey, MergedConfigEntry entry)
    {
        var parts = relativeKey.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0) return;

        var current = root;
        for (var i = 0; i < parts.Length; i++)
        {
            var part = parts[i];
            var isLeaf = i == parts.Length - 1;
            if (!current.Children.TryGetValue(part, out var child))
            {
                child = new TreeNode { Name = part };
                current.Children[part] = child;
            }

            if (isLeaf) child.Entry = entry;

            current = child;
        }
    }

    private static List<ConfigTreeNode> ToNodes(TreeNode node, string prefix)
    {
        var nodes = new List<ConfigTreeNode>();

        foreach (var child in node.Children.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
        {
            var childNode = child.Value;
            var currentPath = string.IsNullOrWhiteSpace(prefix)
                ? childNode.Name
                : $"{prefix.TrimEnd('/')}/{childNode.Name}";
            var children = ToNodes(childNode, currentPath);
            var hasChildren = children.Count > 0;
            var key = !hasChildren && childNode.Entry is not null
                ? ToTreeKey(childNode.Entry, childNode.Name, currentPath)
                : null;

            nodes.Add(new ConfigTreeNode(
                childNode.Name,
                hasChildren ? $"{currentPath}/" : currentPath,
                hasChildren ? "folder" : "key",
                hasChildren ? ResolveFolderSource(childNode) : childNode.Entry?.Source ?? "local",
                hasChildren ? ResolveFolderReadOnly(childNode) : childNode.Entry?.ReadOnly ?? false,
                hasChildren ? ResolveFolderOverride(childNode) : childNode.Entry?.HasLocalOverride ?? false,
                hasChildren ? ResolveFolderEntryType(childNode) : childNode.Entry?.EntryType ?? ConfigEntryType.Data,
                hasChildren ? ConfigPayloadKind.Text : childNode.Entry?.PayloadKind ?? ConfigPayloadKind.Text,
                key,
                children));
        }

        return nodes;
    }

    private static ConfigTreeKey ToTreeKey(MergedConfigEntry entry, string name, string path)
    {
        return new ConfigTreeKey(
            name,
            path,
            string.Empty,
            null,
            null,
            null,
            null,
            entry.MediaType,
            entry.EntryType,
            entry.PayloadKind,
            entry.Labels,
            entry.UpdatedAtUtc,
            entry.UpdatedBy,
            entry.Source,
            entry.ReadOnly,
            entry.HasLocalOverride);
    }

    private static ConfigEntryType ResolveFolderEntryType(TreeNode node)
    {
        if (node.Children.Values.Any(child =>
                child.Entry?.EntryType == ConfigEntryType.Secret ||
                ResolveFolderEntryType(child) == ConfigEntryType.Secret)) return ConfigEntryType.Secret;

        return ConfigEntryType.Data;
    }

    private static string ResolveFolderSource(TreeNode node)
    {
        if (node.Children.Values.Any(child => child.Entry?.Source == "local" || ResolveFolderSource(child) == "local"))
            return "local";

        return "remote";
    }

    private static bool ResolveFolderReadOnly(TreeNode node)
    {
        return !node.Children.Values.Any(child => child.Entry?.ReadOnly == false || !ResolveFolderReadOnly(child));
    }

    private static bool ResolveFolderOverride(TreeNode node)
    {
        return node.Children.Values.Any(child => child.Entry?.HasLocalOverride == true || ResolveFolderOverride(child));
    }

    private sealed class TreeNode
    {
        public string Name { get; set; } = string.Empty;
        public Dictionary<string, TreeNode> Children { get; } = new(StringComparer.OrdinalIgnoreCase);
        public MergedConfigEntry? Entry { get; set; }
    }

    private sealed record RemotePublicConfigEntry(
        string Key,
        string Name,
        string? Value,
        string? BinaryBase64,
        string? EncryptedValue,
        string? EncryptedBinaryBase64,
        string? EncryptedLabels,
        string? MediaType,
        ConfigEntryType EntryType,
        ConfigPayloadKind PayloadKind,
        List<ConfigLabel>? Labels,
        DateTimeOffset UpdatedAtUtc);

    private sealed class RemoteConfigImportValidationException(string message) : Exception(message);
}

public sealed record UpsertConfigRequest(
    ConfigEntryType EntryType,
    ConfigPayloadKind PayloadKind,
    string? Value,
    string? BinaryBase64,
    string? MediaType,
    List<ConfigLabel>? Labels);

public sealed record RenameKeyRequest(string FromKey, string ToKey);

public sealed record RenameFolderRequest(string FromPath, string ToPath);

public sealed record MoveConfigsRequest(
    IReadOnlyList<string> Keys,
    string? TargetFolder,
    bool PreserveSourceNames = false);

public sealed record ImportRemoteConfigsRequest(string Url);

public sealed record ConfigBackupDocument(
    int FormatVersion,
    DateTimeOffset ExportedAtUtc,
    IReadOnlyList<ConfigBackupEntry> Entries,
    IReadOnlyList<ConfigBackupRevision>? Revisions);

public sealed record ConfigBackupEntry(
    string Key,
    ConfigEntryType EntryType,
    ConfigPayloadKind PayloadKind,
    string? Value,
    string? BinaryBase64,
    string? EncryptedValue,
    string? EncryptedBinaryBase64,
    string? EncryptedLabels,
    string? MediaType,
    List<ConfigLabel>? Labels);

public sealed record ConfigBackupRevision(
    string RevisionId,
    string Key,
    ConfigEntryType EntryType,
    ConfigPayloadKind PayloadKind,
    string? Value,
    string? BinaryBase64,
    string? EncryptedValue,
    string? EncryptedBinaryBase64,
    string? EncryptedLabels,
    string? MediaType,
    List<ConfigLabel>? Labels,
    DateTimeOffset UpdatedAtUtc,
    string? UpdatedBy,
    DateTimeOffset CapturedAtUtc,
    string? CapturedBy,
    string Action);

public sealed record ConfigTreeNode(
    string Name,
    string Path,
    string Type,
    string Source,
    bool ReadOnly,
    bool HasLocalOverride,
    ConfigEntryType EntryType,
    ConfigPayloadKind PayloadKind,
    ConfigTreeKey? Key,
    IReadOnlyList<ConfigTreeNode> Children);

public sealed record ConfigTreeKey(
    string Name,
    string Path,
    string Value,
    string? BinaryBase64,
    string? EncryptedValue,
    string? EncryptedBinaryBase64,
    string? EncryptedLabels,
    string? MediaType,
    ConfigEntryType EntryType,
    ConfigPayloadKind PayloadKind,
    List<ConfigLabel> Labels,
    DateTimeOffset UpdatedAtUtc,
    string? UpdatedBy,
    string Source,
    bool ReadOnly,
    bool HasLocalOverride);

public sealed record ConfigEntryRevisionSummary(
    string RevisionId,
    string Key,
    ConfigEntryType EntryType,
    ConfigPayloadKind PayloadKind,
    string? MediaType,
    DateTimeOffset UpdatedAtUtc,
    string? UpdatedBy,
    DateTimeOffset CapturedAtUtc,
    string? CapturedBy,
    string Action);

public sealed class ConfigEntryRevisionDetails
{
    public string RevisionId { get; set; } = string.Empty;
    public string Key { get; set; } = string.Empty;
    public DateTimeOffset CapturedAtUtc { get; set; }
    public string? CapturedBy { get; set; }
    public string Action { get; set; } = "update";
    public ConfigEntryVariant Snapshot { get; set; } = new();
}