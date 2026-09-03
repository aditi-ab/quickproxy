using System.Text;

namespace QuickProxy.Proxy.Config.Storage;

internal static class ConfigValueEncoding
{
    private const string Prefix = "b64:";

    public static string EncodeForStorage(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;

        return Prefix + Convert.ToBase64String(Encoding.UTF8.GetBytes(value));
    }

    public static string DecodeFromStorage(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;

        if (!value.StartsWith(Prefix, StringComparison.Ordinal)) return value;

        var payload = value[Prefix.Length..];
        if (string.IsNullOrWhiteSpace(payload)) return string.Empty;

        try
        {
            return Encoding.UTF8.GetString(Convert.FromBase64String(payload));
        }
        catch (FormatException)
        {
            return value;
        }
    }

    public static string NormalizeStoredValue(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;

        return EncodeForStorage(DecodeFromStorage(value));
    }

    public static string EncodeForApi(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;

        return Convert.ToBase64String(Encoding.UTF8.GetBytes(value));
    }

    public static string DecodeFromApi(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;

        try
        {
            return Encoding.UTF8.GetString(Convert.FromBase64String(value));
        }
        catch (FormatException)
        {
            return value;
        }
    }
}