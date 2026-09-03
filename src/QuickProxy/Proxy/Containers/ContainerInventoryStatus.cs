namespace QuickProxy.Proxy.Containers;

public sealed class ContainerInventoryStatus
{
    public bool Enabled { get; set; } = true;
    public DateTimeOffset? LastRefreshStartedUtc { get; set; }
    public DateTimeOffset? LastRefreshCompletedUtc { get; set; }
    public DateTimeOffset? LastSuccessfulRefreshUtc { get; set; }
    public bool StatsEnabled { get; set; }
    public DateTimeOffset? LastStatsRefreshStartedUtc { get; set; }
    public DateTimeOffset? LastStatsRefreshCompletedUtc { get; set; }
    public DateTimeOffset? LastSuccessfulStatsRefreshUtc { get; set; }
    public string? LastStatsError { get; set; }
    public bool ImageUpdatesEnabled { get; set; }
    public DateTimeOffset? LastImageUpdateStartedUtc { get; set; }
    public DateTimeOffset? LastImageUpdateCompletedUtc { get; set; }
    public DateTimeOffset? LastSuccessfulImageUpdateUtc { get; set; }
    public string? LastImageUpdateError { get; set; }
    public bool EventStreamConnected { get; set; }
    public string? LastError { get; set; }
}