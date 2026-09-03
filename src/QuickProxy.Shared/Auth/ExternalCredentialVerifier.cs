using System.DirectoryServices.Protocols;
using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using QuickProxy.Shared.Configuration;

namespace QuickProxy.Shared.Auth;

public interface IExternalCredentialVerifier
{
    Task<bool> VerifyAsync(string email, string password, CancellationToken cancellationToken = default);
}

public sealed class ExternalCredentialVerifier(
    IOptions<AuthProvidersSettings> options,
    IHttpClientFactory httpClientFactory,
    ILogger<ExternalCredentialVerifier> logger) : IExternalCredentialVerifier
{
    public async Task<bool> VerifyAsync(string email, string password, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password)) return false;

        var settings = options.Value;
        if (settings.Ldap.Enabled && VerifyLdap(settings.Ldap, email, password)) return true;

        if (settings.Entra.Enabled &&
            await VerifyEntraAsync(settings.Entra, email, password, cancellationToken)) return true;

        return false;
    }

    private bool VerifyLdap(LdapAuthSettings settings, string email, string password)
    {
        if (string.IsNullOrWhiteSpace(settings.Server))
        {
            logger.LogWarning("LDAP auth is enabled but server is not configured.");
            return false;
        }

        try
        {
            var identifier = new LdapDirectoryIdentifier(settings.Server, settings.Port);
            using var connection = new LdapConnection(identifier)
            {
                AuthType = AuthType.Basic,
                Credential = new NetworkCredential(BuildBindIdentity(settings, email), password)
            };

            connection.SessionOptions.ProtocolVersion = 3;
            connection.SessionOptions.SecureSocketLayer = settings.UseSsl;
            connection.Bind();
            return true;
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "LDAP authentication failed for '{Email}'.", email);
            return false;
        }
    }

    private async Task<bool> VerifyEntraAsync(
        EntraAuthSettings settings,
        string email,
        string password,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(settings.TenantId) || string.IsNullOrWhiteSpace(settings.ClientId))
        {
            logger.LogWarning("Entra auth is enabled but tenantId/clientId are not fully configured.");
            return false;
        }

        var tokenUrl = $"https://login.microsoftonline.com/{settings.TenantId}/oauth2/v2.0/token";
        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "password",
            ["client_id"] = settings.ClientId,
            ["username"] = email,
            ["password"] = password,
            ["scope"] = string.IsNullOrWhiteSpace(settings.Scope) ? "openid profile email" : settings.Scope
        };

        if (!string.IsNullOrWhiteSpace(settings.ClientSecret)) form["client_secret"] = settings.ClientSecret;

        try
        {
            using var client = httpClientFactory.CreateClient(nameof(ExternalCredentialVerifier));
            using var response = await client.PostAsync(tokenUrl, new FormUrlEncodedContent(form), cancellationToken);
            if (!response.IsSuccessStatusCode) return false;

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            return document.RootElement.TryGetProperty("access_token", out var token) &&
                   token.ValueKind == JsonValueKind.String &&
                   !string.IsNullOrWhiteSpace(token.GetString());
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Entra authentication failed for '{Email}'.", email);
            return false;
        }
    }

    private static string BuildBindIdentity(LdapAuthSettings settings, string email)
    {
        var username = email.Split('@')[0];
        if (!string.IsNullOrWhiteSpace(settings.BindIdentityPattern))
            return settings.BindIdentityPattern
                .Replace("{email}", email, StringComparison.OrdinalIgnoreCase)
                .Replace("{username}", username, StringComparison.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(settings.Domain)) return $"{username}@{settings.Domain}";

        return email;
    }
}