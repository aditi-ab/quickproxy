using QuickProxy.Proxy.Models;
using QuickProxy.Proxy.Storage;

namespace QuickProxy.Proxy.Runtime;

public sealed class DomainTranslationRuntime(
    IDomainTranslationStore store,
    ILogger<DomainTranslationRuntime> logger) : IDomainTranslationRuntime
{
    private readonly object _sync = new();
    private volatile Snapshot _snapshot = Snapshot.Empty;

    public IReadOnlyList<DomainTranslationRule> GetRules()
    {
        return _snapshot.Rules;
    }

    public DomainTranslationRule? GetRule(string id)
    {
        _snapshot.ById.TryGetValue(id, out var rule);
        return rule;
    }

    public DomainTranslationRule? MatchRule(string? hostHeader)
    {
        var host = StripPort(NormalizeHost(hostHeader));
        if (string.IsNullOrWhiteSpace(host)) return null;

        foreach (var rule in _snapshot.EnabledRulesBySpecificity)
            if (HostMatches(host, rule.SourceDomain))
                return rule;

        return null;
    }

    public string TranslateHost(string host, DomainTranslationRule rule)
    {
        var normalizedHost = NormalizeHost(host);
        var hostOnly = StripPort(normalizedHost);
        var leading = hostOnly.Length == rule.SourceDomain.Length
            ? string.Empty
            : hostOnly[..^(rule.SourceDomain.Length + 1)];
        var translatedHost = string.IsNullOrWhiteSpace(leading)
            ? rule.TargetDomain
            : $"{leading}.{rule.TargetDomain}";
        var port = ExtractPort(normalizedHost);
        return port is null ? translatedHost : $"{translatedHost}:{port.Value}";
    }

    public bool TryReload()
    {
        lock (_sync)
        {
            var rules = store.List()
                .Where(x => x is not null)
                .Select(NormalizeRule)
                .OrderBy(x => x.Id, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            _snapshot = new Snapshot(
                rules,
                rules.ToDictionary(x => x.Id, StringComparer.OrdinalIgnoreCase),
                rules.Where(x => x.Enabled)
                    .OrderByDescending(x => x.SourceDomain.Length)
                    .ThenBy(x => x.SourceDomain, StringComparer.OrdinalIgnoreCase)
                    .ToArray());
            logger.LogInformation("Loaded {Count} domain translation rule(s).", rules.Length);
            return true;
        }
    }

    private static DomainTranslationRule NormalizeRule(DomainTranslationRule rule)
    {
        return new DomainTranslationRule
        {
            Id = (rule.Id ?? string.Empty).Trim(),
            Enabled = rule.Enabled,
            SourceDomain = NormalizeHost(rule.SourceDomain),
            TargetDomain = NormalizeHost(rule.TargetDomain),
            CertificateId = string.IsNullOrWhiteSpace(rule.CertificateId) ? null : rule.CertificateId.Trim(),
            RewriteHostHeader = rule.RewriteHostHeader
        };
    }

    private static bool HostMatches(string host, string sourceDomain)
    {
        return string.Equals(host, sourceDomain, StringComparison.OrdinalIgnoreCase)
               || host.EndsWith($".{sourceDomain}", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeHost(string? value)
    {
        return (value ?? string.Empty).Trim().Trim('.').ToLowerInvariant();
    }

    private static string StripPort(string host)
    {
        var colonIndex = host.LastIndexOf(':');
        if (colonIndex <= 0) return host;

        return int.TryParse(host[(colonIndex + 1)..], out _)
            ? host[..colonIndex]
            : host;
    }

    private static int? ExtractPort(string host)
    {
        var colonIndex = host.LastIndexOf(':');
        if (colonIndex <= 0) return null;

        return int.TryParse(host[(colonIndex + 1)..], out var port)
            ? port
            : null;
    }

    private sealed record Snapshot(
        IReadOnlyList<DomainTranslationRule> Rules,
        IReadOnlyDictionary<string, DomainTranslationRule> ById,
        IReadOnlyList<DomainTranslationRule> EnabledRulesBySpecificity)
    {
        public static Snapshot Empty { get; } = new(
            [],
            new Dictionary<string, DomainTranslationRule>(StringComparer.OrdinalIgnoreCase),
            []);
    }
}