using System.Threading.Channels;

namespace QuickProxy.Proxy.Containers;

public interface IContainerRuntimeClient
{
    Task<IReadOnlyList<ContainerInventoryItem>> ListContainersAsync(CancellationToken cancellationToken);
    Task<ContainerStatsSnapshot> GetContainerStatsAsync(string containerId, CancellationToken cancellationToken);

    Task<IReadOnlyList<ContainerImageInventoryItem>> ListImagesAsync(bool includeAll,
        CancellationToken cancellationToken);

    IAsyncEnumerable<ContainerRuntimeEvent> WatchContainerEventsAsync(CancellationToken cancellationToken);

    IAsyncEnumerable<ContainerLogEntry> StreamContainerLogsAsync(string name, string? since, int tail,
        CancellationToken cancellationToken);

    Task<ContainerEditRequest> GetEditableContainerAsync(string name, CancellationToken cancellationToken);

    Task CreateContainerAsync(ContainerEditRequest request, string? imageArchivePath,
        CancellationToken cancellationToken);

    Task UpdateContainerAsync(
        string existingName,
        ContainerEditRequest request,
        string? imageArchivePath,
        CancellationToken cancellationToken,
        bool pullImage = false,
        bool pinPulledImageToDigest = false);

    Task<int> PruneUnusedImagesAsync(CancellationToken cancellationToken);
    Task DeleteContainerAsync(string name, CancellationToken cancellationToken);
    Task StartContainerAsync(string name, CancellationToken cancellationToken);
    Task StopContainerAsync(string name, CancellationToken cancellationToken);

    Task StreamContainerShellAsync(
        string name,
        ChannelReader<ContainerShellClientMessage> input,
        Func<ContainerShellServerMessage, CancellationToken, Task> onMessage,
        CancellationToken cancellationToken);

    Task PullImageAndRestartContainerAsync(string name, string? imageReference, CancellationToken cancellationToken);
    Task RepullImageAndRestartContainerAsync(string name, CancellationToken cancellationToken);
}

public sealed record ContainerLogEntry(string Stream, string Message, string Timestamp);