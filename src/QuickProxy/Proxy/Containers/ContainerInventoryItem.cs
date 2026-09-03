namespace QuickProxy.Proxy.Containers;

public sealed class ContainerInventoryItem
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Image { get; set; } = string.Empty;
    public string ImageId { get; set; } = string.Empty;
    public string? ImageDigest { get; set; }
    public string? ImageArchitecture { get; set; }
    public string? ImageOs { get; set; }
    public string State { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public Dictionary<string, string> ContainerLabels { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> ImageLabels { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public ContainerStatsSnapshot? Stats { get; set; }
    public ContainerImageUpdateInfo? ImageUpdate { get; set; }
    public List<ContainerPortInfo> Ports { get; set; } = [];
    public List<ContainerNetworkInfo> Networks { get; set; } = [];
    public ContainerComposeInfo Compose { get; set; } = new();
    public bool LogsSupported { get; set; } = true;
    public string? LogsUnavailableReason { get; set; }
    public DateTimeOffset LastSeenUtc { get; set; }

    public bool IsRunning => string.Equals(State, "running", StringComparison.OrdinalIgnoreCase);

    public string? ResolveIpAddress(string? preferredNetwork)
    {
        if (!string.IsNullOrWhiteSpace(preferredNetwork))
        {
            var exact = Networks.FirstOrDefault(x =>
                string.Equals(x.Name, preferredNetwork, StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(x.IpAddress));
            if (exact is not null) return exact.IpAddress;
        }

        return Networks
            .Where(x => !string.IsNullOrWhiteSpace(x.IpAddress))
            .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.IpAddress)
            .FirstOrDefault();
    }
}