namespace QuickProxy.Proxy.Containers;

public sealed class ContainerInventoryCache : IContainerInventory
{
    private readonly ContainerInventoryStatus _status = new();
    private readonly object _sync = new();
    private Dictionary<string, ContainerInventoryItem> _containersByName = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<ContainerInventoryItem> ListContainers()
    {
        lock (_sync)
        {
            return _containersByName.Values
                .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
    }

    public ContainerInventoryItem? GetContainer(string name)
    {
        lock (_sync)
        {
            _containersByName.TryGetValue(name, out var container);
            return container;
        }
    }

    public ContainerInventoryStatus GetStatus()
    {
        lock (_sync)
        {
            return CloneStatus(_status);
        }
    }

    public ContainerInventorySnapshot GetSnapshot()
    {
        lock (_sync)
        {
            return new ContainerInventorySnapshot
            {
                Status = CloneStatus(_status),
                Containers = _containersByName.Values
                    .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                    .ToArray()
            };
        }
    }

    public event Action? Changed;

    public void MarkRefreshStarted()
    {
        lock (_sync)
        {
            _status.LastRefreshStartedUtc = DateTimeOffset.UtcNow;
            _status.Enabled = true;
        }
    }

    public void SetImageUpdatesEnabled(bool enabled)
    {
        lock (_sync)
        {
            _status.ImageUpdatesEnabled = enabled;
        }
    }

    public void SetStatsEnabled(bool enabled)
    {
        lock (_sync)
        {
            _status.StatsEnabled = enabled;
        }
    }

    public void MarkRefreshFailed(Exception exception)
    {
        if (IsCancellationException(exception)) return;

        lock (_sync)
        {
            _status.LastRefreshCompletedUtc = DateTimeOffset.UtcNow;
            _status.LastError = exception.Message;
        }
    }

    public void MarkStatsRefreshStarted()
    {
        lock (_sync)
        {
            _status.LastStatsRefreshStartedUtc = DateTimeOffset.UtcNow;
            _status.StatsEnabled = true;
        }
    }

    public void MarkStatsRefreshFailed(Exception exception)
    {
        if (IsCancellationException(exception)) return;

        lock (_sync)
        {
            _status.LastStatsRefreshCompletedUtc = DateTimeOffset.UtcNow;
            _status.LastStatsError = exception.Message;
        }
    }

    public void MarkImageUpdateRefreshStarted()
    {
        lock (_sync)
        {
            _status.LastImageUpdateStartedUtc = DateTimeOffset.UtcNow;
            _status.ImageUpdatesEnabled = true;
        }
    }

    public void MarkImageUpdateRefreshFailed(Exception exception)
    {
        if (IsCancellationException(exception)) return;

        lock (_sync)
        {
            _status.LastImageUpdateCompletedUtc = DateTimeOffset.UtcNow;
            _status.LastImageUpdateError = exception.Message;
        }
    }

    public void SetEventStreamConnected(bool connected)
    {
        lock (_sync)
        {
            if (_status.EventStreamConnected == connected) return;

            _status.EventStreamConnected = connected;
        }
    }

    public void Replace(IReadOnlyList<ContainerInventoryItem> containers)
    {
        Action? changed = null;

        lock (_sync)
        {
            var normalized = containers
                .GroupBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    x => x.Key,
                    x =>
                    {
                        var next = x.First();
                        if (_containersByName.TryGetValue(x.Key, out var existing) &&
                            CanReuseImageUpdate(existing, next))
                            next.ImageUpdate = existing.ImageUpdate is null
                                ? null
                                : CloneImageUpdate(existing.ImageUpdate);

                        if (_containersByName.TryGetValue(x.Key, out existing) && CanReuseStats(existing, next))
                            next.Stats = existing.Stats is null ? null : CloneStats(existing.Stats);

                        return next;
                    },
                    StringComparer.OrdinalIgnoreCase);

            if (!AreEqual(_containersByName, normalized))
            {
                _containersByName = normalized;
                changed = Changed;
            }

            _status.Enabled = true;
            _status.LastRefreshCompletedUtc = DateTimeOffset.UtcNow;
            _status.LastSuccessfulRefreshUtc = _status.LastRefreshCompletedUtc;
            _status.LastError = null;
        }

        changed?.Invoke();
    }

