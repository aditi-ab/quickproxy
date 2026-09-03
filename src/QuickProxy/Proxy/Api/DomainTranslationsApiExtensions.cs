using System.Text.RegularExpressions;
using QuickProxy.Proxy.Models;
using QuickProxy.Proxy.Runtime;
using QuickProxy.Proxy.Storage;
using QuickProxy.Shared.Web;

namespace QuickProxy.Proxy.Api;

public static partial class DomainTranslationsApiExtensions
{
    [GeneratedRegex("^[a-z0-9]+(?:-[a-z0-9]+)*$")]
    private static partial Regex RuleIdRegex();

    [GeneratedRegex("^[a-zA-Z0-9.-]+$")]
    private static partial Regex DomainRegex();

    public static IEndpointRouteBuilder MapDomainTranslationsApi(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup($"{InternalApiPaths.AdminRoot}/domain-translations").RequireAuthorization();

        group.MapGet("/", (IDomainTranslationRuntime runtime) => { return Results.Ok(runtime.GetRules()); });

        group.MapGet("/{id}", (string id, IDomainTranslationRuntime runtime) =>
        {
            var rule = runtime.GetRule(id);
            return rule is null ? NotFound(id) : Results.Ok(rule);
        });

        group.MapPost("/", (
            DomainTranslationRule rule,
            IDomainTranslationStore store,
            IDomainTranslationRuntime runtime,
            ICertificateStore certificateStore,
            ICertificateRuntimeCache certificateRuntimeCache) =>
        {
            if (store.Exists(rule.Id))
                return Conflict("duplicate_id", $"Domain translation '{rule.Id}' already exists.");

            var validationErrors = ValidateForWrite(rule, runtime.GetRules(), certificateStore, null);
            if (validationErrors.Count > 0) return Validation(validationErrors);

            store.Upsert(NormalizeForStorage(rule));
            runtime.TryReload();
            certificateRuntimeCache.InvalidateAll();
            return Results.Created($"{InternalApiPaths.AdminRoot}/domain-translations/{rule.Id}", rule);
        });

        group.MapPut("/{id}", (
            string id,
            DomainTranslationRule rule,
            IDomainTranslationStore store,
            IDomainTranslationRuntime runtime,
            ICertificateStore certificateStore,
            IIssuedCertificateService issuedCertificateService,
            ICertificateRuntimeCache certificateRuntimeCache) =>
        {
            rule.Id = id;
            var validationErrors = ValidateForWrite(rule, runtime.GetRules(), certificateStore, id);
            if (validationErrors.Count > 0) return Validation(validationErrors);

            issuedCertificateService.DeleteForDomainTranslation(id);
            store.Upsert(NormalizeForStorage(rule));
            runtime.TryReload();
            certificateRuntimeCache.InvalidateAll();
            return Results.Ok(rule);
        });

        group.MapDelete("/{id}", (
            string id,
            IDomainTranslationStore store,
            IDomainTranslationRuntime runtime,
            IIssuedCertificateService issuedCertificateService,
            ICertificateRuntimeCache certificateRuntimeCache) =>
        {
            if (!store.Delete(id)) return NotFound(id);

            if (issuedCertificateService.DeleteForDomainTranslation(id)) certificateRuntimeCache.InvalidateAll();

            runtime.TryReload();
            return Results.NoContent();
        });

        return app;
    }

    private static DomainTranslationRule NormalizeForStorage(DomainTranslationRule rule)
    {
        return new DomainTranslationRule
        {
            Id = (rule.Id ?? string.Empty).Trim(),
            Enabled = rule.Enabled,
            SourceDomain = NormalizeDomain(rule.SourceDomain),
            TargetDomain = NormalizeDomain(rule.TargetDomain),
            CertificateId = string.IsNullOrWhiteSpace(rule.CertificateId) ? null : rule.CertificateId.Trim(),
            RewriteHostHeader = rule.RewriteHostHeader
        };
    }

    private static List<string> ValidateForWrite(
        DomainTranslationRule candidate,
        IReadOnlyList<DomainTranslationRule> currentRules,
        ICertificateStore certificateStore,
        string? replaceId)
    {
        var errors = new List<string>();
        candidate.Id = (candidate.Id ?? string.Empty).Trim();
        candidate.SourceDomain = NormalizeDomain(candidate.SourceDomain);
        candidate.TargetDomain = NormalizeDomain(candidate.TargetDomain);

        if (string.IsNullOrWhiteSpace(candidate.Id) || !RuleIdRegex().IsMatch(candidate.Id))
            errors.Add("id is required and must be lowercase kebab-case.");

        if (!IsValidDomain(candidate.SourceDomain))
            errors.Add("sourceDomain must be a valid hostname without scheme, path, or port.");

        if (!IsValidDomain(candidate.TargetDomain))
            errors.Add("targetDomain must be a valid hostname without scheme, path, or port.");

        if (string.Equals(candidate.SourceDomain, candidate.TargetDomain, StringComparison.OrdinalIgnoreCase))
            errors.Add("sourceDomain and targetDomain must be different.");

        var duplicate = currentRules.FirstOrDefault(x =>
            x.Enabled &&
            candidate.Enabled &&
            (replaceId is null || !string.Equals(x.Id, replaceId, StringComparison.OrdinalIgnoreCase)) &&
            string.Equals(x.SourceDomain, candidate.SourceDomain, StringComparison.OrdinalIgnoreCase));
        if (duplicate is not null)
            errors.Add($"sourceDomain '{candidate.SourceDomain}' is already used by '{duplicate.Id}'.");

        if (!string.IsNullOrWhiteSpace(candidate.CertificateId) &&
            !certificateStore.Exists(candidate.CertificateId.Trim()))
            errors.Add($"certificateId '{candidate.CertificateId}' was not found.");

        return errors;
    }

    private static string NormalizeDomain(string? value)
    {
        return (value ?? string.Empty).Trim().Trim('.').ToLowerInvariant();
    }

    private static bool IsValidDomain(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Contains('/') || value.Contains(':')) return false;

        if (!DomainRegex().IsMatch(value)) return false;

        return value.Split('.', StringSplitOptions.RemoveEmptyEntries).All(part =>
            part.Length > 0 &&
            !part.StartsWith('-') &&
            !part.EndsWith('-'));
    }

    private static IResult Validation(List<string> details)
    {
        return Results.BadRequest(new
        {
            code = "validation_error",
            message = "Validation failed.",
            details
        });
    }

    private static IResult Conflict(string code, string message)
    {
        return Results.Conflict(new
        {
            code,
            message
        });
    }

    private static IResult NotFound(string id)
    {
        return Results.NotFound(new
        {
            code = "not_found",
            message = $"Domain translation '{id}' was not found."
        });
    }
}