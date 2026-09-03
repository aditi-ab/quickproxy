using System.Text.RegularExpressions;
using QuickProxy.Proxy.Runtime;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace QuickProxy.Proxy.Containers;

public sealed class ComposeProjectDefaultsMerger(
    IContainerDefaultsStore defaultsStore,
    IHostTemplateValueProvider templateValueProvider,
    ILogger<ComposeProjectDefaultsMerger> logger)
{
    private const string TriggerLabelKey = "quickproxy.defaults";

    private static readonly Regex LabelTemplateRegex =
        new(@"\{label\.([a-z0-9_.-]+)\}", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public ComposeProject ApplyDefaults(ComposeProject project)
    {
        if (string.IsNullOrWhiteSpace(project.ComposeYaml)) return ComposeProjectStorageHelper.Clone(project);

        using var reader = new StringReader(project.ComposeYaml);
        var parser = new MergingParser(new Parser(reader));
        var yaml = new YamlStream();
        yaml.Load(parser);
        if (yaml.Documents.Count == 0 || yaml.Documents[0].RootNode is not YamlMappingNode root)
            return ComposeProjectStorageHelper.Clone(project);

        var services = GetMappingChild(root, "services");
        if (services.Children.Count == 0) return ComposeProjectStorageHelper.Clone(project);

        var topLevelNetworks = GetMappingChild(root, "networks");
        foreach (var entry in services.Children.ToArray())
        {
            if (entry.Key is not YamlScalarNode serviceNameNode ||
                string.IsNullOrWhiteSpace(serviceNameNode.Value)) continue;

            if (entry.Value is not YamlMappingNode serviceNode) continue;

            var defaultsSetId = ResolveDefaultsSetId(serviceNode);
            if (string.IsNullOrWhiteSpace(defaultsSetId)) continue;

            var defaultsSet = defaultsStore.Get(defaultsSetId);
            if (defaultsSet is null)
            {
                logger.LogWarning(
                    "Compose service {ServiceName} in project {ProjectId} requested defaults set {DefaultsSetId}, but it was not found.",
                    serviceNameNode.Value,
                    project.Id,
                    defaultsSetId);
                continue;
            }

            ApplyLabels(serviceNode, defaultsSet.Labels);
            ApplyMarkerLabel(serviceNode, defaultsSetId, defaultsSet);
            var serviceLabels = ReadServiceLabels(serviceNode);
            ApplyEnvironment(serviceNode, defaultsSet.EnvVars, serviceLabels);
            ApplyVolumes(serviceNode, defaultsSet.MountBindings ?? [], serviceLabels);
            ApplyExtraHosts(serviceNode, defaultsSet.HostMappings ?? [], serviceLabels);
            ApplyNetworkAliases(project, serviceNameNode.Value, serviceNode, topLevelNetworks,
                defaultsSet.NetworkAliases ?? [], serviceLabels);
        }

        using var writer = new StringWriter();
        yaml.Save(writer, false);

        var updated = ComposeProjectStorageHelper.Clone(project);
        updated.ComposeYaml = writer.ToString().Trim();
        return updated;
    }

    private static string ResolveDefaultsSetId(YamlMappingNode serviceNode)
    {
        var labelsNode = GetChild(serviceNode, "labels");
        if (labelsNode is null) return string.Empty;

        return labelsNode switch
        {
            YamlMappingNode mapping => mapping.Children
                .Where(x => x.Key is YamlScalarNode)
                .Select(x => new
                {
                    Key = ((YamlScalarNode)x.Key).Value,
                    Value = GetScalarValue(x.Value)
                })
                .FirstOrDefault(x => string.Equals(x.Key?.Trim(), TriggerLabelKey, StringComparison.OrdinalIgnoreCase))
                ?.Value?.Trim() ?? string.Empty,
            YamlSequenceNode sequence => sequence.Children
                .OfType<YamlScalarNode>()
                .Select(x => x.Value ?? string.Empty)
                .Select(x => ParseKeyValue(x))
                .FirstOrDefault(x => string.Equals(x.Key, TriggerLabelKey, StringComparison.OrdinalIgnoreCase))
                .Value,
            _ => string.Empty
        };
    }

    private static void ApplyLabels(YamlMappingNode serviceNode, IEnumerable<ContainerKeyValuePair> defaults)
    {
        var pairs = defaults
            .Where(x => !string.IsNullOrWhiteSpace(x.Key))
            .Select(x => new KeyValuePair<string, string>(x.Key.Trim(), x.Value ?? string.Empty))
            .ToArray();
        if (pairs.Length == 0) return;

        var labelsNode = GetChild(serviceNode, "labels");
        switch (labelsNode)
        {
            case null:
                var mapping = new YamlMappingNode();
                foreach (var pair in pairs) mapping.Add(new YamlScalarNode(pair.Key), new YamlScalarNode(pair.Value));

                serviceNode.Children[new YamlScalarNode("labels")] = mapping;
                break;
            case YamlMappingNode labelsMapping:
                var existingKeys = new HashSet<string>(
                    labelsMapping.Children.Keys
                        .OfType<YamlScalarNode>()
                        .Select(x => x.Value ?? string.Empty)
                        .Where(x => !string.IsNullOrWhiteSpace(x)),
                    StringComparer.OrdinalIgnoreCase);
                foreach (var pair in pairs)
                {
                    if (existingKeys.Contains(pair.Key)) continue;

                    labelsMapping.Add(new YamlScalarNode(pair.Key), new YamlScalarNode(pair.Value));
                    existingKeys.Add(pair.Key);
                }

                break;
            case YamlSequenceNode labelsSequence:
                var existingEntries = new HashSet<string>(
                    labelsSequence.Children
                        .OfType<YamlScalarNode>()
                        .Select(x => ParseKeyValue(x.Value ?? string.Empty).Key)
                        .Where(x => !string.IsNullOrWhiteSpace(x)),
                    StringComparer.OrdinalIgnoreCase);
                foreach (var pair in pairs)
                {
                    if (existingEntries.Contains(pair.Key)) continue;

                    labelsSequence.Add(new YamlScalarNode($"{pair.Key}={pair.Value}"));
                    existingEntries.Add(pair.Key);
                }

                break;
        }
    }

    private static void ApplyMarkerLabel(YamlMappingNode serviceNode, string defaultsSetId,
        ContainerDefaultsSet defaultsSet)
    {
        ApplyLabels(serviceNode,
        [
            new ContainerKeyValuePair
            {
                Key = ContainerDefaultsMarker.LabelKey,
                Value = ContainerDefaultsMarker.Build(defaultsSetId, defaultsSet)
            }
        ]);
    }

    private void ApplyEnvironment(
        YamlMappingNode serviceNode,
        IEnumerable<ContainerKeyValuePair> defaults,
        IReadOnlyDictionary<string, string> labels)
    {
        var pairs = defaults
            .Where(x => !string.IsNullOrWhiteSpace(x.Key))
            .Select(x => new KeyValuePair<string, string>(x.Key.Trim(), ReplaceRequestPlaceholders(x.Value, labels)))
            .ToArray();
        if (pairs.Length == 0) return;

        var environmentNode = GetChild(serviceNode, "environment");
        switch (environmentNode)
        {
            case null:
                var mapping = new YamlMappingNode();
                foreach (var pair in pairs) mapping.Add(new YamlScalarNode(pair.Key), new YamlScalarNode(pair.Value));

                serviceNode.Children[new YamlScalarNode("environment")] = mapping;
                break;
            case YamlMappingNode environmentMapping:
                var existingKeys = new HashSet<string>(
                    environmentMapping.Children.Keys
                        .OfType<YamlScalarNode>()
                        .Select(x => x.Value ?? string.Empty)
                        .Where(x => !string.IsNullOrWhiteSpace(x)),
                    StringComparer.OrdinalIgnoreCase);
                foreach (var pair in pairs)
                {
                    if (existingKeys.Contains(pair.Key)) continue;

                    environmentMapping.Add(new YamlScalarNode(pair.Key), new YamlScalarNode(pair.Value));
                    existingKeys.Add(pair.Key);
                }

                break;
            case YamlSequenceNode environmentSequence:
                var existingEntries = new HashSet<string>(
                    environmentSequence.Children
                        .OfType<YamlScalarNode>()
                        .Select(x => ParseKeyValue(x.Value ?? string.Empty).Key)
                        .Where(x => !string.IsNullOrWhiteSpace(x)),
                    StringComparer.OrdinalIgnoreCase);
                foreach (var pair in pairs)
                {
                    if (existingEntries.Contains(pair.Key)) continue;

                    environmentSequence.Add(new YamlScalarNode($"{pair.Key}={pair.Value}"));
                    existingEntries.Add(pair.Key);
                }

                break;
        }
    }

    private void ApplyVolumes(
        YamlMappingNode serviceNode,
        IEnumerable<ContainerMountBindingRequest> defaults,
        IReadOnlyDictionary<string, string> labels)
    {
        var bindings = defaults
            .Where(x => !string.IsNullOrWhiteSpace(x.HostPath) && !string.IsNullOrWhiteSpace(x.ContainerPath))
            .Select(x => new
            {
                HostPath = ReplaceRequestPlaceholders(x.HostPath, labels),
                ContainerPath = ReplaceRequestPlaceholders(x.ContainerPath, labels),
                x.ReadOnly
            })
            .Where(x => !string.IsNullOrWhiteSpace(x.HostPath) && !string.IsNullOrWhiteSpace(x.ContainerPath))
            .ToArray();
        if (bindings.Length == 0) return;

        var volumesNode = GetChild(serviceNode, "volumes");
        var volumes = volumesNode as YamlSequenceNode;
        if (volumesNode is null)
        {
            volumes = new YamlSequenceNode();
            serviceNode.Children[new YamlScalarNode("volumes")] = volumes;
        }

        if (volumes is null) return;

        var existingContainerPaths = new HashSet<string>(
            volumes.Children
                .Select(ResolveVolumeTarget)
                .Where(x => !string.IsNullOrWhiteSpace(x)),
            StringComparer.OrdinalIgnoreCase);

        foreach (var binding in bindings)
        {
            if (existingContainerPaths.Contains(binding.ContainerPath)) continue;

            var mode = binding.ReadOnly ? ":ro" : string.Empty;
            volumes.Add(new YamlScalarNode($"{binding.HostPath}:{binding.ContainerPath}{mode}"));
            existingContainerPaths.Add(binding.ContainerPath);
        }
    }

    private void ApplyExtraHosts(
        YamlMappingNode serviceNode,
        IEnumerable<ContainerHostMappingRequest> defaults,
        IReadOnlyDictionary<string, string> labels)
    {
        var mappings = defaults
            .Where(x => !string.IsNullOrWhiteSpace(x.Hostname) && !string.IsNullOrWhiteSpace(x.Address))
            .Select(x => new KeyValuePair<string, string>(
                ReplaceRequestPlaceholders(x.Hostname, labels),
                ReplaceRequestPlaceholders(x.Address, labels)))
            .Where(x => !string.IsNullOrWhiteSpace(x.Key) && !string.IsNullOrWhiteSpace(x.Value))
            .ToArray();
        if (mappings.Length == 0) return;

        var extraHostsNode = GetChild(serviceNode, "extra_hosts");
        switch (extraHostsNode)
        {
            case null:
                var mapping = new YamlMappingNode();
                foreach (var entry in mappings)
                    mapping.Add(new YamlScalarNode(entry.Key), new YamlScalarNode(entry.Value));

                serviceNode.Children[new YamlScalarNode("extra_hosts")] = mapping;
                break;
            case YamlMappingNode extraHostsMapping:
                var existingHostnames = new HashSet<string>(
                    extraHostsMapping.Children.Keys
                        .OfType<YamlScalarNode>()
                        .Select(x => x.Value ?? string.Empty)
                        .Where(x => !string.IsNullOrWhiteSpace(x)),
                    StringComparer.OrdinalIgnoreCase);
                foreach (var entry in mappings)
                {
                    if (existingHostnames.Contains(entry.Key)) continue;

                    extraHostsMapping.Add(new YamlScalarNode(entry.Key), new YamlScalarNode(entry.Value));
                    existingHostnames.Add(entry.Key);
                }

                break;
            case YamlSequenceNode extraHostsSequence:
                var existingEntries = new HashSet<string>(
                    extraHostsSequence.Children
                        .Select(ResolveExtraHostName)
                        .Where(x => !string.IsNullOrWhiteSpace(x)),
                    StringComparer.OrdinalIgnoreCase);
                foreach (var entry in mappings)
                {
                    if (existingEntries.Contains(entry.Key)) continue;

                    extraHostsSequence.Add(new YamlScalarNode($"{entry.Key}:{entry.Value}"));
                    existingEntries.Add(entry.Key);
                }

                break;
        }
    }

    private void ApplyNetworkAliases(
        ComposeProject project,
        string serviceName,
        YamlMappingNode serviceNode,
        YamlMappingNode topLevelNetworks,
        IEnumerable<ContainerNetworkAliasRequest> defaults,
        IReadOnlyDictionary<string, string> labels)
    {
        var aliases = defaults
            .Where(x => !string.IsNullOrWhiteSpace(x.Network) && !string.IsNullOrWhiteSpace(x.Alias))
            .Select(x => new
            {
                Network = ReplaceRequestPlaceholders(x.Network, labels),
                Alias = ReplaceRequestPlaceholders(x.Alias, labels)
            })
            .Where(x => !string.IsNullOrWhiteSpace(x.Network) && !string.IsNullOrWhiteSpace(x.Alias))
            .ToArray();
        if (aliases.Length == 0) return;

        var serviceNetworks = EnsureServiceNetworksMapping(serviceNode);
        if (serviceNetworks is null) return;

        foreach (var entry in aliases)
        {
            var hasServiceNetwork = TryGetChild(serviceNetworks, entry.Network, out var existingNetworkNode);
            var networkNode = existingNetworkNode as YamlMappingNode;
            if (!hasServiceNetwork)
            {
                var canAttachNetwork = string.Equals(entry.Network, "default", StringComparison.OrdinalIgnoreCase)
                                       || topLevelNetworks.Children.Keys.OfType<YamlScalarNode>().Any(x =>
                                           string.Equals(x.Value, entry.Network, StringComparison.OrdinalIgnoreCase));
                if (!canAttachNetwork)
                {
                    logger.LogInformation(
                        "Skipping defaults network alias {Alias} for compose service {ServiceName} in project {ProjectId} because network {Network} is not defined in the compose project.",
                        entry.Alias,
                        serviceName,
                        project.Id,
                        entry.Network);
                    continue;
                }

                networkNode = new YamlMappingNode();
                serviceNetworks.Children[new YamlScalarNode(entry.Network)] = networkNode;
            }
            else if (networkNode is null)
            {
                networkNode = new YamlMappingNode();
                serviceNetworks.Children[new YamlScalarNode(entry.Network)] = networkNode;
            }

            var aliasesNode = GetChild(networkNode, "aliases") as YamlSequenceNode;
            if (aliasesNode is null)
            {
                aliasesNode = new YamlSequenceNode();
                networkNode.Children[new YamlScalarNode("aliases")] = aliasesNode;
            }

            var existingAliases = new HashSet<string>(
                aliasesNode.Children.OfType<YamlScalarNode>()
                    .Select(x => x.Value ?? string.Empty)
                    .Where(x => !string.IsNullOrWhiteSpace(x)),
                StringComparer.OrdinalIgnoreCase);
            if (existingAliases.Contains(entry.Alias)) continue;

            aliasesNode.Add(new YamlScalarNode(entry.Alias));
        }
    }

    private static YamlMappingNode? EnsureServiceNetworksMapping(YamlMappingNode serviceNode)
    {
        var networksNode = GetChild(serviceNode, "networks");
        switch (networksNode)
        {
            case null:
                var emptyMapping = new YamlMappingNode();
                serviceNode.Children[new YamlScalarNode("networks")] = emptyMapping;
                return emptyMapping;
            case YamlMappingNode networksMapping:
                return networksMapping;
            case YamlSequenceNode networksSequence:
                var rewritten = new YamlMappingNode();
                foreach (var child in networksSequence.Children)
                    if (child is YamlScalarNode scalar && !string.IsNullOrWhiteSpace(scalar.Value))
                        rewritten.Children[new YamlScalarNode(scalar.Value)] = new YamlMappingNode();

                serviceNode.Children[new YamlScalarNode("networks")] = rewritten;
                return rewritten;
            default:
                return null;
        }
    }

    private static string ResolveVolumeTarget(YamlNode node)
    {
        if (node is YamlMappingNode mapping) return GetScalarValue(GetChild(mapping, "target")).Trim();

        if (node is not YamlScalarNode scalar || string.IsNullOrWhiteSpace(scalar.Value)) return string.Empty;

        var raw = scalar.Value.Trim();
        var parts = raw.Split(':');
        if (parts.Length == 1) return raw;

        if (LooksLikeVolumeMode(parts[^1])) return parts.Length >= 2 ? parts[^2].Trim() : string.Empty;

        return parts[^1].Trim();
    }

    private static bool LooksLikeVolumeMode(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;

        foreach (var token in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (token is "ro" or "rw" or "z" or "Z" or "delegated" or "cached" or "consistent" or "nocopy") continue;

            return false;
        }

        return true;
    }

    private static string ResolveExtraHostName(YamlNode node)
    {
        return node switch
        {
            YamlScalarNode scalar => ParseKeyValue(scalar.Value ?? string.Empty, [':', '=']).Key,
            YamlMappingNode mapping => mapping.Children.Keys.OfType<YamlScalarNode>()
                .Select(x => x.Value ?? string.Empty).FirstOrDefault() ?? string.Empty,
            _ => string.Empty
        };
    }

    private static (string Key, string Value) ParseKeyValue(string value, char[]? separators = null)
    {
        var text = (value ?? string.Empty).Trim();
        if (text.Length == 0) return (string.Empty, string.Empty);

        separators ??= ['='];
        var index = text.IndexOfAny(separators);
        if (index < 0) return (text, string.Empty);

        return (text[..index].Trim(), text[(index + 1)..].Trim());
    }

    private static YamlNode? GetChild(YamlMappingNode mapping, string key)
    {
        return mapping.Children.FirstOrDefault(x =>
                x.Key is YamlScalarNode scalar && string.Equals(scalar.Value, key, StringComparison.OrdinalIgnoreCase))
            .Value;
    }

    private static bool TryGetChild(YamlMappingNode mapping, string key, out YamlNode? value)
    {
        foreach (var entry in mapping.Children)
            if (entry.Key is YamlScalarNode scalar &&
                string.Equals(scalar.Value, key, StringComparison.OrdinalIgnoreCase))
            {
                value = entry.Value;
                return true;
            }

        value = null;
        return false;
    }

    private static YamlMappingNode GetMappingChild(YamlMappingNode mapping, string key)
    {
        return GetChild(mapping, key) as YamlMappingNode ?? new YamlMappingNode();
    }

    private static string GetScalarValue(YamlNode? node)
    {
        return node is YamlScalarNode scalar
            ? scalar.Value ?? string.Empty
            : string.Empty;
    }

    private IReadOnlyDictionary<string, string> ReadServiceLabels(YamlMappingNode serviceNode)
    {
        var labelsNode = GetChild(serviceNode, "labels");
        var labels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        switch (labelsNode)
        {
            case YamlMappingNode mapping:
                foreach (var entry in mapping.Children)
                {
                    if (entry.Key is not YamlScalarNode keyNode || string.IsNullOrWhiteSpace(keyNode.Value)) continue;

                    labels[keyNode.Value.Trim()] =
                        templateValueProvider.ReplacePlaceholders(GetScalarValue(entry.Value));
                }

                break;
            case YamlSequenceNode sequence:
                foreach (var entry in sequence.Children.OfType<YamlScalarNode>())
                {
                    var parsed = ParseKeyValue(entry.Value ?? string.Empty);
                    if (string.IsNullOrWhiteSpace(parsed.Key)) continue;

                    labels[parsed.Key] = templateValueProvider.ReplacePlaceholders(parsed.Value);
                }

                break;
        }

        return labels;
    }

    private string ReplaceRequestPlaceholders(string? input, IReadOnlyDictionary<string, string> labels)
    {
        var replaced = templateValueProvider.ReplacePlaceholders(input ?? string.Empty);
        if (string.IsNullOrWhiteSpace(replaced) || labels.Count == 0) return replaced;

        return LabelTemplateRegex.Replace(replaced, match =>
        {
            var key = match.Groups[1].Value;
            return labels.TryGetValue(key, out var value)
                ? value ?? string.Empty
                : match.Value;
        });
    }
}