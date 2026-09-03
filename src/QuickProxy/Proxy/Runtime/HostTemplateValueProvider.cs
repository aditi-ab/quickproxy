using System.Collections.ObjectModel;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using QuickProxy.Proxy.Config;
using QuickProxy.Proxy.Config.Models;
using QuickProxy.Proxy.Config.Storage;

namespace QuickProxy.Proxy.Runtime;

public sealed partial class HostTemplateValueProvider : IHostTemplateValueProvider
{
    private const string EscapedOpenToken = "\u0001";
    private const string EscapedCloseToken = "\u0002";
    private readonly ILogger<HostTemplateValueProvider> _logger;

    private readonly IServiceProvider _serviceProvider;

    public HostTemplateValueProvider(
        IConfiguration configuration,
        IServiceProvider serviceProvider,
        ILogger<HostTemplateValueProvider> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        FlattenSection(configuration.GetSection("TemplateValues"), values, []);

        var serverName = Environment.GetEnvironmentVariable("SERVERNAME")?.Trim();
        if (!string.IsNullOrWhiteSpace(serverName))
        {
            values["server.name"] = NormalizeServerName(serverName);
        }
        else if (!values.TryGetValue("server.name", out var configuredServerName) ||
                 string.IsNullOrWhiteSpace(configuredServerName))
        {
            var machineName = Environment.MachineName?.Trim();
            if (!string.IsNullOrWhiteSpace(machineName)) values["server.name"] = NormalizeServerName(machineName);
        }
        else
        {
            values["server.name"] = NormalizeServerName(configuredServerName);
        }

        var serverIp = Environment.GetEnvironmentVariable("SERVERIP")?.Trim();
        if (!string.IsNullOrWhiteSpace(serverIp))
        {
            values["server.ip"] = serverIp;
        }
        else if (!values.TryGetValue("server.ip", out var configuredServerIp) ||
                 string.IsNullOrWhiteSpace(configuredServerIp))
        {
            var resolvedServerIp = TryResolveServerIp();
            if (!string.IsNullOrWhiteSpace(resolvedServerIp)) values["server.ip"] = resolvedServerIp;
        }

        TemplateValues = new ReadOnlyDictionary<string, string>(values);
    }

    public IReadOnlyDictionary<string, string> TemplateValues { get; }

    public string ReplacePlaceholders(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return input;

        var resolvedKvValues = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        var escaped = EscapedTemplatePlaceholderRegex().Replace(input, match =>
            $"{EscapedOpenToken}{match.Groups[1].Value}{EscapedCloseToken}");

        var replaced = TemplatePlaceholderRegex().Replace(escaped, match =>
        {
            var key = match.Groups[1].Value;
            if (key.StartsWith("kv.", StringComparison.OrdinalIgnoreCase))
            {
                if (!resolvedKvValues.TryGetValue(key, out var resolvedValue))
                {
                    resolvedValue = TryResolveConfigValue(key["kv.".Length..]);
                    resolvedKvValues[key] = resolvedValue;
                }

                return resolvedValue ?? match.Value;
            }

            if (key.StartsWith("env.", StringComparison.OrdinalIgnoreCase))
            {
                var envName = key["env.".Length..];
                var envValue = Environment.GetEnvironmentVariable(envName);
                if (envValue is not null) return envValue;
            }

            if (TemplateValues.TryGetValue(key, out var value)) return value ?? string.Empty;

            return match.Value;
        });

        return replaced
            .Replace(EscapedOpenToken, "{", StringComparison.Ordinal)
            .Replace(EscapedCloseToken, "}", StringComparison.Ordinal);
    }

    public string ReplaceKvPlaceholders(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return input;

        var resolvedKvValues = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        var escaped = EscapedTemplatePlaceholderRegex().Replace(input, match =>
            match.Groups[1].Value.StartsWith("kv.", StringComparison.OrdinalIgnoreCase)
                ? $"{EscapedOpenToken}{match.Groups[1].Value}{EscapedCloseToken}"
                : match.Value);

        var replaced = TemplatePlaceholderRegex().Replace(escaped, match =>
        {
            var key = match.Groups[1].Value;
            if (!key.StartsWith("kv.", StringComparison.OrdinalIgnoreCase)) return match.Value;

            if (!resolvedKvValues.TryGetValue(key, out var resolvedValue))
            {
                resolvedValue = TryResolveConfigValue(key["kv.".Length..]);
                resolvedKvValues[key] = resolvedValue;
            }

            return resolvedValue ?? match.Value;
        });

        return replaced
            .Replace(EscapedOpenToken, "{", StringComparison.Ordinal)
            .Replace(EscapedCloseToken, "}", StringComparison.Ordinal);
    }

