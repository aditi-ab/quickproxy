namespace QuickProxy.Shared.Configuration;

public sealed class ListenSettings
{
    public int HttpPort { get; set; } = 5045;
    public int HttpsPort { get; set; } = 7159;
    public int InternalPort { get; set; } = 9000;
    public bool AdminUseHttps { get; set; } = false;
    public string AdminAccess { get; set; } = "localhost";
    public AdminCertificateSettings AdminCertificate { get; set; } = new();

    public bool IsAdminLocalhostOnly()
    {
        return !string.Equals(AdminAccess, "any", StringComparison.OrdinalIgnoreCase);
    }

    public void ValidateUniquePorts()
    {
        var uniquePorts = new HashSet<int>();
        foreach (var port in new[] { HttpPort, HttpsPort, InternalPort }.Where(p => p > 0))
            if (!uniquePorts.Add(port))
                throw new InvalidOperationException(
                    $"Duplicate listen port '{port}' found in Listen settings. Ports must be unique.");
    }
}

public sealed class AdminCertificateSettings
{
    public string? Path { get; set; }
    public string? Password { get; set; }
    public string? PasswordEnvVar { get; set; }
}