using Microsoft.Extensions.Options;

namespace QuickProxy.Proxy.Containers;

public sealed class ContainerImageUpdateHostedService(
    ContainerInventoryCache cache,
    ContainerImageUpdateResolver resolver,
    IOptions<ContainerRuntimeSettings> options,
    ILogger<ContainerImageUpdateHostedService> logger) : BackgroundService
{
    private readonly object _refreshSignalSync = new();
    private readonly ContainerRuntimeSettings _runtimeSettings = options.Value;
    private readonly ContainerImageUpdateSettings _settings = options.Value.ImageUpdates;
    private TaskCompletionSource<bool> _refreshRequested = CreateRefreshSignal();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_runtimeSettings.Enabled || !_settings.Enabled)
        {
            cache.SetImageUpdatesEnabled(false);
            return;
        }

        cache.Changed += OnCacheChanged;

        cache.SetImageUpdatesEnabled(true);
        try
        {
            await RefreshAsync(stoppingToken);

            var interval = TimeSpan.FromSeconds(Math.Max(30, _settings.RefreshIntervalSeconds));
            while (!stoppingToken.IsCancellationRequested)
            {
                var signal = GetRefreshSignal();
                var delayTask = Task.Delay(interval, stoppingToken);
                var completed = await Task.WhenAny(delayTask, signal);
                if (completed == signal) ResetRefreshSignal(signal);

                await RefreshAsync(stoppingToken);
            }
        }
        finally
        {
            cache.Changed -= OnCacheChanged;
        }
    }

    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        try
        {
            cache.MarkImageUpdateRefreshStarted();
            var updates = await resolver.ResolveAsync(cache.ListContainers(), cancellationToken);
            cache.UpdateImageUpdates(updates);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed refreshing remote container image metadata.");
            cache.MarkImageUpdateRefreshFailed(ex);
        }
    }

    private void OnCacheChanged()
    {
        var needsRefresh = cache.ListContainers()
            .Any(x => x.IsRunning && x.ImageUpdate is null && !string.IsNullOrWhiteSpace(x.ImageDigest));
        if (!needsRefresh) return;

        lock (_refreshSignalSync)
        {
            _refreshRequested.TrySetResult(true);
        }
    }

    private Task<bool> GetRefreshSignal()
    {
        lock (_refreshSignalSync)
        {
            return _refreshRequested.Task;
        }
    }

    private void ResetRefreshSignal(Task completedTask)
    {
        lock (_refreshSignalSync)
        {
            if (!ReferenceEquals(_refreshRequested.Task, completedTask)) return;

            _refreshRequested = CreateRefreshSignal();
        }
    }

    private static TaskCompletionSource<bool> CreateRefreshSignal()
    {
        return new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}