using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using QuickProxy.Proxy.Models;

namespace QuickProxy.Proxy.Storage.Db;

public sealed class DbCertificateStore(IDbContextFactory<QuickProxyDbContext> factory) : ICertificateStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    static DbCertificateStore()
    {
        JsonOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
    }

    public string CertificatesRootDirectory => "database://certificates";

    public IReadOnlyList<StoredCertificateConfig> List()
    {
        using var db = factory.CreateDbContext();
        var entities = db.Certificates.AsNoTracking()
            .OrderBy(x => x.Id)
            .ToArray();

        return entities
            .Select(x => JsonSerializer.Deserialize<StoredCertificateConfig>(x.Json, JsonOptions))
            .Where(x => x != null)
            .Select(x => x!)
            .Select(ApplyFileFlags)
            .ToArray();
    }

    public StoredCertificateConfig? Get(string id)
    {
        using var db = factory.CreateDbContext();
        var entity = db.Certificates.AsNoTracking().FirstOrDefault(x => x.Id == id);
        if (entity is null) return null;

        var config = JsonSerializer.Deserialize<StoredCertificateConfig>(entity.Json, JsonOptions);
        return config is null ? null : ApplyFileFlags(config);
    }

    public bool Exists(string id)
    {
        using var db = factory.CreateDbContext();
        return db.Certificates.Any(x => x.Id == id);
    }

    public void Upsert(StoredCertificateConfig config)
    {
        using var db = factory.CreateDbContext();
        var json = JsonSerializer.Serialize(config, JsonOptions);
        var existing = db.Certificates.FirstOrDefault(x => x.Id == config.Id);
        if (existing is null)
            db.Certificates.Add(new CertificateConfigEntity { Id = config.Id, Json = json });
        else
            existing.Json = json;

        db.SaveChanges();
    }

    public bool Delete(string id)
    {
        using var db = factory.CreateDbContext();
        var config = db.Certificates.FirstOrDefault(x => x.Id == id);
        if (config is null) return false;

        db.Certificates.Remove(config);

        var files = db.CertificateFiles.Where(x => x.CertificateId == id).ToArray();
        db.CertificateFiles.RemoveRange(files);
        db.SaveChanges();
        return true;
    }

    public byte[]? GetFile(string id, string fileName)
    {
        using var db = factory.CreateDbContext();
        return db.CertificateFiles
            .AsNoTracking()
            .Where(x => x.CertificateId == id && x.FileName == fileName)
            .Select(x => x.Content)
            .FirstOrDefault();
    }

    public void SaveFiles(string id, IReadOnlyDictionary<string, byte[]> files)
    {
        if (files.Count == 0) return;

        using var db = factory.CreateDbContext();
        foreach (var (fileName, content) in files)
        {
            var existing = db.CertificateFiles.FirstOrDefault(x => x.CertificateId == id && x.FileName == fileName);
            if (existing is null)
                db.CertificateFiles.Add(new CertificateFileEntity
                {
                    CertificateId = id,
                    FileName = fileName,
                    Content = content
                });
            else
                existing.Content = content;
        }

        db.SaveChanges();
    }

    private StoredCertificateConfig ApplyFileFlags(StoredCertificateConfig config)
    {
        using var db = factory.CreateDbContext();
        var fileNames = db.CertificateFiles
            .AsNoTracking()
            .Where(x => x.CertificateId == config.Id)
            .Select(x => x.FileName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        config.HasCertificateFile = fileNames.Contains("certificate.pem");
        config.HasKeyFile = fileNames.Contains("key.pem");
        config.HasIntermediateFile = fileNames.Contains("intermediate.pem");
        config.HasPfxFile = fileNames.Contains("certificate.pfx");
        return config;
    }
}