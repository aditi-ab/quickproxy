using System.Data.Common;
using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using QuickProxy.Configuration;
using QuickProxy.Proxy.Containers;
using QuickProxy.Proxy.Provisioning;
using QuickProxy.Shared.Configuration;
using QuickProxy.Shared.Web;

namespace QuickProxy.Proxy.Api;

public static class SystemApiExtensions
{
    private static readonly DateTimeOffset ProcessStartedUtc = DateTimeOffset.UtcNow;

    public static IEndpointRouteBuilder MapSystemApi(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup($"{InternalApiPaths.AdminRoot}/system").RequireAuthorization();

        group.MapGet("/storage",
            (AppModulesConfiguration settings, IOptions<ContainerRuntimeSettings> containerOptions,
                IWebHostEnvironment environment) =>
            {
                var containersEnabled = containerOptions.Value.Enabled;
                var selfUpdate =
                    ResolveSelfUpdateStatus(settings.Proxy.Enabled, containersEnabled, environment, null, null);

                return Results.Ok(new
                {
                    version = GetApplicationVersion(),
                    startedAtUtc = ProcessStartedUtc,
                    proxy = new
                    {
                        enabled = settings.Proxy.Enabled,
                        storage = BuildStorageInfo(settings.Proxy.Storage, environment.ContentRootPath)
                    },
                    config = new
                    {
                        enabled = settings.Config.Enabled,
                        storage = BuildStorageInfo(settings.Config.Storage, environment.ContentRootPath),
                        remote = new
                        {
                            enabled = settings.Config.Remote.Enabled,
                            url = string.IsNullOrWhiteSpace(settings.Config.Remote.Url)
                                ? null
                                : settings.Config.Remote.Url.Trim()
                        }
                    },
                    audit = new
                    {
                        enabled = settings.Audit.Enabled,
                        storage = BuildStorageInfo(settings.Audit.Storage, environment.ContentRootPath)
                    },
                    containers = new
                    {
                        enabled = containersEnabled
                    },
                    selfUpdate
                });
            });

        group.MapGet("/self-update/status", async (
            AppModulesConfiguration settings,
            IOptions<ContainerRuntimeSettings> containerOptions,
            IWebHostEnvironment environment,
            IContainerInventory inventory,
            IContainerRuntimeClient runtimeClient,
            ContainerImageUpdateResolver imageUpdateResolver,
            CancellationToken cancellationToken) =>
        {
            var status = await ResolveSelfUpdateStatusAsync(
                settings.Proxy.Enabled,
                containerOptions.Value.Enabled,
                environment,
                inventory,
                runtimeClient,
                imageUpdateResolver,
                cancellationToken);
            return Results.Ok(status);
        });

        group.MapPost("/self-update", async (
            SelfUpdateRequest? request,
            AppModulesConfiguration settings,
            IOptions<ContainerRuntimeSettings> containerOptions,
            IWebHostEnvironment environment,
            IContainerInventory inventory,
            IContainerRuntimeClient runtimeClient,
            ContainerImageUpdateResolver imageUpdateResolver,
            ILoggerFactory loggerFactory,
            CancellationToken cancellationToken) =>
        {
            var status = await ResolveSelfUpdateStatusAsync(
                settings.Proxy.Enabled,
                containerOptions.Value.Enabled,
                environment,
                inventory,
                runtimeClient,
                imageUpdateResolver,
                cancellationToken);
            if (!status.Supported || string.IsNullOrWhiteSpace(status.ContainerName))
                return Results.BadRequest(new
                {
                    code = "self_update_unavailable",
                    message = status.Reason ?? "Self-update is unavailable in the current environment."
                });

            if (!status.UpdateAvailable)
                return Results.BadRequest(new
                {
                    code = "self_update_unavailable",
                    message = "The current QuickProxy container image is already up to date."
                });

            var containerName = status.ContainerName;
            var imageReference = string.IsNullOrWhiteSpace(request?.ImageReference)
                ? null
                : request.ImageReference.Trim();
            var logger = loggerFactory.CreateLogger("QuickProxySelfUpdate");
            _ = Task.Run(async () =>
            {
                try
                {
                    await runtimeClient.PullImageAndRestartContainerAsync(containerName, imageReference,
                        CancellationToken.None);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Self-update failed for container '{ContainerName}'.", containerName);
                }
            });

            return Results.Accepted(value: new
            {
                message =
                    $"Self-update started for container '{containerName}'. The admin UI may disconnect while QuickProxy restarts.",
                containerName = status.ContainerName,
                image = imageReference ?? status.Image
            });
        });

        group.MapPost("/reprovision", async (
            AppModulesConfiguration settings,
            ProvisioningHostedService provisioningHostedService,
            CancellationToken cancellationToken) =>
        {
            if (!settings.Proxy.Enabled)
                return Results.BadRequest(new
                {
                    code = "reprovision_unavailable",
                    message = "Re-provision requires the Proxy module to be enabled."
                });

            await provisioningHostedService.RunNowAsync(cancellationToken, true);
            return Results.Ok(new
            {
                message = "Provisioning run completed and existing provisioned values were overwritten."
            });
        });

        return app;
    }

