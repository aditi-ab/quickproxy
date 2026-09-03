using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace QuickProxy.Proxy.Containers;

public sealed class ComposeProjectRunner : IComposeProjectRunner
{
    private readonly ConcurrentDictionary<string, bool> _availability = new(StringComparer.OrdinalIgnoreCase);
    private readonly DockerCliBootstrapper _dockerCliBootstrapper;

    public ComposeProjectRunner(DockerCliBootstrapper dockerCliBootstrapper)
    {
        _dockerCliBootstrapper = dockerCliBootstrapper;
    }

    public async Task<ComposeProjectValidationResult> ValidateAsync(ComposeProject project,
        CancellationToken cancellationToken)
    {
        var result = await RunAsync(project, ["config"], cancellationToken, true);
        return new ComposeProjectValidationResult
        {
            Valid = result.ExitCode == 0,
            Output = result.CombinedOutput,
            Errors = result.ExitCode == 0 ? [] : SplitLines(result.CombinedOutput).ToList()
        };
    }

    public async Task<IReadOnlyList<string>> ListServicesAsync(ComposeProject project,
        CancellationToken cancellationToken)
    {
        var result = await RunAsync(project, ["config", "--services"], cancellationToken);
        return SplitLines(result.StandardOutput)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public Task<ComposeProjectActionResult> DeployAsync(ComposeProject project, CancellationToken cancellationToken)
    {
        return RunActionAsync(project, "Project deployed.", ["up", "-d"], cancellationToken);
    }

    public Task<ComposeProjectActionResult> StartAsync(ComposeProject project, CancellationToken cancellationToken)
    {
        return RunActionAsync(project, "Project started.", ["start"], cancellationToken);
    }

    public Task<ComposeProjectActionResult> StopAsync(ComposeProject project, CancellationToken cancellationToken)
    {
        return RunActionAsync(project, "Project stopped.", ["stop"], cancellationToken);
    }

    public Task<ComposeProjectActionResult> RestartAsync(ComposeProject project, CancellationToken cancellationToken)
    {
        return RunActionAsync(project, "Project restarted.", ["restart"], cancellationToken);
    }

    public Task<ComposeProjectActionResult> PullAsync(ComposeProject project, CancellationToken cancellationToken)
    {
        return RunActionAsync(project, "Project images pulled.", ["pull"], cancellationToken);
    }

    public Task<ComposeProjectActionResult> DownAsync(ComposeProject project, CancellationToken cancellationToken)
    {
        return RunActionAsync(project, "Project removed.", ["down"], cancellationToken);
    }

    public async IAsyncEnumerable<ComposeProjectLogEntry> StreamLogsAsync(
        ComposeProject project,
        string? service,
        int tail,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await EnsureAvailableAsync(cancellationToken);
        EnsureWorkspaceExists(project);

        using var process = await CreateProcessAsync(
            project,
            Path.Combine(project.WorkspacePath, "compose.yaml"),
            ["logs", "--follow", "--timestamps", "--tail", Math.Max(1, tail).ToString()],
            cancellationToken);
        if (!string.IsNullOrWhiteSpace(service)) process.StartInfo.ArgumentList.Add(service.Trim());

        if (!process.Start()) throw new InvalidOperationException("Failed to start docker compose logs.");

        var channel = Channel.CreateUnbounded<ComposeProjectLogEntry>();
        var stdoutTask = PumpLogsAsync(process.StandardOutput, channel.Writer, cancellationToken);
        var stderrTask = PumpLogsAsync(process.StandardError, channel.Writer, cancellationToken);
        var waitTask = process.WaitForExitAsync(cancellationToken);

        _ = Task.WhenAll(stdoutTask, stderrTask, waitTask)
            .ContinueWith(_ => channel.Writer.TryComplete(), CancellationToken.None);

        await foreach (var entry in channel.Reader.ReadAllAsync(cancellationToken)) yield return entry;
    }

    private async Task<ComposeProjectActionResult> RunActionAsync(ComposeProject project, string message,
        IReadOnlyList<string> args, CancellationToken cancellationToken)
    {
        var useTemporaryComposeFile = args.Count >= 2
                                      && string.Equals(args[0], "up", StringComparison.OrdinalIgnoreCase)
                                      && string.Equals(args[1], "-d", StringComparison.OrdinalIgnoreCase);
        var result = await RunAsync(project, args, cancellationToken, useTemporaryComposeFile: useTemporaryComposeFile);
        return new ComposeProjectActionResult
        {
            Message = message,
            Output = result.CombinedOutput
        };
    }

    private async Task EnsureAvailableAsync(CancellationToken cancellationToken)
    {
        var command = await _dockerCliBootstrapper.ResolveComposeCommandAsync(cancellationToken);
        var availabilityKey = $"{command.ExecutablePath}|{command.UsesStandaloneCompose}";
        if (_availability.TryGetValue(availabilityKey, out var cached) && cached) return;

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = command.ExecutablePath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };
        if (command.UsesStandaloneCompose)
        {
            process.StartInfo.ArgumentList.Add("version");
        }
        else
        {
            process.StartInfo.ArgumentList.Add("compose");
            process.StartInfo.ArgumentList.Add("version");
        }

        try
        {
            if (!process.Start()) throw new InvalidOperationException("Failed to start docker compose.");
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Docker Compose CLI is unavailable: {ex.Message}", ex);
        }

        await process.WaitForExitAsync(cancellationToken);
        if (process.ExitCode != 0)
        {
            var error = await process.StandardError.ReadToEndAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(error))
                error = await process.StandardOutput.ReadToEndAsync(cancellationToken);

            throw new InvalidOperationException($"Docker Compose CLI is unavailable: {error.Trim()}");
        }

