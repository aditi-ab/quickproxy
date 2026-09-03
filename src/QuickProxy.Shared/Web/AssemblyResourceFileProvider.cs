using System.Reflection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Primitives;

namespace QuickProxy.Shared.Web;

public sealed class AssemblyResourceFileProvider(Assembly assembly, string resourcePrefix) : IFileProvider
{
    private readonly string _resourcePrefix = resourcePrefix.TrimEnd('.');

    private readonly HashSet<string> _resources = assembly
        .GetManifestResourceNames()
        .ToHashSet(StringComparer.Ordinal);

    public IDirectoryContents GetDirectoryContents(string subpath)
    {
        return NotFoundDirectoryContents.Singleton;
    }

    public IFileInfo GetFileInfo(string subpath)
    {
        var normalized = NormalizeSubpath(subpath);
        if (string.IsNullOrWhiteSpace(normalized)) return new NotFoundFileInfo(subpath);

        var resourceName = $"{_resourcePrefix}.{normalized.Replace('/', '.')}";
        if (!_resources.Contains(resourceName)) return new NotFoundFileInfo(subpath);

        var name = Path.GetFileName(normalized);
        return new AssemblyResourceFileInfo(assembly, resourceName, name);
    }

    public IChangeToken Watch(string filter)
    {
        return NullChangeToken.Singleton;
    }

    private static string NormalizeSubpath(string subpath)
    {
        if (string.IsNullOrWhiteSpace(subpath)) return string.Empty;

        return subpath.Trim().TrimStart('/').Replace('\\', '/');
    }
}

internal sealed class AssemblyResourceFileInfo(Assembly assembly, string resourceName, string name) : IFileInfo
{
    public bool Exists => true;

    public long Length
    {
        get
        {
            using var stream = CreateReadStream();
            return stream.Length;
        }
    }

    public string PhysicalPath => string.Empty;
    public string Name { get; } = name;
    public DateTimeOffset LastModified { get; } = DateTimeOffset.UtcNow;
    public bool IsDirectory => false;

    public Stream CreateReadStream()
    {
        return assembly.GetManifestResourceStream(resourceName)
               ?? throw new FileNotFoundException($"Embedded resource '{resourceName}' was not found.");
    }
}