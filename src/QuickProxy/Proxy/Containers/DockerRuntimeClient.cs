using System.Net;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using Docker.DotNet;
using Docker.DotNet.Models;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using QuickProxy.Proxy.Runtime;
using NetworkingConfig = Docker.DotNet.Models.NetworkingConfig;

namespace QuickProxy.Proxy.Containers;

public sealed class DockerRuntimeClient : IContainerRuntimeClient, IDisposable
{
    private const string InternalImageSourceLabelKey = "quickproxy.internal.image-source";
    private const string InternalRequestedImageReferenceLabelKey = "quickproxy.internal.requested-image";
    private const string ArchiveImageSourceLabelValue = "archive";
    private const string SelfUpdateHelperLabelKey = "quickproxy.internal.self-update-helper";

    private static readonly Regex LabelTemplateRegex =
        new(@"\{label\.([a-z0-9_.-]+)\}", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly HashSet<string> SupportedRestartPolicies = new(StringComparer.OrdinalIgnoreCase)
    {
        "no",
        "always",
        "unless-stopped",
        "on-failure"
    };

    private readonly DockerClient _client;
    private readonly string _configuredEndpoint;
    private readonly DockerEngineShellClient _shellClient;
    private readonly IHostTemplateValueProvider _templateValues;

    public DockerRuntimeClient(IOptions<ContainerRuntimeSettings> options, IHostTemplateValueProvider templateValues)
    {
        var endpoint = string.IsNullOrWhiteSpace(options.Value.Endpoint)
            ? GetDefaultEndpoint()
            : new Uri(options.Value.Endpoint);

        _client = new DockerClientConfiguration(endpoint).CreateClient();
        _shellClient = new DockerEngineShellClient(endpoint);
        _templateValues = templateValues;
        _configuredEndpoint = endpoint.ToString();
    }

    public async Task<IReadOnlyList<ContainerInventoryItem>> ListContainersAsync(CancellationToken cancellationToken)
    {
        var containers = await _client.Containers.ListContainersAsync(new ContainersListParameters
        {
            All = true
        }, cancellationToken);

        var results = new List<ContainerInventoryItem>(containers.Count);
        var imageMetadataById = new Dictionary<string, ImageMetadata>(StringComparer.OrdinalIgnoreCase);

        foreach (var container in containers.OrderBy(x => x.Names?.FirstOrDefault(), StringComparer.OrdinalIgnoreCase))
            try
            {
                var inspect = await _client.Containers.InspectContainerAsync(container.ID, cancellationToken);
                var imageMetadata = await GetImageMetadataAsync(inspect.Image, inspect.Config?.Image, imageMetadataById,
                    cancellationToken);
                results.Add(MapContainer(container, inspect, imageMetadata));
            }
            catch (DockerContainerNotFoundException)
            {
            }

        return results;
    }

    public async Task<ContainerStatsSnapshot> GetContainerStatsAsync(string containerId,
        CancellationToken cancellationToken)
    {
        await using var stream = await _client.Containers.GetContainerStatsAsync(containerId,
            new ContainerStatsParameters
            {
                Stream = false,
                OneShot = true
            }, cancellationToken);

        using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: false);
        var payload = await reader.ReadToEndAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(payload))
            throw new InvalidOperationException($"No stats were returned for container '{containerId}'.");

        var snapshot = JsonConvert.DeserializeObject<ContainerStatsResponse>(payload);
        if (snapshot is null)
            throw new InvalidOperationException($"No stats were returned for container '{containerId}'.");

