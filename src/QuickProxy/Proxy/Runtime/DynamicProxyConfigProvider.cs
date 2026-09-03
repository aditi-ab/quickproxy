using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Primitives;
using QuickProxy.Proxy.Containers;
using QuickProxy.Proxy.Models;
using QuickProxy.Proxy.Storage;
using QuickProxy.Proxy.Validation;
using Yarp.ReverseProxy.Configuration;

namespace QuickProxy.Proxy.Runtime;

public sealed class DynamicProxyConfigProvider : IProxyConfigProvider, IProxyHostRuntime
{
    private const string ContainerNamePlaceholder = "{container.name}";
    private const string LegacyContainerNamePlaceholder = "{containername}";

    private static readonly Regex LabelPlaceholderRegex = new(@"\{label\.([^{}]+)\}",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private readonly IContainerInventory _containerInventory;
    private readonly IHostTemplateValueProvider _hostTemplateValueProvider;
    private readonly IIssuedCertificateService _issuedCertificateService;

    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly ILogger<DynamicProxyConfigProvider> _logger;

    private readonly IProxyHostRepository _repository;
    private readonly object _sync = new();
    private volatile ProxyConfigSnapshot _snapshot = ProxyConfigSnapshot.Empty;

    public DynamicProxyConfigProvider(
        IProxyHostRepository repository,
        IContainerInventory containerInventory,
        IHostTemplateValueProvider hostTemplateValueProvider,
        IIssuedCertificateService issuedCertificateService,
        ILogger<DynamicProxyConfigProvider> logger)
    {
        _repository = repository;
        _containerInventory = containerInventory;
        _hostTemplateValueProvider = hostTemplateValueProvider;
        _issuedCertificateService = issuedCertificateService;
        _logger = logger;
        _jsonOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
    }

    public IProxyConfig GetConfig()
    {
        return _snapshot;
    }

    public IReadOnlyList<ProxyHostConfig> GetHosts()
    {
        return _snapshot.EffectiveHostsById.Values.OrderBy(x => x.Id, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public IReadOnlyList<ProxyHostConfig> GetStoredHosts()
    {
        return _snapshot.StoredHostsById.Values.OrderBy(x => x.Id, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public IReadOnlyList<AdminProxyHostDto> GetAdminHosts()
    {
        return _snapshot.AdminHostsById.Values.OrderBy(x => x.Id, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public AdminProxyHostDto? GetAdminHost(string id)
    {
        _snapshot.AdminHostsById.TryGetValue(id, out var host);
        return host;
    }

    public ProxyHostConfig? GetHost(string id)
    {
        _snapshot.EffectiveHostsById.TryGetValue(id, out var host);
        return host;
    }

    public ProxyHostConfig? MatchHost(string? hostHeader)
    {
        var normalized = NormalizeHost(hostHeader);
        if (string.IsNullOrWhiteSpace(normalized)) return null;

        if (_snapshot.DomainToHostId.TryGetValue(normalized, out var hostId) &&
            _snapshot.EffectiveHostsById.TryGetValue(hostId, out var host))
            return host;

        var hostOnly = StripPort(normalized);
        if (!string.Equals(hostOnly, normalized, StringComparison.OrdinalIgnoreCase) &&
            _snapshot.DomainToHostId.TryGetValue(hostOnly, out hostId) &&
            _snapshot.EffectiveHostsById.TryGetValue(hostId, out host))
            return host;

        return null;
    }

    public ProxyRouteConfig? MatchRoute(ProxyHostConfig host, string path)
    {
        return host.Routes
            .Where(x => path.StartsWith(x.Path, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(x => x.Path.Length)
            .FirstOrDefault();
    }

    public bool TryReload()
    {
        lock (_sync)
        {
            var previous = _snapshot;
            var previousGeneratedHostIds = GetGeneratedHostIds(previous);
            var previousStoredHosts =
                new Dictionary<string, ProxyHostConfig>(previous.StoredHostsById, StringComparer.OrdinalIgnoreCase);
            var nextStoredHosts =
                new Dictionary<string, ProxyHostConfig>(previousStoredHosts, StringComparer.OrdinalIgnoreCase);
            var records = _repository.ReadAll();
            var seenHostIds = records.Select(x => x.HostId).ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var removedHostId in previousStoredHosts.Keys
                         .Except(seenHostIds, StringComparer.OrdinalIgnoreCase)
                         .ToArray()) nextStoredHosts.Remove(removedHostId);

            foreach (var record in records.OrderBy(x => x.HostId, StringComparer.OrdinalIgnoreCase))
            {
                if (!TryParseAndValidate(record, out var parsedHost, out var errors))
                {
                    if (previousStoredHosts.ContainsKey(record.HostId))
                    {
                        _logger.LogWarning(
                            "Failed to reload host '{HostId}' from '{Path}', keeping previous config: {Errors}",
                            record.HostId,
                            record.StorageLocation,
                            string.Join("; ", errors));
                    }
                    else
                    {
                        _logger.LogWarning(
                            "Failed to load host '{HostId}' from '{Path}': {Errors}",
                            record.HostId,
                            record.StorageLocation,
                            string.Join("; ", errors));
                        nextStoredHosts.Remove(record.HostId);
                    }

                    continue;
                }

                nextStoredHosts[record.HostId] = parsedHost!;
                var duplicateErrors = ProxyHostValidator.ValidateAcrossHosts(nextStoredHosts.Values);
                if (duplicateErrors.Count > 0)
                {
                    _logger.LogWarning(
                        "Failed to apply host '{HostId}' because of conflicts: {Errors}",
                        record.HostId,
                        string.Join("; ", duplicateErrors));

                    if (previousStoredHosts.TryGetValue(record.HostId, out var previousHost))
                        nextStoredHosts[record.HostId] = previousHost;
                    else
                        nextStoredHosts.Remove(record.HostId);
                }
            }

            var built = BuildSnapshot(nextStoredHosts, _containerInventory, _hostTemplateValueProvider,
                _issuedCertificateService, _logger);
            var currentGeneratedHostIds = GetGeneratedHostIds(built);
            foreach (var staleGeneratedHostId in previousGeneratedHostIds.Except(currentGeneratedHostIds,
                         StringComparer.OrdinalIgnoreCase))
                _issuedCertificateService.DeleteForHost(staleGeneratedHostId);

            _snapshot = built;
            previous.SignalChange();
            _logger.LogInformation("Loaded {Count} active proxy host(s) from {StoredCount} stored host(s).",
                built.EffectiveHostsById.Count, built.StoredHostsById.Count);
            return true;
        }
    }

    private bool TryParseAndValidate(
        StoredProxyHostRecord record,
        out ProxyHostConfig? host,
        out List<string> errors)
    {
        host = null;
        errors = [];

        try
        {
            host = JsonSerializer.Deserialize<ProxyHostConfig>(record.Json, _jsonOptions);
            if (host is null)
            {
                errors.Add("Unable to deserialize host config.");
                return false;
            }

            if (string.IsNullOrWhiteSpace(host.Id)) host.Id = record.HostId;

            var validation = ProxyHostValidator.ValidateSingle(host, record.HostId);
            if (!validation.IsValid)
            {
                errors.AddRange(validation.Errors);
                return false;
            }

            host.DomainNames = host.DomainNames.Select(NormalizeHost).Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList()!;
            host.AutomaticContainer.DomainTemplates = host.AutomaticContainer.DomainTemplates
                .Select(NormalizeHost)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList()!;
            host.AutomaticContainer.LabelSelectors = host.AutomaticContainer.LabelSelectors
                .Where(x => !string.IsNullOrWhiteSpace(x.Key))
                .Select(x => new AutomaticContainerLabelSelector
                {
                    Key = x.Key.Trim(),
                    ValuePattern = string.IsNullOrWhiteSpace(x.ValuePattern) ? null : x.ValuePattern.Trim(),
                    ValuePatterns = x.ValuePatterns
                        .Concat(string.IsNullOrWhiteSpace(x.ValuePattern) ? [] : [x.ValuePattern])
                        .Where(y => !string.IsNullOrWhiteSpace(y))
                        .Select(y => y.Trim())
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList()
                })
                .GroupBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
                .Select(x => x.First())
                .ToList();
            host.Routes = host.Routes
                .OrderByDescending(x => x.Path.Length)
                .ThenBy(x => x.Path, StringComparer.OrdinalIgnoreCase)
                .ToList();

            return true;
        }
        catch (Exception ex)
        {
            errors.Add(ex.Message);
            return false;
        }
    }

    private static ProxyConfigSnapshot BuildSnapshot(
        Dictionary<string, ProxyHostConfig> storedHostsById,
        IContainerInventory containerInventory,
        IHostTemplateValueProvider hostTemplateValueProvider,
        IIssuedCertificateService issuedCertificateService,
        ILogger logger)
    {
        var routes = new List<RouteConfig>();
        var clusters = new List<ClusterConfig>();
        var domainToHostId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var effectiveHostsById = new Dictionary<string, ProxyHostConfig>(StringComparer.OrdinalIgnoreCase);
        var adminHostsById = new Dictionary<string, AdminProxyHostDto>(StringComparer.OrdinalIgnoreCase);

        var generatedHostsByTemplateId = ExpandAutomaticHosts(storedHostsById.Values, containerInventory,
            hostTemplateValueProvider, logger);

        foreach (var storedHost in storedHostsById.Values.OrderBy(x => x.Id, StringComparer.OrdinalIgnoreCase))
        {
            var generated = generatedHostsByTemplateId.TryGetValue(storedHost.Id, out var matches)
                ? matches
                : [];

            adminHostsById[storedHost.Id] = ToAdminHostDto(
                storedHost,
                new ProxyHostRuntimeMetadata
                {
                    ReadOnly = false,
                    IsGenerated = false,
                    ActiveMatchCount = generated.Count
                });

            if (storedHost.Mode != ProxyHostMode.Manual) continue;

            if (string.IsNullOrWhiteSpace(storedHost.CertificateId) ||
                storedHost.CertificateId.StartsWith("issued-", StringComparison.OrdinalIgnoreCase))
                storedHost.CertificateId = issuedCertificateService.EnsureForHost(storedHost);

            if (!TryAddEffectiveHost(storedHost, effectiveHostsById, domainToHostId, logger)) continue;

            AddRuntimeRoutes(storedHost, routes, clusters, containerInventory, logger);
        }

        foreach (var generated in generatedHostsByTemplateId.Values.SelectMany(x => x)
                     .OrderBy(x => x.Host.Id, StringComparer.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(generated.Host.CertificateId) ||
                generated.Host.CertificateId.StartsWith("issued-", StringComparison.OrdinalIgnoreCase))
                generated.Host.CertificateId = issuedCertificateService.EnsureForHost(generated.Host);

            adminHostsById[generated.Host.Id] = ToAdminHostDto(generated.Host, generated.Runtime);

            if (!TryAddEffectiveHost(generated.Host, effectiveHostsById, domainToHostId, logger)) continue;

            AddRuntimeRoutes(generated.Host, routes, clusters, containerInventory, logger);
        }

        return new ProxyConfigSnapshot(routes, clusters, storedHostsById, effectiveHostsById, adminHostsById,
            domainToHostId);
    }

    private static Dictionary<string, List<GeneratedHostRecord>> ExpandAutomaticHosts(
        IEnumerable<ProxyHostConfig> storedHosts,
        IContainerInventory containerInventory,
        IHostTemplateValueProvider hostTemplateValueProvider,
        ILogger logger)
    {
        var containers = containerInventory.ListContainers()
            .Where(x => x.IsRunning)
            .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var generatedByTemplateId = new Dictionary<string, List<GeneratedHostRecord>>(StringComparer.OrdinalIgnoreCase);

        foreach (var template in storedHosts.Where(x => x.Enabled && x.Mode == ProxyHostMode.AutomaticContainer)
                     .OrderBy(x => x.Id, StringComparer.OrdinalIgnoreCase))
        {
            var generated = new List<GeneratedHostRecord>();
            foreach (var container in containers)
            {
                if (!IsMatch(template.AutomaticContainer, container, logger, template.Id)) continue;

                var host = CloneHost(template);
                host.Id = BuildGeneratedHostId(template.Id, container);
                host.Mode = ProxyHostMode.Manual;
                host.DomainNames = ResolveGeneratedDomains(
                    template.AutomaticContainer.DomainTemplates,
                    container,
                    hostTemplateValueProvider,
                    logger,
                    template.Id);
                if (host.DomainNames.Count == 0)
                {
                    logger.LogWarning(
                        "Automatic proxy host template '{TemplateId}' produced no valid domains for container '{ContainerName}'.",
                        template.Id, container.Name);
                    continue;
                }

                foreach (var route in host.Routes.Where(x => x.UpstreamMode == ProxyHostUpstreamMode.Container))
                    route.Container.ContainerName = container.Name;

                generated.Add(new GeneratedHostRecord(
                    host,
                    new ProxyHostRuntimeMetadata
                    {
                        ReadOnly = true,
                        IsGenerated = true,
                        SourceTemplateId = template.Id,
                        MatchedContainerId = container.Id,
                        MatchedContainerName = container.Name,
                        MatchedComposeService = container.Compose.Service
                    }));
            }

            generatedByTemplateId[template.Id] = generated;
        }

        return generatedByTemplateId;
    }

    private static bool IsMatch(AutomaticContainerProxyHostConfig config, ContainerInventoryItem container,
        ILogger logger, string templateId)
    {
        foreach (var selector in config.LabelSelectors)
        {
            if (!container.ContainerLabels.TryGetValue(selector.Key, out var value)) return false;

            var patterns = selector.ValuePatterns
                .Concat(string.IsNullOrWhiteSpace(selector.ValuePattern) ? [] : [selector.ValuePattern])
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (patterns.Length == 0) continue;

            try
            {
                if (!patterns.Any(pattern =>
                        Regex.IsMatch(value, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)))
                    return false;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex,
                    "Automatic proxy host template '{TemplateId}' has an invalid label selector regex for key '{LabelKey}'.",
                    templateId, selector.Key);
                return false;
            }
        }

        return true;
    }

    private static List<string> ResolveGeneratedDomains(
        IEnumerable<string> templates,
        ContainerInventoryItem container,
        IHostTemplateValueProvider templateValueProvider,
        ILogger logger,
        string templateId)
    {
        var domains = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var template in templates)
        {
            var candidateTemplate = template
                .Replace(ContainerNamePlaceholder, container.Name, StringComparison.OrdinalIgnoreCase)
                .Replace(LegacyContainerNamePlaceholder, container.Name, StringComparison.OrdinalIgnoreCase);
            var missingLabel = false;
            candidateTemplate = LabelPlaceholderRegex.Replace(candidateTemplate, match =>
            {
                var labelKey = match.Groups[1].Value.Trim();
                if (string.IsNullOrWhiteSpace(labelKey) ||
                    !container.ContainerLabels.TryGetValue(labelKey, out var labelValue) ||
                    string.IsNullOrWhiteSpace(labelValue))
                {
                    missingLabel = true;
                    return string.Empty;
                }

                return labelValue.Trim();
            });
            candidateTemplate = templateValueProvider.ReplacePlaceholders(candidateTemplate);

            if (missingLabel)
            {
                logger.LogWarning(
                    "Automatic proxy host template '{TemplateId}' references a missing or empty label in domain template '{Template}' for container '{ContainerName}'.",
                    templateId,
                    template,
                    container.Name);
                continue;
            }

            var candidate = NormalizeHost(candidateTemplate);
            if (string.IsNullOrWhiteSpace(candidate)) continue;

            if (!ProxyHostValidator.ValidateSingle(new ProxyHostConfig
                {
                    Id = "generated-host",
                    DomainNames = [candidate],
                    Routes =
                    [
                        new ProxyRouteConfig
                        {
                            Path = "/",
                            RewriteMode = ProxyRouteRewriteMode.Preserve,
                            UpstreamMode = ProxyHostUpstreamMode.Manual,
                            Upstream = new UpstreamTarget { Scheme = "http", Host = "127.0.0.1", Port = 80 }
                        }
                    ]
                }, "generated-host").IsValid)
            {
                logger.LogWarning(
                    "Automatic proxy host template '{TemplateId}' produced invalid domain '{Domain}' for container '{ContainerName}'.",
                    templateId, candidate, container.Name);
                continue;
            }

            domains.Add(candidate);
        }

        return domains.ToList();
    }

    private static bool TryAddEffectiveHost(
        ProxyHostConfig host,
        IDictionary<string, ProxyHostConfig> effectiveHostsById,
        IDictionary<string, string> domainToHostId,
        ILogger logger)
    {
        if (!host.Enabled) return false;

        foreach (var domain in host.DomainNames)
            if (domainToHostId.TryGetValue(domain, out var existingHostId))
            {
                logger.LogWarning(
                    "Skipping host '{HostId}' because domain '{Domain}' is already used by '{ExistingHostId}'.",
                    host.Id, domain, existingHostId);
                return false;
            }

        effectiveHostsById[host.Id] = host;
        foreach (var domain in host.DomainNames) domainToHostId[domain] = host.Id;

        return true;
    }

    private static void AddRuntimeRoutes(
        ProxyHostConfig host,
        List<RouteConfig> routes,
        List<ClusterConfig> clusters,
        IContainerInventory containerInventory,
        ILogger logger)
    {
        foreach (var route in host.Routes)
        {
            var routeClusterId = $"cluster:{host.Id}:{SanitizePath(route.Path)}";
            clusters.Add(BuildCluster(routeClusterId, ResolveUpstream(route, host, containerInventory, logger), route));
            routes.Add(new RouteConfig
            {
                RouteId = $"route:{host.Id}:{SanitizePath(route.Path)}",
                ClusterId = routeClusterId,
                Order = -route.Path.Length,
                Match = new RouteMatch
                {
                    Hosts = host.DomainNames,
                    Path = BuildPathPattern(route.Path)
                },
                Transforms = BuildTransforms(route)
            });
        }
    }

    private static AdminProxyHostDto ToAdminHostDto(ProxyHostConfig host, ProxyHostRuntimeMetadata runtime)
    {
        return new AdminProxyHostDto
        {
            Id = host.Id,
            Mode = host.Mode,
            Enabled = host.Enabled,
            DomainNames = [.. host.DomainNames],
            AutomaticContainer = CloneAutomaticContainer(host.AutomaticContainer),
            ForceSsl = host.ForceSsl,
            CacheAssets = host.CacheAssets,
            Websockets = host.Websockets,
            CertificateId = host.CertificateId,
            Routes = CloneRoutes(host.Routes),
            Tls = CloneTls(host.Tls),
            Runtime = runtime
        };
    }

    private static ClusterConfig BuildCluster(string clusterId, UpstreamTarget upstream, ProxyRouteConfig route)
    {
        var address = $"{upstream.Scheme}://{upstream.Host}:{upstream.Port}/";
        return new ClusterConfig
        {
            ClusterId = clusterId,
            HttpClient = route.IgnoreBadCertificates
                ? new HttpClientConfig
                {
                    DangerousAcceptAnyServerCertificate = true
                }
                : null,
            Destinations = new Dictionary<string, DestinationConfig>(StringComparer.OrdinalIgnoreCase)
            {
                ["destination"] = new()
                {
                    Address = address
                }
            }
        };
    }

    private static UpstreamTarget ResolveUpstream(ProxyRouteConfig route, ProxyHostConfig host,
        IContainerInventory containerInventory, ILogger logger)
    {
        if (route.UpstreamMode != ProxyHostUpstreamMode.Container) return route.Upstream;

        var container = containerInventory.GetContainer(route.Container.ContainerName);
        if (container is null || !container.IsRunning)
        {
            logger.LogWarning(
                "Container upstream '{ContainerName}' for host '{HostId}' route '{RoutePath}' is unavailable.",
                route.Container.ContainerName, host.Id, route.Path);
            return CreateUnavailableUpstream(route.Container.Scheme);
        }

        if (route.Container.PortResolutionMode == ContainerPortResolutionMode.Published)
        {
            var publishedBinding = container.Ports
                .SelectMany(x => x.PublishedBindings)
                .FirstOrDefault(x => x.HostPort == route.Container.Port);

            if (publishedBinding is null)
            {
                logger.LogWarning(
                    "Container upstream '{ContainerName}' for host '{HostId}' route '{RoutePath}' does not publish host port {Port}.",
                    route.Container.ContainerName, host.Id, route.Path, route.Container.Port);
                return CreateUnavailableUpstream(route.Container.Scheme);
            }

            return new UpstreamTarget
            {
                Scheme = route.Container.Scheme,
                Host = NormalizePublishedHost(publishedBinding.HostIp),
                Port = publishedBinding.HostPort
            };
        }

        var address = container.ResolveIpAddress(route.Container.NetworkName);
        if (string.IsNullOrWhiteSpace(address))
        {
            logger.LogWarning(
                "Container upstream '{ContainerName}' for host '{HostId}' route '{RoutePath}' has no routable IP.",
                route.Container.ContainerName, host.Id, route.Path);
            return CreateUnavailableUpstream(route.Container.Scheme);
        }

        var hasPort = container.Ports.Any(x => x.ContainerPort == route.Container.Port);
        if (!hasPort)
        {
            logger.LogWarning(
                "Container upstream '{ContainerName}' for host '{HostId}' route '{RoutePath}' does not expose port {Port}.",
                route.Container.ContainerName, host.Id, route.Path, route.Container.Port);
            return CreateUnavailableUpstream(route.Container.Scheme);
        }

        return new UpstreamTarget
        {
            Scheme = route.Container.Scheme,
            Host = address,
            Port = route.Container.Port
        };
    }

    private static IReadOnlyList<IReadOnlyDictionary<string, string>>? BuildTransforms(ProxyRouteConfig route)
    {
        var transforms = new List<IReadOnlyDictionary<string, string>>();

        transforms.Add(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["RequestHeaderOriginalHost"] = route.PreserveOriginalHostHeader ? "true" : "false"
        });

        if (route.SendForwardedHeaders)
            transforms.Add(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["X-Forwarded"] = "Set"
            });

        if (route.RewriteMode == ProxyRouteRewriteMode.Preserve) return transforms;

        if (route.RewriteMode == ProxyRouteRewriteMode.StripPrefix)
        {
            if (route.Path == "/") return transforms;

            transforms.Add(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["PathRemovePrefix"] = route.Path.TrimEnd('/')
            });
            return transforms;
        }

        if (route.RewriteMode == ProxyRouteRewriteMode.ReplacePrefix)
        {
            var sourcePath = NormalizeRoutePath(route.Path);
            var targetPath = NormalizeRewriteTargetPath(route.RewriteTargetPath);

            if (sourcePath == "/")
            {
                transforms.Add(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["PathPattern"] = $"{targetPath}/{{**catch-all}}"
                });
                return transforms;
            }

            transforms.Add(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["PathRemovePrefix"] = sourcePath.TrimEnd('/')
            });
            transforms.Add(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["PathPrefix"] = targetPath == "/" ? string.Empty : targetPath.TrimEnd('/')
            });
            return transforms;
        }

        transforms.Add(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["PathRemovePrefix"] = route.Path.TrimEnd('/')
        });
        return transforms;
    }

    private static string NormalizeRoutePath(string path)
    {
        var value = string.IsNullOrWhiteSpace(path) ? "/" : path.Trim();
        if (!value.StartsWith('/')) value = "/" + value;

        return value == "/" ? value : value.TrimEnd('/');
    }

    private static string NormalizeRewriteTargetPath(string? path)
    {
        var value = string.IsNullOrWhiteSpace(path) ? "/" : path.Trim();
        if (!value.StartsWith('/')) value = "/" + value;

        return value == "/" ? value : value.TrimEnd('/');
    }

    private static string NormalizePublishedHost(string? hostIp)
    {
        if (string.IsNullOrWhiteSpace(hostIp) ||
            hostIp == "0.0.0.0" ||
            hostIp == "::")
            return "127.0.0.1";

        return hostIp;
    }

    private static UpstreamTarget CreateUnavailableUpstream(string scheme)
    {
        return new UpstreamTarget
        {
            Scheme = string.Equals(scheme, "https", StringComparison.OrdinalIgnoreCase) ? "https" : "http",
            Host = "127.0.0.1",
            Port = 1
        };
    }

    private static string BuildPathPattern(string path)
    {
        var trimmed = path.TrimEnd('/');
        if (string.IsNullOrWhiteSpace(trimmed) || trimmed == "/") return "/{**catch-all}";

        return $"{trimmed}/{{**catch-all}}";
    }

    private static string SanitizePath(string path)
    {
        return path.Trim('/').Replace('/', '_');
    }

    private static string NormalizeHost(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;

        return input.Trim().ToLowerInvariant();
    }

    private static string StripPort(string host)
    {
        var colonIndex = host.LastIndexOf(':');
        if (colonIndex <= 0) return host;

        var portPart = host[(colonIndex + 1)..];
        return int.TryParse(portPart, out _) ? host[..colonIndex] : host;
    }

    private static string BuildGeneratedHostId(string templateId, ContainerInventoryItem container)
    {
        var containerPart = Regex.Replace(container.Name.ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');
        var idPart = container.Id.Length > 12 ? container.Id[..12] : container.Id;
        var combined = $"{templateId}-{containerPart}-{idPart}".Trim('-');
        return Regex.Replace(combined, "-{2,}", "-");
    }

    private static HashSet<string> GetGeneratedHostIds(ProxyConfigSnapshot snapshot)
    {
        return snapshot.AdminHostsById.Values
            .Where(x => x.Runtime.IsGenerated)
            .Select(x => x.Id)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static ProxyHostConfig CloneHost(ProxyHostConfig host)
    {
        return new ProxyHostConfig
        {
            Id = host.Id,
            Mode = host.Mode,
            Enabled = host.Enabled,
            DomainNames = [.. host.DomainNames],
            AutomaticContainer = CloneAutomaticContainer(host.AutomaticContainer),
            ForceSsl = host.ForceSsl,
            CacheAssets = host.CacheAssets,
            Websockets = host.Websockets,
            CertificateId = host.CertificateId,
            Routes = CloneRoutes(host.Routes),
            Tls = CloneTls(host.Tls)
        };
    }

    private static List<ProxyRouteConfig> CloneRoutes(IEnumerable<ProxyRouteConfig> routes)
    {
        return routes.Select(route => new ProxyRouteConfig
        {
            Path = route.Path,
            RewriteMode = route.RewriteMode,
            RewriteTargetPath = route.RewriteTargetPath,
            PreserveOriginalHostHeader = route.PreserveOriginalHostHeader,
            SendForwardedHeaders = route.SendForwardedHeaders,
            IgnoreBadCertificates = route.IgnoreBadCertificates,
            UpstreamMode = route.UpstreamMode,
            Upstream = new UpstreamTarget
            {
                Scheme = route.Upstream.Scheme,
                Host = route.Upstream.Host,
                Port = route.Upstream.Port
            },
            Container = new ContainerUpstreamTarget
            {
                ContainerName = route.Container.ContainerName,
                Scheme = route.Container.Scheme,
                Port = route.Container.Port,
                PortResolutionMode = route.Container.PortResolutionMode,
                NetworkName = route.Container.NetworkName
            }
        }).ToList();
    }

    private static AutomaticContainerProxyHostConfig CloneAutomaticContainer(AutomaticContainerProxyHostConfig config)
    {
        return new AutomaticContainerProxyHostConfig
        {
            LabelSelectors = config.LabelSelectors.Select(x => new AutomaticContainerLabelSelector
            {
                Key = x.Key,
                ValuePattern = x.ValuePattern,
                ValuePatterns = [.. x.ValuePatterns]
            }).ToList(),
            DomainTemplates = [.. config.DomainTemplates]
        };
    }

    private static TlsBindingConfig CloneTls(TlsBindingConfig tls)
    {
        return new TlsBindingConfig
        {
            Mode = tls.Mode,
            PfxPath = tls.PfxPath,
            PfxPassword = tls.PfxPassword,
            PfxPasswordEnvVar = tls.PfxPasswordEnvVar,
            Thumbprint = tls.Thumbprint,
            StoreName = tls.StoreName,
            StoreLocation = tls.StoreLocation
        };
    }

    private sealed record GeneratedHostRecord(ProxyHostConfig Host, ProxyHostRuntimeMetadata Runtime);

    private sealed class ProxyConfigSnapshot(
        IReadOnlyList<RouteConfig> routes,
        IReadOnlyList<ClusterConfig> clusters,
        Dictionary<string, ProxyHostConfig> storedHostsById,
        Dictionary<string, ProxyHostConfig> effectiveHostsById,
        Dictionary<string, AdminProxyHostDto> adminHostsById,
        Dictionary<string, string> domainToHostId) : IProxyConfig
    {
        private readonly CancellationTokenSource _cts = new();

        public static ProxyConfigSnapshot Empty { get; } = new(
            [],
            [],
            new Dictionary<string, ProxyHostConfig>(StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, ProxyHostConfig>(StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, AdminProxyHostDto>(StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));

        public Dictionary<string, ProxyHostConfig> StoredHostsById { get; } = storedHostsById;
        public Dictionary<string, ProxyHostConfig> EffectiveHostsById { get; } = effectiveHostsById;
        public Dictionary<string, AdminProxyHostDto> AdminHostsById { get; } = adminHostsById;
        public Dictionary<string, string> DomainToHostId { get; } = domainToHostId;

        public IReadOnlyList<RouteConfig> Routes { get; } = routes;
        public IReadOnlyList<ClusterConfig> Clusters { get; } = clusters;
        public IChangeToken ChangeToken => new CancellationChangeToken(_cts.Token);

        public void SignalChange()
        {
            _cts.Cancel();
        }
    }
}