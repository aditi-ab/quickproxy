using System.DirectoryServices.Protocols;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.DataProtection;
using QuickProxy.Proxy.Config.Storage;
using QuickProxy.Proxy.Models;
using QuickProxy.Proxy.Storage;
using QuickProxy.Shared.Web;

namespace QuickProxy.Proxy.Api;

public interface IExternalAuthService
{
    IReadOnlyList<ExternalLoginProviderOption> ListLoginProviders();
    AuthProviderConfig? GetProvider(string id);

    Task<ExternalAuthResult?> AuthenticatePasswordAsync(string email, string password,
        CancellationToken cancellationToken = default);

    Task<string> BuildOidcChallengeUrlAsync(HttpContext context, string providerId, string? returnUrl,
        CancellationToken cancellationToken = default);

    Task<ExternalAuthResult> CompleteOidcAsync(HttpContext context, string providerId, string code, string state,
        CancellationToken cancellationToken = default);

    Task<ExternalAuthTestResult> TestLdapAsync(AuthProviderConfig provider,
        CancellationToken cancellationToken = default);

    Task<ExternalAuthTestResult> TestOidcDiscoveryAsync(AuthProviderConfig provider,
        CancellationToken cancellationToken = default);
}

public sealed record ExternalLoginProviderOption(string Id, string DisplayName, AuthProviderType Type);

public sealed record ExternalAuthResult(
    string ProviderId,
    string Subject,
    string Email,
    string? FullName,
    string ReturnUrl);

public sealed record ExternalAuthTestResult(bool Success, string Message);

