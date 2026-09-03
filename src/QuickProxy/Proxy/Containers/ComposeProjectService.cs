using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace QuickProxy.Proxy.Containers;

public sealed class ComposeProjectService(
    IComposeProjectStore store,
    IComposeProjectRunner runner,
    ComposeProjectDefaultsMerger defaultsMerger,
    IContainerInventory inventory,
    IHostEnvironment environment)
{
    private static readonly string[] UnsupportedYamlPatterns =
    [
        @"^\s*secrets\s*:",
        @"^\s*configs\s*:",
        @"^\s*include\s*:",
        @"^\s*extends\s*:",
        @"^\s*profiles\s*:"
    ];

    public IReadOnlyList<ComposeProjectListItem> List()
    {
        return store.List()
            .Select(project => new ComposeProjectListItem
            {
                Project = project,
                Runtime = BuildRuntimeSnapshot(project)
            })
            .ToArray();
    }

    public async Task<ComposeProjectListItem?> GetAsync(string id, CancellationToken cancellationToken)
    {
        var project = store.Get(id);
        if (project is null) return null;

        var runtime = BuildRuntimeSnapshot(project);
        try
        {
            var services = await runner.ListServicesAsync(project, cancellationToken);
            runtime.ServiceCount = Math.Max(runtime.ServiceCount, services.Count);
            foreach (var service in services.Except(runtime.Services.Select(x => x.Name),
                         StringComparer.OrdinalIgnoreCase))
                runtime.Services.Add(new ComposeProjectServiceRuntime
                {
                    Name = service
                });

            runtime.Services = runtime.Services
                .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch
        {
        }

        return new ComposeProjectListItem
        {
            Project = project,
            Runtime = runtime
        };
    }

    public ComposeProject Upsert(ComposeProject project)
    {
        var normalized = PrepareForSave(project);
        return store.Upsert(normalized);
    }

    public bool Delete(string id)
    {
        return store.Delete(id);
    }

    public async Task<ComposeProjectValidationResult> ValidateAsync(string id, CancellationToken cancellationToken)
    {
        var project = GetRequired(id);
        ValidateProjectShape(project);
        return await runner.ValidateAsync(project, cancellationToken);
    }

    public async Task<ComposeProjectActionResult> DeployAsync(string id, CancellationToken cancellationToken)
    {
        var project = GetRequired(id);
        ValidateProjectShape(project);
        var preparedProject = defaultsMerger.ApplyDefaults(project);
        var result = await runner.DeployAsync(preparedProject, cancellationToken);
        return SaveActionOutcome(project, "running", null, DateTimeOffset.UtcNow, result);
    }

    public async Task<ComposeProjectActionResult> StartAsync(string id, CancellationToken cancellationToken)
    {
        var project = GetRequired(id);
        var result = await runner.StartAsync(project, cancellationToken);
        return SaveActionOutcome(project, "running", null, null, result);
    }

    public async Task<ComposeProjectActionResult> StopAsync(string id, CancellationToken cancellationToken)
    {
        var project = GetRequired(id);
        var result = await runner.StopAsync(project, cancellationToken);
        return SaveActionOutcome(project, "stopped", null, null, result);
    }

    public async Task<ComposeProjectActionResult> RestartAsync(string id, CancellationToken cancellationToken)
    {
        var project = GetRequired(id);
        var result = await runner.RestartAsync(project, cancellationToken);
        return SaveActionOutcome(project, "running", null, null, result);
    }

    public async Task<ComposeProjectActionResult> PullAsync(string id, CancellationToken cancellationToken)
    {
        var project = GetRequired(id);
        var result = await runner.PullAsync(project, cancellationToken);
        return SaveActionOutcome(project, project.Status, null, null, result);
    }

    public async Task<ComposeProjectActionResult> DownAsync(string id, CancellationToken cancellationToken)
    {
        var project = GetRequired(id);
        var result = await runner.DownAsync(project, cancellationToken);
        return SaveActionOutcome(project, "stopped", null, null, result);
    }

    public async IAsyncEnumerable<ComposeProjectLogEntry> StreamLogsAsync(
        string id,
        string? service,
        int tail,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var project = GetRequired(id);
        await foreach (var entry in runner.StreamLogsAsync(project, service, tail, cancellationToken))
            yield return entry;
    }

    private ComposeProjectActionResult SaveActionOutcome(
        ComposeProject project,
        string status,
        string? error,
        DateTimeOffset? deployedAtUtc,
        ComposeProjectActionResult result)
    {
        project.Status = status;
        project.LastError = error;
        if (deployedAtUtc.HasValue) project.LastDeployAtUtc = deployedAtUtc;

        var updated = store.Upsert(project);
        result.Runtime = BuildRuntimeSnapshot(updated);
        return result;
    }

    private ComposeProject PrepareForSave(ComposeProject project)
    {
        var normalized = ComposeProjectStorageHelper.NormalizeProject(project, environment, DateTimeOffset.UtcNow);
        ValidateProjectShape(normalized);
        return normalized;
    }

    private ComposeProject GetRequired(string id)
    {
        return store.Get(id)
               ?? throw new InvalidOperationException($"Compose project '{id}' was not found.");
    }

    private static void ValidateProjectShape(ComposeProject project)
    {
        if (string.IsNullOrWhiteSpace(project.Id)) throw new InvalidOperationException("Project id is required.");

        if (string.IsNullOrWhiteSpace(project.ComposeYaml))
            throw new InvalidOperationException("Compose YAML is required.");

        foreach (var file in project.ManagedFiles) ComposeProjectStorageHelper.NormalizeManagedPath(file.Path);

        var yaml = project.ComposeYaml.Replace("\r\n", "\n");
        foreach (var pattern in UnsupportedYamlPatterns)
            if (Regex.IsMatch(yaml, pattern,
                    RegexOptions.Multiline | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                throw new InvalidOperationException("Compose YAML contains unsupported features for v1.");

        foreach (Match match in Regex.Matches(yaml, @"env_file\s*:\s*(?<value>.+)$",
                     RegexOptions.Multiline | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            var value = match.Groups["value"].Value.Trim().Trim('"', '\'');
            if (string.IsNullOrWhiteSpace(value) || value.StartsWith("[", StringComparison.Ordinal)) continue;

            if (Path.IsPathRooted(value) || value.StartsWith("../", StringComparison.Ordinal) ||
                value.Contains("/../", StringComparison.Ordinal))
                throw new InvalidOperationException(
                    "env_file references must stay inside the managed project workspace.");
        }
    }

    private ComposeProjectRuntimeSnapshot BuildRuntimeSnapshot(ComposeProject project)
    {
        var containers = inventory.ListContainers()
            .Where(x => string.Equals(x.Compose.Project, project.Slug, StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => x.Compose.Service, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var runtimeContainers = containers.Select(x => new ComposeProjectContainerRuntime
        {
            Id = x.Id,
            Name = x.Name,
            Service = x.Compose.Service ?? string.Empty,
            State = x.State,
            Status = x.Status
        }).ToList();

        var runtimeServices = runtimeContainers
            .GroupBy(x => x.Service, StringComparer.OrdinalIgnoreCase)
            .Where(x => !string.IsNullOrWhiteSpace(x.Key))
            .Select(x => new ComposeProjectServiceRuntime
            {
                Name = x.Key,
                ContainerCount = x.Count(),
                RunningCount =
                    x.Count(item => string.Equals(item.State, "running", StringComparison.OrdinalIgnoreCase)),
                ContainerNames = x.Select(item => item.Name).OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                    .ToList()
            })
            .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var status = project.Status;
        if (runtimeContainers.Count > 0)
        {
            var running =
                runtimeContainers.Count(x => string.Equals(x.State, "running", StringComparison.OrdinalIgnoreCase));
            status = running == runtimeContainers.Count ? "running"
                : running == 0 ? "stopped"
                : "partial";
        }

        return new ComposeProjectRuntimeSnapshot
        {
            ProjectName = project.Slug,
            Status = status,
            ServiceCount = runtimeServices.Count,
            ContainerCount = runtimeContainers.Count,
            Services = runtimeServices,
            Containers = runtimeContainers
        };
    }
}