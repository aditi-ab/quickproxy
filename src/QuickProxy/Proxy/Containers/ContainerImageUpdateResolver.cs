using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace QuickProxy.Proxy.Containers;

public sealed class ContainerImageUpdateResolver(
    IHttpClientFactory httpClientFactory,
    IOptions<ContainerRuntimeSettings> options,
    ILogger<ContainerImageUpdateResolver> logger)
{
    private static readonly string[] ManifestAcceptHeaders =
    [
        "application/vnd.oci.image.index.v1+json",
        "application/vnd.docker.distribution.manifest.list.v2+json",
        "application/vnd.oci.image.manifest.v1+json",
        "application/vnd.docker.distribution.manifest.v2+json"
    ];

    private readonly ContainerImageUpdateSettings _settings = options.Value.ImageUpdates;

    public async Task<IReadOnlyDictionary<string, ContainerImageUpdateInfo>> ResolveAsync(
        IReadOnlyList<ContainerInventoryItem> containers,
        CancellationToken cancellationToken)
    {
        var results = new Dictionary<string, ContainerImageUpdateInfo>(StringComparer.OrdinalIgnoreCase);
        var running = containers
            .Where(x => x.IsRunning)
            .Where(x => !string.IsNullOrWhiteSpace(x.Name))
            .ToArray();

        if (running.Length == 0) return results;

        var imageCache = new Dictionary<string, ContainerImageUpdateInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var container in running)
        {
            var cacheKey =
                $"{container.Image}|{container.ImageDigest}|{container.ImageArchitecture}|{container.ImageOs}";
            if (!imageCache.TryGetValue(cacheKey, out var resolved))
            {
                resolved = await ResolveForContainerAsync(container, cancellationToken);
                imageCache[cacheKey] = resolved;
            }

            results[container.Name] = Clone(resolved);
        }

        return results;
    }

    private async Task<ContainerImageUpdateInfo> ResolveForContainerAsync(ContainerInventoryItem container,
        CancellationToken cancellationToken)
    {
        var checkedAtUtc = DateTimeOffset.UtcNow;

        if (string.Equals(container.ContainerLabels.GetValueOrDefault("quickproxy.internal.image-source"), "archive",
                StringComparison.OrdinalIgnoreCase))
            return new ContainerImageUpdateInfo
            {
                Status = "unsupported",
                Error = "Image was loaded from a local archive.",
                CheckedAtUtc = checkedAtUtc,
                LocalDigest = container.ImageDigest
            };

        var imageReference = ContainerImageReferenceParser.Parse(container.Image);
        if (string.IsNullOrWhiteSpace(imageReference.Repository))
            return new ContainerImageUpdateInfo
            {
                Status = "unsupported",
                Error = "Container image reference is empty.",
                CheckedAtUtc = checkedAtUtc,
                LocalDigest = container.ImageDigest
            };

        if (imageReference.IsDigestReference)
            return new ContainerImageUpdateInfo
            {
                Status = "unsupported",
                Error = "Container uses an immutable digest reference.",
                CheckedAtUtc = checkedAtUtc,
                LocalDigest = imageReference.Digest
            };

        if (string.IsNullOrWhiteSpace(container.ImageDigest))
            return new ContainerImageUpdateInfo
            {
                Status = "error",
                Error = "Local image digest is unavailable.",
                CheckedAtUtc = checkedAtUtc
            };

        var localOs = container.ImageOs;
        var localArchitecture = container.ImageArchitecture;

        var attempts = BuildAttempts(imageReference);
        Exception? lastException = null;

        foreach (var attempt in attempts)
            try
            {
                var remote = await TryResolveRemoteAsync(attempt, localOs, localArchitecture, cancellationToken);
                if (remote is null) continue;

                var localDigest = container.ImageDigest;
                var isCurrent =
                    string.Equals(localDigest, remote.TagDigest, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(localDigest, remote.PlatformDigest, StringComparison.OrdinalIgnoreCase);
                var remoteDigest = SelectDisplayedRemoteDigest(localDigest, remote);

                return new ContainerImageUpdateInfo
                {
                    Status = isCurrent ? "current" : "outdated",
                    UpdateAvailable = !isCurrent,
                    Source = remote.Source,
                    LocalDigest = localDigest,
                    RemoteDigest = remoteDigest,
                    CheckedAtUtc = checkedAtUtc,
                    RemoteCreatedUtc = remote.RemoteCreatedUtc,
                    RemoteArchitecture = remote.RemoteArchitecture,
                    RemoteOs = remote.RemoteOs,
                    RemoteLabels = new Dictionary<string, string>(remote.RemoteLabels, StringComparer.OrdinalIgnoreCase)
                };
            }
            catch (Exception ex)
            {
                lastException = ex;
                logger.LogDebug(ex, "Failed resolving image update metadata for '{Image}' via {Source}.",
                    container.Image, attempt.Source);
            }

        return new ContainerImageUpdateInfo
        {
            Status = "error",
            LocalDigest = container.ImageDigest,
            CheckedAtUtc = checkedAtUtc,
            Error = lastException?.Message ?? "Remote image metadata could not be resolved."
        };
    }

    private List<RegistryAttempt> BuildAttempts(ContainerImageReference imageReference)
    {
        var attempts = new List<RegistryAttempt>();

        if (imageReference.UsesDefaultRegistry)
        {
            attempts.Add(new RegistryAttempt(
                "https://registry-1.docker.io",
                imageReference.Repository,
                imageReference.Tag,
                "dockerhub"));

            var harborBaseUrl = (_settings.HarborUrl ?? string.Empty).Trim().TrimEnd('/');
            if (!string.IsNullOrWhiteSpace(harborBaseUrl))
            {
                var prefix = (_settings.HarborRepositoryPrefix ?? string.Empty).Trim().Trim('/');
                var repository = string.IsNullOrWhiteSpace(prefix)
                    ? imageReference.Repository
                    : $"{prefix}/{imageReference.Repository}";
                attempts.Add(new RegistryAttempt(
                    harborBaseUrl,
                    repository,
                    imageReference.Tag,
                    "harbor"));
            }

            return attempts;
        }

        attempts.Add(new RegistryAttempt(
            $"https://{imageReference.RegistryHost}",
            imageReference.Repository,
            imageReference.Tag,
            imageReference.RegistryHost));

        return attempts;
    }

    private async Task<RemoteImageMetadata?> TryResolveRemoteAsync(
        RegistryAttempt attempt,
        string? localOs,
        string? localArchitecture,
        CancellationToken cancellationToken)
    {
        using var client = httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(Math.Max(5, _settings.TimeoutSeconds));

        var manifestResponse = await SendRegistryRequestAsync(
            client,
            attempt.BuildManifestUri(attempt.Tag),
            true,
            cancellationToken);
        if (manifestResponse.StatusCode == HttpStatusCode.NotFound) return null;

        manifestResponse.EnsureSuccessStatusCode();
        var rootManifest = await JsonDocument.ParseAsync(
            await manifestResponse.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
        var rootDigest = ReadDigestHeader(manifestResponse);
        var mediaType = manifestResponse.Content.Headers.ContentType?.MediaType;
        if (string.IsNullOrWhiteSpace(mediaType)
            && rootManifest.RootElement.TryGetProperty("mediaType", out var mediaTypeElement))
            mediaType = mediaTypeElement.GetString();

        var selectedDigest = rootDigest;
        if (IsManifestIndex(mediaType))
        {
            selectedDigest = SelectPlatformManifestDigest(rootManifest.RootElement, localOs, localArchitecture) ??
                             rootDigest;
            if (string.IsNullOrWhiteSpace(selectedDigest)) return null;
        }

        JsonDocument imageManifest;
        string manifestDigest;
        if (!string.IsNullOrWhiteSpace(selectedDigest) &&
            !string.Equals(selectedDigest, rootDigest, StringComparison.OrdinalIgnoreCase))
        {
            using var selectedManifestResponse = await SendRegistryRequestAsync(
                client,
                attempt.BuildManifestUri(selectedDigest),
                true,
                cancellationToken);
            selectedManifestResponse.EnsureSuccessStatusCode();
            imageManifest = await JsonDocument.ParseAsync(
                await selectedManifestResponse.Content.ReadAsStreamAsync(cancellationToken),
                cancellationToken: cancellationToken);
            manifestDigest = ReadDigestHeader(selectedManifestResponse) ?? selectedDigest;
        }
        else
        {
            imageManifest = rootManifest;
            manifestDigest = rootDigest ?? selectedDigest ?? string.Empty;
        }

        if (!imageManifest.RootElement.TryGetProperty("config", out var configElement) ||
            !configElement.TryGetProperty("digest", out var configDigestElement))
            return new RemoteImageMetadata(rootDigest, manifestDigest, attempt.Source, null, null, null,
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));

        var configDigest = configDigestElement.GetString();
        if (string.IsNullOrWhiteSpace(configDigest))
            return new RemoteImageMetadata(rootDigest, manifestDigest, attempt.Source, null, null, null,
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));

        using var configResponse =
            await SendRegistryRequestAsync(client, attempt.BuildBlobUri(configDigest), false, cancellationToken);
        configResponse.EnsureSuccessStatusCode();
        using var config = await JsonDocument.ParseAsync(
            await configResponse.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);

        return new RemoteImageMetadata(
            rootDigest,
            manifestDigest,
            attempt.Source,
            TryReadDateTimeOffset(config.RootElement, "created"),
            TryReadString(config.RootElement, "architecture"),
            TryReadString(config.RootElement, "os"),
            TryReadLabels(config.RootElement));
    }

    private static async Task<HttpResponseMessage> SendRegistryRequestAsync(
        HttpClient client,
        Uri uri,
        bool manifestRequest,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        if (manifestRequest)
            foreach (var value in ManifestAcceptHeaders)
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(value));

        var response = await client.SendAsync(request, cancellationToken);
        if (response.StatusCode != HttpStatusCode.Unauthorized || response.Headers.WwwAuthenticate.Count == 0)
            return response;

        var challenge = response.Headers.WwwAuthenticate.FirstOrDefault(x =>
            string.Equals(x.Scheme, "Bearer", StringComparison.OrdinalIgnoreCase));
        if (challenge is null) return response;

        var challengeValues = ParseAuthenticationParameters(challenge.Parameter);
        if (!challengeValues.TryGetValue("realm", out var realm) || string.IsNullOrWhiteSpace(realm)) return response;

        var tokenUri = BuildTokenUri(realm, challengeValues);
        if (tokenUri is null) return response;

        response.Dispose();
        using var tokenResponse = await client.GetAsync(tokenUri, cancellationToken);
        tokenResponse.EnsureSuccessStatusCode();
        using var tokenPayload = await JsonDocument.ParseAsync(
            await tokenResponse.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
        var token = TryReadString(tokenPayload.RootElement, "token") ??
                    TryReadString(tokenPayload.RootElement, "access_token");
        if (string.IsNullOrWhiteSpace(token))
            throw new InvalidOperationException("Registry authentication token was missing from the auth response.");

        using var authenticatedRequest = new HttpRequestMessage(HttpMethod.Get, uri);
        authenticatedRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (manifestRequest)
            foreach (var value in ManifestAcceptHeaders)
                authenticatedRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(value));

        return await client.SendAsync(authenticatedRequest, cancellationToken);
    }

    private static string? ReadDigestHeader(HttpResponseMessage response)
    {
        return response.Headers.TryGetValues("Docker-Content-Digest", out var values)
            ? values.FirstOrDefault()
            : null;
    }

    private static bool IsManifestIndex(string? mediaType)
    {
        return string.Equals(mediaType, "application/vnd.oci.image.index.v1+json", StringComparison.OrdinalIgnoreCase)
               || string.Equals(mediaType, "application/vnd.docker.distribution.manifest.list.v2+json",
                   StringComparison.OrdinalIgnoreCase);
    }

    private static string? SelectPlatformManifestDigest(JsonElement root, string? os, string? architecture)
    {
        if (!root.TryGetProperty("manifests", out var manifestsElement) ||
            manifestsElement.ValueKind != JsonValueKind.Array) return null;

        string? fallback = null;
        foreach (var manifest in manifestsElement.EnumerateArray())
        {
            var digest = TryReadString(manifest, "digest");
            if (fallback is null && !string.IsNullOrWhiteSpace(digest)) fallback = digest;

            if (!manifest.TryGetProperty("platform", out var platform)) continue;

            var candidateOs = TryReadString(platform, "os");
            var candidateArchitecture = TryReadString(platform, "architecture");
            if (string.Equals(candidateOs, os, StringComparison.OrdinalIgnoreCase)
                && string.Equals(candidateArchitecture, architecture, StringComparison.OrdinalIgnoreCase))
                return digest;
        }

        return fallback;
    }

    private static Dictionary<string, string> TryReadLabels(JsonElement root)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!root.TryGetProperty("config", out var configElement) ||
            !configElement.TryGetProperty("Labels", out var labelsElement) ||
            labelsElement.ValueKind != JsonValueKind.Object)
            return result;

        foreach (var property in labelsElement.EnumerateObject())
            result[property.Name] = property.Value.GetString() ?? string.Empty;

        return result;
    }

    private static DateTimeOffset? TryReadDateTimeOffset(JsonElement element, string propertyName)
    {
        var value = TryReadString(element, propertyName);
        return DateTimeOffset.TryParse(value, out var parsed) ? parsed : null;
    }

    private static string? TryReadString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
    }

    private static Dictionary<string, string> ParseAuthenticationParameters(string? parameter)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var segment in (parameter ?? string.Empty).Split(',',
                     StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var separatorIndex = segment.IndexOf('=');
            if (separatorIndex <= 0) continue;

            var key = segment[..separatorIndex].Trim();
            var value = segment[(separatorIndex + 1)..].Trim().Trim('"');
            result[key] = value;
        }

        return result;
    }

    private static Uri? BuildTokenUri(string realm, IReadOnlyDictionary<string, string> challengeValues)
    {
        if (!Uri.TryCreate(realm, UriKind.Absolute, out var baseUri)) return null;

        var queryParts = new List<string>();
        foreach (var key in new[] { "service", "scope" })
            if (challengeValues.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
                queryParts.Add($"{Uri.EscapeDataString(key)}={Uri.EscapeDataString(value)}");

        var builder = new UriBuilder(baseUri)
        {
            Query = string.Join("&", queryParts)
        };
        return builder.Uri;
    }

    private static ContainerImageUpdateInfo Clone(ContainerImageUpdateInfo source)
    {
        return new ContainerImageUpdateInfo
        {
            Status = source.Status,
            UpdateAvailable = source.UpdateAvailable,
            Source = source.Source,
            LocalDigest = source.LocalDigest,
            RemoteDigest = source.RemoteDigest,
            Error = source.Error,
            CheckedAtUtc = source.CheckedAtUtc,
            RemoteCreatedUtc = source.RemoteCreatedUtc,
            RemoteArchitecture = source.RemoteArchitecture,
            RemoteOs = source.RemoteOs,
            RemoteLabels = new Dictionary<string, string>(source.RemoteLabels, StringComparer.OrdinalIgnoreCase)
        };
    }

    private static string? SelectDisplayedRemoteDigest(string? localDigest, RemoteImageMetadata remote)
    {
        if (!string.IsNullOrWhiteSpace(localDigest))
        {
            if (string.Equals(localDigest, remote.TagDigest, StringComparison.OrdinalIgnoreCase))
                return remote.TagDigest;

            if (string.Equals(localDigest, remote.PlatformDigest, StringComparison.OrdinalIgnoreCase))
                return remote.PlatformDigest;
        }

        return remote.TagDigest ?? remote.PlatformDigest;
    }

    private sealed record RegistryAttempt(string BaseUrl, string Repository, string Tag, string Source)
    {
        public Uri BuildManifestUri(string reference)
        {
            return new Uri($"{BaseUrl.TrimEnd('/')}/v2/{Repository}/manifests/{Uri.EscapeDataString(reference)}",
                UriKind.Absolute);
        }

        public Uri BuildBlobUri(string digest)
        {
            return new Uri($"{BaseUrl.TrimEnd('/')}/v2/{Repository}/blobs/{Uri.EscapeDataString(digest)}",
                UriKind.Absolute);
        }
    }

    private sealed record RemoteImageMetadata(
        string? TagDigest,
        string? PlatformDigest,
        string Source,
        DateTimeOffset? RemoteCreatedUtc,
        string? RemoteArchitecture,
        string? RemoteOs,
        Dictionary<string, string> RemoteLabels);
}