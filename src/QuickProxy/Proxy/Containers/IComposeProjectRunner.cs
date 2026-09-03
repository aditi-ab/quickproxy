namespace QuickProxy.Proxy.Containers;

public interface IComposeProjectRunner
{
    Task<ComposeProjectValidationResult> ValidateAsync(ComposeProject project, CancellationToken cancellationToken);
    Task<IReadOnlyList<string>> ListServicesAsync(ComposeProject project, CancellationToken cancellationToken);
    Task<ComposeProjectActionResult> DeployAsync(ComposeProject project, CancellationToken cancellationToken);
    Task<ComposeProjectActionResult> StartAsync(ComposeProject project, CancellationToken cancellationToken);
    Task<ComposeProjectActionResult> StopAsync(ComposeProject project, CancellationToken cancellationToken);
    Task<ComposeProjectActionResult> RestartAsync(ComposeProject project, CancellationToken cancellationToken);
    Task<ComposeProjectActionResult> PullAsync(ComposeProject project, CancellationToken cancellationToken);
    Task<ComposeProjectActionResult> DownAsync(ComposeProject project, CancellationToken cancellationToken);

    IAsyncEnumerable<ComposeProjectLogEntry> StreamLogsAsync(ComposeProject project, string? service, int tail,
        CancellationToken cancellationToken);
}