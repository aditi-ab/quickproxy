namespace QuickProxy.Proxy.Containers;

internal sealed record ContainerImageReference(
    string Original,
    string RegistryHost,
    string Repository,
    string Tag,
    string? Digest,
    bool UsesDefaultRegistry)
{
    public bool IsDigestReference => !string.IsNullOrWhiteSpace(Digest);
}

internal static class ContainerImageReferenceParser
{
    private static readonly string[] DockerHubHosts =
    [
        "docker.io",
        "index.docker.io",
        "registry-1.docker.io"
    ];

    public static ContainerImageReference Parse(string? imageReference)
    {
        var original = (imageReference ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(original))
            return new ContainerImageReference(string.Empty, "registry-1.docker.io", string.Empty, "latest", null,
                true);

        var withoutDigest = original;
        string? digest = null;
        var atIndex = original.IndexOf('@');
        if (atIndex >= 0)
        {
            withoutDigest = original[..atIndex];
            digest = original[(atIndex + 1)..].Trim();
        }

        var normalized = withoutDigest;
        var lastSlash = normalized.LastIndexOf('/');
        var lastColon = normalized.LastIndexOf(':');
        var tag = "latest";
        if (lastColon > lastSlash)
        {
            tag = normalized[(lastColon + 1)..].Trim();
            normalized = normalized[..lastColon];
        }

        var firstSlash = normalized.IndexOf('/');
        var firstSegment = firstSlash >= 0 ? normalized[..firstSlash] : normalized;
        var hasRegistryHost = firstSegment.Contains('.', StringComparison.Ordinal)
                              || firstSegment.Contains(':', StringComparison.Ordinal)
                              || string.Equals(firstSegment, "localhost", StringComparison.OrdinalIgnoreCase);

        var registryHost = hasRegistryHost ? firstSegment : "registry-1.docker.io";
        var repository = hasRegistryHost && firstSlash >= 0
            ? normalized[(firstSlash + 1)..]
            : normalized;

        var usesDefaultRegistry =
            !hasRegistryHost || DockerHubHosts.Contains(registryHost, StringComparer.OrdinalIgnoreCase);
        if (usesDefaultRegistry && !repository.Contains('/')) repository = $"library/{repository}";

        return new ContainerImageReference(
            original,
            registryHost,
            repository.Trim('/'),
            string.IsNullOrWhiteSpace(tag) ? "latest" : tag,
            string.IsNullOrWhiteSpace(digest) ? null : digest,
            usesDefaultRegistry);
    }

    public static string NormalizeRepository(string? imageReference)
    {
        var parsed = Parse(imageReference);
        return NormalizeRepository(parsed.RegistryHost, parsed.Repository, parsed.UsesDefaultRegistry);
    }

    public static string NormalizeRepository(string? registryHost, string? repository, bool usesDefaultRegistry)
    {
        var host = (registryHost ?? string.Empty).Trim().ToLowerInvariant();
        var repo = (repository ?? string.Empty).Trim().Trim('/').ToLowerInvariant();

        if (usesDefaultRegistry || DockerHubHosts.Contains(host, StringComparer.OrdinalIgnoreCase))
            return repo.StartsWith("library/", StringComparison.OrdinalIgnoreCase)
                ? repo
                : repo.Contains('/', StringComparison.Ordinal)
                    ? repo
                    : $"library/{repo}";

        return repo;
    }
}