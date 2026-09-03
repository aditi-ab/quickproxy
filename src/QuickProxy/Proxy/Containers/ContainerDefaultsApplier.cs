namespace QuickProxy.Proxy.Containers;

public sealed class ContainerDefaultsApplier(
    IContainerInventory inventory,
    IContainerRuntimeClient runtimeClient,
    IContainerDefaultsStore defaultsStore,
    IInternalDnsService internalDnsService,
    ILogger<ContainerDefaultsApplier> logger) : IContainerDefaultsApplier
{
    private const string TriggerLabelKey = "quickproxy.defaults";
    private const string MarkerLabelKey = ContainerDefaultsMarker.LabelKey;
    private const string DnsMarkerLabelKey = "quickproxy.internal.dns-applied";
    private const string DnsServerLabelKey = "quickproxy.internal.defaults-dns-server";

    public bool ApplyToRequest(ContainerEditRequest request)
    {
        var defaultsSet = ResolveDefaultsSet(request.Labels, null, out _);
        if (defaultsSet is null) return false;

        return ApplyDefaultsToRequest(request, defaultsSet);
    }

    public async Task<int> ApplyForDefaultsSetAsync(string defaultsSetId, CancellationToken cancellationToken)
    {
        var normalizedId = Normalize(defaultsSetId);
        if (string.IsNullOrWhiteSpace(normalizedId)) return 0;

        var containers = inventory.ListContainers()
            .Where(container => container.ContainerLabels.TryGetValue(TriggerLabelKey, out var rawSetId)
                                && string.Equals(Normalize(rawSetId), normalizedId, StringComparison.OrdinalIgnoreCase))
            .Select(container => container.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var appliedCount = 0;
        foreach (var containerName in containers)
        {
            var result = await ApplyForStartAsync(containerName, false, cancellationToken);
            if (result.Applied) appliedCount += 1;
        }

        return appliedCount;
    }

    public async Task<ContainerDefaultsApplyResult> ApplyForStartAsync(string containerName, bool startAfterApply,
        CancellationToken cancellationToken)
    {
        var container = inventory.GetContainer(containerName);
        if (container is null) return new ContainerDefaultsApplyResult(false, false);

        var dnsStatus = internalDnsService.GetStatus();
        var defaultsSet = ResolveDefaultsSet(null, container.ContainerLabels, out var setId);
        var defaultsMarkerValue = defaultsSet is null
            ? null
            : ContainerDefaultsMarker.Build(setId, defaultsSet);
        var dnsMarkerValue = BuildDnsMarkerValue(dnsStatus);

        var defaultsMarkerMatches = defaultsMarkerValue is not null
                                    && container.ContainerLabels.TryGetValue(MarkerLabelKey,
                                        out var currentDefaultsMarker)
                                    && string.Equals(currentDefaultsMarker, defaultsMarkerValue,
                                        StringComparison.Ordinal);
        var dnsMarkerMatches = container.ContainerLabels.TryGetValue(DnsMarkerLabelKey, out var currentDnsMarker)
                               && string.Equals(currentDnsMarker, dnsMarkerValue, StringComparison.Ordinal);

        if ((defaultsSet is null || defaultsMarkerMatches) && dnsMarkerMatches)
            return new ContainerDefaultsApplyResult(false, false);

        var request = await runtimeClient.GetEditableContainerAsync(containerName, cancellationToken);
        var previousDnsServer = container.ContainerLabels.TryGetValue(DnsServerLabelKey, out var dnsServer)
            ? Normalize(dnsServer)
            : string.Empty;
        var changed = false;
        if (defaultsSet is not null)
        {
            changed |= ApplyDefaultsToRequest(request, defaultsSet);
            changed |= UpsertLabel(request.Labels, MarkerLabelKey, defaultsMarkerValue!);
        }

        changed |= ApplyInternalDns(request, dnsStatus, previousDnsServer);
        changed |= UpsertLabel(request.Labels, DnsMarkerLabelKey, dnsMarkerValue);

        if (!changed) return new ContainerDefaultsApplyResult(false, false);

        var wasRunning = string.Equals(container.State, "running", StringComparison.OrdinalIgnoreCase);
        await runtimeClient.UpdateContainerAsync(containerName, request, null, cancellationToken);

        if (!startAfterApply && !wasRunning)
        {
            await runtimeClient.StopContainerAsync(containerName, cancellationToken);
            return new ContainerDefaultsApplyResult(true, false);
        }

        return new ContainerDefaultsApplyResult(true, !wasRunning);
    }

    private ContainerDefaultsSet? ResolveDefaultsSet(
        IReadOnlyList<ContainerKeyValuePair>? requestLabels,
        IReadOnlyDictionary<string, string>? containerLabels,
        out string setId)
    {
        setId = string.Empty;
        var rawSetId = requestLabels?
            .FirstOrDefault(x => string.Equals(x.Key?.Trim(), TriggerLabelKey, StringComparison.OrdinalIgnoreCase))
            ?.Value;

        if (string.IsNullOrWhiteSpace(rawSetId) && containerLabels is not null)
            containerLabels.TryGetValue(TriggerLabelKey, out rawSetId);

        setId = Normalize(rawSetId);
        if (string.IsNullOrWhiteSpace(setId)) return null;

        var defaultsSet = defaultsStore.Get(setId);
        if (defaultsSet is null)
            logger.LogWarning("Container defaults set '{DefaultsSetId}' was requested but does not exist.", setId);

        return defaultsSet;
    }

    private bool ApplyDefaultsToRequest(
        ContainerEditRequest request,
        ContainerDefaultsSet defaultsSet)
    {
        request.MountBindings ??= [];
        request.HostMappings ??= [];
        request.NetworkAliases ??= [];
        var changed = false;
        changed |= MergeMissing(request.Labels, defaultsSet.Labels);
        changed |= MergeMissing(request.EnvVars, defaultsSet.EnvVars);
        changed |= MergeMissingMountBindings(request.MountBindings, defaultsSet.MountBindings ?? []);
        changed |= MergeMissingHostMappings(request.HostMappings, defaultsSet.HostMappings ?? []);
        changed |= MergeMissingNetworkAliases(request.NetworkAliases, defaultsSet.NetworkAliases ?? []);
        return changed;
    }

    private bool ApplyInternalDns(ContainerEditRequest request, InternalDnsStatus dnsStatus, string? previousDnsServer)
    {
        request.InternalDnsServers ??= [];
        request.InternalDnsServersToRemove ??= [];
        var normalizedPreviousDnsServer = Normalize(previousDnsServer);
        if (!dnsStatus.Enabled || dnsStatus.Names.Count == 0)
        {
            if (!string.IsNullOrWhiteSpace(normalizedPreviousDnsServer))
            {
                request.InternalDnsServersToRemove.Add(normalizedPreviousDnsServer);
                logger.LogInformation(
                    "Removing previously injected internal DNS server {DnsServerIp} from container {ContainerName} because internal DNS is disabled or has no configured names.",
                    normalizedPreviousDnsServer,
                    request.Name);
                RemoveLabel(request.Labels, DnsServerLabelKey);
                return true;
            }

            return false;
        }

        if (!dnsStatus.CanInject || string.IsNullOrWhiteSpace(dnsStatus.AdvertisedDnsServerIp))
        {
            logger.LogWarning(
                "Skipping internal DNS injection for defaults-applied container because DNS is unavailable. Healthy={Healthy}, AdvertisedDnsServerIp={AdvertisedDnsServerIp}, UpstreamCount={UpstreamCount}.",
                dnsStatus.Healthy,
                dnsStatus.AdvertisedDnsServerIp ?? "<none>",
                dnsStatus.UpstreamServers.Count);
            if (!string.IsNullOrWhiteSpace(normalizedPreviousDnsServer))
            {
                request.InternalDnsServersToRemove.Add(normalizedPreviousDnsServer);
                logger.LogInformation(
                    "Removing previously injected internal DNS server {DnsServerIp} from container {ContainerName} because internal DNS is unavailable.",
                    normalizedPreviousDnsServer,
                    request.Name);
                return RemoveLabel(request.Labels, DnsServerLabelKey) || true;
            }

            return false;
        }

        var changed = false;
        if (!string.IsNullOrWhiteSpace(normalizedPreviousDnsServer)
            && !string.Equals(normalizedPreviousDnsServer, dnsStatus.AdvertisedDnsServerIp,
                StringComparison.OrdinalIgnoreCase))
        {
            request.InternalDnsServersToRemove.Add(normalizedPreviousDnsServer);
            logger.LogInformation(
                "Replacing previously injected internal DNS server {PreviousDnsServerIp} with {DnsServerIp} for container {ContainerName}.",
                normalizedPreviousDnsServer,
                dnsStatus.AdvertisedDnsServerIp,
                request.Name);
            changed = true;
        }

        if (!request.InternalDnsServers.Contains(dnsStatus.AdvertisedDnsServerIp, StringComparer.OrdinalIgnoreCase))
        {
            request.InternalDnsServers.Insert(0, dnsStatus.AdvertisedDnsServerIp);
            changed = true;
            logger.LogInformation(
                "Injecting internal DNS server {DnsServerIp} into container {ContainerName}.",
                dnsStatus.AdvertisedDnsServerIp,
                request.Name);
        }

        changed |= UpsertLabel(request.Labels, DnsServerLabelKey, dnsStatus.AdvertisedDnsServerIp);
        return changed;
    }

    private static bool MergeMissing(List<ContainerKeyValuePair> target, IEnumerable<ContainerKeyValuePair> defaults)
    {
        var changed = false;
        var existingKeys = new HashSet<string>(
            target.Where(x => !string.IsNullOrWhiteSpace(x.Key)).Select(x => x.Key.Trim()),
            StringComparer.OrdinalIgnoreCase);

        foreach (var pair in defaults)
        {
            var key = Normalize(pair.Key);
            if (string.IsNullOrWhiteSpace(key)) continue;

            if (existingKeys.Contains(key)) continue;

            target.Add(new ContainerKeyValuePair
            {
                Key = key,
                Value = pair.Value ?? string.Empty
            });
            existingKeys.Add(key);
            changed = true;
        }

        return changed;
    }

    private static bool UpsertLabel(List<ContainerKeyValuePair> labels, string key, string value)
    {
        var existing =
            labels.FirstOrDefault(x => string.Equals(x.Key?.Trim(), key, StringComparison.OrdinalIgnoreCase));
        if (existing is null)
        {
            labels.Add(new ContainerKeyValuePair
            {
                Key = key,
                Value = value
            });
            return true;
        }

        if (string.Equals(existing.Value, value, StringComparison.Ordinal)) return false;

        existing.Value = value;
        return true;
    }

    private static bool RemoveLabel(List<ContainerKeyValuePair> labels, string key)
    {
        var removed = labels.RemoveAll(x => string.Equals(x.Key?.Trim(), key, StringComparison.OrdinalIgnoreCase));
        return removed > 0;
    }

    private static bool MergeMissingMountBindings(
        List<ContainerMountBindingRequest> target,
        IEnumerable<ContainerMountBindingRequest> defaults)
    {
        var changed = false;
        var existingContainerPaths = new HashSet<string>(
            target
                .Where(x => !string.IsNullOrWhiteSpace(x.ContainerPath))
                .Select(x => x.ContainerPath.Trim()),
            StringComparer.OrdinalIgnoreCase);

        foreach (var binding in defaults)
        {
            var hostPath = Normalize(binding.HostPath);
            var containerPath = Normalize(binding.ContainerPath);
            if (string.IsNullOrWhiteSpace(hostPath) || string.IsNullOrWhiteSpace(containerPath)) continue;

            if (existingContainerPaths.Contains(containerPath)) continue;

            target.Add(new ContainerMountBindingRequest
            {
                HostPath = hostPath,
                ContainerPath = containerPath,
                ReadOnly = binding.ReadOnly
            });
            existingContainerPaths.Add(containerPath);
            changed = true;
        }

        return changed;
    }

    private static bool MergeMissingNetworkAliases(
        List<ContainerNetworkAliasRequest> target,
        IEnumerable<ContainerNetworkAliasRequest> defaults)
    {
        var changed = false;
        var existing = new HashSet<string>(
            target
                .Where(x => !string.IsNullOrWhiteSpace(x.Network) && !string.IsNullOrWhiteSpace(x.Alias))
                .Select(x => $"{x.Network.Trim()}\u001f{x.Alias.Trim()}"),
            StringComparer.OrdinalIgnoreCase);

        foreach (var alias in defaults)
        {
            var network = Normalize(alias.Network);
            var value = Normalize(alias.Alias);
            if (string.IsNullOrWhiteSpace(network) || string.IsNullOrWhiteSpace(value)) continue;

            var dedupeKey = $"{network}\u001f{value}";
            if (existing.Contains(dedupeKey)) continue;

            target.Add(new ContainerNetworkAliasRequest
            {
                Network = network,
                Alias = value
            });
            existing.Add(dedupeKey);
            changed = true;
        }

        return changed;
    }

    private static bool MergeMissingHostMappings(
        List<ContainerHostMappingRequest> target,
        IEnumerable<ContainerHostMappingRequest> defaults)
    {
        var changed = false;
        var existing = new HashSet<string>(
            target
                .Where(x => !string.IsNullOrWhiteSpace(x.Hostname))
                .Select(x => x.Hostname.Trim()),
            StringComparer.OrdinalIgnoreCase);

        foreach (var mapping in defaults)
        {
            var hostname = Normalize(mapping.Hostname);
            var address = Normalize(mapping.Address);
            if (string.IsNullOrWhiteSpace(hostname) || string.IsNullOrWhiteSpace(address)) continue;

            if (existing.Contains(hostname)) continue;

            target.Add(new ContainerHostMappingRequest
            {
                Hostname = hostname,
                Address = address
            });
            existing.Add(hostname);
            changed = true;
        }

        return changed;
    }

    private static string BuildDnsMarkerValue(InternalDnsStatus dnsStatus)
    {
        return dnsStatus.BuildFingerprint();
    }

    private static string Normalize(string? value)
    {
        return (value ?? string.Empty).Trim();
    }
}