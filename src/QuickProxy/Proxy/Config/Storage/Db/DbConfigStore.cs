using System.Data;
using Microsoft.EntityFrameworkCore;
using QuickProxy.Proxy.Config.Models;

namespace QuickProxy.Proxy.Config.Storage.Db;

public sealed class DbConfigStore(
    IDbContextFactory<QuickConfigDbContext> factory,
    IConfigEncryptionService encryptionService) : ILocalConfigStore
{
    public IReadOnlyList<ConfigEntry> List(string? prefix = null)
    {
        using var db = factory.CreateDbContext();
        EnsureSchema(db);

        NormalizeEntries(db);

        var query = db.ConfigEntries.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(prefix)) query = query.Where(x => x.Key.StartsWith(prefix));

        return query
            .OrderBy(x => x.Key)
            .ToArray()
            .Select(ToModel)
            .ToArray();
    }

    public ConfigEntry? Get(string key)
    {
        using var db = factory.CreateDbContext();
        EnsureSchema(db);

        NormalizeEntries(db);

        var normalizedKey = ConfigKeyNormalizer.NormalizeKey(key);
        var entity = db.ConfigEntries.AsNoTracking().FirstOrDefault(x => x.Key == normalizedKey);
        return entity is null ? null : ToModel(entity);
    }

    public void Upsert(ConfigEntry entry)
    {
        using var db = factory.CreateDbContext();
        EnsureSchema(db);

        NormalizeEntries(db);

        var normalizedEntry = ConfigEntrySerializer.Normalize(entry);
        var existing = db.ConfigEntries.FirstOrDefault(x => x.Key == normalizedEntry.Key);
        if (existing is not null)
            AppendRevision(db, existing, "update", normalizedEntry.UpdatedAtUtc, normalizedEntry.UpdatedBy);

        UpsertInternal(db, ToEntity(normalizedEntry));
        EnsureParentFolders(db, normalizedEntry);
        db.SaveChanges();
    }

    public void ReplaceAll(IReadOnlyList<ConfigEntry> entries)
    {
        using var db = factory.CreateDbContext();
        EnsureSchema(db);

        NormalizeEntries(db);

        var existingEntries = db.ConfigEntries.ToArray();
        var normalizedEntries = entries.Select(ConfigEntrySerializer.Normalize).ToArray();
        var capturedAtUtc = normalizedEntries.FirstOrDefault()?.UpdatedAtUtc ?? DateTimeOffset.UtcNow;
        var capturedBy = normalizedEntries.FirstOrDefault()?.UpdatedBy;

        foreach (var existing in existingEntries)
            AppendRevision(db, existing, "replace-all", capturedAtUtc, capturedBy);

        db.ConfigEntries.RemoveRange(existingEntries);
        db.SaveChanges();
        db.ChangeTracker.Clear();

        var replacement = BuildReplacementEntities(normalizedEntries);
        if (replacement.Count == 0) return;

        db.ConfigEntries.AddRange(replacement);
        db.SaveChanges();
    }

    public void ReplaceAll(IReadOnlyList<ConfigEntry> entries, IReadOnlyList<ConfigEntryRevision> revisions)
    {
        using var db = factory.CreateDbContext();
        EnsureSchema(db);

        db.ConfigEntryRevisions.RemoveRange(db.ConfigEntryRevisions);
        db.ConfigEntries.RemoveRange(db.ConfigEntries);
        db.SaveChanges();
        db.ChangeTracker.Clear();

        var replacementEntries = BuildReplacementEntities(entries);
        if (replacementEntries.Count > 0) db.ConfigEntries.AddRange(replacementEntries);

        var replacementRevisions = revisions
            .Select(NormalizeRevision)
            .Select(ToRevisionEntity)
            .ToArray();
        if (replacementRevisions.Length > 0) db.ConfigEntryRevisions.AddRange(replacementRevisions);

        db.SaveChanges();
    }

    public bool Delete(string key)
    {
        using var db = factory.CreateDbContext();
        EnsureSchema(db);

        NormalizeEntries(db);

        var normalizedKey = ConfigKeyNormalizer.NormalizeKey(key);
        if (string.IsNullOrWhiteSpace(normalizedKey)) return false;

        var prefix = $"{normalizedKey}/";
        var entities = db.ConfigEntries
            .Where(x => x.Key == normalizedKey || x.Key.StartsWith(prefix))
            .ToArray();

        if (entities.Length == 0) return false;

        db.ConfigEntries.RemoveRange(entities);
        DeleteRevisionEntities(db, normalizedKey, true);
        db.SaveChanges();
        return true;
    }

    public bool DeleteExact(string key)
    {
        using var db = factory.CreateDbContext();
        EnsureSchema(db);

        NormalizeEntries(db);

        var normalizedKey = ConfigKeyNormalizer.NormalizeKey(key);
        if (string.IsNullOrWhiteSpace(normalizedKey)) return false;

        var entity = db.ConfigEntries.FirstOrDefault(x => x.Key == normalizedKey);
        if (entity is null) return false;

        db.ConfigEntries.Remove(entity);
        DeleteRevisionEntities(db, normalizedKey, false);
        db.SaveChanges();
        return true;
    }

    public IReadOnlyList<ConfigEntryRevision> ListRevisions(string key)
    {
        using var db = factory.CreateDbContext();
        EnsureSchema(db);

        NormalizeEntries(db);
        NormalizeRevisions(db);

        var normalizedKey = ConfigKeyNormalizer.NormalizeKey(key);
        if (string.IsNullOrWhiteSpace(normalizedKey)) return [];

        return db.ConfigEntryRevisions
            .AsNoTracking()
            .Where(x => x.Key == normalizedKey)
            .ToArray()
            .OrderByDescending(x => x.CapturedAtUtc)
            .ThenByDescending(x => x.RevisionId, StringComparer.OrdinalIgnoreCase)
            .Select(ToRevisionModel)
            .ToArray();
    }

    public IReadOnlyList<ConfigEntryRevision> ListAllRevisions()
    {
        using var db = factory.CreateDbContext();
        EnsureSchema(db);

        NormalizeEntries(db);
        NormalizeRevisions(db);

        return db.ConfigEntryRevisions
            .AsNoTracking()
            .ToArray()
            .OrderByDescending(x => x.CapturedAtUtc)
            .ThenByDescending(x => x.RevisionId, StringComparer.OrdinalIgnoreCase)
            .Select(ToRevisionModel)
            .ToArray();
    }

    public ConfigEntryRevision? GetRevision(string key, string revisionId)
    {
        using var db = factory.CreateDbContext();
        EnsureSchema(db);

        NormalizeEntries(db);
        NormalizeRevisions(db);

        var normalizedKey = ConfigKeyNormalizer.NormalizeKey(key);
        if (string.IsNullOrWhiteSpace(normalizedKey) || string.IsNullOrWhiteSpace(revisionId)) return null;

        var entity = db.ConfigEntryRevisions.AsNoTracking()
            .FirstOrDefault(x => x.Key == normalizedKey && x.RevisionId == revisionId);
        return entity is null ? null : ToRevisionModel(entity);
    }

    public ConfigEntry? RestoreRevision(string key, string revisionId, DateTimeOffset restoredAtUtc, string? restoredBy)
    {
        using var db = factory.CreateDbContext();
        EnsureSchema(db);

        NormalizeEntries(db);
        NormalizeRevisions(db);

        var normalizedKey = ConfigKeyNormalizer.NormalizeKey(key);
        if (string.IsNullOrWhiteSpace(normalizedKey) || string.IsNullOrWhiteSpace(revisionId)) return null;

        var revision =
            db.ConfigEntryRevisions.FirstOrDefault(x => x.Key == normalizedKey && x.RevisionId == revisionId);
        if (revision is null) return null;

        var existing = db.ConfigEntries.FirstOrDefault(x => x.Key == normalizedKey);
        if (existing is not null) AppendRevision(db, existing, "restore", restoredAtUtc, restoredBy);

        var restoredEntry = ToModel(revision);
        restoredEntry.Key = normalizedKey;
        restoredEntry.UpdatedAtUtc = restoredAtUtc;
        restoredEntry.UpdatedBy = restoredBy;

        UpsertInternal(db, ToEntity(restoredEntry));
        EnsureParentFolders(db, restoredEntry);
        db.SaveChanges();
        return restoredEntry;
    }

    public void MoveRevisionHistory(IReadOnlyDictionary<string, string> keyMap, DateTimeOffset movedAtUtc,
        string? movedBy)
    {
        using var db = factory.CreateDbContext();
        EnsureSchema(db);

        NormalizeEntries(db);
        NormalizeRevisions(db);

        foreach (var pair in keyMap)
        {
            var fromKey = ConfigKeyNormalizer.NormalizeKey(pair.Key);
            var toKey = ConfigKeyNormalizer.NormalizeKey(pair.Value);
            if (string.IsNullOrWhiteSpace(fromKey) || string.IsNullOrWhiteSpace(toKey) ||
                string.Equals(fromKey, toKey, StringComparison.OrdinalIgnoreCase)) continue;

            var existingTarget = db.ConfigEntries.FirstOrDefault(x => x.Key == toKey);
            if (existingTarget is not null)
            {
                AppendRevision(db, existingTarget, "move-overwrite", movedAtUtc, movedBy);
                db.ConfigEntries.Remove(existingTarget);
            }

            var revisionCopies = db.ConfigEntryRevisions
                .Where(x => x.Key == fromKey)
                .AsNoTracking()
                .ToArray()
                .Select(revision => new ConfigEntryRevisionEntity
                {
                    RevisionId = revision.RevisionId,
                    Key = toKey,
                    Value = revision.Value,
                    EntryType = revision.EntryType,
                    PayloadKind = revision.PayloadKind,
                    MediaType = revision.MediaType,
                    LabelsJson = revision.LabelsJson,
                    UpdatedAtUtc = revision.UpdatedAtUtc,
                    UpdatedBy = revision.UpdatedBy,
                    CapturedAtUtc = revision.CapturedAtUtc,
                    CapturedBy = revision.CapturedBy,
                    Action = revision.Action
                })
                .ToArray();

            DeleteRevisionEntities(db, toKey, false);
            DeleteRevisionEntities(db, fromKey, false);
            if (revisionCopies.Length > 0) db.ConfigEntryRevisions.AddRange(revisionCopies);

            var source = db.ConfigEntries.FirstOrDefault(x => x.Key == fromKey);
            if (source is not null)
            {
                var movedEntry = new ConfigEntryEntity
                {
                    Key = toKey,
                    Value = source.Value,
                    EntryType = source.EntryType,
                    PayloadKind = source.PayloadKind,
                    MediaType = source.MediaType,
                    LabelsJson = source.LabelsJson,
                    UpdatedAtUtc = movedAtUtc,
                    UpdatedBy = movedBy
                };

                db.ConfigEntries.Remove(source);
                db.ConfigEntries.Add(movedEntry);
            }
        }

        db.SaveChanges();
    }

    private void EnsureSchema(QuickConfigDbContext db)
    {
        db.Database.OpenConnection();
        try
        {
            var existing = GetExistingColumns(db);

            EnsureColumn(db, existing, "EntryType", db.Database.IsSqlite() ? "TEXT NULL" : "nvarchar(32) NULL");
            EnsureColumn(db, existing, "PayloadKind", db.Database.IsSqlite() ? "TEXT NULL" : "nvarchar(32) NULL");
            EnsureColumn(db, existing, "MediaType", db.Database.IsSqlite() ? "TEXT NULL" : "nvarchar(256) NULL");
            EnsureColumn(db, existing, "LabelsJson", db.Database.IsSqlite() ? "TEXT NULL" : "nvarchar(max) NULL");
            EnsureRevisionTable(db);
        }
        finally
        {
            db.Database.CloseConnection();
        }
    }

    private static HashSet<string> GetExistingColumns(QuickConfigDbContext db)
    {
        if (db.Database.IsSqlite())
        {
            using var command = db.Database.GetDbConnection().CreateCommand();
            command.CommandText = "PRAGMA table_info('ConfigEntries')";
            using var reader = command.ExecuteReader();

            var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            while (reader.Read())
                if (!reader.IsDBNull(1))
                    columns.Add(reader.GetString(1));

            return columns;
        }

        var connection = db.Database.GetDbConnection();
        var schema = connection.GetSchema("Columns");
        return schema.Rows.Cast<DataRow>()
            .Where(row =>
                string.Equals(row["TABLE_NAME"]?.ToString(), "ConfigEntries", StringComparison.OrdinalIgnoreCase))
            .Select(row => row["COLUMN_NAME"]?.ToString() ?? string.Empty)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static void EnsureColumn(QuickConfigDbContext db, HashSet<string> existing, string name, string sqlType)
    {
        if (existing.Contains(name)) return;

        var sql = name switch
        {
            "EntryType" => $"ALTER TABLE ConfigEntries ADD EntryType {sqlType}",
            "PayloadKind" => $"ALTER TABLE ConfigEntries ADD PayloadKind {sqlType}",
            "MediaType" => $"ALTER TABLE ConfigEntries ADD MediaType {sqlType}",
            "LabelsJson" => $"ALTER TABLE ConfigEntries ADD LabelsJson {sqlType}",
            _ => throw new InvalidOperationException($"Unsupported config schema column '{name}'.")
        };

        db.Database.ExecuteSqlRaw(sql);
        existing.Add(name);
    }

    private static void EnsureRevisionTable(QuickConfigDbContext db)
    {
        if (db.Database.IsSqlite())
        {
            db.Database.ExecuteSqlRaw("""
                                      CREATE TABLE IF NOT EXISTS ConfigEntryRevisions (
                                          RevisionId TEXT NOT NULL PRIMARY KEY,
                                          Key TEXT NOT NULL,
                                          Value TEXT NOT NULL,
                                          EntryType TEXT NULL,
                                          PayloadKind TEXT NULL,
                                          MediaType TEXT NULL,
                                          LabelsJson TEXT NULL,
                                          UpdatedAtUtc TEXT NOT NULL,
                                          UpdatedBy TEXT NULL,
                                          CapturedAtUtc TEXT NOT NULL,
                                          CapturedBy TEXT NULL,
                                          Action TEXT NULL
                                      )
                                      """);
            db.Database.ExecuteSqlRaw(
                "CREATE INDEX IF NOT EXISTS IX_ConfigEntryRevisions_Key_CapturedAtUtc ON ConfigEntryRevisions(Key, CapturedAtUtc)");
            return;
        }

        db.Database.ExecuteSqlRaw("""
                                  IF OBJECT_ID(N'dbo.ConfigEntryRevisions', N'U') IS NULL
                                  BEGIN
                                      CREATE TABLE [dbo].[ConfigEntryRevisions](
                                          [RevisionId] nvarchar(64) NOT NULL PRIMARY KEY,
                                          [Key] nvarchar(1024) NOT NULL,
                                          [Value] nvarchar(max) NOT NULL,
                                          [EntryType] nvarchar(32) NULL,
                                          [PayloadKind] nvarchar(32) NULL,
                                          [MediaType] nvarchar(256) NULL,
                                          [LabelsJson] nvarchar(max) NULL,
                                          [UpdatedAtUtc] datetimeoffset NOT NULL,
                                          [UpdatedBy] nvarchar(320) NULL,
                                          [CapturedAtUtc] datetimeoffset NOT NULL,
                                          [CapturedBy] nvarchar(320) NULL,
                                          [Action] nvarchar(64) NULL
                                      );
                                      CREATE INDEX [IX_ConfigEntryRevisions_Key_CapturedAtUtc] ON [dbo].[ConfigEntryRevisions]([Key], [CapturedAtUtc]);
                                  END
                                  """);
    }

    private void NormalizeEntries(QuickConfigDbContext db)
    {
        var entities = db.ConfigEntries.ToArray();
        var normalized = new Dictionary<string, ConfigEntryEntity>(StringComparer.OrdinalIgnoreCase);
        var changed = false;

        foreach (var entity in entities)
        {
            var normalizedEntity = NormalizeEntity(entity, out var entityChanged);
            changed |= entityChanged;
            if (!normalized.TryGetValue(normalizedEntity.Key, out var existing) ||
                normalizedEntity.UpdatedAtUtc > existing.UpdatedAtUtc)
                normalized[normalizedEntity.Key] = normalizedEntity;
            else
                changed = true;
        }

        if (!changed && normalized.Count == entities.Length) return;

        db.ConfigEntries.RemoveRange(db.ConfigEntries);
        db.SaveChanges();
        db.ChangeTracker.Clear();
        db.ConfigEntries.AddRange(normalized.Values.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase));
        db.SaveChanges();
    }

    private void NormalizeRevisions(QuickConfigDbContext db)
    {
        var revisions = db.ConfigEntryRevisions.ToArray();
        var normalized = new Dictionary<string, ConfigEntryRevisionEntity>(StringComparer.OrdinalIgnoreCase);
        var changed = false;

        foreach (var revision in revisions)
        {
            var normalizedRevision = NormalizeRevisionEntity(revision, out var revisionChanged);
            changed |= revisionChanged;
            normalized[normalizedRevision.RevisionId] = normalizedRevision;
        }

        if (!changed && normalized.Count == revisions.Length) return;

        db.ConfigEntryRevisions.RemoveRange(db.ConfigEntryRevisions);
        db.SaveChanges();
        db.ChangeTracker.Clear();
        db.ConfigEntryRevisions.AddRange(normalized.Values.OrderByDescending(x => x.CapturedAtUtc));
        db.SaveChanges();
    }

    private ConfigEntryEntity NormalizeEntity(ConfigEntryEntity entity, out bool changed)
    {
        changed = false;

        var normalizedKey = ConfigKeyNormalizer.NormalizeKey(entity.Key);
        var entryType = ParseEntryType(entity.EntryType);
        var payloadKind = ParsePayloadKind(entity.PayloadKind);
        var mediaType = ConfigEntrySerializer.NormalizeOptional(entity.MediaType) ??
                        (payloadKind == ConfigPayloadKind.Binary ? "application/octet-stream" : "text/plain");
        var labelsJson = entryType == ConfigEntryType.Secret
            ? ConfigEntrySerializer.NormalizeOptional(entity.LabelsJson) ?? string.Empty
            : ConfigEntrySerializer.SerializeLabels(ConfigEntrySerializer.DeserializeLabels(entity.LabelsJson));
        var value = NormalizeStoredValue(entity.Value, entryType, payloadKind);

        changed |= !string.Equals(entity.Key, normalizedKey, StringComparison.Ordinal);
        changed |= !string.Equals(entity.EntryType, entryType.ToString(), StringComparison.Ordinal);
        changed |= !string.Equals(entity.PayloadKind, payloadKind.ToString(), StringComparison.Ordinal);
        changed |= !string.Equals(entity.MediaType, mediaType, StringComparison.Ordinal);
        changed |= !string.Equals(entity.LabelsJson, labelsJson, StringComparison.Ordinal);
        changed |= !string.Equals(entity.Value, value, StringComparison.Ordinal);

        return new ConfigEntryEntity
        {
            Key = normalizedKey,
            Value = value,
            EntryType = entryType.ToString(),
            PayloadKind = payloadKind.ToString(),
            MediaType = mediaType,
            LabelsJson = labelsJson,
            UpdatedAtUtc = entity.UpdatedAtUtc,
            UpdatedBy = entity.UpdatedBy
        };
    }

    private ConfigEntryRevisionEntity NormalizeRevisionEntity(ConfigEntryRevisionEntity entity, out bool changed)
    {
        changed = false;

        var normalizedKey = ConfigKeyNormalizer.NormalizeKey(entity.Key);
        var entryType = ParseEntryType(entity.EntryType);
        var payloadKind = ParsePayloadKind(entity.PayloadKind);
        var mediaType = ConfigEntrySerializer.NormalizeOptional(entity.MediaType) ??
                        (payloadKind == ConfigPayloadKind.Binary ? "application/octet-stream" : "text/plain");
        var labelsJson = entryType == ConfigEntryType.Secret
            ? ConfigEntrySerializer.NormalizeOptional(entity.LabelsJson) ?? string.Empty
            : ConfigEntrySerializer.SerializeLabels(ConfigEntrySerializer.DeserializeLabels(entity.LabelsJson));
        var value = NormalizeStoredValue(entity.Value, entryType, payloadKind);
        var normalizedRevisionId =
            string.IsNullOrWhiteSpace(entity.RevisionId) ? CreateRevisionId() : entity.RevisionId.Trim();
        var normalizedAction = string.IsNullOrWhiteSpace(entity.Action) ? "update" : entity.Action.Trim();

        changed |= !string.Equals(entity.Key, normalizedKey, StringComparison.Ordinal);
        changed |= !string.Equals(entity.EntryType, entryType.ToString(), StringComparison.Ordinal);
        changed |= !string.Equals(entity.PayloadKind, payloadKind.ToString(), StringComparison.Ordinal);
        changed |= !string.Equals(entity.MediaType, mediaType, StringComparison.Ordinal);
        changed |= !string.Equals(entity.LabelsJson, labelsJson, StringComparison.Ordinal);
        changed |= !string.Equals(entity.Value, value, StringComparison.Ordinal);
        changed |= !string.Equals(entity.RevisionId, normalizedRevisionId, StringComparison.Ordinal);
        changed |= !string.Equals(entity.Action, normalizedAction, StringComparison.Ordinal);

        return new ConfigEntryRevisionEntity
        {
            RevisionId = normalizedRevisionId,
            Key = normalizedKey,
            Value = value,
            EntryType = entryType.ToString(),
            PayloadKind = payloadKind.ToString(),
            MediaType = mediaType,
            LabelsJson = labelsJson,
            UpdatedAtUtc = entity.UpdatedAtUtc,
            UpdatedBy = entity.UpdatedBy,
            CapturedAtUtc = entity.CapturedAtUtc,
            CapturedBy = entity.CapturedBy,
            Action = normalizedAction
        };
    }

    private static string NormalizeStoredValue(string value, ConfigEntryType entryType, ConfigPayloadKind payloadKind)
    {
        if (entryType == ConfigEntryType.Secret) return ConfigEntrySerializer.NormalizeOptional(value) ?? string.Empty;

        return payloadKind == ConfigPayloadKind.Binary
            ? ConfigEntrySerializer.NormalizeBinaryBase64(value)
            : ConfigValueEncoding.NormalizeStoredValue(value);
    }

    private void AppendRevision(QuickConfigDbContext db, ConfigEntryEntity entity, string action,
        DateTimeOffset capturedAtUtc, string? capturedBy)
    {
        db.ConfigEntryRevisions.Add(new ConfigEntryRevisionEntity
        {
            RevisionId = CreateRevisionId(),
            Key = entity.Key,
            Value = entity.Value,
            EntryType = entity.EntryType,
            PayloadKind = entity.PayloadKind,
            MediaType = entity.MediaType,
            LabelsJson = entity.LabelsJson,
            UpdatedAtUtc = entity.UpdatedAtUtc,
            UpdatedBy = entity.UpdatedBy,
            CapturedAtUtc = capturedAtUtc,
            CapturedBy = capturedBy,
            Action = action
        });
    }

    private void DeleteRevisionEntities(QuickConfigDbContext db, string key, bool includeDescendants)
    {
        var prefix = $"{key}/";
        var revisions = db.ConfigEntryRevisions.Where(x =>
            x.Key == key || (includeDescendants && x.Key.StartsWith(prefix)));
        db.ConfigEntryRevisions.RemoveRange(revisions);
    }

    private void UpsertInternal(QuickConfigDbContext db, ConfigEntryEntity entry)
    {
        var entity = db.ConfigEntries.FirstOrDefault(x => x.Key == entry.Key);
        if (entity is null)
        {
            db.ConfigEntries.Add(entry);
            return;
        }

        entity.Value = entry.Value;
        entity.EntryType = entry.EntryType;
        entity.PayloadKind = entry.PayloadKind;
        entity.MediaType = entry.MediaType;
        entity.LabelsJson = entry.LabelsJson;
        entity.UpdatedAtUtc = entry.UpdatedAtUtc;
        entity.UpdatedBy = entry.UpdatedBy;
    }

    private static void EnsureParentFolders(QuickConfigDbContext db, ConfigEntry entry)
    {
        foreach (var folderKey in GetParentFolderKeys(entry.Key))
        {
            var entity = db.ConfigEntries.FirstOrDefault(x => x.Key == folderKey);
            if (entity is null)
                db.ConfigEntries.Add(new ConfigEntryEntity
                {
                    Key = folderKey,
                    Value = string.Empty,
                    EntryType = nameof(ConfigEntryType.Data),
                    PayloadKind = nameof(ConfigPayloadKind.Text),
                    MediaType = "text/plain",
                    UpdatedAtUtc = entry.UpdatedAtUtc,
                    UpdatedBy = entry.UpdatedBy
                });
        }
    }

    private static IEnumerable<string> GetParentFolderKeys(string key)
    {
        var normalized = ConfigKeyNormalizer.NormalizeKey(key).TrimEnd('/');
        var parts = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        for (var i = 1; i < parts.Length; i++) yield return string.Join('/', parts.Take(i));
    }

    private IReadOnlyList<ConfigEntryEntity> BuildReplacementEntities(IEnumerable<ConfigEntry> entries)
    {
        var replacement = new Dictionary<string, ConfigEntryEntity>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in entries.Select(ConfigEntrySerializer.Normalize))
        {
            if (string.IsNullOrWhiteSpace(entry.Key)) continue;

            replacement[entry.Key] = ToEntity(entry);
            foreach (var folderKey in GetParentFolderKeys(entry.Key))
                replacement.TryAdd(folderKey, new ConfigEntryEntity
                {
                    Key = folderKey,
                    Value = string.Empty,
                    EntryType = nameof(ConfigEntryType.Data),
                    PayloadKind = nameof(ConfigPayloadKind.Text),
                    MediaType = "text/plain",
                    UpdatedAtUtc = entry.UpdatedAtUtc,
                    UpdatedBy = entry.UpdatedBy
                });
        }

        return replacement.Values.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private ConfigEntry ToModel(ConfigEntryEntity entity)
    {
        var entryType = ParseEntryType(entity.EntryType);
        var payloadKind = ParsePayloadKind(entity.PayloadKind);
        var model = new ConfigEntry
        {
            Key = entity.Key,
            EntryType = entryType,
            PayloadKind = payloadKind,
            MediaType = entity.MediaType,
            UpdatedAtUtc = entity.UpdatedAtUtc,
            UpdatedBy = entity.UpdatedBy
        };

        if (entryType == ConfigEntryType.Secret)
        {
            model.EncryptedLabels = entity.LabelsJson;
            if (payloadKind == ConfigPayloadKind.Binary)
                model.EncryptedBinaryBase64 = entity.Value;
            else
                model.EncryptedValue = entity.Value;

            return model;
        }

        model.Labels = ConfigEntrySerializer.DeserializeLabels(entity.LabelsJson);
        if (payloadKind == ConfigPayloadKind.Binary)
            model.BinaryBase64 = string.IsNullOrWhiteSpace(entity.Value)
                ? string.Empty
                : ConfigEntrySerializer.NormalizeBinaryBase64(entity.Value);
        else
            model.Value = ConfigValueEncoding.DecodeFromStorage(entity.Value);

        return model;
    }

    private ConfigEntry ToModel(ConfigEntryRevisionEntity entity)
    {
        var entryType = ParseEntryType(entity.EntryType);
        var payloadKind = ParsePayloadKind(entity.PayloadKind);
        var model = new ConfigEntry
        {
            Key = entity.Key,
            EntryType = entryType,
            PayloadKind = payloadKind,
            MediaType = entity.MediaType,
            UpdatedAtUtc = entity.UpdatedAtUtc,
            UpdatedBy = entity.UpdatedBy
        };

        if (entryType == ConfigEntryType.Secret)
        {
            model.EncryptedLabels = entity.LabelsJson;
            if (payloadKind == ConfigPayloadKind.Binary)
                model.EncryptedBinaryBase64 = entity.Value;
            else
                model.EncryptedValue = entity.Value;

            return model;
        }

        model.Labels = ConfigEntrySerializer.DeserializeLabels(entity.LabelsJson);
        if (payloadKind == ConfigPayloadKind.Binary)
            model.BinaryBase64 = string.IsNullOrWhiteSpace(entity.Value)
                ? string.Empty
                : ConfigEntrySerializer.NormalizeBinaryBase64(entity.Value);
        else
            model.Value = ConfigValueEncoding.DecodeFromStorage(entity.Value);

        return model;
    }

    private ConfigEntryRevision ToRevisionModel(ConfigEntryRevisionEntity entity)
    {
        var entry = ToModel(entity);
        return new ConfigEntryRevision
        {
            RevisionId = entity.RevisionId,
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
            UpdatedBy = entry.UpdatedBy,
            CapturedAtUtc = entity.CapturedAtUtc,
            CapturedBy = entity.CapturedBy,
            Action = entity.Action ?? "update"
        };
    }

    private ConfigEntryEntity ToEntity(ConfigEntry entry)
    {
        var normalizedEntry = ConfigEntrySerializer.Normalize(entry);
        var entity = new ConfigEntryEntity
        {
            Key = normalizedEntry.Key,
            EntryType = normalizedEntry.EntryType.ToString(),
            PayloadKind = normalizedEntry.PayloadKind.ToString(),
            MediaType = normalizedEntry.MediaType,
            UpdatedAtUtc = normalizedEntry.UpdatedAtUtc,
            UpdatedBy = normalizedEntry.UpdatedBy
        };

        if (normalizedEntry.EntryType == ConfigEntryType.Secret)
        {
            entity.LabelsJson = normalizedEntry.EncryptedLabels ??
                                encryptionService.EncryptLabels(normalizedEntry.Labels);
            entity.Value = normalizedEntry.PayloadKind == ConfigPayloadKind.Binary
                ? normalizedEntry.EncryptedBinaryBase64 ??
                  encryptionService.EncryptBinaryBase64(normalizedEntry.BinaryBase64 ?? string.Empty)
                : normalizedEntry.EncryptedValue ?? encryptionService.EncryptString(normalizedEntry.Value);
            return entity;
        }

        entity.LabelsJson = ConfigEntrySerializer.SerializeLabels(normalizedEntry.Labels);
        entity.Value = normalizedEntry.PayloadKind == ConfigPayloadKind.Binary
            ? ConfigEntrySerializer.NormalizeBinaryBase64(normalizedEntry.BinaryBase64)
            : ConfigValueEncoding.EncodeForStorage(normalizedEntry.Value);
        return entity;
    }

    private static ConfigEntryType ParseEntryType(string? entryType)
    {
        return Enum.TryParse<ConfigEntryType>(entryType, true, out var parsed)
            ? parsed
            : ConfigEntryType.Data;
    }

    private static ConfigPayloadKind ParsePayloadKind(string? payloadKind)
    {
        return Enum.TryParse<ConfigPayloadKind>(payloadKind, true, out var parsed)
            ? parsed
            : ConfigPayloadKind.Text;
    }

    private static string CreateRevisionId()
    {
        return Guid.NewGuid().ToString("N");
    }

    private static ConfigEntryRevision NormalizeRevision(ConfigEntryRevision revision)
    {
        var normalizedEntry = ConfigEntrySerializer.Normalize(new ConfigEntry
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

        return new ConfigEntryRevision
        {
            RevisionId = string.IsNullOrWhiteSpace(revision.RevisionId)
                ? CreateRevisionId()
                : revision.RevisionId.Trim(),
            Key = normalizedEntry.Key,
            Value = normalizedEntry.Value,
            BinaryBase64 = normalizedEntry.BinaryBase64,
            EncryptedValue = normalizedEntry.EncryptedValue,
            EncryptedBinaryBase64 = normalizedEntry.EncryptedBinaryBase64,
            EncryptedLabels = normalizedEntry.EncryptedLabels,
            MediaType = normalizedEntry.MediaType,
            EntryType = normalizedEntry.EntryType,
            PayloadKind = normalizedEntry.PayloadKind,
            Labels = normalizedEntry.Labels,
            UpdatedAtUtc = normalizedEntry.UpdatedAtUtc,
            UpdatedBy = normalizedEntry.UpdatedBy,
            CapturedAtUtc = revision.CapturedAtUtc,
            CapturedBy = revision.CapturedBy,
            Action = string.IsNullOrWhiteSpace(revision.Action) ? "update" : revision.Action.Trim()
        };
    }

    private static ConfigEntryRevisionEntity ToRevisionEntity(ConfigEntryRevision revision)
    {
        return new ConfigEntryRevisionEntity
        {
            RevisionId = revision.RevisionId,
            Key = revision.Key,
            Value = revision.EntryType == ConfigEntryType.Secret
                ? revision.PayloadKind == ConfigPayloadKind.Binary
                    ? revision.EncryptedBinaryBase64 ?? string.Empty
                    : revision.EncryptedValue ?? string.Empty
                : revision.PayloadKind == ConfigPayloadKind.Binary
                    ? ConfigEntrySerializer.NormalizeBinaryBase64(revision.BinaryBase64)
                    : ConfigValueEncoding.EncodeForStorage(revision.Value),
            EntryType = revision.EntryType.ToString(),
            PayloadKind = revision.PayloadKind.ToString(),
            MediaType = revision.MediaType,
            LabelsJson = revision.EntryType == ConfigEntryType.Secret
                ? revision.EncryptedLabels ?? string.Empty
                : ConfigEntrySerializer.SerializeLabels(revision.Labels),
            UpdatedAtUtc = revision.UpdatedAtUtc,
            UpdatedBy = revision.UpdatedBy,
            CapturedAtUtc = revision.CapturedAtUtc,
            CapturedBy = revision.CapturedBy,
            Action = revision.Action
        };
    }
}