        _availability[availabilityKey] = true;
    }

    private async Task<Process> CreateProcessAsync(ComposeProject project, string composeFilePath,
        IReadOnlyList<string> args, CancellationToken cancellationToken)
    {
        var command = await _dockerCliBootstrapper.ResolveComposeCommandAsync(cancellationToken);
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = command.ExecutablePath,
                WorkingDirectory = project.WorkspacePath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        if (!command.UsesStandaloneCompose) process.StartInfo.ArgumentList.Add("compose");
        process.StartInfo.ArgumentList.Add("--project-name");
        process.StartInfo.ArgumentList.Add(project.Slug);
        process.StartInfo.ArgumentList.Add("--file");
        process.StartInfo.ArgumentList.Add(composeFilePath);

        foreach (var arg in args) process.StartInfo.ArgumentList.Add(arg);

        return process;
    }

    private async Task<(int ExitCode, string StandardOutput, string StandardError, string CombinedOutput)> RunAsync(
        ComposeProject project,
        IReadOnlyList<string> args,
        CancellationToken cancellationToken,
        bool allowFailure = false,
        bool useTemporaryComposeFile = false)
    {
        await EnsureAvailableAsync(cancellationToken);
        EnsureWorkspaceExists(project);

        var composeFilePath = Path.Combine(project.WorkspacePath, "compose.yaml");
        var temporaryComposeFilePath = string.Empty;

        try
        {
            if (useTemporaryComposeFile)
            {
                temporaryComposeFilePath = Path.Combine(project.WorkspacePath,
                    $".quickproxy.deploy.{Guid.NewGuid():N}.compose.yaml");
                await File.WriteAllTextAsync(temporaryComposeFilePath, project.ComposeYaml + Environment.NewLine,
                    cancellationToken);
                composeFilePath = temporaryComposeFilePath;
            }

            using var process = await CreateProcessAsync(project, composeFilePath, args, cancellationToken);
            if (!process.Start()) throw new InvalidOperationException("Failed to start docker compose.");

            var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);

            var stdout = await stdoutTask;
            var stderr = await stderrTask;
            var combined = string.Join(Environment.NewLine,
                new[] { stdout, stderr }.Where(x => !string.IsNullOrWhiteSpace(x))).Trim();

            if (!allowFailure && process.ExitCode != 0)
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(combined)
                    ? "Docker Compose command failed."
                    : combined);

            return (process.ExitCode, stdout, stderr, combined);
        }
        finally
        {
            if (!string.IsNullOrWhiteSpace(temporaryComposeFilePath) && File.Exists(temporaryComposeFilePath))
                File.Delete(temporaryComposeFilePath);
        }
    }

    private static async Task PumpLogsAsync(
        StreamReader reader,
        ChannelWriter<ComposeProjectLogEntry> writer,
        CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(cancellationToken);
            if (line is null) break;

            if (string.IsNullOrWhiteSpace(line)) continue;

            await writer.WriteAsync(ParseLogLine(line), cancellationToken);
        }
    }

    private static ComposeProjectLogEntry ParseLogLine(string line)
    {
        var service = string.Empty;
        var payload = line.Trim();
        var pipeIndex = payload.IndexOf('|');
        if (pipeIndex > 0)
        {
            service = payload[..pipeIndex].Trim();
            payload = payload[(pipeIndex + 1)..].Trim();
        }

        var separatorIndex = payload.IndexOf(' ');
        if (separatorIndex <= 0) return new ComposeProjectLogEntry(service, payload, string.Empty);

        var timestamp = payload[..separatorIndex].Trim();
        var message = payload[(separatorIndex + 1)..].Trim();
        return new ComposeProjectLogEntry(service, message, timestamp);
    }

    private static string[] SplitLines(string? value)
    {
        return (value ?? string.Empty)
            .Replace("\r\n", "\n")
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static void EnsureWorkspaceExists(ComposeProject project)
    {
        ComposeProjectStorageHelper.PersistWorkspace(project);
    }
}