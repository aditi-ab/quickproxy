using System.IO.Compression;

namespace QuickProxy.Proxy.Containers;

public sealed class DockerCliBootstrapper(
    IHttpClientFactory httpClientFactory,
    ILogger<DockerCliBootstrapper> logger)
{
    private const string DockerWindowsZipUrl = "https://download.docker.com/win/static/stable/x86_64/docker-29.2.1.zip";

    private const string DockerComposeWindowsUrl =
        "https://github.com/docker/compose/releases/download/v5.1.0/docker-compose-windows-x86_64.exe";

    private static readonly SemaphoreSlim Sync = new(1, 1);

    public async Task<ComposeCliCommand> ResolveComposeCommandAsync(CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows()) return new ComposeCliCommand("docker", false);

        var toolsDirectory = Path.Combine(Path.GetTempPath(), "QuickProxy", "docker-cli");
        var dockerPath = Path.Combine(toolsDirectory, "docker.exe");
        var dockerComposePath = Path.Combine(toolsDirectory, "docker-compose.exe");

        if (File.Exists(dockerComposePath)) return new ComposeCliCommand(dockerComposePath, true);

        if (File.Exists(dockerPath)) return new ComposeCliCommand(dockerPath, false);

        await Sync.WaitAsync(cancellationToken);
        try
        {
            Directory.CreateDirectory(toolsDirectory);

            if (!File.Exists(dockerPath))
                await DownloadAndExtractDockerCliAsync(dockerPath, toolsDirectory, cancellationToken);

            if (!File.Exists(dockerComposePath)) await DownloadDockerComposeAsync(dockerComposePath, cancellationToken);

            if (File.Exists(dockerComposePath)) return new ComposeCliCommand(dockerComposePath, true);

            if (File.Exists(dockerPath)) return new ComposeCliCommand(dockerPath, false);

            throw new InvalidOperationException(
                "Docker CLI bootstrap completed without producing docker.exe or docker-compose.exe.");
        }
        finally
        {
            Sync.Release();
        }
    }

    private async Task DownloadAndExtractDockerCliAsync(string dockerPath, string toolsDirectory,
        CancellationToken cancellationToken)
    {
        logger.LogInformation("Downloading Docker CLI from {Url} to {Directory}", DockerWindowsZipUrl, toolsDirectory);

        var tempZipPath = Path.Combine(Path.GetTempPath(), $"quickproxy-docker-cli-{Guid.NewGuid():N}.zip");
        try
        {
            using var client = httpClientFactory.CreateClient();
            await using (var responseStream = await client.GetStreamAsync(DockerWindowsZipUrl, cancellationToken))
            await using (var fileStream = File.Create(tempZipPath))
            {
                await responseStream.CopyToAsync(fileStream, cancellationToken);
            }

            var extractRoot = Path.Combine(Path.GetTempPath(), $"quickproxy-docker-cli-{Guid.NewGuid():N}");
            Directory.CreateDirectory(extractRoot);
            try
            {
                ZipFile.ExtractToDirectory(tempZipPath, extractRoot, true);

                var extractedDockerPath = Path.Combine(extractRoot, "docker", "docker.exe");
                if (!File.Exists(extractedDockerPath))
                    throw new InvalidOperationException("Downloaded Docker CLI archive did not contain docker.exe.");

                File.Copy(extractedDockerPath, dockerPath, true);
            }
            finally
            {
                if (Directory.Exists(extractRoot)) Directory.Delete(extractRoot, true);
            }
        }
        finally
        {
            if (File.Exists(tempZipPath)) File.Delete(tempZipPath);
        }
    }

    private async Task DownloadDockerComposeAsync(string dockerComposePath, CancellationToken cancellationToken)
    {
        logger.LogInformation("Downloading Docker Compose CLI from {Url} to {Path}", DockerComposeWindowsUrl,
            dockerComposePath);

        var tempPath = Path.Combine(Path.GetTempPath(), $"quickproxy-docker-compose-{Guid.NewGuid():N}.exe");
        try
        {
            using var client = httpClientFactory.CreateClient();
            await using (var responseStream = await client.GetStreamAsync(DockerComposeWindowsUrl, cancellationToken))
            await using (var fileStream = File.Create(tempPath))
            {
                await responseStream.CopyToAsync(fileStream, cancellationToken);
            }

            File.Copy(tempPath, dockerComposePath, true);
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }
}

public sealed record ComposeCliCommand(string ExecutablePath, bool UsesStandaloneCompose);