    private static async Task<SelfUpdateStatus> ResolveSelfUpdateStatusAsync(
        bool proxyEnabled,
        bool containersEnabled,
        IWebHostEnvironment environment,
        IContainerInventory inventory,
        IContainerRuntimeClient runtimeClient,
        ContainerImageUpdateResolver imageUpdateResolver,
        CancellationToken cancellationToken)
    {
        var fallback = ResolveSelfUpdateStatus(proxyEnabled, containersEnabled, environment, null, null);
        if (!fallback.Supported) return fallback;

        try
        {
            var cachedContainers = inventory.ListContainers();
            var containers = await runtimeClient.ListContainersAsync(cancellationToken);
            if (containers.Count == 0) containers = cachedContainers;

            var self = FindCurrentContainer(containers, GetSelfContainerIdCandidates(environment));
            if (self is null)
                return fallback with
                {
                    Supported = false,
                    Reason =
                    "QuickProxy appears to run in a container, but its container could not be identified via Docker."
                };

            var imageUpdate = await ResolveSelfImageUpdateAsync(self, imageUpdateResolver, cancellationToken)
                              ?? self.ImageUpdate;

            return fallback with
            {
                Supported = true,
                Reason = null,
                ContainerName = self.Name,
                Image = self.Image,
                UpdateAvailable = imageUpdate?.UpdateAvailable == true,
                LocalDigest = imageUpdate?.LocalDigest ?? self.ImageDigest,
                RemoteDigest = imageUpdate?.RemoteDigest,
                ImageUpdateStatus = imageUpdate?.Status,
                ImageUpdateError = imageUpdate?.Error
            };
        }
        catch (Exception ex)
        {
            return fallback with
            {
                Supported = false,
                Reason = $"Self-update is unavailable because Docker runtime metadata could not be read: {ex.Message}"
            };
        }
    }

    private static SelfUpdateStatus ResolveSelfUpdateStatus(
        bool proxyEnabled,
        bool containersEnabled,
        IWebHostEnvironment environment,
        string? containerName,
        string? image)
    {
        if (!proxyEnabled)
            return new SelfUpdateStatus(
                false,
                "Self-update requires the Proxy module to be enabled.",
                null,
                null);

        if (!containersEnabled)
            return new SelfUpdateStatus(
                false,
                "Self-update requires the Containers module to be enabled.",
                null,
                null);

        if (!IsLikelyRunningInContainer(environment))
            return new SelfUpdateStatus(
                false,
                "QuickProxy is not running inside a container.",
                null,
                null);

        return new SelfUpdateStatus(
            true,
            null,
            containerName,
            image);
    }

    private static bool IsLikelyRunningInContainer(IWebHostEnvironment environment)
    {
        var dotnetFlag = Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER");
        if (string.Equals(dotnetFlag, "true", StringComparison.OrdinalIgnoreCase)) return true;

        try
        {
            var dockerEnvPath = Path.Combine(Path.GetPathRoot(environment.ContentRootPath) ?? "/", ".dockerenv");
            if (File.Exists("/.dockerenv") || File.Exists(dockerEnvPath)) return true;
        }
        catch
        {
            // ignored
        }

        var hostname = Environment.GetEnvironmentVariable("HOSTNAME") ?? Environment.MachineName;
        return IsLikelyContainerId(hostname);
    }

    private static IReadOnlyList<string> GetSelfContainerIdCandidates(IWebHostEnvironment environment)
    {
        var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        AddCandidate(candidates, Environment.GetEnvironmentVariable("HOSTNAME"));
        AddCandidate(candidates, Environment.MachineName);

        TryAddFromFile(candidates, "/proc/self/cgroup");
        TryAddFromFile(candidates, "/proc/1/cpuset");
        TryAddFromFile(candidates,
            Path.Combine(Path.GetPathRoot(environment.ContentRootPath) ?? "/", "proc", "self", "cgroup"));

        return candidates.ToArray();
    }

    private static async Task<ContainerImageUpdateInfo?> ResolveSelfImageUpdateAsync(
        ContainerInventoryItem self,
        ContainerImageUpdateResolver imageUpdateResolver,
        CancellationToken cancellationToken)
    {
        var updates = await imageUpdateResolver.ResolveAsync([self], cancellationToken);
        return updates.TryGetValue(self.Name, out var imageUpdate)
            ? imageUpdate
            : null;
    }

