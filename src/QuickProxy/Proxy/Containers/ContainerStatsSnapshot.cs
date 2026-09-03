using System.Text.Json.Serialization;

namespace QuickProxy.Proxy.Containers;

public sealed class ContainerStatsSnapshot
{
    public DateTimeOffset CollectedAtUtc { get; set; }
    public double? CpuPercent { get; set; }
    public ulong? MemoryUsageBytes { get; set; }
    public ulong? MemoryLimitBytes { get; set; }
    public double? MemoryPercent { get; set; }
    public ulong? NetworkRxBytes { get; set; }
    public ulong? NetworkTxBytes { get; set; }
    public ulong? BlockReadBytes { get; set; }
    public ulong? BlockWriteBytes { get; set; }
    public ulong? PidsCurrent { get; set; }

    [JsonIgnore] public ulong? CpuTotalUsage { get; set; }

    [JsonIgnore] public uint? ProcessorCount { get; set; }
}