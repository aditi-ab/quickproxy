using Microsoft.Extensions.Options;
using QuickProxy.Proxy.Runtime;

namespace QuickProxy.Proxy.Containers;

public sealed class ContainerInventoryHostedService(
    IContainerRuntimeClient runtimeClient,
    IContainerDefaultsApplier defaultsApplier,
    ContainerInventoryCache cache,
    IOptions<ContainerRuntimeSettings> options,
    IProxyHostRuntime proxyHostRuntime,
    ILogger<ContainerInventoryHostedService> logger) : BackgroundService
{
    private const string DefaultsTriggerLabelKey = "quickproxy.defaults";
    private const string DnsMarkerLabelKey = "quickproxy.internal.dns-applied";
    private const string DnsServerLabelKey = "quickproxy.internal.defaults-dns-server";
    private readonly Dictionary<string, DateTimeOffset> _applyCooldown = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _cooldownSync = new();
    private readonly ContainerRuntimeSettings _settings = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_settings.Enabled)
        {
            cache.SetEventStreamConnected(false);
            return;
        }

        cache.Changed += OnChanged;

        try
        {
            await RefreshAsync(stoppingToken);

            var eventTask = WatchEventsLoopAsync(stoppingToken);
            var resyncTask = ResyncLoopAsync(stoppingToken);
            await WaitForInitialEventStreamConnectionAsync(eventTask, stoppingToken);
            await ReconcileContainerRuntimeConfigAsync(stoppingToken);

            await Task.WhenAll(eventTask, resyncTask);
        }
        finally
        {
            cache.Changed -= OnChanged;
        }
    }

    private async Task ResyncLoopAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(5, _settings.ResyncIntervalSeconds)));
        while (await timer.WaitForNextTickAsync(cancellationToken))
        {
            await RefreshAsync(cancellationToken);
            await ReconcileContainerRuntimeConfigAsync(cancellationToken);
        }
    }

    private async Task WatchEventsLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
            try
            {
                cache.SetEventStreamConnected(false);

                var eventStream = runtimeClient.WatchContainerEventsAsync(cancellationToken);
                cache.SetEventStreamConnected(true);

                await foreach (var runtimeEvent in eventStream)
                {
                    await RefreshAsync(cancellationToken);
                    await TryApplyDefaultsAfterStartAsync(runtimeEvent, cancellationToken);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Docker event stream failed. Retrying.");
                cache.SetEventStreamConnected(false);
                await Task.Delay(TimeSpan.FromSeconds(Math.Max(5, _settings.RefreshIntervalSeconds)),
                    cancellationToken);
            }
    }

    private async Task WaitForInitialEventStreamConnectionAsync(Task eventTask, CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(5));

        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(100));
            while (!timeoutCts.IsCancellationRequested)
            {
                if (cache.GetStatus().EventStreamConnected || eventTask.IsCompleted) return;

                await timer.WaitForNextTickAsync(timeoutCts.Token);
            }
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
        {
        }
    }

    private async Task TryApplyDefaultsAfterStartAsync(ContainerRuntimeEvent runtimeEvent,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(runtimeEvent.Action, "start", StringComparison.OrdinalIgnoreCase)) return;

        var containerName = runtimeEvent.ContainerName;
        if (string.IsNullOrWhiteSpace(containerName) && !string.IsNullOrWhiteSpace(runtimeEvent.ContainerId))
            containerName = cache.ListContainers()
                .FirstOrDefault(x => string.Equals(x.Id, runtimeEvent.ContainerId, StringComparison.OrdinalIgnoreCase))
                ?.Name;

        if (string.IsNullOrWhiteSpace(containerName)) return;

        var cooldownKey = !string.IsNullOrWhiteSpace(runtimeEvent.ContainerId)
            ? runtimeEvent.ContainerId
            : containerName;
        if (!TryAcquireApplyCooldown(cooldownKey)) return;

        try
        {
            var result = await defaultsApplier.ApplyForStartAsync(containerName, true, cancellationToken);
            if (result.Applied) await RefreshAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed applying container defaults after start event for '{ContainerName}'.",
                containerName);
        }
    }

    private bool TryAcquireApplyCooldown(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return false;

        var utcNow = DateTimeOffset.UtcNow;
        lock (_cooldownSync)
        {
            var expired = _applyCooldown
                .Where(x => x.Value <= utcNow)
                .Select(x => x.Key)
                .ToArray();
            foreach (var item in expired) _applyCooldown.Remove(item);

            if (_applyCooldown.TryGetValue(key, out var blockedUntil) && blockedUntil > utcNow) return false;

            _applyCooldown[key] = utcNow.AddSeconds(10);
            return true;
        }
    }

    private async Task ReconcileContainerRuntimeConfigAsync(CancellationToken cancellationToken)
    {
        var containers = cache.ListContainers()
            .Where(ShouldReconcileContainer)
            .ToArray();

        var appliedAny = false;
        foreach (var container in containers)
        {
            var cooldownKey = !string.IsNullOrWhiteSpace(container.Id)
                ? container.Id
                : container.Name;
            if (!TryAcquireApplyCooldown(cooldownKey)) continue;

            try
            {
                var result = await defaultsApplier.ApplyForStartAsync(container.Name, false, cancellationToken);
                appliedAny |= result.Applied;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed reconciling runtime container settings for '{ContainerName}'.",
                    container.Name);
            }
        }

        if (appliedAny) await RefreshAsync(cancellationToken);
    }

    private static bool ShouldReconcileContainer(ContainerInventoryItem container)
    {
        return container.ContainerLabels.ContainsKey(DefaultsTriggerLabelKey)
               || container.ContainerLabels.ContainsKey(DnsMarkerLabelKey)
               || container.ContainerLabels.ContainsKey(DnsServerLabelKey);
    }

    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        try
        {
            cache.MarkRefreshStarted();
            var containers = await runtimeClient.ListContainersAsync(cancellationToken);
            cache.Replace(containers);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to refresh Docker container inventory.");
            cache.MarkRefreshFailed(ex);
        }
    }

    private void OnChanged()
    {
        try
        {
            proxyHostRuntime.TryReload();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to reload proxy config after Docker inventory change.");
        }
    }
}