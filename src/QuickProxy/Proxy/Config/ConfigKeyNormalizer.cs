namespace QuickProxy.Proxy.Config;

internal static class ConfigKeyNormalizer
{
    public static string NormalizeKey(string value)
    {
        var parts = (value ?? string.Empty)
            .Trim()
            .Trim('/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return string.Join('/', parts);
    }

    public static string NormalizePrefix(string? value)
    {
        var normalized = NormalizeKey(value ?? string.Empty);
        if (string.IsNullOrWhiteSpace(normalized)) return string.Empty;

        return normalized.EndsWith('/') ? normalized : $"{normalized}/";
    }
}