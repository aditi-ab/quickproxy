using System.Reflection;
using Microsoft.Extensions.FileProviders;

namespace QuickProxy.Shared.Web;

public static class FallbackPageFileProviderExtensions
{
    public static IFileProvider? CreateEmbeddedClientFallbackFileProvider(this Assembly assembly)
    {
        var names = assembly.GetManifestResourceNames();
        var resource =
            names.FirstOrDefault(x => x.EndsWith(".ClientFallback.not-found.html", StringComparison.Ordinal));
        if (resource is null) return null;

        var prefix = resource[..^"not-found.html".Length].TrimEnd('.');
        var provider = new AssemblyResourceFileProvider(assembly, prefix);
        return provider.GetFileInfo("not-found.html").Exists ? provider : null;
    }
}