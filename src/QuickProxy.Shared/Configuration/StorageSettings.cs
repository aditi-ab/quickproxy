namespace QuickProxy.Shared.Configuration;

public sealed class StorageSettings
{
    public string Provider { get; set; } = "sqlite";
    public string ConnectionString { get; set; } = "Data Source=Data/quickproxy.db";

    public void Validate(string sectionName)
    {
        if (!string.Equals(Provider, "sqlite", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(Provider, "sqlserver", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                $"{sectionName}:Storage:Provider must be either 'sqlite' or 'sqlserver'.");

        if (string.IsNullOrWhiteSpace(ConnectionString))
            throw new InvalidOperationException($"{sectionName}:Storage:ConnectionString is required.");
    }
}