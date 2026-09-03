using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace QuickProxy.Proxy.Storage.Db;

public sealed class QuickProxyDbContext(DbContextOptions<QuickProxyDbContext> options) : DbContext(options),
    IDataProtectionKeyContext
{
    public DbSet<HostConfigEntity> Hosts => Set<HostConfigEntity>();
    public DbSet<DomainTranslationRuleEntity> DomainTranslations => Set<DomainTranslationRuleEntity>();
    public DbSet<FallbackSettingsEntity> FallbackSettings => Set<FallbackSettingsEntity>();
    public DbSet<ContainerDefaultsSettingsEntity> ContainerDefaultsSettings => Set<ContainerDefaultsSettingsEntity>();
    public DbSet<ComposeProjectsSettingsEntity> ComposeProjectsSettings => Set<ComposeProjectsSettingsEntity>();
    public DbSet<CertificateConfigEntity> Certificates => Set<CertificateConfigEntity>();
    public DbSet<CertificateFileEntity> CertificateFiles => Set<CertificateFileEntity>();
    public DbSet<UserEntity> Users => Set<UserEntity>();
    public DbSet<AuthProviderConfigEntity> AuthProviders => Set<AuthProviderConfigEntity>();
    public DbSet<AdminIdentityUserEntity> AdminIdentityUsers => Set<AdminIdentityUserEntity>();
    public DbSet<AdminIdentityProviderEntity> AdminIdentityProviders => Set<AdminIdentityProviderEntity>();
    public DbSet<ApplicationDataEntity> ApplicationData => Set<ApplicationDataEntity>();
    public DbSet<DataProtectionKey> DataProtectionKeys { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<HostConfigEntity>(b =>
        {
            b.ToTable("ProxyHosts");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasMaxLength(200);
            b.Property(x => x.Json).IsRequired();
        });

        modelBuilder.Entity<DomainTranslationRuleEntity>(b =>
        {
            b.ToTable("DomainTranslations");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasMaxLength(200);
            b.Property(x => x.Json).IsRequired();
        });

        modelBuilder.Entity<FallbackSettingsEntity>(b =>
        {
            b.ToTable("FallbackSettings");
            b.HasKey(x => x.Id);
            b.Property(x => x.Json).IsRequired();
        });

        modelBuilder.Entity<ContainerDefaultsSettingsEntity>(b =>
        {
            b.ToTable("ContainerDefaultsSettings");
            b.HasKey(x => x.Id);
            b.Property(x => x.Json).IsRequired();
        });

        modelBuilder.Entity<ComposeProjectsSettingsEntity>(b =>
        {
            b.ToTable("ComposeProjectsSettings");
            b.HasKey(x => x.Id);
            b.Property(x => x.Json).IsRequired();
        });

        modelBuilder.Entity<CertificateConfigEntity>(b =>
        {
            b.ToTable("CertificateConfigs");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasMaxLength(200);
            b.Property(x => x.Json).IsRequired();
        });

        modelBuilder.Entity<CertificateFileEntity>(b =>
        {
            b.ToTable("CertificateFiles");
            b.HasKey(x => new { x.CertificateId, x.FileName });
            b.Property(x => x.CertificateId).HasMaxLength(200);
            b.Property(x => x.FileName).HasMaxLength(100);
            b.Property(x => x.Content).IsRequired();
        });

        modelBuilder.Entity<UserEntity>(b =>
        {
            b.ToTable("Users");
            b.HasKey(x => x.Email);
            b.Property(x => x.Email).HasMaxLength(320);
            b.Property(x => x.PasswordHash).IsRequired();
            b.Property(x => x.FullName).HasMaxLength(200);
            b.Property(x => x.ExternalIdentitiesJson);
        });

        modelBuilder.Entity<AuthProviderConfigEntity>(b =>
        {
            b.ToTable("AuthProviderConfigs");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasMaxLength(200);
            b.Property(x => x.Json).IsRequired();
        });

        modelBuilder.Entity<AdminIdentityUserEntity>(b =>
        {
            b.ToTable("AdminIdentityUsers");
            b.HasKey(x => x.Id);
            b.HasIndex(x => x.NormalizedUsername).IsUnique();
            b.Property(x => x.NormalizedUsername).HasMaxLength(320);
            b.Property(x => x.Json).IsRequired();
        });

        modelBuilder.Entity<AdminIdentityProviderEntity>(b =>
        {
            b.ToTable("AdminIdentityProviders");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasMaxLength(200);
            b.Property(x => x.Json).IsRequired();
        });

        modelBuilder.Entity<ApplicationDataEntity>(b =>
        {
            b.ToTable("ApplicationData");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasMaxLength(200);
            b.Property(x => x.Content).IsRequired();
        });

        modelBuilder.Entity<DataProtectionKey>(b => b.ToTable("DataProtectionKeys"));
    }
}

public sealed class HostConfigEntity
{
    public string Id { get; set; } = string.Empty;
    public string Json { get; set; } = string.Empty;
}

public sealed class FallbackSettingsEntity
{
    public int Id { get; set; } = 1;
    public string Json { get; set; } = string.Empty;
}

public sealed class DomainTranslationRuleEntity
{
    public string Id { get; set; } = string.Empty;
    public string Json { get; set; } = string.Empty;
}

public sealed class CertificateConfigEntity
{
    public string Id { get; set; } = string.Empty;
    public string Json { get; set; } = string.Empty;
}

public sealed class ContainerDefaultsSettingsEntity
{
    public int Id { get; set; } = 1;
    public string Json { get; set; } = string.Empty;
}

public sealed class ComposeProjectsSettingsEntity
{
    public int Id { get; set; } = 1;
    public string Json { get; set; } = string.Empty;
}

public sealed class CertificateFileEntity
{
    public string CertificateId { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public byte[] Content { get; set; } = [];
}

public sealed class UserEntity
{
    public string Email { get; set; } = string.Empty;
    public string? FullName { get; set; }
    public bool Enabled { get; set; } = true;
    public string PasswordHash { get; set; } = string.Empty;
    public string? ExternalIdentitiesJson { get; set; }
}

public sealed class AuthProviderConfigEntity
{
    public string Id { get; set; } = string.Empty;
    public string Json { get; set; } = string.Empty;
}

public sealed class AdminIdentityUserEntity
{
    public Guid Id { get; set; }
    public string NormalizedUsername { get; set; } = string.Empty;
    public string Json { get; set; } = string.Empty;
}

public sealed class AdminIdentityProviderEntity
{
    public string Id { get; set; } = string.Empty;
    public string Json { get; set; } = string.Empty;
}

public sealed class ApplicationDataEntity
{
    public string Id { get; set; } = string.Empty;
    public byte[] Content { get; set; } = [];
}