    public void UpdateStats(IReadOnlyDictionary<string, ContainerStatsSnapshot> statsByContainerName)
    {
        lock (_sync)
        {
            var updated = _containersByName.ToDictionary(
                pair => pair.Key,
                pair =>
                {
                    var clone = CloneContainer(pair.Value);
                    if (!clone.IsRunning || !statsByContainerName.TryGetValue(pair.Key, out var stats))
                    {
                        clone.Stats = null;
                        return clone;
                    }

                    clone.Stats = CloneStats(stats);
                    return clone;
                },
                StringComparer.OrdinalIgnoreCase);

            _containersByName = updated;

            _status.StatsEnabled = true;
            _status.LastStatsRefreshCompletedUtc = DateTimeOffset.UtcNow;
            _status.LastSuccessfulStatsRefreshUtc = _status.LastStatsRefreshCompletedUtc;
            _status.LastStatsError = null;
        }
    }

    public void UpdateImageUpdates(IReadOnlyDictionary<string, ContainerImageUpdateInfo> updatesByContainerName)
    {
        Action? changed = null;

        lock (_sync)
        {
            var updated = _containersByName.ToDictionary(
                pair => pair.Key,
                pair =>
                {
                    var clone = CloneContainer(pair.Value);
                    if (!updatesByContainerName.TryGetValue(pair.Key, out var imageUpdate))
                    {
                        clone.ImageUpdate = null;
                        return clone;
                    }

                    clone.ImageUpdate = CloneImageUpdate(imageUpdate);
                    return clone;
                },
                StringComparer.OrdinalIgnoreCase);

            if (!AreEqual(_containersByName, updated))
            {
                _containersByName = updated;
                changed = Changed;
            }

            _status.ImageUpdatesEnabled = true;
            _status.LastImageUpdateCompletedUtc = DateTimeOffset.UtcNow;
            _status.LastSuccessfulImageUpdateUtc = _status.LastImageUpdateCompletedUtc;
            _status.LastImageUpdateError = null;
        }

        changed?.Invoke();
    }