    [GeneratedRegex(@"\{\{((?:[a-z0-9_-]+(?:\.[a-z0-9_-]+)*)|(?:kv\.[^{}]+))\}\}",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex EscapedTemplatePlaceholderRegex();

    [GeneratedRegex(@"\{((?:[a-z0-9_-]+(?:\.[a-z0-9_-]+)*)|(?:kv\.[^{}]+))\}",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TemplatePlaceholderRegex();

    private string? TryResolveConfigValue(string rawKey)
    {
        var normalizedKey = ConfigKeyNormalizer.NormalizeKey(rawKey);
        if (string.IsNullOrWhiteSpace(normalizedKey)) return null;

        var readService = _serviceProvider.GetService<IConfigReadService>();
        if (readService is null) return null;

        try
        {
            var entry = readService.GetAsync(normalizedKey).GetAwaiter().GetResult();
            return entry is null ? null : TryReadEntryValue(entry);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to resolve template placeholder '{{kv.{Key}}}'.", normalizedKey);
            return null;
        }
    }

    private string? TryReadEntryValue(MergedConfigEntry entry)
    {
        if (entry.PayloadKind != ConfigPayloadKind.Text)
        {
            _logger.LogWarning(
                "Template placeholder '{{kv.{Key}}}' references a non-text config entry and cannot be expanded.",
                entry.Key);
            return null;
        }

        if (entry.EntryType != ConfigEntryType.Secret) return entry.Value;

        var encryptionService = _serviceProvider.GetService<IConfigEncryptionService>();
        if (encryptionService is null)
        {
            _logger.LogWarning(
                "Template placeholder '{{kv.{Key}}}' references a secret entry, but config encryption services are unavailable.",
                entry.Key);
            return null;
        }

        return encryptionService.DecryptString(entry.EncryptedValue ?? string.Empty);
    }

    private static void FlattenSection(IConfigurationSection section, IDictionary<string, string> values,
        IReadOnlyList<string> path)
    {
        var children = section.GetChildren().ToArray();
        if (children.Length == 0)
        {
            if (section.Value is null || path.Count == 0) return;

            var key = string.Join('.', path).ToLowerInvariant();
            values[key] = section.Value;
            return;
        }

        foreach (var child in children)
        {
            var childPath = new List<string>(path) { child.Key };
            FlattenSection(child, values, childPath);
        }
    }

    private static string? TryResolveServerIp()
    {
        try
        {
            var candidate = NetworkInterface.GetAllNetworkInterfaces()
                .Where(x =>
                    x.OperationalStatus == OperationalStatus.Up
                    && x.NetworkInterfaceType != NetworkInterfaceType.Loopback
                    && x.NetworkInterfaceType != NetworkInterfaceType.Tunnel)
                .SelectMany(x => x.GetIPProperties().UnicastAddresses)
                .Select(x => x.Address)
                .Where(IsUsableIpv4Address)
                .Select(x => x.ToString())
                .FirstOrDefault();

            if (!string.IsNullOrWhiteSpace(candidate)) return candidate;

            return Dns.GetHostAddresses(Dns.GetHostName())
                .Where(IsUsableIpv4Address)
                .Select(x => x.ToString())
                .FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }

    private static bool IsUsableIpv4Address(IPAddress address)
    {
        if (address.AddressFamily != AddressFamily.InterNetwork || IPAddress.IsLoopback(address)) return false;

        var bytes = address.GetAddressBytes();
        return bytes.Length == 4
               && !(bytes[0] == 169 && bytes[1] == 254)
               && !address.Equals(IPAddress.Any);
    }

    private static string NormalizeServerName(string value)
    {
        return (value ?? string.Empty).Trim().ToLowerInvariant();
    }
}