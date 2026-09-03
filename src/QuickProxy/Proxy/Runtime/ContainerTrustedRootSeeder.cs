using System.Security.Cryptography.X509Certificates;

namespace QuickProxy.Proxy.Runtime;

public static class ContainerTrustedRootSeeder
{
    private static readonly string[] SupportedExtensions = [".cer", ".crt", ".pem", ".pfx"];

    public static void ImportFromDataDirectoryIfRunningInContainer(IWebHostEnvironment environment, ILogger logger)
    {
        if (!IsRunningInContainer(environment)) return;

        var directory = Path.Combine(environment.ContentRootPath, "Data", "CA");
        if (!Directory.Exists(directory))
        {
            logger.LogInformation("Trusted root import skipped because CA directory '{Directory}' does not exist.",
                directory);
            return;
        }

        logger.LogInformation("Scanning CA directory '{Directory}' for trusted root imports.", directory);

        var files = Directory.EnumerateFiles(directory, "*.*", SearchOption.TopDirectoryOnly)
            .Where(path => SupportedExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (files.Length == 0)
        {
            logger.LogInformation(
                "Trusted root import skipped because CA directory '{Directory}' contains no supported certificate files.",
                directory);
            return;
        }

        using var rootStore = new X509Store(StoreName.Root, StoreLocation.LocalMachine);
        rootStore.Open(OpenFlags.ReadWrite);

        var importedCount = 0;
        foreach (var file in files)
            try
            {
                using var certificate = LoadCertificate(file);
                if (certificate is null)
                {
                    logger.LogWarning("Skipping CA file '{File}' because no certificate could be loaded.", file);
                    continue;
                }

                if (!IsCertificateAuthority(certificate))
                {
                    logger.LogInformation(
                        "Skipping certificate '{File}' because it is not a CA certificate. Subject='{Subject}', Thumbprint='{Thumbprint}'.",
                        file, certificate.Subject, certificate.Thumbprint);
                    continue;
                }

                var existing =
                    rootStore.Certificates.Find(X509FindType.FindByThumbprint, certificate.Thumbprint, false);
                if (existing.Count > 0)
                {
                    logger.LogInformation("Trusted root already contains certificate '{Thumbprint}' from '{File}'.",
                        certificate.Thumbprint, file);
                    continue;
                }

                rootStore.Add(certificate);
                importedCount++;
                logger.LogInformation("Imported CA certificate '{Thumbprint}' from '{File}' into LocalMachine Root.",
                    certificate.Thumbprint, file);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed importing CA certificate from '{File}' into LocalMachine Root.", file);
            }

        logger.LogInformation(
            "Trusted root import completed. Imported {ImportedCount} certificate(s) from '{Directory}'.", importedCount,
            directory);
    }

    private static X509Certificate2? LoadCertificate(string path)
    {
        var extension = Path.GetExtension(path);
        if (string.Equals(extension, ".pfx", StringComparison.OrdinalIgnoreCase))
            return X509CertificateLoader.LoadPkcs12FromFile(path, null);

        if (string.Equals(extension, ".pem", StringComparison.OrdinalIgnoreCase))
            return X509Certificate2.CreateFromPemFile(path);

        return X509CertificateLoader.LoadCertificateFromFile(path);
    }

    private static bool IsCertificateAuthority(X509Certificate2 certificate)
    {
        foreach (var extension in certificate.Extensions.OfType<X509BasicConstraintsExtension>())
            return extension.CertificateAuthority;

        return false;
    }

    private static bool IsRunningInContainer(IWebHostEnvironment environment)
    {
        var dotnetFlag = Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER");
        if (string.Equals(dotnetFlag, "true", StringComparison.OrdinalIgnoreCase)) return true;

        try
        {
            var root = Path.GetPathRoot(environment.ContentRootPath);
            var dockerEnvPath = Path.Combine(root ?? Path.DirectorySeparatorChar.ToString(), ".dockerenv");
            if (File.Exists("/.dockerenv") || File.Exists(dockerEnvPath)) return true;
        }
        catch
        {
            // ignored
        }

        return false;
    }
}