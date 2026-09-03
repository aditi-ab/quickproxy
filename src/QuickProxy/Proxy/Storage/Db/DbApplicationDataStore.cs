using Microsoft.EntityFrameworkCore;

namespace QuickProxy.Proxy.Storage.Db;

public interface IApplicationDataStore
{
    byte[] GetOrCreate(string id, Func<byte[]> createContent);
}

public sealed class DbApplicationDataStore(IDbContextFactory<QuickProxyDbContext> factory) : IApplicationDataStore
{
    private static readonly object Gate = new();

    public byte[] GetOrCreate(string id, Func<byte[]> createContent)
    {
        lock (Gate)
        {
            using var db = factory.CreateDbContext();
            var content = db.ApplicationData.AsNoTracking()
                .Where(item => item.Id == id)
                .Select(item => item.Content)
                .SingleOrDefault();
            if (content is not null) return content;

            content = createContent();
            db.ApplicationData.Add(new ApplicationDataEntity { Id = id, Content = content });
            db.SaveChanges();
            return content;
        }
    }
}