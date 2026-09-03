using Microsoft.EntityFrameworkCore;

namespace QuickProxy.Proxy.Config.Storage.Db;

public sealed class QuickConfigDbContext(DbContextOptions<QuickConfigDbContext> options) : DbContext(options)
{
    public DbSet<ConfigEntryEntity> ConfigEntries => Set<ConfigEntryEntity>();
    public DbSet<ConfigEntryRevisionEntity> ConfigEntryRevisions => Set<ConfigEntryRevisionEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ConfigEntryEntity>(b =>
        {
            b.ToTable("ConfigEntries");
            b.HasKey(x => x.Key);
            b.Property(x => x.Key).HasMaxLength(1024);
            b.Property(x => x.UpdatedBy).HasMaxLength(320);
            b.Property(x => x.Value).IsRequired();
            b.Property(x => x.EntryType).HasMaxLength(32);
            b.Property(x => x.PayloadKind).HasMaxLength(32);
            b.Property(x => x.MediaType).HasMaxLength(256);
        });

        modelBuilder.Entity<ConfigEntryRevisionEntity>(b =>
        {
            b.ToTable("ConfigEntryRevisions");
            b.HasKey(x => x.RevisionId);
            b.HasIndex(x => new { x.Key, x.CapturedAtUtc });
            b.Property(x => x.RevisionId).HasMaxLength(64);
            b.Property(x => x.Key).HasMaxLength(1024);
            b.Property(x => x.UpdatedBy).HasMaxLength(320);
            b.Property(x => x.CapturedBy).HasMaxLength(320);
            b.Property(x => x.Action).HasMaxLength(64);
            b.Property(x => x.Value).IsRequired();
            b.Property(x => x.EntryType).HasMaxLength(32);
            b.Property(x => x.PayloadKind).HasMaxLength(32);
            b.Property(x => x.MediaType).HasMaxLength(256);
        });
    }
}

public sealed class ConfigEntryEntity
{
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string? EntryType { get; set; }
    public string? PayloadKind { get; set; }
    public string? MediaType { get; set; }
    public string? LabelsJson { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public string? UpdatedBy { get; set; }
}

public sealed class ConfigEntryRevisionEntity
{
    public string RevisionId { get; set; } = string.Empty;
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string? EntryType { get; set; }
    public string? PayloadKind { get; set; }
    public string? MediaType { get; set; }
    public string? LabelsJson { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public string? UpdatedBy { get; set; }
    public DateTimeOffset CapturedAtUtc { get; set; }
    public string? CapturedBy { get; set; }
    public string? Action { get; set; }
}