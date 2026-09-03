namespace QuickProxy;

internal static class DocumentationPathRewriter
{
    internal static PathString Resolve(PathString requestPath, Func<string, bool> fileExists)
    {
        if (!requestPath.StartsWithSegments("/docs", out var remainingPath) || !remainingPath.HasValue)
            return requestPath;

        var documentationFilePath = $"docs{remainingPath.Value}.html";
        return fileExists(documentationFilePath)
            ? new PathString($"{requestPath.Value}.html")
            : requestPath;
    }
}