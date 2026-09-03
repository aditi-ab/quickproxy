using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace QuickProxy.Proxy.Containers;

internal static class ComposeProjectStorageHelper
{
    private static readonly Regex SlugRegex = new("[^a-z0-9]+", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public static string GetProjectsRoot(IHostEnvironment environment)
    {
        var projectsRoot = Path.Combine(Path.GetTempPath(), "QuickProxy", "Containers", "Projects");
        Directory.CreateDirectory(projectsRoot);
        return projectsRoot;
    }

    public static ComposeProject PrepareRuntimeProject(ComposeProject project, IHostEnvironment environment)
    {
        var prepared = Clone(project);
        prepared.WorkspacePath = Path.Combine(GetProjectsRoot(environment), prepared.Slug);
        return prepared;
    }

    public static ComposeProject NormalizeProject(ComposeProject project, IHostEnvironment environment,
        DateTimeOffset now)
    {
        var normalized = Clone(project);
        var canonicalName = NormalizeProjectName(normalized.Id, normalized.Slug, normalized.DisplayName);
        normalized.Id = canonicalName;
        normalized.DisplayName = canonicalName;
        normalized.Slug = canonicalName;
        normalized.Status = string.IsNullOrWhiteSpace(normalized.Status) ? "draft" : normalized.Status.Trim();
        normalized.ComposeYaml = NormalizeLineEndings(normalized.ComposeYaml).Trim();
        normalized.ManagedFiles = NormalizeManagedFiles(normalized.ManagedFiles);
        normalized.CreatedAtUtc = normalized.CreatedAtUtc == default ? now : normalized.CreatedAtUtc;
        normalized.UpdatedAtUtc = now;
        normalized.WorkspacePath = Path.Combine(GetProjectsRoot(environment), normalized.Slug);
        return normalized;
    }

    public static void PersistWorkspace(ComposeProject project)
    {
        if (string.IsNullOrWhiteSpace(project.WorkspacePath))
            throw new InvalidOperationException("Compose project workspace path is required.");

        Directory.CreateDirectory(project.WorkspacePath);

        foreach (var entry in Directory.EnumerateFiles(project.WorkspacePath, "*", SearchOption.AllDirectories))
            File.Delete(entry);

        foreach (var directory in Directory
                     .EnumerateDirectories(project.WorkspacePath, "*", SearchOption.AllDirectories)
                     .OrderByDescending(x => x.Length))
            if (!Directory.EnumerateFileSystemEntries(directory).Any())
                Directory.Delete(directory);

        File.WriteAllText(Path.Combine(project.WorkspacePath, "compose.yaml"),
            NormalizeLineEndings(project.ComposeYaml) + Environment.NewLine, Encoding.UTF8);

        foreach (var file in project.ManagedFiles)
        {
            var fullPath = Path.Combine(project.WorkspacePath, file.Path.Replace('/', Path.DirectorySeparatorChar));
            var parent = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrWhiteSpace(parent)) Directory.CreateDirectory(parent);

            File.WriteAllText(fullPath, NormalizeLineEndings(file.Content), Encoding.UTF8);
        }
    }

    public static void DeleteWorkspace(IHostEnvironment environment, string slug)
    {
        var normalizedSlug = NormalizeSlug(slug, slug, slug);
        var workspace = Path.Combine(GetProjectsRoot(environment), normalizedSlug);
        if (Directory.Exists(workspace)) Directory.Delete(workspace, true);
    }

    public static ComposeProject Clone(ComposeProject project)
    {
        return new ComposeProject
        {
            Id = project.Id,
            DisplayName = project.DisplayName,
            Slug = project.Slug,
            Status = project.Status,
            ComposeYaml = project.ComposeYaml,
            WorkspacePath = project.WorkspacePath,
            ManagedFiles = (project.ManagedFiles ?? []).Select(x => new ComposeManagedFile
            {
                Path = x.Path,
                Content = x.Content
            }).ToList(),
            CreatedAtUtc = project.CreatedAtUtc,
            UpdatedAtUtc = project.UpdatedAtUtc,
            LastDeployAtUtc = project.LastDeployAtUtc,
            LastError = project.LastError
        };
    }

    public static string NormalizeId(string? value)
    {
        return (value ?? string.Empty).Trim();
    }

    public static string NormalizeProjectName(params string?[] values)
    {
        return NormalizeSlug(null, values);
    }

    public static bool MatchesProjectId(ComposeProject project, string? id)
    {
        var normalizedId = NormalizeProjectName(id);
        if (string.IsNullOrWhiteSpace(normalizedId)) return false;

        return string.Equals(project.Id, id?.Trim(), StringComparison.OrdinalIgnoreCase)
               || string.Equals(project.Id, normalizedId, StringComparison.OrdinalIgnoreCase)
               || string.Equals(project.Slug, normalizedId, StringComparison.OrdinalIgnoreCase)
               || string.Equals(NormalizeProjectName(project.Id, project.Slug, project.DisplayName), normalizedId,
                   StringComparison.OrdinalIgnoreCase);
    }

    public static string NormalizeSlug(string? slug, params string?[] fallbacks)
    {
        var source = !string.IsNullOrWhiteSpace(slug)
            ? slug!
            : fallbacks.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? "project";
        var normalized = SlugRegex.Replace(source.Trim().ToLowerInvariant(), "-").Trim('-');
        return string.IsNullOrWhiteSpace(normalized) ? "project" : normalized;
    }

    public static string NormalizeManagedPath(string? value)
    {
        var trimmed = (value ?? string.Empty).Trim().Replace('\\', '/');
        if (string.IsNullOrWhiteSpace(trimmed)) return string.Empty;

        if (Path.IsPathRooted(trimmed) || trimmed.StartsWith("../", StringComparison.Ordinal) ||
            trimmed.Contains("/../", StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"Managed file path '{trimmed}' must stay inside the project workspace.");

        return trimmed.TrimStart('/');
    }

    private static string NormalizeLineEndings(string? value)
    {
        return (value ?? string.Empty).Replace("\r\n", "\n").Replace("\r", "\n");
    }

    private static List<ComposeManagedFile> NormalizeManagedFiles(IReadOnlyList<ComposeManagedFile>? files)
    {
        var results = new List<ComposeManagedFile>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in files ?? [])
        {
            var path = NormalizeManagedPath(file.Path);
            if (string.IsNullOrWhiteSpace(path) || !seen.Add(path)) continue;

            results.Add(new ComposeManagedFile
            {
                Path = path,
                Content = NormalizeLineEndings(file.Content)
            });
        }

        return results;
    }
}