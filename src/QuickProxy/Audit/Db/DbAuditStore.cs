using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace QuickProxy.Audit.Db;

public sealed class DbAuditStore(IDbContextFactory<QuickAuditDbContext> factory) : IAuditStore
{
    public void Append(AuditEvent auditEvent)
    {
        using var db = factory.CreateDbContext();
        db.AuditEvents.Add(ToEntity(AuditEventNormalizer.Normalize(auditEvent)));
        db.SaveChanges();
    }

    public AuditEvent? Get(string id)
    {
        using var db = factory.CreateDbContext();
        var entity = db.AuditEvents.AsNoTracking().FirstOrDefault(x => x.Id == id);
        return entity is null ? null : ToModel(entity);
    }

    public AuditListResponse List(AuditQuery query)
    {
        using var db = factory.CreateDbContext();
        var normalizedModule = Normalize(query.Module);
        var normalizedAction = Normalize(query.Action);
        var normalizedActor = Normalize(query.Actor);
        var normalizedTarget = Normalize(query.Target);
        var normalizedOutcome = Normalize(query.Outcome);

        var eventsQuery = db.AuditEvents.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(normalizedModule))
            eventsQuery = eventsQuery.Where(x => x.Module == normalizedModule);

        if (!string.IsNullOrWhiteSpace(normalizedAction))
            eventsQuery = eventsQuery.Where(x => x.Action == normalizedAction);

        if (!string.IsNullOrWhiteSpace(normalizedOutcome))
            eventsQuery = eventsQuery.Where(x => x.Outcome == normalizedOutcome);

        if (!string.IsNullOrWhiteSpace(normalizedActor))
            eventsQuery = eventsQuery.Where(x =>
                (x.ActorId != null && x.ActorId.Contains(normalizedActor))
                || (x.ActorDisplayName != null && x.ActorDisplayName.Contains(normalizedActor)));

        if (!string.IsNullOrWhiteSpace(normalizedTarget))
            eventsQuery = eventsQuery.Where(x =>
                (x.TargetId != null && x.TargetId.Contains(normalizedTarget))
                || (x.TargetType != null && x.TargetType.Contains(normalizedTarget)));

        if (query.FromUtc.HasValue)
        {
            var fromUtc = query.FromUtc.Value.UtcDateTime;
            eventsQuery = eventsQuery.Where(x => x.OccurredAtUtc >= fromUtc);
        }

        if (query.ToUtc.HasValue)
        {
            var toUtc = query.ToUtc.Value.UtcDateTime;
            eventsQuery = eventsQuery.Where(x => x.OccurredAtUtc <= toUtc);
        }

        var total = eventsQuery.Count();
        var items = eventsQuery
            .OrderByDescending(x => x.OccurredAtUtc)
            .ThenByDescending(x => x.Id)
            .Skip(Math.Max(0, query.Offset))
            .Take(Math.Clamp(query.Limit, 1, 1000))
            .ToArray()
            .Select(ToModel)
            .Select(AuditEventNormalizer.ToListItem)
            .ToArray();

        return new AuditListResponse
        {
            Total = total,
            Items = items
        };
    }

    private static AuditEventEntity ToEntity(AuditEvent auditEvent)
    {
        return new AuditEventEntity
        {
            Id = auditEvent.Id,
            OccurredAtUtc = auditEvent.OccurredAtUtc.UtcDateTime,
            Module = auditEvent.Module,
            Action = auditEvent.Action,
            TargetType = auditEvent.TargetType,
            TargetId = auditEvent.TargetId,
            ActorType = auditEvent.Actor.Type,
            ActorId = auditEvent.Actor.Id,
            ActorDisplayName = auditEvent.Actor.DisplayName,
            Source = auditEvent.Source,
            Outcome = auditEvent.Outcome,
            StatusCode = auditEvent.StatusCode,
            CorrelationId = auditEvent.CorrelationId,
            Error = auditEvent.Error,
            ChangesJson = auditEvent.Changes is null ? null : JsonSerializer.Serialize(auditEvent.Changes)
        };
    }

    private static AuditEvent ToModel(AuditEventEntity entity)
    {
        return new AuditEvent
        {
            Id = entity.Id,
            OccurredAtUtc = new DateTimeOffset(DateTime.SpecifyKind(entity.OccurredAtUtc, DateTimeKind.Utc)),
            Module = entity.Module,
            Action = entity.Action,
            TargetType = entity.TargetType,
            TargetId = entity.TargetId,
            Actor = new AuditActor
            {
                Type = entity.ActorType,
                Id = entity.ActorId,
                DisplayName = entity.ActorDisplayName
            },
            Source = entity.Source,
            Outcome = entity.Outcome,
            StatusCode = entity.StatusCode,
            CorrelationId = entity.CorrelationId,
            Error = entity.Error,
            Changes = string.IsNullOrWhiteSpace(entity.ChangesJson)
                ? null
                : JsonSerializer.Deserialize<AuditChangeSet>(entity.ChangesJson)
        };
    }

    private static string? Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToLowerInvariant();
    }
}