        return MapStats(snapshot);
    }

    public async Task<IReadOnlyList<ContainerImageInventoryItem>> ListImagesAsync(bool includeAll,
        CancellationToken cancellationToken)
    {
        var images = await _client.Images.ListImagesAsync(new ImagesListParameters
        {
            All = includeAll
        }, cancellationToken);

        var visibleImages = includeAll
            ? images
            : images.Where(IsVisibleInDefaultImageList).ToList();

        return visibleImages
            .OrderBy(x => GetPrimaryImageTag(x), StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.ID, StringComparer.OrdinalIgnoreCase)
            .Select(MapImage)
            .ToList();
    }

    public async IAsyncEnumerable<ContainerLogEntry> StreamContainerLogsAsync(
        string name,
        string? since,
        int tail,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var container = await FindContainerAsync(name, cancellationToken);
        var inspect = await _client.Containers.InspectContainerAsync(container.ID, cancellationToken);
        var logSupport = GetLogSupport(inspect);
        if (!logSupport.Supported)
            throw new InvalidOperationException(logSupport.Reason ?? $"Logs are unavailable for container '{name}'.");

        using var stream = await _client.Containers.GetContainerLogsAsync(
            container.ID,
            inspect.Config?.Tty == true,
            new ContainerLogsParameters
            {
                ShowStdout = true,
                ShowStderr = true,
                Follow = true,
                Timestamps = true,
                Tail = Math.Max(1, tail).ToString(),
                Since = string.IsNullOrWhiteSpace(since) ? null : since
            },
            cancellationToken);

        var buffer = new byte[16 * 1024];
        var pending = new StringBuilder();

        while (!cancellationToken.IsCancellationRequested)
        {
            var result = await stream.ReadOutputAsync(buffer, 0, buffer.Length, cancellationToken);
            if (result.EOF) break;

            if (result.Count <= 0) continue;

            pending.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
            while (true)
            {
                var newlineIndex = IndexOfNewline(pending);
                if (newlineIndex < 0) break;

                var line = pending.ToString(0, newlineIndex);
                pending.Remove(0, newlineIndex + 1);
                if (line.EndsWith("\r", StringComparison.Ordinal)) line = line[..^1];

                if (string.IsNullOrWhiteSpace(line)) continue;

                yield return ParseLogEntry(line, result.Target.ToString());
            }
        }

        if (pending.Length > 0)
        {
            var line = pending.ToString().TrimEnd('\r', '\n');
            if (!string.IsNullOrWhiteSpace(line)) yield return ParseLogEntry(line, "stdout");
        }
    }

    public async IAsyncEnumerable<ContainerRuntimeEvent> WatchContainerEventsAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var channel = Channel.CreateUnbounded<ContainerRuntimeEvent>();
        var progress = new Progress<Message>(message =>
        {
            if (!string.Equals(message.Type, "container", StringComparison.OrdinalIgnoreCase)) return;

            if (string.Equals(message.Action, "start", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(message.Action, "stop", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(message.Action, "die", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(message.Action, "destroy", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(message.Action, "create", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(message.Action, "rename", StringComparison.OrdinalIgnoreCase))
            {
                var containerName = string.Empty;
                message.Actor?.Attributes?.TryGetValue("name", out containerName);
                channel.Writer.TryWrite(new ContainerRuntimeEvent(
                    message.Action ?? string.Empty,
                    message.ID,
                    containerName));
            }
        });

        var monitorTask =
            _client.System.MonitorEventsAsync(new ContainerEventsParameters(), progress, cancellationToken);
        _ = monitorTask.ContinueWith(task =>
        {
            if (task.IsCanceled)
            {
                channel.Writer.TryComplete();
                return;
            }

            if (task.IsFaulted)
            {
                channel.Writer.TryComplete(task.Exception?.GetBaseException());
                return;
            }

            channel.Writer.TryComplete();
        }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);

        await foreach (var item in channel.Reader.ReadAllAsync(cancellationToken)) yield return item;

        await monitorTask;
    }

    public async Task<ContainerEditRequest> GetEditableContainerAsync(string name, CancellationToken cancellationToken)
    {
        var container = await FindContainerAsync(name, cancellationToken);
        var inspect = await _client.Containers.InspectContainerAsync(container.ID, cancellationToken);
        var imageMetadata = await GetImageMetadataAsync(
            inspect.Image,
            inspect.Config?.Image,
            new Dictionary<string, ImageMetadata>(StringComparer.OrdinalIgnoreCase),
            cancellationToken);
        return MapEditableContainer(inspect, imageMetadata.Labels);
    }

    public async Task CreateContainerAsync(ContainerEditRequest request, string? imageArchivePath,
        CancellationToken cancellationToken)
    {
        var imageReference = request.Image.Trim();
        var createdFromArchive = !string.IsNullOrWhiteSpace(imageArchivePath);

        if (createdFromArchive)
        {
            imageReference = await LoadImageArchiveAsync(imageArchivePath!, imageReference, cancellationToken);
            request.Image = imageReference;
            SetInternalImageSourceLabel(request.Labels, ArchiveImageSourceLabelValue);
            RemoveInternalRequestedImageReferenceLabel(request.Labels);
        }
        else
        {
            await PullImageAsync(imageReference, cancellationToken);
            RemoveInternalImageSourceLabel(request.Labels);
            SetInternalRequestedImageReferenceLabel(request.Labels, imageReference);
            request.Image = imageReference;
        }

        var imageMetadata = await GetImageMetadataAsync(
            request.Image,
            request.Image,
            new Dictionary<string, ImageMetadata>(StringComparer.OrdinalIgnoreCase),
            cancellationToken);

        ApplyTemplateValues(request, imageMetadata.Labels);
        request.NetworkAliases = NormalizeNetworkAliases(request.NetworkAliases);
        ValidateEditRequest(request);

        var createParameters = BuildCreateParameters(request);
        var created = await _client.Containers.CreateContainerAsync(createParameters, cancellationToken);
        var started =
            await _client.Containers.StartContainerAsync(created.ID, new ContainerStartParameters(), cancellationToken);
        if (!started)
        {
            await _client.Containers.RemoveContainerAsync(created.ID, new ContainerRemoveParameters { Force = true },
                cancellationToken);
            throw new InvalidOperationException($"Failed to start created container '{request.Name}'.");
        }
    }

    public async Task UpdateContainerAsync(
        string existingName,
        ContainerEditRequest request,
        string? imageArchivePath,
        CancellationToken cancellationToken,
        bool pullImage = false,
        bool pinPulledImageToDigest = false)
    {
        var container = await FindContainerAsync(existingName, cancellationToken);
        var inspect = await _client.Containers.InspectContainerAsync(container.ID, cancellationToken);
        var originalName = NormalizeName(inspect.Name);
        if (string.IsNullOrWhiteSpace(originalName))
            throw new InvalidOperationException($"Container '{existingName}' does not have a valid name.");

        if (!string.IsNullOrWhiteSpace(imageArchivePath))
        {
            request.Image = await LoadImageArchiveAsync(imageArchivePath, request.Image.Trim(), cancellationToken);
            EnsureArchiveRepositoryMatchesExistingContainer(inspect, request.Image);
            SetInternalImageSourceLabel(request.Labels, ArchiveImageSourceLabelValue);
            RemoveInternalRequestedImageReferenceLabel(request.Labels);
        }
        else
        {
            var requestedImageReference = request.Image.Trim();
            var preservesArchiveImage = IsArchiveImageContainer(inspect)
                                        && string.Equals(requestedImageReference, inspect.Config?.Image?.Trim(),
                                            StringComparison.OrdinalIgnoreCase);
            await EnsureImageAvailableAsync(request.Image, pullImage, cancellationToken);

            if (preservesArchiveImage)
            {
                SetInternalImageSourceLabel(request.Labels, ArchiveImageSourceLabelValue);
                RemoveInternalRequestedImageReferenceLabel(request.Labels);
            }
            else
            {
                RemoveInternalImageSourceLabel(request.Labels);
                SetInternalRequestedImageReferenceLabel(request.Labels, requestedImageReference);
            }

            if (pullImage && pinPulledImageToDigest)
                request.Image = await ResolveExactImageReferenceAsync(requestedImageReference, cancellationToken);
        }

        var imageMetadata = await GetImageMetadataAsync(
            request.Image,
            request.Image,
            new Dictionary<string, ImageMetadata>(StringComparer.OrdinalIgnoreCase),
            cancellationToken);

        ApplyTemplateValues(request, imageMetadata.Labels);
        request.NetworkAliases = NormalizeNetworkAliases(request.NetworkAliases);
        ValidateEditRequest(request);

        var backupName = $"{originalName}-backup-{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";
        string? newContainerId = null;
        var originalWasRunning = string.Equals(inspect.State?.Status, "running", StringComparison.OrdinalIgnoreCase);

        try
        {
            if (originalWasRunning)
                await _client.Containers.StopContainerAsync(container.ID, new ContainerStopParameters(),
                    cancellationToken);

            await _client.Containers.RenameContainerAsync(container.ID, new ContainerRenameParameters
            {
                NewName = backupName
            }, cancellationToken);

            var createParameters = BuildCreateParameters(inspect, request);
            var created = await _client.Containers.CreateContainerAsync(createParameters, cancellationToken);
            newContainerId = created.ID;

            var started = await _client.Containers.StartContainerAsync(newContainerId, new ContainerStartParameters(),
                cancellationToken);
            if (!started) throw new InvalidOperationException($"Failed to start recreated container '{request.Name}'.");

            await _client.Containers.RemoveContainerAsync(container.ID, new ContainerRemoveParameters
            {
                Force = true
            }, cancellationToken);
        }
        catch
        {
            if (!string.IsNullOrWhiteSpace(newContainerId))
                try
                {
                    await _client.Containers.RemoveContainerAsync(newContainerId, new ContainerRemoveParameters
                    {
                        Force = true
                    }, cancellationToken);
                }
                catch
                {
                }

            try
            {
                await _client.Containers.RenameContainerAsync(container.ID, new ContainerRenameParameters
                {
                    NewName = originalName
                }, cancellationToken);

                if (originalWasRunning)
                    await _client.Containers.StartContainerAsync(container.ID, new ContainerStartParameters(),
                        cancellationToken);
            }
            catch
            {
            }

            throw;
        }
    }

    public async Task StartContainerAsync(string name, CancellationToken cancellationToken)
    {
        var container = await FindContainerAsync(name, cancellationToken);
        var started =
            await _client.Containers.StartContainerAsync(container.ID, new ContainerStartParameters(),
                cancellationToken);
        if (!started) throw new InvalidOperationException($"Failed to start container '{name}'.");
    }

    public async Task StopContainerAsync(string name, CancellationToken cancellationToken)
    {
        var container = await FindContainerAsync(name, cancellationToken);
        await _client.Containers.StopContainerAsync(container.ID, new ContainerStopParameters(), cancellationToken);
    }

    public async Task StreamContainerShellAsync(
        string name,
        ChannelReader<ContainerShellClientMessage> input,
        Func<ContainerShellServerMessage, CancellationToken, Task> onMessage,
        CancellationToken cancellationToken)
    {
        var container = await FindContainerAsync(name, cancellationToken);
        var inspect = await _client.Containers.InspectContainerAsync(container.ID, cancellationToken);
        if (!string.Equals(inspect.State?.Status, "running", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Container '{name}' must be running to open a shell.");

        var imageMetadata = await GetImageMetadataAsync(
            inspect.Image,
            inspect.Config?.Image,
            new Dictionary<string, ImageMetadata>(StringComparer.OrdinalIgnoreCase),
            cancellationToken);

        Exception? lastShellException = null;
        var attemptedShells = new List<string>();
        foreach (var shellCommand in ResolveShellCommands(imageMetadata.Os))
        {
            attemptedShells.Add(shellCommand);
            try
            {
                await _shellClient.StreamShellAsync(container.ID, shellCommand, input, onMessage, cancellationToken);
                return;
            }
            catch (DockerApiException ex) when (IsMissingShellError(ex.ResponseBody ?? ex.Message))
            {
                lastShellException = ex;
            }
            catch (InvalidOperationException ex) when (IsMissingShellError(ex.Message))
            {
                lastShellException = ex;
            }
        }

        throw new InvalidOperationException(
            $"Container '{name}' does not appear to have a supported interactive shell. Tried: {string.Join(", ", attemptedShells)}.",
            lastShellException);
    }

    public async Task DeleteContainerAsync(string name, CancellationToken cancellationToken)
    {
        var container = await FindContainerAsync(name, cancellationToken);
        await _client.Containers.RemoveContainerAsync(container.ID, new ContainerRemoveParameters
        {
            Force = true
        }, cancellationToken);
    }

    public async Task<int> PruneUnusedImagesAsync(CancellationToken cancellationToken)
    {
        var response = await _client.Images.PruneImagesAsync(new ImagesPruneParameters(), cancellationToken);
        return response?.ImagesDeleted?.Count ?? 0;
    }

    public async Task RepullImageAndRestartContainerAsync(string name, CancellationToken cancellationToken)
    {
        var container = await FindContainerAsync(name, cancellationToken);
        var inspect = await _client.Containers.InspectContainerAsync(container.ID, cancellationToken);
        if (IsArchiveImageContainer(inspect))
            throw new InvalidOperationException(
                $"Container '{name}' uses an image loaded from a local archive and cannot be re-pulled from a registry.");

        var request = await GetEditableContainerAsync(name, cancellationToken);
        request.Image = ResolveRequestedImageReference(inspect, null);
        await UpdateContainerAsync(name, request, null, cancellationToken, true);
    }

    public async Task PullImageAndRestartContainerAsync(string name, string? imageReference,
        CancellationToken cancellationToken)
    {
        var container = await FindContainerAsync(name, cancellationToken);
        var inspect = await _client.Containers.InspectContainerAsync(container.ID, cancellationToken);
        if (IsArchiveImageContainer(inspect))
            throw new InvalidOperationException(
                $"Container '{name}' uses an image loaded from a local archive and cannot be re-pulled from a registry.");

        var imageToPull = ResolveRequestedImageReference(inspect, imageReference);

        if (string.IsNullOrWhiteSpace(imageToPull))
            throw new InvalidOperationException($"Container '{name}' has no image reference configured.");

        if (IsCurrentContainer(inspect))
        {
            await PullImageAsync(imageToPull, cancellationToken);
            var helperImageReference = inspect.Config?.Image?.Trim();
            await LaunchSelfUpdateHelperAsync(inspect, helperImageReference, imageToPull, cancellationToken);
            return;
        }

        var request = await GetEditableContainerAsync(name, cancellationToken);
        request.Image = imageToPull;
        await UpdateContainerAsync(name, request, null, cancellationToken, true, false);
    }

    public void Dispose()
    {
        _client.Dispose();
    }

    private static async Task PumpProcessOutputAsync(
        Stream source,
        Func<ContainerShellServerMessage, CancellationToken, Task> onMessage,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[4096];
        var decoder = Encoding.UTF8.GetDecoder();
        var charBuffer = new char[4096];

        while (!cancellationToken.IsCancellationRequested)
        {
            var read = await source.ReadAsync(buffer, cancellationToken);
            if (read <= 0) break;

            var charCount = decoder.GetChars(buffer, 0, read, charBuffer, 0, false);
            if (charCount > 0)
                await onMessage(new ContainerShellServerMessage("output", new string(charBuffer, 0, charCount)),
                    cancellationToken);
        }
    }

    public async Task RunSelfUpdateWorkerAsync(string name, string? imageReference, CancellationToken cancellationToken)
    {
        var container = await FindContainerAsync(name, cancellationToken);
        var inspect = await _client.Containers.InspectContainerAsync(container.ID, cancellationToken);
        if (IsArchiveImageContainer(inspect))
            throw new InvalidOperationException(
                $"Container '{name}' uses an image loaded from a local archive and cannot be re-pulled from a registry.");

        var imageToPull = ResolveRequestedImageReference(inspect, imageReference);

        if (string.IsNullOrWhiteSpace(imageToPull))
            throw new InvalidOperationException($"Container '{name}' has no image reference configured.");

        var request = await GetEditableContainerAsync(name, cancellationToken);
        request.Image = imageToPull;
        await RemoveAndRecreateContainerAsync(name, request, cancellationToken, false, false);
    }

    private async Task<string> LoadImageArchiveAsync(string imageArchivePath, string requestedImage,
        CancellationToken cancellationToken)
    {
        var repoTags = await ContainerImageArchiveInspector.ReadRepoTagsAsync(imageArchivePath, cancellationToken);
        var resolvedImage = ResolveArchiveImageReference(requestedImage, repoTags);

        await using var fileStream = File.OpenRead(imageArchivePath);
        await using var archiveStream = ContainerImageArchiveInspector.OpenArchiveStream(fileStream);
        await _client.Images.LoadImageAsync(new ImageLoadParameters(), archiveStream, new Progress<JSONMessage>(),
            cancellationToken);

        return resolvedImage;
    }

    private static string ResolveArchiveImageReference(string requestedImage, IReadOnlyList<string> repoTags)
    {
        if (!string.IsNullOrWhiteSpace(requestedImage)) return requestedImage;

        if (repoTags.Count == 1) return repoTags[0];

        if (repoTags.Count == 0)
            throw new InvalidOperationException(
                "Image archive does not contain any repo tags. Enter an image name manually.");

        throw new InvalidOperationException(
            "Image archive contains multiple repo tags. Enter the image name you want to use manually.");
    }

    private async Task<ContainerListResponse> FindContainerAsync(string name, CancellationToken cancellationToken)
    {
        var containers = await _client.Containers.ListContainersAsync(new ContainersListParameters
        {
            All = true
        }, cancellationToken);

        var container = containers.FirstOrDefault(x =>
            x.Names?.Any(candidate =>
                string.Equals(NormalizeName(candidate), name, StringComparison.OrdinalIgnoreCase)) == true);

        return container ?? throw new InvalidOperationException($"Container '{name}' was not found.");
    }

    private async Task PullImageAsync(string imageReference, CancellationToken cancellationToken)
    {
        var (fromImage, tag) = SplitImageReference(imageReference);
        await _client.Images.CreateImageAsync(new ImagesCreateParameters
        {
            FromImage = fromImage,
            Tag = tag
        }, null, new Progress<JSONMessage>(), cancellationToken);
    }

    private async Task EnsureImageAvailableAsync(string imageReference, bool pullImage,
        CancellationToken cancellationToken)
    {
        if (pullImage)
        {
            await PullImageAsync(imageReference, cancellationToken);
            return;
        }

        if (await ImageExistsLocallyAsync(imageReference, cancellationToken)) return;

        await PullImageAsync(imageReference, cancellationToken);
    }

    private static ContainerImageInventoryItem MapImage(ImagesListResponse image)
    {
        return new ContainerImageInventoryItem
        {
            Id = image.ID ?? string.Empty,
            RepoTags = image.RepoTags?.Where(x => !string.IsNullOrWhiteSpace(x)).ToList() ?? [],
            RepoDigests = image.RepoDigests?.Where(x => !string.IsNullOrWhiteSpace(x)).ToList() ?? [],
            CreatedUtc = new DateTimeOffset(DateTime.SpecifyKind(image.Created, DateTimeKind.Utc)),
            SizeBytes = image.Size,
            SharedSizeBytes = image.SharedSize,
            VirtualSizeBytes = image.VirtualSize,
            Containers = (int)image.Containers,
            Labels = image.Labels is null
                ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, string>(image.Labels, StringComparer.OrdinalIgnoreCase)
        };
    }

    private static string GetPrimaryImageTag(ImagesListResponse image)
    {
        return image.RepoTags?.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x))
               ?? image.ID
               ?? string.Empty;
    }

    private static bool IsVisibleInDefaultImageList(ImagesListResponse image)
    {
        var tags = image.RepoTags?.Where(x => !string.IsNullOrWhiteSpace(x)).ToList() ?? [];
        if (tags.Count == 0) return false;

        return tags.Any(tag => !IsNoneTag(tag));
    }

    private static ContainerStatsSnapshot MapStats(ContainerStatsResponse stats)
    {
        var collectedAtUtc = stats.Read == default
            ? DateTimeOffset.UtcNow
            : new DateTimeOffset(DateTime.SpecifyKind(stats.Read, DateTimeKind.Utc));
        var cpuTotalUsage = stats.CPUStats?.CPUUsage?.TotalUsage;
        var processorCount = GetProcessorCount(stats);
        var cpuPercent = CalculateCpuPercent(stats);
        var memoryUsageBytes = GetMemoryUsageBytes(stats.MemoryStats);
        ulong? memoryLimitBytes = stats.MemoryStats?.Limit > 0 ? stats.MemoryStats.Limit : null;
        double? memoryPercent = memoryUsageBytes.HasValue && memoryLimitBytes.HasValue && memoryLimitBytes.Value > 0
            ? (double)memoryUsageBytes.Value / memoryLimitBytes.Value * 100d
            : null;

        ulong networkRxBytes = 0;
        ulong networkTxBytes = 0;
        if (stats.Networks is not null)
            foreach (var network in stats.Networks.Values)
            {
                networkRxBytes += network.RxBytes;
                networkTxBytes += network.TxBytes;
            }

        return new ContainerStatsSnapshot
        {
            CollectedAtUtc = collectedAtUtc,
            CpuPercent = cpuPercent,
            CpuTotalUsage = cpuTotalUsage,
            ProcessorCount = processorCount,
            MemoryUsageBytes = memoryUsageBytes,
            MemoryLimitBytes = memoryLimitBytes,
            MemoryPercent = memoryPercent,
            NetworkRxBytes = networkRxBytes,
            NetworkTxBytes = networkTxBytes,
            BlockReadBytes = SumBlockIo(stats.BlkioStats?.IoServiceBytesRecursive, "read"),
            BlockWriteBytes = SumBlockIo(stats.BlkioStats?.IoServiceBytesRecursive, "write"),
            PidsCurrent = stats.PidsStats?.Current > 0 ? stats.PidsStats.Current : null
        };
    }

    private static double? CalculateCpuPercent(ContainerStatsResponse stats)
    {
        var totalUsage = stats.CPUStats?.CPUUsage?.TotalUsage ?? 0;
        var previousTotalUsage = stats.PreCPUStats?.CPUUsage?.TotalUsage ?? 0;
        var systemUsage = stats.CPUStats?.SystemUsage ?? 0;
        var previousSystemUsage = stats.PreCPUStats?.SystemUsage ?? 0;
        var cpuDelta = totalUsage > previousTotalUsage ? totalUsage - previousTotalUsage : 0;
        var systemDelta = systemUsage > previousSystemUsage ? systemUsage - previousSystemUsage : 0;
        if (cpuDelta == 0 || systemDelta == 0) return null;

        var onlineCpus = (int)(stats.CPUStats?.OnlineCPUs ?? 0);
        if (onlineCpus <= 0) onlineCpus = stats.CPUStats?.CPUUsage?.PercpuUsage?.Count ?? 0;

        if (onlineCpus <= 0) onlineCpus = 1;

        return (double)cpuDelta / systemDelta * onlineCpus * 100d;
    }

    private static uint? GetProcessorCount(ContainerStatsResponse stats)
    {
        if (stats.NumProcs > 0) return stats.NumProcs;

        if (stats.CPUStats?.OnlineCPUs > 0) return stats.CPUStats.OnlineCPUs;

        var perCpuCount = stats.CPUStats?.CPUUsage?.PercpuUsage?.Count ?? 0;
        if (perCpuCount > 0) return (uint)perCpuCount;

        return null;
    }

    private static ulong? GetMemoryUsageBytes(MemoryStats? memoryStats)
    {
        if (memoryStats is null) return null;

        if (memoryStats.PrivateWorkingSet > 0) return memoryStats.PrivateWorkingSet;

        if (memoryStats.Commit > 0) return memoryStats.Commit;

        if (memoryStats.Usage == 0) return null;

        ulong cacheBytes = 0;
        if (memoryStats.Stats is not null)
        {
            if (memoryStats.Stats.TryGetValue("inactive_file", out var inactiveFile))
                cacheBytes = inactiveFile;
            else if (memoryStats.Stats.TryGetValue("cache", out var cache)) cacheBytes = cache;
        }

        return memoryStats.Usage > cacheBytes ? memoryStats.Usage - cacheBytes : memoryStats.Usage;
    }

    private static ulong? SumBlockIo(IList<BlkioStatEntry>? entries, string operation)
    {
        if (entries is null || entries.Count == 0) return null;

        ulong total = 0;
        foreach (var entry in entries)
            if (string.Equals(entry.Op, operation, StringComparison.OrdinalIgnoreCase))
                total += entry.Value;

        return total == 0 ? null : total;
    }

    private static bool IsNoneTag(string tag)
    {
        return string.Equals(tag.Trim(), "<none>:<none>", StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<string> ResolveShellCommands(string? imageOs)
    {
        return string.Equals(imageOs?.Trim(), "windows", StringComparison.OrdinalIgnoreCase)
            ? ["cmd.exe", "powershell.exe", "pwsh.exe"]
            : ["/bin/sh", "/bin/bash", "sh", "bash", "/busybox/sh"];
    }

    private static bool IsMissingShellError(string? message)
    {
        if (string.IsNullOrWhiteSpace(message)) return false;

        return message.Contains("executable file not found", StringComparison.OrdinalIgnoreCase)
               || message.Contains("no such file or directory", StringComparison.OrdinalIgnoreCase)
               || message.Contains("starting container process caused", StringComparison.OrdinalIgnoreCase)
               || message.Contains("failed to exec in container", StringComparison.OrdinalIgnoreCase)
               || message.Contains("system cannot find the file specified", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<bool> ImageExistsLocallyAsync(string imageReference, CancellationToken cancellationToken)
    {
        try
        {
            _ = await _client.Images.InspectImageAsync(imageReference, cancellationToken);
            return true;
        }
        catch (DockerApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }
    }

    private async Task<ImageMetadata> GetImageMetadataAsync(
        string? imageId,
        string? imageReference,
        IDictionary<string, ImageMetadata> cache,
        CancellationToken cancellationToken)
    {
        var cacheKey = !string.IsNullOrWhiteSpace(imageId) ? imageId : imageReference;
        if (string.IsNullOrWhiteSpace(cacheKey)) return new ImageMetadata();

        if (cache.TryGetValue(cacheKey, out var existing)) return existing;

        var metadata = await TryGetImageMetadataAsync(imageId, imageReference, cancellationToken);
        if (metadata.Labels.Count == 0
            && string.IsNullOrWhiteSpace(metadata.Digest)
            && !string.IsNullOrWhiteSpace(imageReference)
            && !string.Equals(imageReference, imageId, StringComparison.OrdinalIgnoreCase))
            metadata = await TryGetImageMetadataAsync(imageReference, imageReference, cancellationToken);

        cache[cacheKey] = metadata;
        return metadata;
    }

    private async Task<ImageMetadata> TryGetImageMetadataAsync(string? imageLookup, string? imageReference,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(imageLookup)) return new ImageMetadata();

        try
        {
            var imageInspect = await _client.Images.InspectImageAsync(imageLookup, cancellationToken);
            var labels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            if (imageInspect.Config?.Labels is not null)
                foreach (var pair in imageInspect.Config.Labels)
                    labels[pair.Key] = pair.Value;

            if (imageInspect.ContainerConfig?.Labels is not null)
                foreach (var pair in imageInspect.ContainerConfig.Labels)
                    labels[pair.Key] = pair.Value;

            return new ImageMetadata
            {
                Labels = labels,
                Digest = ResolveImageDigest(imageInspect.RepoDigests, imageReference),
                Architecture = imageInspect.Architecture,
                Os = imageInspect.Os
            };
        }
        catch (DockerApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return new ImageMetadata();
        }
    }

    private async Task<string> ResolveExactImageReferenceAsync(string imageReference,
        CancellationToken cancellationToken)
    {
        var trimmed = imageReference.Trim();
        if (string.IsNullOrWhiteSpace(trimmed)) return trimmed;

        var metadata = await GetImageMetadataAsync(
            trimmed,
            trimmed,
            new Dictionary<string, ImageMetadata>(StringComparer.OrdinalIgnoreCase),
            cancellationToken);

        if (string.IsNullOrWhiteSpace(metadata.Digest)) return trimmed;

        var digest = metadata.Digest.Trim();
        return trimmed.Contains('@', StringComparison.Ordinal)
            ? trimmed
            : $"{ExtractImageRepository(trimmed)}@{digest}";
    }

    private static string? ResolveImageDigest(IList<string>? repoDigests, string? imageReference)
    {
        var parsed = ContainerImageReferenceParser.Parse(imageReference);
        if (parsed.IsDigestReference) return parsed.Digest;

        var digests = repoDigests?
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToArray() ?? [];
        if (digests.Length == 0) return null;

        var normalizedRepository =
            ContainerImageReferenceParser.NormalizeRepository(parsed.RegistryHost, parsed.Repository,
                parsed.UsesDefaultRegistry);
        foreach (var candidate in digests)
        {
            var atIndex = candidate.IndexOf('@');
            if (atIndex <= 0 || atIndex >= candidate.Length - 1) continue;

            var repository = candidate[..atIndex];
            var digest = candidate[(atIndex + 1)..];
            if (string.Equals(
                    ContainerImageReferenceParser.NormalizeRepository(parsed.RegistryHost, repository,
                        parsed.UsesDefaultRegistry),
                    normalizedRepository,
                    StringComparison.OrdinalIgnoreCase))
                return digest;
        }

        var fallback = digests[0];
        var fallbackAt = fallback.IndexOf('@');
        return fallbackAt >= 0 && fallbackAt < fallback.Length - 1
            ? fallback[(fallbackAt + 1)..]
            : null;
    }

    private static CreateContainerParameters BuildCreateParameters(ContainerEditRequest request)
    {
        var networkMode = ResolveNetworkMode(null, request.NetworkAliases);
        return new CreateContainerParameters
        {
            Name = request.Name.Trim(),
            Image = request.Image.Trim(),
            Env = BuildEnvArray(request.EnvVars),
            Labels = BuildDictionary(request.Labels),
            ExposedPorts = BuildExposedPorts(request.PublishedPorts),
            NetworkingConfig = BuildNetworkingConfig(request.NetworkAliases),
            HostConfig = new HostConfig
            {
                RestartPolicy = BuildRestartPolicy(request.RestartPolicy),
                PortBindings = BuildPortBindings(request.PublishedPorts),
                Binds = BuildBinds(request.MountBindings ?? []),
                DNS = BuildDnsServers(request.InternalDnsServers, null, request.InternalDnsServersToRemove),
                ExtraHosts = BuildExtraHosts(request.HostMappings),
                NetworkMode = networkMode
            }
        };
    }

    private static CreateContainerParameters BuildCreateParameters(ContainerInspectResponse inspect,
        ContainerEditRequest request)
    {
        return new CreateContainerParameters
        {
            Name = request.Name.Trim(),
            Image = request.Image.Trim(),
            Cmd = inspect.Config?.Cmd,
            Entrypoint = inspect.Config?.Entrypoint,
            Env = BuildEnvArray(request.EnvVars),
            Labels = BuildDictionary(request.Labels),
            WorkingDir = inspect.Config?.WorkingDir,
            User = inspect.Config?.User,
            Hostname = inspect.Config?.Hostname,
            Domainname = inspect.Config?.Domainname,
            ExposedPorts = BuildExposedPorts(request.PublishedPorts),
            NetworkingConfig = BuildNetworkingConfig(inspect, request.NetworkAliases),
            HostConfig = BuildHostConfig(inspect.HostConfig, request),
            Tty = inspect.Config?.Tty ?? false,
            OpenStdin = inspect.Config?.OpenStdin ?? false,
            AttachStdin = inspect.Config?.AttachStdin ?? false,
            AttachStdout = inspect.Config?.AttachStdout ?? false,
            AttachStderr = inspect.Config?.AttachStderr ?? false,
            StdinOnce = inspect.Config?.StdinOnce ?? false
        };
    }

    private static HostConfig BuildHostConfig(HostConfig? source, ContainerEditRequest request)
    {
        var hostConfig = CloneHostConfig(source) ?? new HostConfig();
        hostConfig.RestartPolicy = BuildRestartPolicy(request.RestartPolicy);
        hostConfig.PortBindings = BuildPortBindings(request.PublishedPorts);
        hostConfig.Binds = BuildBinds(request.MountBindings ?? []);
        hostConfig.DNS =
            BuildDnsServers(request.InternalDnsServers, hostConfig.DNS, request.InternalDnsServersToRemove);
        hostConfig.ExtraHosts = BuildExtraHosts(request.HostMappings);
        hostConfig.Mounts = FilterRetainedMounts(hostConfig.Mounts, request.MountBindings);
        hostConfig.PublishAllPorts = false;
        hostConfig.NetworkMode = ResolveNetworkMode(hostConfig.NetworkMode, request.NetworkAliases);
        return hostConfig;
    }

    private static HostConfig? CloneHostConfig(HostConfig? source)
    {
        if (source is null) return null;

        return new HostConfig
        {
            AutoRemove = source.AutoRemove,
            Binds = source.Binds,
            CapAdd = source.CapAdd,
            CapDrop = source.CapDrop,
            ConsoleSize = source.ConsoleSize,
            DNS = source.DNS,
            DNSOptions = source.DNSOptions,
            DNSSearch = source.DNSSearch,
            ExtraHosts = source.ExtraHosts,
            GroupAdd = source.GroupAdd,
            Init = source.Init,
            IpcMode = source.IpcMode,
            Links = source.Links,
            LogConfig = source.LogConfig,
            Memory = source.Memory,
            MemoryReservation = source.MemoryReservation,
            MemorySwap = source.MemorySwap,
            MemorySwappiness = source.MemorySwappiness,
            Mounts = CloneMounts(source.Mounts),
            NanoCPUs = source.NanoCPUs,
            NetworkMode = source.NetworkMode,
            OomKillDisable = source.OomKillDisable,
            OomScoreAdj = source.OomScoreAdj,
            PidMode = source.PidMode,
            PortBindings = source.PortBindings,
            Privileged = source.Privileged,
            PublishAllPorts = source.PublishAllPorts,
            ReadonlyRootfs = source.ReadonlyRootfs,
            RestartPolicy = source.RestartPolicy,
            SecurityOpt = source.SecurityOpt,
            ShmSize = source.ShmSize,
            Tmpfs = source.Tmpfs,
            UTSMode = source.UTSMode,
            UsernsMode = source.UsernsMode,
            VolumeDriver = source.VolumeDriver,
            VolumesFrom = source.VolumesFrom
        };
    }

    private async Task LaunchSelfUpdateHelperAsync(
        ContainerInspectResponse inspect,
        string? helperImageReference,
        string targetImageReference,
        CancellationToken cancellationToken)
    {
        var targetName = NormalizeName(inspect.Name);
        if (string.IsNullOrWhiteSpace(targetName))
            throw new InvalidOperationException(
                "QuickProxy could not determine its own container name for self-update.");

        var helperImage = (helperImageReference ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(helperImage))
            throw new InvalidOperationException($"Container '{targetName}' has no current image reference configured.");

        var helperName = $"{targetName}-self-update-{Guid.NewGuid():N}";
        var labels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [SelfUpdateHelperLabelKey] = "true",
            ["quickproxy.target-container"] = targetName
        };

        var hostConfig = new HostConfig
        {
            AutoRemove = false,
            Binds = inspect.HostConfig?.Binds?.ToList() ?? [],
            Mounts = CloneMounts(inspect.HostConfig?.Mounts),
            NetworkMode = inspect.HostConfig?.NetworkMode
        };

        var created = await _client.Containers.CreateContainerAsync(new CreateContainerParameters
        {
            Name = helperName,
            Image = helperImage,
            Cmd =
            [
                "--self-update-worker",
                targetName,
                targetImageReference
            ],
            Env =
            [
                $"Containers__Endpoint={_configuredEndpoint}"
            ],
            Labels = labels,
            HostConfig = hostConfig,
            User = inspect.Config?.User
        }, cancellationToken);

        var started =
            await _client.Containers.StartContainerAsync(created.ID, new ContainerStartParameters(), cancellationToken);
        if (!started)
        {
            await _client.Containers.RemoveContainerAsync(created.ID, new ContainerRemoveParameters
            {
                Force = true
            }, cancellationToken);
            throw new InvalidOperationException($"Failed to start self-update helper for container '{targetName}'.");
        }
    }

    private async Task RemoveAndRecreateContainerAsync(
        string existingName,
        ContainerEditRequest request,
        CancellationToken cancellationToken,
        bool pullImage = false,
        bool pinPulledImageToDigest = false)
    {
        var container = await FindContainerAsync(existingName, cancellationToken);
        var inspect = await _client.Containers.InspectContainerAsync(container.ID, cancellationToken);
        var originalName = NormalizeName(inspect.Name);
        if (string.IsNullOrWhiteSpace(originalName))
            throw new InvalidOperationException($"Container '{existingName}' does not have a valid name.");

        var requestedImageReference = request.Image.Trim();
        await EnsureImageAvailableAsync(request.Image, pullImage, cancellationToken);

        RemoveInternalImageSourceLabel(request.Labels);
        SetInternalRequestedImageReferenceLabel(request.Labels, requestedImageReference);

        if (pullImage && pinPulledImageToDigest)
            request.Image = await ResolveExactImageReferenceAsync(requestedImageReference, cancellationToken);

        var imageMetadata = await GetImageMetadataAsync(
            request.Image,
            request.Image,
            new Dictionary<string, ImageMetadata>(StringComparer.OrdinalIgnoreCase),
            cancellationToken);

        ApplyTemplateValues(request, imageMetadata.Labels);
        request.NetworkAliases = NormalizeNetworkAliases(request.NetworkAliases);
        ValidateEditRequest(request);

        var createParameters = BuildCreateParameters(inspect, request);
        var replacementName = $"{originalName}-replacement-{Guid.NewGuid():N}";
        createParameters.Name = replacementName;
        string? newContainerId = null;
        var originalWasRunning = string.Equals(inspect.State?.Status, "running", StringComparison.OrdinalIgnoreCase);

        try
        {
            Console.WriteLine(
                $"[self-update] Creating replacement container '{replacementName}' from image '{request.Image}'.");
            var created = await _client.Containers.CreateContainerAsync(createParameters, cancellationToken);
            newContainerId = created.ID;
            Console.WriteLine($"[self-update] Replacement container created with id '{newContainerId}'.");

            if (originalWasRunning)
            {
                Console.WriteLine($"[self-update] Stopping original container '{originalName}'.");
                await _client.Containers.StopContainerAsync(container.ID, new ContainerStopParameters(),
                    cancellationToken);
            }

            Console.WriteLine($"[self-update] Removing original container '{originalName}'.");
            await _client.Containers.RemoveContainerAsync(container.ID, new ContainerRemoveParameters
            {
                Force = true
            }, cancellationToken);

            Console.WriteLine($"[self-update] Renaming replacement container '{replacementName}' to '{originalName}'.");
            await _client.Containers.RenameContainerAsync(newContainerId, new ContainerRenameParameters
            {
                NewName = originalName
            }, cancellationToken);

            Console.WriteLine($"[self-update] Starting recreated container '{originalName}'.");
            var started = await _client.Containers.StartContainerAsync(newContainerId, new ContainerStartParameters(),
                cancellationToken);
            if (!started) throw new InvalidOperationException($"Failed to start recreated container '{request.Name}'.");

            Console.WriteLine($"[self-update] Recreated container '{originalName}' started successfully.");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[self-update] Replacement failed for '{originalName}': {ex}");
            if (!string.IsNullOrWhiteSpace(newContainerId))
                try
                {
                    await _client.Containers.RemoveContainerAsync(newContainerId, new ContainerRemoveParameters
                    {
                        Force = true
                    }, cancellationToken);
                }
                catch
                {
                }

            throw;
        }
    }

    private static IList<Mount>? CloneMounts(IList<Mount>? source)
    {
        if (source is null) return null;

        return source.Select(mount => new Mount
        {
            Type = mount.Type,
            Source = mount.Source,
            Target = mount.Target,
            ReadOnly = mount.ReadOnly,
            Consistency = mount.Consistency,
            BindOptions = mount.BindOptions,
            TmpfsOptions = mount.TmpfsOptions,
            VolumeOptions = mount.VolumeOptions
        }).ToList();
    }

    private static IList<Mount>? FilterRetainedMounts(
        IList<Mount>? source,
        IReadOnlyList<ContainerMountBindingRequest>? requestedBindings)
    {
        if (source is null || source.Count == 0) return source;

        var requestedContainerPaths = new HashSet<string>(
            (requestedBindings ?? [])
            .Where(x => !string.IsNullOrWhiteSpace(x.ContainerPath))
            .Select(x => x.ContainerPath.Trim()),
            StringComparer.OrdinalIgnoreCase);

        if (requestedContainerPaths.Count == 0) return source;

        return source
            .Where(mount => !string.Equals(mount.Type, "bind", StringComparison.OrdinalIgnoreCase)
                            || string.IsNullOrWhiteSpace(mount.Target)
                            || !requestedContainerPaths.Contains(mount.Target.Trim()))
            .ToList();
    }

    private static bool IsCurrentContainer(ContainerInspectResponse inspect)
    {
        var candidates = GetSelfContainerIdCandidates();
        if (candidates.Count == 0) return false;

        var containerId = inspect.ID?.Trim();
        if (string.IsNullOrWhiteSpace(containerId)) return false;

        return candidates.Any(candidate =>
            string.Equals(containerId, candidate, StringComparison.OrdinalIgnoreCase)
            || containerId.StartsWith(candidate, StringComparison.OrdinalIgnoreCase)
            || candidate.StartsWith(containerId, StringComparison.OrdinalIgnoreCase));
    }

    private static IReadOnlyList<string> GetSelfContainerIdCandidates()
    {
        var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AddSelfCandidate(candidates, Environment.GetEnvironmentVariable("HOSTNAME"));
        AddSelfCandidate(candidates, Environment.MachineName);
        TryAddSelfCandidatesFromFile(candidates, "/proc/self/cgroup");
        TryAddSelfCandidatesFromFile(candidates, "/proc/1/cpuset");
        return candidates.ToArray();
    }

    private static void TryAddSelfCandidatesFromFile(ISet<string> candidates, string path)
    {
        try
        {
            if (!File.Exists(path)) return;

            foreach (var line in File.ReadAllLines(path))
            {
                var token = line;
                var slashIndex = token.LastIndexOf('/');
                if (slashIndex >= 0 && slashIndex < token.Length - 1) token = token[(slashIndex + 1)..];

                AddSelfCandidate(candidates, token);
            }
        }
        catch
        {
        }
    }

    private static void AddSelfCandidate(ISet<string> candidates, string? value)
    {
        var trimmed = (value ?? string.Empty).Trim();
        if (IsLikelyContainerId(trimmed)) candidates.Add(trimmed);
    }

    private static bool IsLikelyContainerId(string? value)
    {
        var trimmed = (value ?? string.Empty).Trim();
        return !string.IsNullOrWhiteSpace(trimmed)
               && Regex.IsMatch(trimmed, "^[a-f0-9]{12}$|^[a-f0-9]{64}$",
                   RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static NetworkingConfig? BuildNetworkingConfig(IReadOnlyList<ContainerNetworkAliasRequest>? aliases)
    {
        var groupedAliases = GroupNetworkAliases(aliases);
        if (groupedAliases.Count == 0) return null;

        var endpoints = groupedAliases.ToDictionary(
            x => x.Key,
            x => new EndpointSettings
            {
                Aliases = x.Value
            },
            StringComparer.OrdinalIgnoreCase);

        return new NetworkingConfig
        {
            EndpointsConfig = endpoints
        };
    }

    private static NetworkingConfig? BuildNetworkingConfig(ContainerInspectResponse inspect,
        IReadOnlyList<ContainerNetworkAliasRequest>? aliases)
    {
        var networks = inspect.NetworkSettings?.Networks;
        var groupedAliases = GroupNetworkAliases(aliases);

        if ((networks is null || networks.Count == 0) && groupedAliases.Count == 0) return null;

        var endpoints = new Dictionary<string, EndpointSettings>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in networks ?? new Dictionary<string, EndpointSettings>())
            endpoints[entry.Key] = new EndpointSettings
            {
                Aliases = groupedAliases.TryGetValue(entry.Key, out var networkAliases)
                    ? networkAliases
                    : [],
                IPAddress = entry.Value?.IPAddress,
                IPAMConfig = entry.Value?.IPAMConfig,
                Links = entry.Value?.Links
            };

        foreach (var aliasGroup in groupedAliases)
        {
            if (endpoints.ContainsKey(aliasGroup.Key)) continue;

            endpoints[aliasGroup.Key] = new EndpointSettings
            {
                Aliases = aliasGroup.Value
            };
        }

        return new NetworkingConfig
        {
            EndpointsConfig = endpoints
        };
    }

    private static Dictionary<string, IList<string>> GroupNetworkAliases(
        IReadOnlyList<ContainerNetworkAliasRequest>? aliases)
    {
        return NormalizeNetworkAliases(aliases)
            .Where(x => !string.IsNullOrWhiteSpace(x.Network) && !string.IsNullOrWhiteSpace(x.Alias))
            .GroupBy(x => x.Network.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                x => x.Key,
                x => (IList<string>)x
                    .Select(y => y.Alias.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(y => y, StringComparer.OrdinalIgnoreCase)
                    .ToList(),
                StringComparer.OrdinalIgnoreCase);
    }

    private static List<ContainerNetworkAliasRequest> NormalizeNetworkAliases(
        IReadOnlyList<ContainerNetworkAliasRequest>? aliases)
    {
        var normalizedAliases = new List<ContainerNetworkAliasRequest>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var alias in aliases ?? [])
        {
            var network = (alias.Network ?? string.Empty).Trim();
            var value = (alias.Alias ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(network) || string.IsNullOrWhiteSpace(value)) continue;

            if (!seen.Add($"{network}\u001f{value}")) continue;

            normalizedAliases.Add(new ContainerNetworkAliasRequest
            {
                Network = network,
                Alias = value
            });
        }

        return normalizedAliases;
    }

    private static string? ResolveNetworkMode(string? currentNetworkMode,
        IReadOnlyList<ContainerNetworkAliasRequest>? aliases)
    {
        var aliasNetworks = (aliases ?? [])
            .Select(x => (x.Network ?? string.Empty).Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (aliasNetworks.Count == 0) return currentNetworkMode;

        var trimmedCurrent = currentNetworkMode?.Trim();
        if (!string.IsNullOrWhiteSpace(trimmedCurrent)
            && aliasNetworks.Contains(trimmedCurrent, StringComparer.OrdinalIgnoreCase))
            return trimmedCurrent;

        return aliasNetworks[0];
    }

    private static IDictionary<string, EmptyStruct> BuildExposedPorts(IEnumerable<ContainerPublishedPortRequest> ports)
    {
        return ports
            .GroupBy(x => $"{x.ContainerPort}/{NormalizeProtocol(x.Protocol)}", StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, _ => default(EmptyStruct), StringComparer.OrdinalIgnoreCase);
    }

    private static IDictionary<string, IList<PortBinding>> BuildPortBindings(
        IEnumerable<ContainerPublishedPortRequest> ports)
    {
        return ports
            .GroupBy(x => $"{x.ContainerPort}/{NormalizeProtocol(x.Protocol)}", StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                x => x.Key,
                x => (IList<PortBinding>)x
                    .OrderBy(y => y.HostPort)
                    .Select(y => new PortBinding
                    {
                        HostIP = string.IsNullOrWhiteSpace(y.HostIp) ? string.Empty : y.HostIp,
                        HostPort = y.HostPort.ToString()
                    })
                    .ToList(),
                StringComparer.OrdinalIgnoreCase);
    }

    private static IList<string> BuildBinds(IEnumerable<ContainerMountBindingRequest> mountBindings)
    {
        return mountBindings
            .Where(x => !string.IsNullOrWhiteSpace(x.HostPath) && !string.IsNullOrWhiteSpace(x.ContainerPath))
            .GroupBy(x => x.ContainerPath.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(x => x.Last())
            .Select(x => $"{x.HostPath.Trim()}:{x.ContainerPath.Trim()}:{(x.ReadOnly ? "ro" : "rw")}")
            .ToList();
    }

    private static Dictionary<string, string> BuildDictionary(IEnumerable<ContainerKeyValuePair> values)
    {
        return values
            .Where(x => !string.IsNullOrWhiteSpace(x.Key))
            .GroupBy(x => x.Key.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.Last().Value ?? string.Empty, StringComparer.OrdinalIgnoreCase);
    }

    private static void SetInternalImageSourceLabel(List<ContainerKeyValuePair> labels, string value)
    {
        var existing = labels.FirstOrDefault(x =>
            string.Equals(x.Key, InternalImageSourceLabelKey, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            existing.Value = value;
            return;
        }

        labels.Add(new ContainerKeyValuePair
        {
            Key = InternalImageSourceLabelKey,
            Value = value
        });
    }

    private static void SetInternalRequestedImageReferenceLabel(List<ContainerKeyValuePair> labels, string value)
    {
        var existing = labels.FirstOrDefault(x =>
            string.Equals(x.Key, InternalRequestedImageReferenceLabelKey, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            existing.Value = value;
            return;
        }

        labels.Add(new ContainerKeyValuePair
        {
            Key = InternalRequestedImageReferenceLabelKey,
            Value = value
        });
    }

    private static void RemoveInternalImageSourceLabel(List<ContainerKeyValuePair> labels)
    {
        labels.RemoveAll(x => string.Equals(x.Key, InternalImageSourceLabelKey, StringComparison.OrdinalIgnoreCase));
    }

    private static void RemoveInternalRequestedImageReferenceLabel(List<ContainerKeyValuePair> labels)
    {
        labels.RemoveAll(x =>
            string.Equals(x.Key, InternalRequestedImageReferenceLabelKey, StringComparison.OrdinalIgnoreCase));
    }

    private static void EnsureArchiveRepositoryMatchesExistingContainer(ContainerInspectResponse inspect,
        string replacementImage)
    {
        var existingRepository = ExtractImageRepository(inspect.Config?.Image);
        var replacementRepository = ExtractImageRepository(replacementImage);

        if (string.IsNullOrWhiteSpace(existingRepository) || string.IsNullOrWhiteSpace(replacementRepository)) return;

        if (!string.Equals(existingRepository, replacementRepository, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                $"Replacement archive image repository '{replacementRepository}' does not match the existing container image repository '{existingRepository}'.");
    }

    private static string ExtractImageRepository(string? imageReference)
    {
        if (string.IsNullOrWhiteSpace(imageReference)) return string.Empty;

        var withoutDigest = imageReference.Split('@', 2)[0];
        var lastSlash = withoutDigest.LastIndexOf('/');
        var lastColon = withoutDigest.LastIndexOf(':');
        return lastColon > lastSlash
            ? withoutDigest[..lastColon]
            : withoutDigest;
    }

    private static bool IsArchiveImageContainer(ContainerInspectResponse inspect)
    {
        return inspect.Config?.Labels?.TryGetValue(InternalImageSourceLabelKey, out var value) == true
               && string.Equals(value, ArchiveImageSourceLabelValue, StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolveRequestedImageReference(ContainerInspectResponse inspect,
        string? requestedImageReference)
    {
        var explicitReference = (requestedImageReference ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(explicitReference)) return explicitReference;

        if (inspect.Config?.Labels?.TryGetValue(InternalRequestedImageReferenceLabelKey, out var storedReference) ==
            true
            && !string.IsNullOrWhiteSpace(storedReference))
            return storedReference.Trim();

        return inspect.Config?.Image?.Trim() ?? string.Empty;
    }

    private static List<string> BuildEnvArray(IEnumerable<ContainerKeyValuePair> values)
    {
        return values
            .Where(x => !string.IsNullOrWhiteSpace(x.Key))
            .GroupBy(x => x.Key.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(x => $"{x.Key}={x.Last().Value ?? string.Empty}")
            .ToList();
    }

    private static RestartPolicy BuildRestartPolicy(string? name)
    {
        var normalized = string.IsNullOrWhiteSpace(name) ? "no" : name.Trim().ToLowerInvariant();
        if (!SupportedRestartPolicies.Contains(normalized))
            throw new InvalidOperationException($"Restart policy '{name}' is not supported.");

        return new RestartPolicy
        {
            Name = normalized switch
            {
                "always" => RestartPolicyKind.Always,
                "unless-stopped" => RestartPolicyKind.UnlessStopped,
                "on-failure" => RestartPolicyKind.OnFailure,
                _ => RestartPolicyKind.No
            }
        };
    }

    private static ContainerEditRequest MapEditableContainer(
        ContainerInspectResponse inspect,
        IReadOnlyDictionary<string, string> imageLabels)
    {
        var labels = (inspect.Config?.Labels ?? new Dictionary<string, string>())
            .Where(x => !imageLabels.TryGetValue(x.Key, out var imageValue) ||
                        !string.Equals(x.Value, imageValue, StringComparison.Ordinal))
            .OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
            .Select(x => new ContainerKeyValuePair
            {
                Key = x.Key,
                Value = x.Value
            })
            .ToList();

        var envVars = (inspect.Config?.Env ?? [])
            .Select(ParseEnvVar)
            .Where(x => !string.IsNullOrWhiteSpace(x.Key))
            .OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var publishedPorts = ReadPublishedPorts(inspect)
            .SelectMany(x => x.Value.Select(binding => new ContainerPublishedPortRequest
            {
                ContainerPort = x.Key.Port,
                Protocol = x.Key.Protocol,
                HostPort = binding.HostPort,
                HostIp = binding.HostIp
            }))
            .OrderBy(x => x.ContainerPort)
            .ThenBy(x => x.HostPort)
            .ToList();

        var mountBindings = ReadEditableMountBindings(inspect)
            .Where(x => x is not null)
            .Select(x => x!)
            .OrderBy(x => x.ContainerPath, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.HostPath, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new ContainerEditRequest
        {
            Name = NormalizeName(inspect.Name),
            Image = inspect.Config?.Image ?? string.Empty,
            Labels = labels,
            EnvVars = envVars,
            MountBindings = mountBindings,
            HostMappings = ReadEditableHostMappings(inspect),
            NetworkAliases = ReadEditableNetworkAliases(inspect),
            RestartPolicy = inspect.HostConfig?.RestartPolicy?.Name switch
            {
                RestartPolicyKind.Always => "always",
                RestartPolicyKind.UnlessStopped => "unless-stopped",
                RestartPolicyKind.OnFailure => "on-failure",
                _ => "no"
            },
            PublishedPorts = publishedPorts
        };
    }

    private static ContainerKeyValuePair ParseEnvVar(string? env)
    {
        if (string.IsNullOrWhiteSpace(env)) return new ContainerKeyValuePair();

        var separatorIndex = env.IndexOf('=');
        if (separatorIndex < 0)
            return new ContainerKeyValuePair
            {
                Key = env,
                Value = string.Empty
            };

        return new ContainerKeyValuePair
        {
            Key = env[..separatorIndex],
            Value = env[(separatorIndex + 1)..]
        };
    }

    private static void ValidateEditRequest(ContainerEditRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name)) throw new InvalidOperationException("Container name is required.");

        if (string.IsNullOrWhiteSpace(request.Image))
            throw new InvalidOperationException("Container image is required.");

        foreach (var label in request.Labels)
            if (string.IsNullOrWhiteSpace(label.Key))
                throw new InvalidOperationException("Container labels must not contain empty keys.");

        foreach (var env in request.EnvVars)
            if (string.IsNullOrWhiteSpace(env.Key))
                throw new InvalidOperationException("Environment variables must not contain empty keys.");

        foreach (var mount in request.MountBindings ?? [])
        {
            if (string.IsNullOrWhiteSpace(mount.HostPath))
                throw new InvalidOperationException("Mount bindings must not contain empty host paths.");

            if (string.IsNullOrWhiteSpace(mount.ContainerPath))
                throw new InvalidOperationException("Mount bindings must not contain empty container paths.");
        }

        var seenHostMappings = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var mapping in request.HostMappings ?? [])
        {
            var hostname = (mapping.Hostname ?? string.Empty).Trim();
            var address = (mapping.Address ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(hostname) || string.IsNullOrWhiteSpace(address)) continue;

            if (!seenHostMappings.Add(hostname))
                throw new InvalidOperationException($"Host mapping for '{hostname}' is duplicated.");
        }

        var seenAliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var alias in request.NetworkAliases ?? [])
        {
            var network = (alias.Network ?? string.Empty).Trim();
            var value = (alias.Alias ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(network) || string.IsNullOrWhiteSpace(value)) continue;

            if (!seenAliases.Add($"{network}\u001f{value}"))
                throw new InvalidOperationException($"Network alias '{value}' is duplicated for network '{network}'.");
        }

        foreach (var port in request.PublishedPorts)
        {
            if (port.ContainerPort is < 1 or > 65535)
                throw new InvalidOperationException("Container port must be between 1 and 65535.");

            if (port.HostPort is < 1 or > 65535)
                throw new InvalidOperationException("Host port must be between 1 and 65535.");

            if (!string.Equals(NormalizeProtocol(port.Protocol), "tcp", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(NormalizeProtocol(port.Protocol), "udp", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Published port protocol must be tcp or udp.");
        }

        _ = BuildRestartPolicy(request.RestartPolicy);
    }

    private static string NormalizeProtocol(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? "tcp" : value.Trim().ToLowerInvariant();
    }

    private static ContainerMountBindingRequest? ParseMountBinding(string? bind)
    {
        if (string.IsNullOrWhiteSpace(bind)) return null;

        var trimmed = bind.Trim();
        var parts = trimmed.Split(':');
        if (parts.Length < 2) return null;

        var hasMode = parts.Length >= 3 &&
                      (string.Equals(parts[^1], "ro", StringComparison.OrdinalIgnoreCase)
                       || string.Equals(parts[^1], "rw", StringComparison.OrdinalIgnoreCase));
        var mode = hasMode ? parts[^1] : null;
        var partsWithoutMode = hasMode ? parts[..^1] : parts;

        if (partsWithoutMode.Length < 2) return null;

        // Windows bind strings include drive letters on both sides, e.g. C:\host:C:\container[:ro].
        // Detect the container path from the last drive-letter segment and keep any earlier colons in the host path.
        var containerStartPartIndex = -1;
        for (var i = 1; i < partsWithoutMode.Length; i++)
            if (IsWindowsDriveSegment(partsWithoutMode[i]))
                containerStartPartIndex = i;

        string hostPath;
        string containerPath;

        if (containerStartPartIndex > 0)
        {
            hostPath = string.Join(':', partsWithoutMode[..containerStartPartIndex]);
            containerPath = string.Join(':', partsWithoutMode[containerStartPartIndex..]);
        }
        else
        {
            hostPath = string.Join(':', partsWithoutMode[..^1]);
            containerPath = partsWithoutMode[^1];
        }

        if (string.IsNullOrWhiteSpace(hostPath) || string.IsNullOrWhiteSpace(containerPath)) return null;

        return new ContainerMountBindingRequest
        {
            HostPath = hostPath.Trim(),
            ContainerPath = containerPath.Trim(),
            ReadOnly = string.Equals(mode, "ro", StringComparison.OrdinalIgnoreCase)
        };
    }

    private static bool IsWindowsDriveSegment(string value)
    {
        return value.Length == 1 && char.IsLetter(value[0]);
    }

    private static IEnumerable<ContainerMountBindingRequest?> ReadEditableMountBindings(
        ContainerInspectResponse inspect)
    {
        foreach (var bind in inspect.HostConfig?.Binds ?? []) yield return ParseMountBinding(bind);

        foreach (var mount in inspect.HostConfig?.Mounts ?? [])
        {
            if (!string.Equals(mount.Type, "bind", StringComparison.OrdinalIgnoreCase)) continue;

            if (string.IsNullOrWhiteSpace(mount.Source) || string.IsNullOrWhiteSpace(mount.Target)) continue;

            yield return new ContainerMountBindingRequest
            {
                HostPath = mount.Source.Trim(),
                ContainerPath = mount.Target.Trim(),
                ReadOnly = mount.ReadOnly
            };
        }
    }

    private static (string FromImage, string Tag) SplitImageReference(string imageReference)
    {
        var atIndex = imageReference.IndexOf('@');
        if (atIndex >= 0) return (imageReference[..atIndex], imageReference[(atIndex + 1)..]);

        var lastColon = imageReference.LastIndexOf(':');
        var lastSlash = imageReference.LastIndexOf('/');
        if (lastColon > lastSlash) return (imageReference[..lastColon], imageReference[(lastColon + 1)..]);

        return (imageReference, "latest");
    }

    private static ContainerInventoryItem MapContainer(
        ContainerListResponse container,
        ContainerInspectResponse inspect,
        ImageMetadata imageMetadata)
    {
        var containerLabels = inspect.Config?.Labels ?? new Dictionary<string, string>();
        var exposedPorts = ReadExposedPorts(inspect).ToList();
        var publishedPorts = ReadPublishedPorts(inspect);
        var ports = exposedPorts
            .Concat(publishedPorts.Keys)
            .Distinct()
            .OrderBy(x => x.Port)
            .ThenBy(x => x.Protocol, StringComparer.OrdinalIgnoreCase)
            .Select(x => new ContainerPortInfo
            {
                ContainerPort = x.Port,
                Protocol = x.Protocol,
                IsExposed = exposedPorts.Contains(x),
                PublishedPorts = publishedPorts.TryGetValue(x, out var published)
                    ? published.Select(y => y.HostPort).Distinct().OrderBy(y => y).ToList()
                    : [],
                PublishedBindings = publishedPorts.TryGetValue(x, out published) ? published : []
            })
            .ToList();

        var networks = ReadNetworks(inspect);

        return new ContainerInventoryItem
        {
            Id = container.ID,
            Name = NormalizeName(container.Names?.FirstOrDefault() ?? inspect.Name),
            Image = string.IsNullOrWhiteSpace(inspect.Config?.Image) ? container.Image : inspect.Config.Image,
            ImageId = inspect.Image,
            ImageDigest = imageMetadata.Digest,
            ImageArchitecture = imageMetadata.Architecture,
            ImageOs = imageMetadata.Os,
            State = container.State,
            Status = container.Status,
            ContainerLabels = new Dictionary<string, string>(containerLabels, StringComparer.OrdinalIgnoreCase),
            ImageLabels = new Dictionary<string, string>(imageMetadata.Labels, StringComparer.OrdinalIgnoreCase),
            Ports = ports,
            Networks = networks,
            LogsSupported = GetLogSupport(inspect).Supported,
            LogsUnavailableReason = GetLogSupport(inspect).Reason,
            LastSeenUtc = DateTimeOffset.UtcNow,
            Compose = new ContainerComposeInfo
            {
                Project = containerLabels.TryGetValue("com.docker.compose.project", out var project) ? project : null,
                Service = containerLabels.TryGetValue("com.docker.compose.service", out var service) ? service : null,
                ContainerNumber = containerLabels.TryGetValue("com.docker.compose.container-number", out var number)
                    ? number
                    : null
            }
        };
    }

    private static (bool Supported, string? Reason) GetLogSupport(ContainerInspectResponse inspect)
    {
        var driver = inspect.HostConfig?.LogConfig?.Type;
        if (string.IsNullOrWhiteSpace(driver)) return (true, null);

        if (string.Equals(driver, "none", StringComparison.OrdinalIgnoreCase))
            return (false, "Logs are unavailable because the container uses the 'none' logging driver.");

        // Be permissive here and rely on the stream call as the final authority.
        // Docker logging driver support varies by environment, and over-disabling
        // the UI is worse than allowing the action with a clean runtime fallback.
        return (true, null);
    }

    private static List<ContainerNetworkInfo> ReadNetworks(ContainerInspectResponse inspect)
    {
        var results = new List<ContainerNetworkInfo>();
        var networks = inspect.NetworkSettings?.Networks;
        if (networks is null) return results;

        foreach (var entry in networks)
            results.Add(new ContainerNetworkInfo
            {
                Name = entry.Key,
                IpAddress = entry.Value?.IPAddress
            });

        return results.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static Dictionary<(int Port, string Protocol), List<PublishedPortBinding>> ReadPublishedPorts(
        ContainerInspectResponse inspect)
    {
        var results = new Dictionary<(int Port, string Protocol), List<PublishedPortBinding>>();
        var networkPorts = inspect.NetworkSettings?.Ports;
        if (networkPorts is not null)
            foreach (var entry in networkPorts)
            {
                var parsed = ParseExposedPort(entry.Key);
                if (parsed is null) continue;

                var list = new List<PublishedPortBinding>();
                foreach (var binding in entry.Value ?? [])
                    if (int.TryParse(binding.HostPort, out var publishedPort))
                        list.Add(new PublishedPortBinding
                        {
                            HostIp = binding.HostIP ?? string.Empty,
                            HostPort = publishedPort
                        });

                results[parsed.Value] = NormalizePublishedBindings(list);
            }

        var configuredBindings = inspect.HostConfig?.PortBindings;
        if (configuredBindings is not null)
            foreach (var entry in configuredBindings)
            {
                var parsed = ParseExposedPort(entry.Key);
                if (parsed is null) continue;

                var configured = (entry.Value ?? [])
                    .Where(binding => int.TryParse(binding.HostPort, out _))
                    .Select(binding => new PublishedPortBinding
                    {
                        HostIp = binding.HostIP ?? string.Empty,
                        HostPort = int.Parse(binding.HostPort)
                    });

                if (results.TryGetValue(parsed.Value, out var existing))
                    results[parsed.Value] = NormalizePublishedBindings(existing.Concat(configured));
                else
                    results[parsed.Value] = NormalizePublishedBindings(configured);
            }

        return results;
    }

    private static List<PublishedPortBinding> NormalizePublishedBindings(IEnumerable<PublishedPortBinding> bindings)
    {
        return bindings
            .GroupBy(x => new { x.HostIp, x.HostPort })
            .Select(x => x.First())
            .OrderBy(x => x.HostPort)
            .ToList();
    }

    private static IEnumerable<(int Port, string Protocol)> ReadExposedPorts(ContainerInspectResponse inspect)
    {
        var ports = inspect.Config?.ExposedPorts;
        if (ports is null) yield break;

        foreach (var entry in ports)
        {
            var parsed = ParseExposedPort(entry.Key);
            if (parsed is not null) yield return parsed.Value;
        }
    }

    private static string NormalizeName(string? value)
    {
        return (value ?? string.Empty).Trim().TrimStart('/');
    }

    private static int IndexOfNewline(StringBuilder value)
    {
        for (var i = 0; i < value.Length; i++)
            if (value[i] == '\n')
                return i;

        return -1;
    }

    private static ContainerLogEntry ParseLogEntry(string line, string stream)
    {
        var separatorIndex = line.IndexOf(' ');
        if (separatorIndex <= 0)
        {
            var normalizedStream = NormalizeLogStream(stream);
            return new ContainerLogEntry(normalizedStream, TrimEmbeddedStreamPrefix(line, normalizedStream),
                string.Empty);
        }

        var normalized = NormalizeLogStream(stream);
        var timestamp = line[..separatorIndex];
        var message = TrimEmbeddedStreamPrefix(line[(separatorIndex + 1)..], normalized);
        return new ContainerLogEntry(normalized, message, timestamp);
    }

    private static string NormalizeLogStream(string? value)
    {
        return !string.IsNullOrWhiteSpace(value) &&
               value.Contains("err", StringComparison.OrdinalIgnoreCase)
            ? "stderr"
            : "stdout";
    }

    private static string TrimEmbeddedStreamPrefix(string message, string normalizedStream)
    {
        var trimmed = message.TrimStart();
        var prefix = $"{normalizedStream}:";
        return trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? trimmed[prefix.Length..].TrimStart()
            : message;
    }

    private static (int Port, string Protocol)? ParseExposedPort(string? key)
    {
        if (string.IsNullOrWhiteSpace(key)) return null;

        var parts = key.Split('/', 2, StringSplitOptions.TrimEntries);
        if (!int.TryParse(parts[0], out var port)) return null;

        var protocol = parts.Length > 1 ? NormalizeProtocol(parts[1]) : "tcp";
        return (port, protocol);
    }

    private static Uri GetDefaultEndpoint()
    {
        return OperatingSystem.IsWindows()
            ? new Uri("npipe://./pipe/docker_engine")
            : new Uri("unix:///var/run/docker.sock");
    }

    private void ApplyTemplateValues(ContainerEditRequest request,
        IReadOnlyDictionary<string, string>? imageLabels = null)
    {
        foreach (var label in request.Labels)
            label.Value = _templateValues.ReplacePlaceholders(label.Value ?? string.Empty);

        var labels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var imageLabel in imageLabels ?? new Dictionary<string, string>())
        {
            if (string.IsNullOrWhiteSpace(imageLabel.Key)) continue;

            labels[imageLabel.Key.Trim()] = imageLabel.Value ?? string.Empty;
        }

        foreach (var label in request.Labels
                     .Where(x => !string.IsNullOrWhiteSpace(x.Key))
                     .GroupBy(x => x.Key.Trim(), StringComparer.OrdinalIgnoreCase))
            labels[label.Key] = label.Last().Value ?? string.Empty;

        foreach (var env in request.EnvVars) env.Value = ReplaceRequestPlaceholders(env.Value, labels);

        foreach (var mount in request.MountBindings ?? [])
        {
            mount.HostPath = ReplaceRequestPlaceholders(mount.HostPath, labels);
            mount.ContainerPath = ReplaceRequestPlaceholders(mount.ContainerPath, labels);
        }

        foreach (var mapping in request.HostMappings ?? [])
        {
            mapping.Hostname = ReplaceRequestPlaceholders(mapping.Hostname, labels);
            mapping.Address = ReplaceRequestPlaceholders(mapping.Address, labels);
        }

        foreach (var alias in request.NetworkAliases ?? [])
        {
            alias.Network = ReplaceRequestPlaceholders(alias.Network, labels);
            alias.Alias = ReplaceRequestPlaceholders(alias.Alias, labels);
        }
    }

    private string ReplaceRequestPlaceholders(string? input, IReadOnlyDictionary<string, string> labels)
    {
        var replaced = _templateValues.ReplacePlaceholders(input ?? string.Empty);
        if (string.IsNullOrWhiteSpace(replaced) || labels.Count == 0) return replaced;

        return LabelTemplateRegex.Replace(replaced, match =>
        {
            var key = match.Groups[1].Value;
            return labels.TryGetValue(key, out var value)
                ? value ?? string.Empty
                : match.Value;
        });
    }

    private static List<ContainerNetworkAliasRequest> ReadEditableNetworkAliases(ContainerInspectResponse inspect)
    {
        var normalizedContainerName = NormalizeName(inspect.Name);
        var hostname = (inspect.Config?.Hostname ?? string.Empty).Trim();
        var containerId = (inspect.ID ?? string.Empty).Trim();
        var implicitAliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(normalizedContainerName)) implicitAliases.Add(normalizedContainerName);

        if (!string.IsNullOrWhiteSpace(hostname)) implicitAliases.Add(hostname);

        if (!string.IsNullOrWhiteSpace(containerId))
        {
            implicitAliases.Add(containerId);
            if (containerId.Length >= 12) implicitAliases.Add(containerId[..12]);
        }

        return (inspect.NetworkSettings?.Networks ?? new Dictionary<string, EndpointSettings>())
            .SelectMany(entry => (entry.Value?.Aliases ?? [])
                .Where(alias => !string.IsNullOrWhiteSpace(alias) && !implicitAliases.Contains(alias.Trim()))
                .Select(alias => new ContainerNetworkAliasRequest
                {
                    Network = entry.Key,
                    Alias = alias.Trim()
                }))
            .GroupBy(x => $"{x.Network}\u001f{x.Alias}", StringComparer.OrdinalIgnoreCase)
            .Select(x => x.First())
            .OrderBy(x => x.Network, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Alias, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static List<ContainerHostMappingRequest> ReadEditableHostMappings(ContainerInspectResponse inspect)
    {
        return (inspect.HostConfig?.ExtraHosts ?? [])
            .Select(ParseEditableHostMapping)
            .Where(x => x is not null)
            .Select(x => x!)
            .GroupBy(x => x.Hostname, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.First())
            .OrderBy(x => x.Hostname, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static ContainerHostMappingRequest? ParseEditableHostMapping(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;

        var separatorIndex = value.IndexOf(':');
        if (separatorIndex <= 0 || separatorIndex >= value.Length - 1) return null;

        var hostname = value[..separatorIndex].Trim();
        var address = value[(separatorIndex + 1)..].Trim();
        if (string.IsNullOrWhiteSpace(hostname) || string.IsNullOrWhiteSpace(address)) return null;

        return new ContainerHostMappingRequest
        {
            Hostname = hostname,
            Address = address
        };
    }

    private static IList<string> BuildExtraHosts(IReadOnlyList<ContainerHostMappingRequest>? hostMappings)
    {
        return (hostMappings ?? [])
            .Where(x => !string.IsNullOrWhiteSpace(x.Hostname) && !string.IsNullOrWhiteSpace(x.Address))
            .GroupBy(x => x.Hostname.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(x => x.First())
            .Select(x => $"{x.Hostname.Trim()}:{x.Address.Trim()}")
            .ToList();
    }

    private static IList<string>? BuildDnsServers(
        IReadOnlyList<string>? internalDnsServers,
        IList<string>? source,
        IReadOnlyList<string>? removeServers)
    {
        var removeSet = new HashSet<string>(
            (removeServers ?? []).Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()),
            StringComparer.OrdinalIgnoreCase);
        var merged = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var server in internalDnsServers ?? [])
        {
            var value = (server ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(value) && !removeSet.Contains(value) && seen.Add(value)) merged.Add(value);
        }

        foreach (var server in source ?? [])
        {
            var value = (server ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(value) && !removeSet.Contains(value) && seen.Add(value)) merged.Add(value);
        }

        return merged.Count > 0 ? merged : null;
    }

    private sealed class ImageMetadata
    {
        public Dictionary<string, string> Labels { get; init; } = new(StringComparer.OrdinalIgnoreCase);
        public string? Digest { get; init; }
        public string? Architecture { get; init; }
        public string? Os { get; init; }
    }
}