public sealed class ExternalAuthService(
    IAuthProviderStore authProviderStore,
    IConfigEncryptionService encryptionService,
    IDataProtectionProvider dataProtectionProvider,
    IHttpClientFactory httpClientFactory,
    ILogger<ExternalAuthService> logger) : IExternalAuthService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IDataProtector _stateProtector =
        dataProtectionProvider.CreateProtector("QuickProxy.ExternalAuth.OidcState");

    public IReadOnlyList<ExternalLoginProviderOption> ListLoginProviders()
    {
        return authProviderStore.List()
            .Where(x => x.Enabled && x.Type == AuthProviderType.Oidc)
            .OrderBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Id, StringComparer.OrdinalIgnoreCase)
            .Select(x => new ExternalLoginProviderOption(x.Id, x.DisplayName, x.Type))
            .ToArray();
    }

    public AuthProviderConfig? GetProvider(string id)
    {
        return authProviderStore.Get(id);
    }

    public async Task<ExternalAuthResult?> AuthenticatePasswordAsync(string email, string password,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password)) return null;

        foreach (var provider in authProviderStore.List().Where(x => x.Enabled && x.Type == AuthProviderType.Ldap))
            try
            {
                var result = AuthenticateLdap(provider, email, password);
                if (result is not null) return await Task.FromResult(result);
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "LDAP authentication failed for provider '{ProviderId}'.", provider.Id);
            }

        return null;
    }

    public async Task<string> BuildOidcChallengeUrlAsync(HttpContext context, string providerId, string? returnUrl,
        CancellationToken cancellationToken = default)
    {
        var provider = RequireEnabledProvider(providerId, AuthProviderType.Oidc);
        var metadata = await GetOidcMetadataAsync(provider, cancellationToken);
        if (string.IsNullOrWhiteSpace(metadata.AuthorizationEndpoint))
            throw new InvalidOperationException("OIDC metadata did not include an authorization endpoint.");

        var codeVerifier = provider.Oidc.UsePkce ? CreateCodeVerifier() : string.Empty;
        var state = ProtectState(new OidcStatePayload(provider.Id, NormalizeReturnUrl(returnUrl), codeVerifier,
            DateTimeOffset.UtcNow));
        var redirectUri = BuildRedirectUri(context, provider.Id);
        var parameters = new List<KeyValuePair<string, string>>
        {
            new("response_type", "code"),
            new("client_id", provider.Oidc.ClientId),
            new("redirect_uri", redirectUri),
            new("scope",
                string.IsNullOrWhiteSpace(provider.Oidc.Scopes) ? "openid profile email" : provider.Oidc.Scopes),
            new("state", state)
        };

        if (provider.Oidc.UsePkce)
        {
            parameters.Add(new KeyValuePair<string, string>("code_challenge_method", "S256"));
            parameters.Add(new KeyValuePair<string, string>("code_challenge", CreateCodeChallenge(codeVerifier)));
        }

        var separator = metadata.AuthorizationEndpoint.Contains('?', StringComparison.Ordinal) ? "&" : "?";
        return metadata.AuthorizationEndpoint + separator + string.Join("&",
            parameters.Select(x => $"{Uri.EscapeDataString(x.Key)}={Uri.EscapeDataString(x.Value)}"));
    }

    public async Task<ExternalAuthResult> CompleteOidcAsync(HttpContext context, string providerId, string code,
        string state, CancellationToken cancellationToken = default)
    {
        var provider = RequireEnabledProvider(providerId, AuthProviderType.Oidc);
        var payload = UnprotectState(state);
        if (!string.Equals(payload.ProviderId, provider.Id, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("OIDC state was issued for a different provider.");

        if (DateTimeOffset.UtcNow - payload.IssuedAtUtc > TimeSpan.FromMinutes(10))
            throw new InvalidOperationException("OIDC state has expired.");

        var metadata = await GetOidcMetadataAsync(provider, cancellationToken);
        if (string.IsNullOrWhiteSpace(metadata.TokenEndpoint))
            throw new InvalidOperationException("OIDC metadata did not include a token endpoint.");

        using var client = httpClientFactory.CreateClient(nameof(ExternalAuthService));
        var tokenRequest = new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = BuildRedirectUri(context, provider.Id),
            ["client_id"] = provider.Oidc.ClientId
        };

        var clientSecret = DecryptSecret(provider.Oidc.EncryptedClientSecret);
        if (!string.IsNullOrWhiteSpace(clientSecret)) tokenRequest["client_secret"] = clientSecret;

        if (provider.Oidc.UsePkce) tokenRequest["code_verifier"] = payload.CodeVerifier;

        using var tokenResponse = await client.PostAsync(metadata.TokenEndpoint,
            new FormUrlEncodedContent(tokenRequest), cancellationToken);
        var tokenJson = await tokenResponse.Content.ReadAsStringAsync(cancellationToken);
        if (!tokenResponse.IsSuccessStatusCode)
            throw new InvalidOperationException(
                $"OIDC token exchange failed with status {(int)tokenResponse.StatusCode}.");

        var tokenPayload = JsonSerializer.Deserialize<OidcTokenResponse>(tokenJson, JsonOptions)
                           ?? throw new InvalidOperationException("OIDC token response was empty.");

        Dictionary<string, string?> claims = new(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(tokenPayload.IdToken)) claims = ReadJwtPayload(tokenPayload.IdToken);

        if (!string.IsNullOrWhiteSpace(metadata.UserInfoEndpoint) &&
            !string.IsNullOrWhiteSpace(tokenPayload.AccessToken))
        {
            var userInfoClaims = await GetUserInfoClaimsAsync(client, metadata.UserInfoEndpoint,
                tokenPayload.AccessToken, cancellationToken);
            foreach (var pair in userInfoClaims) claims[pair.Key] = pair.Value;
        }

        if (claims.Count == 0) throw new InvalidOperationException("OIDC response did not include user information.");

        var subject = GetClaim(claims, provider.Oidc.SubjectClaim, "sub");
        var email = GetClaim(claims, provider.Oidc.EmailClaim, "email", "preferred_username", "upn");
        if (string.IsNullOrWhiteSpace(subject) || string.IsNullOrWhiteSpace(email))
            throw new InvalidOperationException("OIDC response did not contain the required subject/email claims.");

        var fullName = GetClaim(claims, provider.Oidc.NameClaim, "name", "displayName");
        return new ExternalAuthResult(provider.Id, subject, email, fullName, payload.ReturnUrl);
    }

    public async Task<ExternalAuthTestResult> TestLdapAsync(AuthProviderConfig provider,
        CancellationToken cancellationToken = default)
    {
        if (provider.Type != AuthProviderType.Ldap) return new ExternalAuthTestResult(false, "Provider is not LDAP.");

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var connection = CreateLdapConnection(provider.Ldap);
            BindServiceAccount(connection, provider.Ldap);
            SearchUser(connection, provider.Ldap, "test@example.com", "test");
            return new ExternalAuthTestResult(true, "LDAP connection and search completed successfully.");
        }
        catch (Exception ex)
        {
            return new ExternalAuthTestResult(false, ex.Message);
        }
    }

    public async Task<ExternalAuthTestResult> TestOidcDiscoveryAsync(AuthProviderConfig provider,
        CancellationToken cancellationToken = default)
    {
        if (provider.Type != AuthProviderType.Oidc) return new ExternalAuthTestResult(false, "Provider is not OIDC.");

        try
        {
            var metadata = await GetOidcMetadataAsync(provider, cancellationToken);
            return string.IsNullOrWhiteSpace(metadata.AuthorizationEndpoint) ||
                   string.IsNullOrWhiteSpace(metadata.TokenEndpoint)
                ? new ExternalAuthTestResult(false, "OIDC metadata is missing authorizationEndpoint or tokenEndpoint.")
                : new ExternalAuthTestResult(true, "OIDC discovery completed successfully.");
        }
        catch (Exception ex)
        {
            return new ExternalAuthTestResult(false, ex.Message);
        }
    }

    private AuthProviderConfig RequireEnabledProvider(string providerId, AuthProviderType expectedType)
    {
        var provider = authProviderStore.Get(providerId);
        if (provider is null || !provider.Enabled || provider.Type != expectedType)
            throw new InvalidOperationException($"Provider '{providerId}' is unavailable.");

        return provider;
    }

    private ExternalAuthResult? AuthenticateLdap(AuthProviderConfig provider, string email, string password)
    {
        using var connection = CreateLdapConnection(provider.Ldap);
        BindServiceAccount(connection, provider.Ldap);
        var entry = SearchUser(connection, provider.Ldap, email, GetUsername(email))
                    ?? throw new InvalidOperationException("User was not found in LDAP.");
        var userDn = entry.DistinguishedName;
        if (string.IsNullOrWhiteSpace(userDn))
            throw new InvalidOperationException("LDAP user entry did not include a distinguished name.");

        using var verifyConnection = CreateLdapConnection(provider.Ldap);
        verifyConnection.AuthType = AuthType.Basic;
        verifyConnection.Credential = new NetworkCredential(userDn, password);
        verifyConnection.Bind();

        var resolvedEmail = GetLdapAttribute(entry, provider.Ldap.EmailAttribute) ?? email;
        var fullName = GetLdapAttribute(entry, provider.Ldap.FullNameAttribute) ?? resolvedEmail;
        return new ExternalAuthResult(provider.Id, userDn, resolvedEmail, fullName, "/");
    }

    private static LdapConnection CreateLdapConnection(LdapAuthProviderConfig settings)
    {
        var connection = new LdapConnection(new LdapDirectoryIdentifier(settings.Server, settings.Port))
        {
            AuthType = AuthType.Basic
        };
        connection.SessionOptions.ProtocolVersion = 3;
        connection.SessionOptions.SecureSocketLayer = settings.UseSsl;
        return connection;
    }

    private void BindServiceAccount(LdapConnection connection, LdapAuthProviderConfig settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.BindDn))
            connection.Credential =
                new NetworkCredential(settings.BindDn, DecryptSecret(settings.EncryptedBindPassword));

        connection.Bind();
    }

    private static SearchResultEntry? SearchUser(LdapConnection connection, LdapAuthProviderConfig settings,
        string email, string username)
    {
        var filter = (string.IsNullOrWhiteSpace(settings.UserFilter) ? "(mail={email})" : settings.UserFilter)
            .Replace("{email}", EscapeLdapFilterValue(email), StringComparison.OrdinalIgnoreCase)
            .Replace("{username}", EscapeLdapFilterValue(username), StringComparison.OrdinalIgnoreCase);
        var request = new SearchRequest(settings.BaseDn, filter, SearchScope.Subtree, settings.EmailAttribute,
            settings.FullNameAttribute);
        var response = (SearchResponse)connection.SendRequest(request);
        return response.Entries.Count > 0 ? response.Entries[0] : null;
    }

    private static string? GetLdapAttribute(SearchResultEntry entry, string attributeName)
    {
        if (string.IsNullOrWhiteSpace(attributeName) || !entry.Attributes.Contains(attributeName)) return null;

        var values = entry.Attributes[attributeName]?.GetValues(typeof(string));
        return values is { Length: > 0 } ? values[0]?.ToString() : null;
    }

    private async Task<OidcMetadata> GetOidcMetadataAsync(AuthProviderConfig provider,
        CancellationToken cancellationToken)
    {
        var metadataUrl = NormalizeOidcDiscoveryEndpoint(provider.Oidc.MetadataUrl, provider.Oidc.Authority);
        if (string.IsNullOrWhiteSpace(metadataUrl))
            throw new InvalidOperationException("OIDC discovery endpoint is required.");

        using var client = httpClientFactory.CreateClient(nameof(ExternalAuthService));
        using var response = await client.GetAsync(metadataUrl, cancellationToken);
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        var root = document.RootElement;
        return new OidcMetadata(
            root.TryGetProperty("authorization_endpoint", out var auth) ? auth.GetString() : null,
            root.TryGetProperty("token_endpoint", out var token) ? token.GetString() : null,
            root.TryGetProperty("userinfo_endpoint", out var userInfo) ? userInfo.GetString() : null);
    }

    private static string BuildOidcMetadataUrl(string? authority)
    {
        return string.IsNullOrWhiteSpace(authority)
            ? string.Empty
            : authority.Trim().TrimEnd('/') + "/.well-known/openid-configuration";
    }

    private static string NormalizeOidcDiscoveryEndpoint(string? metadataUrl, string? authority)
    {
        return !string.IsNullOrWhiteSpace(metadataUrl)
            ? metadataUrl.Trim()
            : BuildOidcMetadataUrl(authority);
    }

    private async Task<Dictionary<string, string?>> GetUserInfoClaimsAsync(HttpClient client, string userInfoEndpoint,
        string accessToken, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, userInfoEndpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await client.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        return document.RootElement.EnumerateObject()
            .ToDictionary(x => x.Name,
                x => x.Value.ValueKind == JsonValueKind.String ? x.Value.GetString() : x.Value.ToString(),
                StringComparer.OrdinalIgnoreCase);
    }

    private static Dictionary<string, string?> ReadJwtPayload(string token)
    {
        var segments = token.Split('.');
        if (segments.Length < 2) throw new InvalidOperationException("OIDC id_token is not a valid JWT.");

        using var document = JsonDocument.Parse(Base64UrlDecode(segments[1]));
        return document.RootElement.EnumerateObject()
            .ToDictionary(x => x.Name,
                x => x.Value.ValueKind == JsonValueKind.String ? x.Value.GetString() : x.Value.ToString(),
                StringComparer.OrdinalIgnoreCase);
    }

    private string ProtectState(OidcStatePayload payload)
    {
        return _stateProtector.Protect(JsonSerializer.Serialize(payload, JsonOptions));
    }

    private OidcStatePayload UnprotectState(string state)
    {
        try
        {
            return JsonSerializer.Deserialize<OidcStatePayload>(_stateProtector.Unprotect(state), JsonOptions)
                   ?? throw new InvalidOperationException("OIDC state was invalid.");
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            throw new InvalidOperationException("OIDC state could not be validated.", ex);
        }
    }

    private string DecryptSecret(string? ciphertext)
    {
        return string.IsNullOrWhiteSpace(ciphertext) ? string.Empty : encryptionService.DecryptString(ciphertext);
    }

    private static string BuildRedirectUri(HttpContext context, string providerId)
    {
        return
            $"{context.Request.Scheme}://{context.Request.Host}{InternalApiPaths.AdminRoot}/auth/oidc/{Uri.EscapeDataString(providerId)}/callback";
    }

    private static string NormalizeReturnUrl(string? returnUrl)
    {
        return string.IsNullOrWhiteSpace(returnUrl) || !returnUrl.StartsWith('/') ? "/" : returnUrl;
    }

    private static string GetUsername(string email)
    {
        return email.Contains('@', StringComparison.Ordinal) ? email[..email.IndexOf('@')] : email;
    }

    private static string GetClaim(IReadOnlyDictionary<string, string?> claims, params string[] names)
    {
        foreach (var name in names)
            if (!string.IsNullOrWhiteSpace(name) && claims.TryGetValue(name, out var value) &&
                !string.IsNullOrWhiteSpace(value))
                return value;

        return string.Empty;
    }

    private static string EscapeLdapFilterValue(string value)
    {
        return value.Replace("\\", "\\5c", StringComparison.Ordinal)
            .Replace("*", "\\2a", StringComparison.Ordinal)
            .Replace("(", "\\28", StringComparison.Ordinal)
            .Replace(")", "\\29", StringComparison.Ordinal)
            .Replace("\0", "\\00", StringComparison.Ordinal);
    }

    private static string CreateCodeVerifier()
    {
        return Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
    }

    private static string CreateCodeChallenge(string verifier)
    {
        return Base64UrlEncode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
    }

    private static string Base64UrlEncode(byte[] bytes)
    {
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static byte[] Base64UrlDecode(string value)
    {
        var base64 = value.Replace('-', '+').Replace('_', '/');
        switch (base64.Length % 4)
        {
            case 2:
                base64 += "==";
                break;
            case 3:
                base64 += "=";
                break;
        }

        return Convert.FromBase64String(base64);
    }

    private sealed record OidcStatePayload(
        string ProviderId,
        string ReturnUrl,
        string CodeVerifier,
        DateTimeOffset IssuedAtUtc);

    private sealed record OidcMetadata(string? AuthorizationEndpoint, string? TokenEndpoint, string? UserInfoEndpoint);

    private sealed record OidcTokenResponse(
        [property: JsonPropertyName("access_token")]
        string? AccessToken,
        [property: JsonPropertyName("id_token")]
        string? IdToken);
}