using Microsoft.EntityFrameworkCore;

namespace QuickProxy.Audit.Db;

public sealed class QuickAuditDbContext(DbContextOptions<QuickAuditDbContext> options) : DbContext(options)
{
    public DbSet<AuditEventEntity> AuditEvents => Set<AuditEventEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AuditEventEntity>(b =>
        {
            b.ToTable("AuditEvents");
            b.HasKey(x => x.Id);
            b.HasIndex(x => x.OccurredAtUtc);
            b.HasIndex(x => x.Module);
            b.HasIndex(x => x.Action);
            b.HasIndex(x => x.TargetId);
            b.HasIndex(x => x.ActorId);
            b.Property(x => x.Id).HasMaxLength(64);
            b.Property(x => x.Module).HasMaxLength(128);
            b.Property(x => x.Action).HasMaxLength(128);
            b.Property(x => x.TargetType).HasMaxLength(128);
            b.Property(x => x.TargetId).HasMaxLength(512);
            b.Property(x => x.ActorType).HasMaxLength(64);
            b.Property(x => x.ActorId).HasMaxLength(320);
            b.Property(x => x.ActorDisplayName).HasMaxLength(320);
            b.Property(x => x.Source).HasMaxLength(128);
            b.Property(x => x.Outcome).HasMaxLength(64);
            b.Property(x => x.CorrelationId).HasMaxLength(128);
        });
    }
}

public sealed class AuditEventEntity
{
    public string Id { get; set; } = string.Empty;
    public DateTime OccurredAtUtc { get; set; }
    public string Module { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string? TargetType { get; set; }
    public string? TargetId { get; set; }
    public string ActorType { get; set; } = "user";
    public string? ActorId { get; set; }
    public string? ActorDisplayName { get; set; }
    public string Source { get; set; } = "admin-api";
    public string Outcome { get; set; } = "success";
    public int? StatusCode { get; set; }
    public string? CorrelationId { get; set; }
    public string? Error { get; set; }
    public string? ChangesJson { get; set; }
}