using System.Collections.Concurrent;
using Microsoft.Extensions.Options;

namespace QuickProxy.Proxy.Containers;

public sealed class ContainerStatsHostedService(
    IContainerRuntimeClient runtimeClient,
    ContainerInventoryCache cache,
    IOptions<ContainerRuntimeSettings> options,
    ILogger<ContainerStatsHostedService> logger) : BackgroundService
{
    private readonly ConcurrentDictionary<string, ContainerStatsSnapshot> _previousStatsByContainerId =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly ContainerRuntimeSettings _settings = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_settings.Enabled || !_settings.Stats.Enabled)
        {
            cache.SetStatsEnabled(false);
            return;
        }

        await RefreshAsync(stoppingToken);

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(5, _settings.Stats.RefreshIntervalSeconds)));
        while (await timer.WaitForNextTickAsync(stoppingToken)) await RefreshAsync(stoppingToken);
    }

    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        try
        {
            cache.MarkStatsRefreshStarted();

            var runningContainers = cache.ListContainers()
                .Where(x => x.IsRunning && !string.IsNullOrWhiteSpace(x.Id))
                .ToArray();

            if (runningContainers.Length == 0)
            {
                cache.UpdateStats(new Dictionary<string, ContainerStatsSnapshot>(StringComparer.OrdinalIgnoreCase));
                return;
            }

            var statsByContainerName =
                new ConcurrentDictionary<string, ContainerStatsSnapshot>(StringComparer.OrdinalIgnoreCase);
            var timeout = TimeSpan.FromSeconds(Math.Max(1, _settings.Stats.TimeoutSeconds));

            await Parallel.ForEachAsync(runningContainers, new ParallelOptions
            {
                CancellationToken = cancellationToken,
                MaxDegreeOfParallelism = Math.Min(4, runningContainers.Length)
            }, async (container, ct) =>
            {
                try
                {
                    using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    timeoutSource.CancelAfter(timeout);

                    var stats = await runtimeClient.GetContainerStatsAsync(container.Id, timeoutSource.Token);
                    ApplyDerivedCpu(container.Id, stats);
                    statsByContainerName[container.Name] = stats;
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    logger.LogWarning("Timed out gathering stats for container '{ContainerName}'.", container.Name);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed gathering stats for container '{ContainerName}'.", container.Name);
                }
            });

            cache.UpdateStats(statsByContainerName);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to refresh Docker container stats.");
            cache.MarkStatsRefreshFailed(ex);
        }
    }

    private void ApplyDerivedCpu(string containerId, ContainerStatsSnapshot stats)
    {
        try
        {
            if (_previousStatsByContainerId.TryGetValue(containerId, out var previous) &&
                stats.CpuPercent is null &&
                previous.CpuTotalUsage.HasValue &&
                stats.CpuTotalUsage.HasValue &&
                stats.CollectedAtUtc > previous.CollectedAtUtc)
            {
                var elapsed = stats.CollectedAtUtc - previous.CollectedAtUtc;
                var cpuDelta = stats.CpuTotalUsage.Value >= previous.CpuTotalUsage.Value
                    ? stats.CpuTotalUsage.Value - previous.CpuTotalUsage.Value
                    : 0;
                var processorCount = (double)(stats.ProcessorCount ?? previous.ProcessorCount ?? 1);

                if (elapsed > TimeSpan.Zero && cpuDelta > 0 && processorCount > 0)
                {
                    var cpuPercent = cpuDelta / elapsed.TotalMilliseconds / 10_000d / processorCount;
                    stats.CpuPercent = Math.Max(0d, Math.Min(cpuPercent, 100d));
                }
            }
        }
        finally
        {
            _previousStatsByContainerId[containerId] = new ContainerStatsSnapshot
            {
                CollectedAtUtc = stats.CollectedAtUtc,
                CpuTotalUsage = stats.CpuTotalUsage,
                ProcessorCount = stats.ProcessorCount
            };
        }
    }
}