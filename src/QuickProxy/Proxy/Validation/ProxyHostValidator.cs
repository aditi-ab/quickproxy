using System.Text.RegularExpressions;
using QuickProxy.Proxy.Models;

namespace QuickProxy.Proxy.Validation;

public static partial class ProxyHostValidator
{
    private const string ContainerNamePlaceholder = "{container.name}";
    private const string LegacyContainerNamePlaceholder = "{containername}";

    private static readonly HashSet<string> AllowedSchemes = new(StringComparer.OrdinalIgnoreCase)
    {
        "http",
        "https"
    };

    [GeneratedRegex("^[a-z0-9]+(?:-[a-z0-9]+)*$")]
    private static partial Regex HostIdRegex();

    [GeneratedRegex("^[a-zA-Z0-9.-]+(?::[0-9]{1,5})?$")]
    private static partial Regex DomainRegex();

    [GeneratedRegex(@"\{label\.([^{}]+)\}", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LabelPlaceholderRegex();

    [GeneratedRegex(@"\{\{([a-z0-9_-]+(?:\.[a-z0-9_-]+)*)\}\}",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex EscapedTemplatePlaceholderRegex();

    [GeneratedRegex(@"\{([a-z0-9_-]+(?:\.[a-z0-9_-]+)*)\}", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TemplatePlaceholderRegex();

    public static ProxyHostValidationResult ValidateSingle(ProxyHostConfig config, string expectedId)
    {
        var result = new ProxyHostValidationResult();
        if (config is null)
        {
            result.Errors.Add("Config cannot be null.");
            return result;
        }

        if (string.IsNullOrWhiteSpace(config.Id))
        {
            result.Errors.Add("id is required.");
        }
        else
        {
            if (!HostIdRegex().IsMatch(config.Id)) result.Errors.Add("id contains invalid characters.");

            if (!string.Equals(config.Id, expectedId, StringComparison.OrdinalIgnoreCase))
                result.Errors.Add("id must match the filename.");
        }

        if (config.Mode is not (ProxyHostMode.Manual or ProxyHostMode.AutomaticContainer))
            result.Errors.Add("mode is invalid.");

        if (config.Mode == ProxyHostMode.AutomaticContainer)
        {
            ValidateAutomaticContainer(config.AutomaticContainer, result);
        }
        else if (config.DomainNames.Count == 0)
        {
            result.Errors.Add("domainNames must contain at least one host.");
        }
        else
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var domain in config.DomainNames)
            {
                if (string.IsNullOrWhiteSpace(domain))
                {
                    result.Errors.Add("domainNames contains an empty value.");
                    continue;
                }

                var trimmed = domain.Trim();
                if (!DomainRegex().IsMatch(trimmed))
                    result.Errors.Add($"domainNames contains invalid host '{trimmed}'.");

                if (!seen.Add(trimmed)) result.Errors.Add($"domainNames contains duplicate host '{trimmed}'.");
            }
        }

        if (config.Routes.Count == 0) result.Errors.Add("routes must contain at least one route.");

        var routePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var route in config.Routes)
        {
            if (string.IsNullOrWhiteSpace(route.Path))
                result.Errors.Add("routes contains an empty path.");
            else if (!route.Path.StartsWith('/')) result.Errors.Add($"route path '{route.Path}' must start with '/'.");

            if (!routePaths.Add(route.Path)) result.Errors.Add($"routes contains duplicate path '{route.Path}'.");

            if (route.UpstreamMode is not (ProxyHostUpstreamMode.Manual or ProxyHostUpstreamMode.Container))
            {
                result.Errors.Add($"route '{route.Path}' upstreamMode is invalid.");
                continue;
            }

            if (route.UpstreamMode == ProxyHostUpstreamMode.Container)
                ValidateContainerTarget(route.Container, $"route '{route.Path}'", result,
                    config.Mode != ProxyHostMode.AutomaticContainer);
            else
                ValidateUpstream(route.Upstream, $"route '{route.Path}' upstream", result);

            if (route.RewriteMode == ProxyRouteRewriteMode.ReplacePrefix)
            {
                if (string.IsNullOrWhiteSpace(route.RewriteTargetPath))
                    result.Errors.Add(
                        $"route '{route.Path}'.rewriteTargetPath is required when rewriteMode is 'replacePrefix'.");
                else if (!route.RewriteTargetPath.StartsWith('/'))
                    result.Errors.Add($"route '{route.Path}'.rewriteTargetPath must start with '/'.");
            }
        }

        ValidateTls(config.Tls, result);

        return result;
    }

    public static List<string> ValidateAcrossHosts(IEnumerable<ProxyHostConfig> hosts)
    {
        var errors = new List<string>();
        var domainOwners = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var host in hosts.Where(h => h.Enabled))
        {
            if (host.Mode != ProxyHostMode.Manual) continue;

            foreach (var domain in host.DomainNames.Select(d => d.Trim()))
                if (domainOwners.TryGetValue(domain, out var existing))
                    errors.Add($"Domain '{domain}' is used by both '{existing}' and '{host.Id}'.");
                else
                    domainOwners[domain] = host.Id;
        }

        return errors;
    }

    private static void ValidateUpstream(UpstreamTarget upstream, string name, ProxyHostValidationResult result)
    {
        if (!AllowedSchemes.Contains(upstream.Scheme)) result.Errors.Add($"{name}.scheme must be 'http' or 'https'.");

        if (string.IsNullOrWhiteSpace(upstream.Host)) result.Errors.Add($"{name}.host is required.");

        if (upstream.Port is < 1 or > 65535) result.Errors.Add($"{name}.port must be between 1 and 65535.");
    }

    private static void ValidateContainerTarget(ContainerUpstreamTarget container, string prefix,
        ProxyHostValidationResult result, bool requireContainerName)
    {
        if (!AllowedSchemes.Contains(container.Scheme))
            result.Errors.Add($"{prefix}.container.scheme must be 'http' or 'https'.");

        if (requireContainerName && string.IsNullOrWhiteSpace(container.ContainerName))
            result.Errors.Add($"{prefix}.container.containerName is required.");

        if (container.Port is < 1 or > 65535)
            result.Errors.Add($"{prefix}.container.port must be between 1 and 65535.");

        if (container.PortResolutionMode is not (ContainerPortResolutionMode.Container
            or ContainerPortResolutionMode.Published))
            result.Errors.Add($"{prefix}.container.portResolutionMode is invalid.");
    }

    private static void ValidateAutomaticContainer(AutomaticContainerProxyHostConfig automaticContainer,
        ProxyHostValidationResult result)
    {
        if (automaticContainer.LabelSelectors.Count == 0)
            result.Errors.Add(
                "automaticContainer.labelSelectors must contain at least one selector when mode is 'automaticContainer'.");

        if (automaticContainer.DomainTemplates.Count == 0)
            result.Errors.Add("automaticContainer.domainTemplates must contain at least one value.");

        var selectorKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var selector in automaticContainer.LabelSelectors)
        {
            if (string.IsNullOrWhiteSpace(selector.Key))
            {
                result.Errors.Add("automaticContainer.labelSelectors contains an empty key.");
                continue;
            }

            var trimmedKey = selector.Key.Trim();
            if (!selectorKeys.Add(trimmedKey))
                result.Errors.Add($"automaticContainer.labelSelectors contains duplicate key '{trimmedKey}'.");

            var patterns = selector.ValuePatterns
                .Concat(string.IsNullOrWhiteSpace(selector.ValuePattern) ? [] : [selector.ValuePattern])
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            foreach (var pattern in patterns)
                try
                {
                    _ = new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                }
                catch (Exception ex)
                {
                    result.Errors.Add(
                        $"automaticContainer.labelSelectors['{trimmedKey}'] contains invalid value pattern '{pattern}': {ex.Message}");
                }
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var template in automaticContainer.DomainTemplates)
        {
            if (string.IsNullOrWhiteSpace(template))
            {
                result.Errors.Add("automaticContainer.domainTemplates contains an empty value.");
                continue;
            }

            var trimmed = template.Trim();
            var normalized = trimmed
                .Replace(ContainerNamePlaceholder, "container-name", StringComparison.OrdinalIgnoreCase)
                .Replace(LegacyContainerNamePlaceholder, "container-name", StringComparison.OrdinalIgnoreCase);
            normalized = LabelPlaceholderRegex().Replace(normalized, "label-value");
            normalized = EscapedTemplatePlaceholderRegex().Replace(normalized, "template-literal");
            normalized = TemplatePlaceholderRegex().Replace(normalized, "template-value");
            if (normalized.Contains('{') || normalized.Contains('}'))
                result.Errors.Add(
                    $"automaticContainer.domainTemplates contains unsupported placeholders in '{trimmed}'.");

            if (!DomainRegex().IsMatch(normalized))
                result.Errors.Add($"automaticContainer.domainTemplates contains invalid host template '{trimmed}'.");

            if (!seen.Add(trimmed))
                result.Errors.Add($"automaticContainer.domainTemplates contains duplicate value '{trimmed}'.");
        }
    }

    private static void ValidateTls(TlsBindingConfig tls, ProxyHostValidationResult result)
    {
        if (tls.Mode is not (TlsBindingMode.None or TlsBindingMode.Pfx or TlsBindingMode.Thumbprint))
        {
            result.Errors.Add("tls.mode is invalid.");
            return;
        }

        if (tls.Mode == TlsBindingMode.Pfx && string.IsNullOrWhiteSpace(tls.PfxPath))
            result.Errors.Add("tls.pfxPath is required when tls.mode is 'pfx'.");

        if (tls.Mode == TlsBindingMode.Thumbprint && string.IsNullOrWhiteSpace(tls.Thumbprint))
            result.Errors.Add("tls.thumbprint is required when tls.mode is 'thumbprint'.");
    }
}