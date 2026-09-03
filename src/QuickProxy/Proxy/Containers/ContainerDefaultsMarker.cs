using System.Security.Cryptography;
using System.Text;

namespace QuickProxy.Proxy.Containers;

internal static class ContainerDefaultsMarker
{
    public const string LabelKey = "quickproxy.internal.defaults-applied";

    public static string Build(string setId, ContainerDefaultsSet defaultsSet)
    {
        var serialized = new StringBuilder();
        serialized.Append((setId ?? string.Empty).Trim());

        foreach (var label in defaultsSet.Labels
                     .Where(x => !string.IsNullOrWhiteSpace(x.Key))
                     .OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
        {
            serialized.Append("|L:");
            serialized.Append(label.Key.Trim());
            serialized.Append('=');
            serialized.Append(label.Value ?? string.Empty);
        }

        foreach (var env in defaultsSet.EnvVars
                     .Where(x => !string.IsNullOrWhiteSpace(x.Key))
                     .OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
        {
            serialized.Append("|E:");
            serialized.Append(env.Key.Trim());
            serialized.Append('=');
            serialized.Append(env.Value ?? string.Empty);
        }

        foreach (var mount in (defaultsSet.MountBindings ?? [])
                 .Where(x => !string.IsNullOrWhiteSpace(x.HostPath) && !string.IsNullOrWhiteSpace(x.ContainerPath))
                 .OrderBy(x => x.ContainerPath, StringComparer.OrdinalIgnoreCase)
                 .ThenBy(x => x.HostPath, StringComparer.OrdinalIgnoreCase))
        {
            serialized.Append("|M:");
            serialized.Append(mount.HostPath.Trim());
            serialized.Append("=>");
            serialized.Append(mount.ContainerPath.Trim());
            serialized.Append(':');
            serialized.Append(mount.ReadOnly ? "ro" : "rw");
        }

        foreach (var mapping in (defaultsSet.HostMappings ?? [])
                 .Where(x => !string.IsNullOrWhiteSpace(x.Hostname) && !string.IsNullOrWhiteSpace(x.Address))
                 .OrderBy(x => x.Hostname, StringComparer.OrdinalIgnoreCase))
        {
            serialized.Append("|H:");
            serialized.Append(mapping.Hostname.Trim());
            serialized.Append("=>");
            serialized.Append(mapping.Address.Trim());
        }

        foreach (var alias in (defaultsSet.NetworkAliases ?? [])
                 .Where(x => !string.IsNullOrWhiteSpace(x.Network) && !string.IsNullOrWhiteSpace(x.Alias))
                 .OrderBy(x => x.Network, StringComparer.OrdinalIgnoreCase)
                 .ThenBy(x => x.Alias, StringComparer.OrdinalIgnoreCase))
        {
            serialized.Append("|N:");
            serialized.Append(alias.Network.Trim());
            serialized.Append("=>");
            serialized.Append(alias.Alias.Trim());
        }

        var bytes = Encoding.UTF8.GetBytes(serialized.ToString());
        var hash = SHA256.HashData(bytes);
        var fingerprint = Convert.ToHexString(hash).ToLowerInvariant()[..12];
        return $"{setId}:{fingerprint}";
    }
}