    private static ContainerInventoryItem? FindCurrentContainer(
        IReadOnlyList<ContainerInventoryItem> containers,
        IReadOnlyList<string> candidateIds)
    {
        if (candidateIds.Count > 0)
            foreach (var candidate in candidateIds)
            {
                var direct = containers.FirstOrDefault(x =>
                    !string.IsNullOrWhiteSpace(x.Id) &&
                    (string.Equals(x.Id, candidate, StringComparison.OrdinalIgnoreCase)
                     || x.Id.StartsWith(candidate, StringComparison.OrdinalIgnoreCase)
                     || candidate.StartsWith(x.Id, StringComparison.OrdinalIgnoreCase)));
                if (direct is not null) return direct;
            }

        var labeled = containers
            .Where(x => x.IsRunning && x.ContainerLabels.TryGetValue("quickproxy.role", out var role) &&
                        string.Equals(role, "system", StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return labeled.Length == 1 ? labeled[0] : null;
    }

    private static void TryAddFromFile(ISet<string> candidates, string path)
    {
        try
        {
            if (!File.Exists(path)) return;

            var lines = File.ReadAllLines(path);
            foreach (var line in lines)
            {
                var token = line;
                var slashIndex = token.LastIndexOf('/');
                if (slashIndex >= 0 && slashIndex < token.Length - 1) token = token[(slashIndex + 1)..];

                AddCandidate(candidates, token);
            }
        }
        catch
        {
            // ignored
        }
    }

    private static void AddCandidate(ISet<string> candidates, string? value)
    {
        var trimmed = (value ?? string.Empty).Trim();
        if (IsLikelyContainerId(trimmed)) candidates.Add(trimmed);
    }

    private static bool IsLikelyContainerId(string? value)
    {
        var trimmed = (value ?? string.Empty).Trim();
        return Regex.IsMatch(trimmed, "^[a-f0-9]{12}$|^[a-f0-9]{64}$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }


    private static string GetApplicationVersion()
    {
        var assembly = Assembly.GetExecutingAssembly();
        return assembly
                   .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
               ?? assembly.GetName().Version?.ToString()
               ?? "unknown";
    }

    private static object BuildStorageInfo(StorageSettings settings, string contentRootPath)
    {
        var databaseProvider = settings.Provider?.Trim().ToLowerInvariant() ?? "sqlite";
        var connectionString = settings.ConnectionString ?? string.Empty;
        var connectionParts = ParseConnectionString(connectionString);

        if (databaseProvider == "sqlserver")
        {
            var server = FirstValue(connectionParts, "data source", "server", "addr", "address", "network address");
            var database = FirstValue(connectionParts, "initial catalog", "database");

            return new
            {
                provider = "database",
                databaseProvider = "sqlserver",
                label = "SQL Server",
                server,
                database,
                details = BuildSqlServerDetails(server, database)
            };
        }

        var dataSource = FirstValue(connectionParts, "data source", "datasource", "filename");
        var resolvedPath = ResolveSqlitePath(contentRootPath, dataSource);

        return new
        {
            provider = "database",
            databaseProvider = "sqlite",
            label = "SQLite",
            path = resolvedPath,
            details = resolvedPath
        };
    }

    private static Dictionary<string, string> ParseConnectionString(string connectionString)
    {
        var builder = new DbConnectionStringBuilder
        {
            ConnectionString = connectionString
        };

        return builder.Cast<KeyValuePair<string, object>>()
            .ToDictionary(
                pair => pair.Key.Trim().ToLowerInvariant(),
                pair => pair.Value?.ToString() ?? string.Empty,
                StringComparer.OrdinalIgnoreCase);
    }

    private static string? FirstValue(IReadOnlyDictionary<string, string> parts, params string[] keys)
    {
        foreach (var key in keys)
            if (parts.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
                return value;

        return null;
    }

    private static string BuildSqlServerDetails(string? server, string? database)
    {
        if (!string.IsNullOrWhiteSpace(server) && !string.IsNullOrWhiteSpace(database)) return $"{server} / {database}";

        if (!string.IsNullOrWhiteSpace(server)) return server;

        if (!string.IsNullOrWhiteSpace(database)) return database;

        return "Connection string configured";
    }

    private static string ResolveSqlitePath(string contentRootPath, string? dataSource)
    {
        if (string.IsNullOrWhiteSpace(dataSource) || dataSource == ":memory:") return dataSource ?? string.Empty;

        if (Path.IsPathRooted(dataSource)) return Path.GetFullPath(dataSource);

        return Path.GetFullPath(Path.Combine(contentRootPath, dataSource));
    }

    private sealed record SelfUpdateStatus(
        bool Supported,
        string? Reason,
        string? ContainerName,
        string? Image,
        bool UpdateAvailable = false,
        string? LocalDigest = null,
        string? RemoteDigest = null,
        string? ImageUpdateStatus = null,
        string? ImageUpdateError = null);

    private sealed class SelfUpdateRequest
    {
        public string? ImageReference { get; set; }
    }
}