    private static bool AreEqual(
        IReadOnlyDictionary<string, ContainerInventoryItem> left,
        IReadOnlyDictionary<string, ContainerInventoryItem> right)
    {
        if (left.Count != right.Count) return false;

        foreach (var pair in left)
        {
            if (!right.TryGetValue(pair.Key, out var other)) return false;

            if (!string.Equals(pair.Value.Id, other.Id, StringComparison.Ordinal) ||
                !string.Equals(pair.Value.ImageId, other.ImageId, StringComparison.Ordinal) ||
                !string.Equals(pair.Value.ImageDigest, other.ImageDigest, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(pair.Value.State, other.State, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(pair.Value.Status, other.Status, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(pair.Value.Image, other.Image, StringComparison.Ordinal))
                return false;

            if (pair.Value.Ports.Count != other.Ports.Count ||
                pair.Value.Networks.Count != other.Networks.Count ||
                pair.Value.ContainerLabels.Count != other.ContainerLabels.Count ||
                pair.Value.ImageLabels.Count != other.ImageLabels.Count)
                return false;

            if (!ImageUpdatesEqual(pair.Value.ImageUpdate, other.ImageUpdate)) return false;

            if (!StatsEqual(pair.Value.Stats, other.Stats)) return false;

            foreach (var label in pair.Value.ContainerLabels)
                if (!other.ContainerLabels.TryGetValue(label.Key, out var otherValue) ||
                    !string.Equals(label.Value, otherValue, StringComparison.Ordinal))
                    return false;

            foreach (var label in pair.Value.ImageLabels)
                if (!other.ImageLabels.TryGetValue(label.Key, out var otherValue) ||
                    !string.Equals(label.Value, otherValue, StringComparison.Ordinal))
                    return false;
        }

        return true;
    }

    private static ContainerInventoryStatus CloneStatus(ContainerInventoryStatus value)
    {
        return new ContainerInventoryStatus
        {
            Enabled = value.Enabled,
            LastRefreshStartedUtc = value.LastRefreshStartedUtc,
            LastRefreshCompletedUtc = value.LastRefreshCompletedUtc,
            LastSuccessfulRefreshUtc = value.LastSuccessfulRefreshUtc,
            StatsEnabled = value.StatsEnabled,
            LastStatsRefreshStartedUtc = value.LastStatsRefreshStartedUtc,
            LastStatsRefreshCompletedUtc = value.LastStatsRefreshCompletedUtc,
            LastSuccessfulStatsRefreshUtc = value.LastSuccessfulStatsRefreshUtc,
            LastStatsError = value.LastStatsError,
            ImageUpdatesEnabled = value.ImageUpdatesEnabled,
            LastImageUpdateStartedUtc = value.LastImageUpdateStartedUtc,
            LastImageUpdateCompletedUtc = value.LastImageUpdateCompletedUtc,
            LastSuccessfulImageUpdateUtc = value.LastSuccessfulImageUpdateUtc,
            LastImageUpdateError = value.LastImageUpdateError,
            EventStreamConnected = value.EventStreamConnected,
            LastError = value.LastError
        };
    }

    private static bool ImageUpdatesEqual(ContainerImageUpdateInfo? left, ContainerImageUpdateInfo? right)
    {
        if (left is null && right is null) return true;

        if (left is null || right is null) return false;

        return string.Equals(left.Status, right.Status, StringComparison.OrdinalIgnoreCase)
               && left.UpdateAvailable == right.UpdateAvailable
               && string.Equals(left.Source, right.Source, StringComparison.OrdinalIgnoreCase)
               && string.Equals(left.LocalDigest, right.LocalDigest, StringComparison.OrdinalIgnoreCase)
               && string.Equals(left.RemoteDigest, right.RemoteDigest, StringComparison.OrdinalIgnoreCase)
               && string.Equals(left.Error, right.Error, StringComparison.Ordinal)
               && string.Equals(left.RemoteArchitecture, right.RemoteArchitecture, StringComparison.OrdinalIgnoreCase)
               && string.Equals(left.RemoteOs, right.RemoteOs, StringComparison.OrdinalIgnoreCase);
    }

    private static bool StatsEqual(ContainerStatsSnapshot? left, ContainerStatsSnapshot? right)
    {
        if (left is null && right is null) return true;

        if (left is null || right is null) return false;

        return left.CollectedAtUtc == right.CollectedAtUtc
               && left.CpuPercent == right.CpuPercent
               && left.MemoryUsageBytes == right.MemoryUsageBytes
               && left.MemoryLimitBytes == right.MemoryLimitBytes
               && left.MemoryPercent == right.MemoryPercent
               && left.NetworkRxBytes == right.NetworkRxBytes
               && left.NetworkTxBytes == right.NetworkTxBytes
               && left.BlockReadBytes == right.BlockReadBytes
               && left.BlockWriteBytes == right.BlockWriteBytes
               && left.PidsCurrent == right.PidsCurrent
               && left.CpuTotalUsage == right.CpuTotalUsage
               && left.ProcessorCount == right.ProcessorCount;
    }

    private static ContainerImageUpdateInfo CloneImageUpdate(ContainerImageUpdateInfo value)
    {
        return new ContainerImageUpdateInfo
        {
            Status = value.Status,
            UpdateAvailable = value.UpdateAvailable,
            Source = value.Source,
            LocalDigest = value.LocalDigest,
            RemoteDigest = value.RemoteDigest,
            Error = value.Error,
            CheckedAtUtc = value.CheckedAtUtc,
            RemoteCreatedUtc = value.RemoteCreatedUtc,
            RemoteArchitecture = value.RemoteArchitecture,
            RemoteOs = value.RemoteOs,
            RemoteLabels = new Dictionary<string, string>(value.RemoteLabels, StringComparer.OrdinalIgnoreCase)
        };
    }

    private static ContainerStatsSnapshot CloneStats(ContainerStatsSnapshot value)
    {
        return new ContainerStatsSnapshot
        {
            CollectedAtUtc = value.CollectedAtUtc,
            CpuPercent = value.CpuPercent,
            MemoryUsageBytes = value.MemoryUsageBytes,
            MemoryLimitBytes = value.MemoryLimitBytes,
            MemoryPercent = value.MemoryPercent,
            NetworkRxBytes = value.NetworkRxBytes,
            NetworkTxBytes = value.NetworkTxBytes,
            BlockReadBytes = value.BlockReadBytes,
            BlockWriteBytes = value.BlockWriteBytes,
            PidsCurrent = value.PidsCurrent,
            CpuTotalUsage = value.CpuTotalUsage,
            ProcessorCount = value.ProcessorCount
        };
    }

    private static ContainerInventoryItem CloneContainer(ContainerInventoryItem value)
    {
        return new ContainerInventoryItem
        {
            Id = value.Id,
            Name = value.Name,
            Image = value.Image,
            ImageId = value.ImageId,
            ImageDigest = value.ImageDigest,
            ImageArchitecture = value.ImageArchitecture,
            ImageOs = value.ImageOs,
            State = value.State,
            Status = value.Status,
            ContainerLabels = new Dictionary<string, string>(value.ContainerLabels, StringComparer.OrdinalIgnoreCase),
            ImageLabels = new Dictionary<string, string>(value.ImageLabels, StringComparer.OrdinalIgnoreCase),
            Stats = value.Stats is null ? null : CloneStats(value.Stats),
            ImageUpdate = value.ImageUpdate is null ? null : CloneImageUpdate(value.ImageUpdate),
            Ports = value.Ports.ToList(),
            Networks = value.Networks.ToList(),
            Compose = new ContainerComposeInfo
            {
                Project = value.Compose.Project,
                Service = value.Compose.Service,
                ContainerNumber = value.Compose.ContainerNumber
            },
            LogsSupported = value.LogsSupported,
            LogsUnavailableReason = value.LogsUnavailableReason,
            LastSeenUtc = value.LastSeenUtc
        };
    }

    private static bool CanReuseImageUpdate(ContainerInventoryItem existing, ContainerInventoryItem next)
    {
        return string.Equals(existing.Image, next.Image, StringComparison.Ordinal)
               && string.Equals(existing.ImageId, next.ImageId, StringComparison.Ordinal)
               && string.Equals(existing.ImageDigest, next.ImageDigest, StringComparison.OrdinalIgnoreCase)
               && string.Equals(existing.ImageArchitecture, next.ImageArchitecture, StringComparison.OrdinalIgnoreCase)
               && string.Equals(existing.ImageOs, next.ImageOs, StringComparison.OrdinalIgnoreCase);
    }

    private static bool CanReuseStats(ContainerInventoryItem existing, ContainerInventoryItem next)
    {
        return existing.IsRunning
               && next.IsRunning
               && string.Equals(existing.Id, next.Id, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsCancellationException(Exception exception)
    {
        return exception is OperationCanceledException or TaskCanceledException
               || exception.Message.Contains("The operation was canceled", StringComparison.OrdinalIgnoreCase)
               || exception.Message.Contains("The operation was cancelled", StringComparison.OrdinalIgnoreCase);
    }
}