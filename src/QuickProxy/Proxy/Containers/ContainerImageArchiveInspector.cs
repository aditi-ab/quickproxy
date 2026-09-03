using System.Formats.Tar;
using System.IO.Compression;
using System.Text.Json;

namespace QuickProxy.Proxy.Containers;

public static class ContainerImageArchiveInspector
{
    public static async Task<IReadOnlyList<string>> ReadRepoTagsAsync(string archivePath,
        CancellationToken cancellationToken)
    {
        await using var fileStream = File.OpenRead(archivePath);
        await using var archiveStream = OpenArchiveStream(fileStream);
        var reader = new TarReader(archiveStream, false);

        TarEntry? entry;
        while ((entry = await reader.GetNextEntryAsync(false, cancellationToken)) is not null)
        {
            if (!string.Equals(entry.Name, "manifest.json", StringComparison.OrdinalIgnoreCase)) continue;

            await using var manifestStream = entry.DataStream;
            if (manifestStream is null) return [];

            var manifest =
                await JsonSerializer.DeserializeAsync<List<ManifestEntry>>(manifestStream,
                    cancellationToken: cancellationToken)
                ?? [];

            return manifest
                .SelectMany(x => x.RepoTags ?? [])
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        return [];
    }

    public static Stream OpenArchiveStream(Stream source)
    {
        if (IsGzip(source)) return new GZipStream(source, CompressionMode.Decompress, false);

        return source;
    }

    private static bool IsGzip(Stream source)
    {
        if (!source.CanSeek) return false;

        var originalPosition = source.Position;
        Span<byte> header = stackalloc byte[2];

        try
        {
            var bytesRead = source.Read(header);
            return bytesRead == 2 && header[0] == 0x1F && header[1] == 0x8B;
        }
        finally
        {
            source.Position = originalPosition;
        }
    }

    private sealed class ManifestEntry
    {
        public List<string>? RepoTags { get; set; }
    }
}