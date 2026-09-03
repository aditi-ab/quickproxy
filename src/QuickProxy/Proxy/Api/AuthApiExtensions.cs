using System.Security.Claims;
using System.Security.Cryptography;
using System.DirectoryServices.Protocols;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Quick.Shared.Auth;
using Quick.Shared.Web;
using QuickProxy.Proxy.Config.Storage;
using QuickProxy.Proxy.Models;
using QuickProxy.Proxy.Runtime;
using QuickProxy.Proxy.Storage;

namespace QuickProxy.Proxy.Api;

public static partial class AuthApiExtensions
{
    [GeneratedRegex("^[a-z0-9]+(?:-[a-z0-9]+)*$")]
    private static partial Regex ProviderIdRegex();

    public static IEndpointRouteBuilder MapAuthApi(this IEndpointRouteBuilder app)
    {
        var authGroup = app.MapGroup($"{InternalApiPaths.AdminRoot}/auth");

        authGroup.MapGet("/status", (ClaimsPrincipal principal, IUserStore store, IExternalAuthService externalAuthService) =>
        {
            var user = BuildUserFromPrincipal(principal, store);
            return Results.Ok(new
            {
                authenticated = user is not null,
                hasUsers = store.AnyUsers(),
                user,
                externalProviders = externalAuthService.ListLoginProviders()
            });
        });

        authGroup.MapGet("/providers", (IExternalAuthService externalAuthService) =>
        {
            return Results.Ok(externalAuthService.ListLoginProviders());
        });

        authGroup.MapGet("/me", (ClaimsPrincipal principal, IUserStore store) =>
        {
            var user = BuildUserFromPrincipal(principal, store);
            return user is null ? Results.Unauthorized() : Results.Ok(user);
        });

        authGroup.MapPost("/bootstrap", async (
            HttpContext context,
            BootstrapRequest request,
            IUserStore store,
            IPasswordHashingService hasher) =>
        {
            if (!TryResolveRequestPassword(request.Password, request.PasswordBase64, out var password))
            {
                return Validation(["password is required and must be valid base64 when passwordBase64 is used."]);
            }

            if (store.AnyUsers())
            {
                return Results.Conflict(new
                {
                    code = "users_exist",
                    message = "Bootstrap is only available when no users exist."
                });
            }

            var errors = UserInput.ValidateNewUser(request.Email, password, request.FullName);
            if (errors.Count > 0)
            {
                return Validation(errors);
            }

            var user = new AdminUserRecord
            {
                Email = NormalizeEmail(request.Email),
                FullName = UserInput.NormalizeFullName(request.FullName),
                Enabled = true,
                PasswordHash = hasher.HashPassword(password)
            };

            store.Upsert(user);
            await SignInAsync(context, user, "local", null);
            return Results.Ok(ToResponse(user, "local", null));
        });

        authGroup.MapPost("/login", async (
            HttpContext context,
            LoginRequest request,
            IUserStore store,
            IPasswordHashingService hasher,
            IExternalAuthService externalAuthService,
            CancellationToken cancellationToken) =>
        {
            if (!TryResolveRequestPassword(request.Password, request.PasswordBase64, out var password))
            {
                return InvalidCredentials();
            }

            var email = NormalizeEmail(request.Email);
            var user = store.GetByEmail(email);
            if (user is not null && user.Enabled && hasher.Verify(user.PasswordHash, password))
            {
                await SignInAsync(context, user, "local", null);
                return Results.Ok(ToResponse(user, "local", null));
            }

            var externalResult = await externalAuthService.AuthenticatePasswordAsync(email, password, cancellationToken);
            if (externalResult is null)
            {
                return InvalidCredentials();
            }

            var provider = externalAuthService.GetProvider(externalResult.ProviderId);
            if (provider is null)
            {
                return InvalidCredentials();
            }

            var externalUser = ResolveOrCreateExternalUser(store, provider, externalResult);
            if (externalUser is null)
            {
                return Results.BadRequest(new
                {
                    code = "external_access_denied",
                    message = $"Provider '{provider.DisplayName}' authenticated successfully, but access is not allowed."
                });
            }

            await SignInAsync(context, externalUser, provider.Type.ToString().ToLowerInvariant(), provider.Id);
            return Results.Ok(ToResponse(externalUser, provider.Type.ToString().ToLowerInvariant(), provider.Id));
        });

        authGroup.MapPost("/oidc/{providerId}/start", async (
            HttpContext context,
            string providerId,
            StartOidcRequest request,
            IExternalAuthService externalAuthService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var url = await externalAuthService.BuildOidcChallengeUrlAsync(context, providerId, request.ReturnUrl, cancellationToken);
                return Results.Ok(new { url });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new
                {
                    code = "oidc_start_failed",
                    message = ex.Message
                });
            }
        });

        authGroup.MapGet("/oidc/{providerId}/callback", async (
            HttpContext context,
            string providerId,
            string? code,
            string? state,
            string? error,
            IUserStore store,
            IExternalAuthService externalAuthService,
            CancellationToken cancellationToken) =>
        {
            if (!string.IsNullOrWhiteSpace(error))
            {
                return Results.Redirect($"/login?error={Uri.EscapeDataString(error)}");
            }

            if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(state))
            {
                return Results.Redirect("/login?error=oidc_callback_invalid");
            }

            try
            {
                var externalResult = await externalAuthService.CompleteOidcAsync(context, providerId, code, state, cancellationToken);
                var provider = externalAuthService.GetProvider(externalResult.ProviderId);
                if (provider is null)
                {
                    return Results.Redirect("/login?error=oidc_provider_missing");
                }

                var user = ResolveOrCreateExternalUser(store, provider, externalResult);
                if (user is null)
                {
                    return Results.Redirect("/login?error=external_access_denied");
                }

                await SignInAsync(context, user, provider.Type.ToString().ToLowerInvariant(), provider.Id);
                return Results.Redirect(externalResult.ReturnUrl);
            }
            catch (Exception ex)
            {
                return Results.Redirect($"/login?error={Uri.EscapeDataString(ex.Message)}");
            }
        });

        authGroup.MapPost("/logout", async (HttpContext context) =>
        {
            await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.NoContent();
        });

        var providersGroup = app.MapGroup($"{InternalApiPaths.AdminRoot}/auth-providers").RequireAuthorization();

        providersGroup.MapGet("/", (IAuthProviderStore store) =>
        {
            return Results.Ok(store.List().Select(ToProviderResponse));
        });

        providersGroup.MapGet("/{id}", (string id, IAuthProviderStore store) =>
        {
            var provider = store.Get(id);
            return provider is null ? ProviderNotFound(id) : Results.Ok(ToProviderResponse(provider));
        });

        providersGroup.MapPost("/", (
            UpsertAuthProviderRequest request,
            IAuthProviderStore store,
            IConfigEncryptionService encryptionService) =>
        {
            var errors = ValidateProviderRequest(request, store.List(), null);
            if (errors.Count > 0)
            {
                return Validation(errors);
            }

            var provider = ToStoredProvider(request, null, encryptionService);
            store.Upsert(provider);
            return Results.Created($"{InternalApiPaths.AdminRoot}/auth-providers/{provider.Id}", ToProviderResponse(provider));
        });

        providersGroup.MapPut("/{id}", (
            string id,
            UpsertAuthProviderRequest request,
            IAuthProviderStore store,
            IConfigEncryptionService encryptionService) =>
        {
            var existing = store.Get(id);
            if (existing is null)
            {
                return ProviderNotFound(id);
            }

            request = request with { Id = id };
            var errors = ValidateProviderRequest(request, store.List(), id);
            if (errors.Count > 0)
            {
                return Validation(errors);
            }

            var provider = ToStoredProvider(request, existing, encryptionService);
            store.Upsert(provider);
            return Results.Ok(ToProviderResponse(provider));
        });

        providersGroup.MapDelete("/{id}", (string id, IAuthProviderStore store) =>
        {
            return store.Delete(id) ? Results.NoContent() : ProviderNotFound(id);
        });

        providersGroup.MapPost("/test/ldap", async (
            UpsertAuthProviderRequest request,
            IAuthProviderStore store,
            IConfigEncryptionService encryptionService,
            IExternalAuthService externalAuthService,
            CancellationToken cancellationToken) =>
        {
            var provider = ToStoredProvider(request, null, encryptionService);
            var result = await externalAuthService.TestLdapAsync(provider, cancellationToken);
            return Results.Ok(result);
        });

        providersGroup.MapPost("/test/oidc-discovery", async (
            UpsertAuthProviderRequest request,
            IAuthProviderStore store,
            IConfigEncryptionService encryptionService,
            IExternalAuthService externalAuthService,
            CancellationToken cancellationToken) =>
        {
            var provider = ToStoredProvider(request, null, encryptionService);
            var result = await externalAuthService.TestOidcDiscoveryAsync(provider, cancellationToken);
            return Results.Ok(result);
        });

        return app;
    }

    private static AdminUserRecord? ResolveOrCreateExternalUser(IUserStore store, AuthProviderConfig provider, ExternalAuthResult result)
    {
        var existingByIdentity = store.List().FirstOrDefault(x => x.ExternalIdentities.Any(identity =>
            string.Equals(identity.ProviderId, provider.Id, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(identity.Subject, result.Subject, StringComparison.Ordinal)));
        var user = existingByIdentity ?? store.GetByEmail(NormalizeEmail(result.Email));

        if (user is not null && !user.Enabled)
        {
            return null;
        }

        if (user is null)
        {
            if (!provider.AllowAutoAccess)
            {
                return null;
            }

            user = new AdminUserRecord
            {
                Email = NormalizeEmail(result.Email),
                FullName = UserInput.NormalizeFullName(result.FullName),
                Enabled = true,
                PasswordHash = string.Empty
            };
        }

        user.FullName = UserInput.NormalizeFullName(result.FullName) ?? user.FullName;
        if (!user.ExternalIdentities.Any(x =>
            string.Equals(x.ProviderId, provider.Id, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(x.Subject, result.Subject, StringComparison.Ordinal)))
        {
            user.ExternalIdentities.Add(new AdminUserExternalIdentity
            {
                ProviderId = provider.Id,
                Subject = result.Subject
            });
        }

        store.Upsert(user);
        return store.GetByEmail(user.Email);
    }

    private static AdminUserResponse? BuildUserFromPrincipal(ClaimsPrincipal principal, IUserStore store)
    {
        if (principal.Identity?.IsAuthenticated != true)
        {
            return null;
        }

        var email = principal.FindFirstValue(ClaimTypes.Email) ?? principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(email))
        {
            return null;
        }

        var user = store.GetByEmail(email);
        if (user is null || !user.Enabled)
        {
            return null;
        }

        return ToResponse(
            user,
            principal.FindFirstValue("quickproxy_auth_type") ?? "local",
            principal.FindFirstValue("quickproxy_auth_provider"));
    }

    private static async Task SignInAsync(HttpContext context, AdminUserRecord user, string authType, string? providerId)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Email),
            new(ClaimTypes.Email, user.Email),
            new(ClaimTypes.Name, string.IsNullOrWhiteSpace(user.FullName) ? user.Email : user.FullName),
            new("quickproxy_auth_type", authType)
        };

        if (!string.IsNullOrWhiteSpace(providerId))
        {
            claims.Add(new Claim("quickproxy_auth_provider", providerId));
        }

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var principal = new ClaimsPrincipal(identity);

        await context.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            principal,
            new AuthenticationProperties
            {
                IsPersistent = true,
                AllowRefresh = true,
                ExpiresUtc = DateTimeOffset.UtcNow.AddDays(7)
            });
    }

    private static AdminUserResponse ToResponse(AdminUserRecord user, string authType, string? providerId)
    {
        return new AdminUserResponse(
            user.Email,
            user.FullName,
            user.Enabled,
            authType,
            providerId,
            !string.IsNullOrWhiteSpace(user.PasswordHash),
            user.ExternalIdentities.Count);
    }

    private static AdminAuthProviderResponse ToProviderResponse(AuthProviderConfig provider)
    {
        return new AdminAuthProviderResponse(
            provider.Id,
            provider.DisplayName,
            provider.Enabled,
            provider.AllowAutoAccess,
            provider.Type,
            new AdminLdapAuthProviderSettings(
                provider.Ldap.Server,
                provider.Ldap.Port,
                provider.Ldap.UseSsl,
                provider.Ldap.BindDn,
                !string.IsNullOrWhiteSpace(provider.Ldap.EncryptedBindPassword),
                provider.Ldap.BaseDn,
                provider.Ldap.UserFilter,
                provider.Ldap.EmailAttribute,
                provider.Ldap.FullNameAttribute),
            new AdminOidcAuthProviderSettings(
                provider.Oidc.Authority,
                provider.Oidc.MetadataUrl,
                provider.Oidc.ClientId,
                !string.IsNullOrWhiteSpace(provider.Oidc.EncryptedClientSecret),
                provider.Oidc.Scopes,
                provider.Oidc.EmailClaim,
                provider.Oidc.NameClaim,
                provider.Oidc.SubjectClaim,
                provider.Oidc.UsePkce));
    }

    private static AuthProviderConfig ToStoredProvider(UpsertAuthProviderRequest request, AuthProviderConfig? existing, IConfigEncryptionService encryptionService)
    {
        var provider = new AuthProviderConfig
        {
            Id = (request.Id ?? string.Empty).Trim(),
            DisplayName = string.IsNullOrWhiteSpace(request.DisplayName) ? (request.Id ?? string.Empty).Trim() : request.DisplayName.Trim(),
            Enabled = request.Enabled,
            AllowAutoAccess = request.AllowAutoAccess,
            Type = request.Type
        };

        provider.Ldap = new LdapAuthProviderConfig
        {
            Server = request.Ldap.Server?.Trim() ?? string.Empty,
            Port = request.Ldap.Port <= 0 ? 389 : request.Ldap.Port,
            UseSsl = request.Ldap.UseSsl,
            BindDn = request.Ldap.BindDn?.Trim() ?? string.Empty,
            EncryptedBindPassword = ResolveEncryptedSecret(request.Ldap.BindPassword, request.Ldap.ClearBindPassword, existing?.Ldap.EncryptedBindPassword, encryptionService),
            BaseDn = request.Ldap.BaseDn?.Trim() ?? string.Empty,
            UserFilter = string.IsNullOrWhiteSpace(request.Ldap.UserFilter) ? "(mail={email})" : request.Ldap.UserFilter.Trim(),
            EmailAttribute = string.IsNullOrWhiteSpace(request.Ldap.EmailAttribute) ? "mail" : request.Ldap.EmailAttribute.Trim(),
            FullNameAttribute = string.IsNullOrWhiteSpace(request.Ldap.FullNameAttribute) ? "displayName" : request.Ldap.FullNameAttribute.Trim()
        };

        provider.Oidc = new OidcAuthProviderConfig
        {
            Authority = request.Oidc.Authority?.Trim() ?? string.Empty,
            MetadataUrl = request.Oidc.MetadataUrl?.Trim() ?? string.Empty,
            ClientId = request.Oidc.ClientId?.Trim() ?? string.Empty,
            EncryptedClientSecret = ResolveEncryptedSecret(request.Oidc.ClientSecret, request.Oidc.ClearClientSecret, existing?.Oidc.EncryptedClientSecret, encryptionService),
            Scopes = string.IsNullOrWhiteSpace(request.Oidc.Scopes) ? "openid profile email" : request.Oidc.Scopes.Trim(),
            EmailClaim = string.IsNullOrWhiteSpace(request.Oidc.EmailClaim) ? "email" : request.Oidc.EmailClaim.Trim(),
            NameClaim = string.IsNullOrWhiteSpace(request.Oidc.NameClaim) ? "name" : request.Oidc.NameClaim.Trim(),
            SubjectClaim = string.IsNullOrWhiteSpace(request.Oidc.SubjectClaim) ? "sub" : request.Oidc.SubjectClaim.Trim(),
            UsePkce = request.Oidc.UsePkce
        };

        return provider;
    }

    private static string ResolveEncryptedSecret(string? plaintext, bool clear, string? existingCiphertext, IConfigEncryptionService encryptionService)
    {
        if (clear)
        {
            return string.Empty;
        }

        if (!string.IsNullOrWhiteSpace(plaintext))
        {
            return encryptionService.EncryptString(plaintext);
        }

        return existingCiphertext ?? string.Empty;
    }

    private static List<string> ValidateProviderRequest(UpsertAuthProviderRequest request, IReadOnlyList<AuthProviderConfig> currentProviders, string? replaceId)
    {
        var errors = new List<string>();
        var id = (request.Id ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(id) || !ProviderIdRegex().IsMatch(id))
        {
            errors.Add("id is required and must be lowercase kebab-case.");
        }

        if (currentProviders.Any(x =>
            (replaceId is null || !string.Equals(x.Id, replaceId, StringComparison.OrdinalIgnoreCase)) &&
            string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase)))
        {
            errors.Add($"Provider '{id}' already exists.");
        }

        if (request.Type == AuthProviderType.Ldap)
        {
            if (string.IsNullOrWhiteSpace(request.Ldap.Server))
            {
                errors.Add("ldap.server is required.");
            }

            if (string.IsNullOrWhiteSpace(request.Ldap.BaseDn))
            {
                errors.Add("ldap.baseDn is required.");
            }
        }

        if (request.Type == AuthProviderType.Oidc)
        {
            if (string.IsNullOrWhiteSpace(request.Oidc.Authority) && string.IsNullOrWhiteSpace(request.Oidc.MetadataUrl))
            {
                errors.Add("oidc.authority or oidc.metadataUrl is required.");
            }

            if (string.IsNullOrWhiteSpace(request.Oidc.ClientId))
            {
                errors.Add("oidc.clientId is required.");
            }
        }

        return errors;
    }

    private static string NormalizeEmail(string value) => UserInput.NormalizeEmail(value);

    private static IResult Validation(List<string> details)
    {
        return Results.BadRequest(new
        {
            code = "validation_error",
            message = "Validation failed.",
            details
        });
    }

    private static IResult ProviderNotFound(string id)
    {
        return Results.NotFound(new
        {
            code = "not_found",
            message = $"Auth provider '{id}' was not found."
        });
    }

    private static IResult InvalidCredentials()
    {
        return Results.BadRequest(new
        {
            code = "invalid_credentials",
            message = "Invalid email or password."
        });
    }

    public sealed record LoginRequest(string Email, string? Password, string? PasswordBase64);
    public sealed record BootstrapRequest(string Email, string? Password, string? PasswordBase64, string? FullName);
    public sealed record StartOidcRequest(string? ReturnUrl);
    public sealed record AdminUserResponse(
        string Email,
        string? FullName,
        bool Enabled,
        string AuthType,
        string? AuthProviderId,
        bool HasPassword,
        int ExternalIdentityCount);
    public sealed record UpsertAuthProviderRequest(
        string Id,
        string DisplayName,
        bool Enabled,
        bool AllowAutoAccess,
        AuthProviderType Type,
        UpsertLdapAuthProviderSettings Ldap,
        UpsertOidcAuthProviderSettings Oidc);
    public sealed record UpsertLdapAuthProviderSettings(
        string Server,
        int Port,
        bool UseSsl,
        string BindDn,
        string? BindPassword,
        bool ClearBindPassword,
        string BaseDn,
        string UserFilter,
        string EmailAttribute,
        string FullNameAttribute);
    public sealed record UpsertOidcAuthProviderSettings(
        string Authority,
        string MetadataUrl,
        string ClientId,
        string? ClientSecret,
        bool ClearClientSecret,
        string Scopes,
        string EmailClaim,
        string NameClaim,
        string SubjectClaim,
        bool UsePkce);
    public sealed record AdminAuthProviderResponse(
        string Id,
        string DisplayName,
        bool Enabled,
        bool AllowAutoAccess,
        AuthProviderType Type,
        AdminLdapAuthProviderSettings Ldap,
        AdminOidcAuthProviderSettings Oidc);
    public sealed record AdminLdapAuthProviderSettings(
        string Server,
        int Port,
        bool UseSsl,
        string BindDn,
        bool HasBindPassword,
        string BaseDn,
        string UserFilter,
        string EmailAttribute,
        string FullNameAttribute);
public sealed record AdminOidcAuthProviderSettings(
        string Authority,
        string MetadataUrl,
        string ClientId,
        bool HasClientSecret,
        string Scopes,
        string EmailClaim,
        string NameClaim,
        string SubjectClaim,
        bool UsePkce);
}

public interface IExternalAuthService
{
    IReadOnlyList<ExternalLoginProviderOption> ListLoginProviders();
    AuthProviderConfig? GetProvider(string id);
    Task<ExternalAuthResult?> AuthenticatePasswordAsync(string email, string password, CancellationToken cancellationToken = default);
    Task<string> BuildOidcChallengeUrlAsync(HttpContext context, string providerId, string? returnUrl, CancellationToken cancellationToken = default);
    Task<ExternalAuthResult> CompleteOidcAsync(HttpContext context, string providerId, string code, string state, CancellationToken cancellationToken = default);
    Task<ExternalAuthTestResult> TestLdapAsync(AuthProviderConfig provider, CancellationToken cancellationToken = default);
    Task<ExternalAuthTestResult> TestOidcDiscoveryAsync(AuthProviderConfig provider, CancellationToken cancellationToken = default);
}

public sealed record ExternalLoginProviderOption(string Id, string DisplayName, AuthProviderType Type);
public sealed record ExternalAuthResult(string ProviderId, string Subject, string Email, string? FullName, string ReturnUrl);
public sealed record ExternalAuthTestResult(bool Success, string Message);

public sealed class ExternalAuthService(
    IAuthProviderStore authProviderStore,
    IConfigEncryptionService encryptionService,
    IDataProtectionProvider dataProtectionProvider,
    IHttpClientFactory httpClientFactory,
    ILogger<ExternalAuthService> logger) : IExternalAuthService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IDataProtector _stateProtector = dataProtectionProvider.CreateProtector("QuickProxy.ExternalAuth.OidcState");

    public IReadOnlyList<ExternalLoginProviderOption> ListLoginProviders()
    {
        return authProviderStore.List()
            .Where(x => x.Enabled && x.Type == AuthProviderType.Oidc)
            .OrderBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Id, StringComparer.OrdinalIgnoreCase)
            .Select(x => new ExternalLoginProviderOption(x.Id, x.DisplayName, x.Type))
            .ToArray();
    }

    public AuthProviderConfig? GetProvider(string id) => authProviderStore.Get(id);

    public async Task<ExternalAuthResult?> AuthenticatePasswordAsync(string email, string password, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            return null;
        }

        foreach (var provider in authProviderStore.List().Where(x => x.Enabled && x.Type == AuthProviderType.Ldap))
        {
            try
            {
                var result = AuthenticateLdap(provider, email, password);
                if (result is not null)
                {
                    return await Task.FromResult(result);
                }
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "LDAP authentication failed for provider '{ProviderId}'.", provider.Id);
            }
        }

        return null;
    }

    public async Task<string> BuildOidcChallengeUrlAsync(HttpContext context, string providerId, string? returnUrl, CancellationToken cancellationToken = default)
    {
        var provider = RequireEnabledProvider(providerId, AuthProviderType.Oidc);
        var metadata = await GetOidcMetadataAsync(provider, cancellationToken);
        if (string.IsNullOrWhiteSpace(metadata.AuthorizationEndpoint))
        {
            throw new InvalidOperationException("OIDC metadata did not include an authorization endpoint.");
        }

        var codeVerifier = provider.Oidc.UsePkce ? CreateCodeVerifier() : string.Empty;
        var state = ProtectState(new OidcStatePayload(
            provider.Id,
            NormalizeReturnUrl(returnUrl),
            codeVerifier,
            DateTimeOffset.UtcNow));
        var redirectUri = BuildRedirectUri(context, provider.Id);
        var parameters = new List<KeyValuePair<string, string>>
        {
            new("response_type", "code"),
            new("client_id", provider.Oidc.ClientId),
            new("redirect_uri", redirectUri),
            new("scope", string.IsNullOrWhiteSpace(provider.Oidc.Scopes) ? "openid profile email" : provider.Oidc.Scopes),
            new("state", state)
        };

        if (provider.Oidc.UsePkce)
        {
            parameters.Add(new("code_challenge_method", "S256"));
            parameters.Add(new("code_challenge", CreateCodeChallenge(codeVerifier)));
        }

        var separator = metadata.AuthorizationEndpoint.Contains('?', StringComparison.Ordinal) ? "&" : "?";
        return metadata.AuthorizationEndpoint + separator + string.Join("&", parameters.Select(x => $"{Uri.EscapeDataString(x.Key)}={Uri.EscapeDataString(x.Value)}"));
    }

    public async Task<ExternalAuthResult> CompleteOidcAsync(HttpContext context, string providerId, string code, string state, CancellationToken cancellationToken = default)
    {
        var provider = RequireEnabledProvider(providerId, AuthProviderType.Oidc);
        var payload = UnprotectState(state);
        if (!string.Equals(payload.ProviderId, provider.Id, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("OIDC state was issued for a different provider.");
        }

        if (DateTimeOffset.UtcNow - payload.IssuedAtUtc > TimeSpan.FromMinutes(10))
        {
            throw new InvalidOperationException("OIDC state has expired.");
        }

        var metadata = await GetOidcMetadataAsync(provider, cancellationToken);
        if (string.IsNullOrWhiteSpace(metadata.TokenEndpoint))
        {
            throw new InvalidOperationException("OIDC metadata did not include a token endpoint.");
        }

        using var client = httpClientFactory.CreateClient(nameof(ExternalAuthService));
        var tokenRequest = new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = BuildRedirectUri(context, provider.Id),
            ["client_id"] = provider.Oidc.ClientId
        };

        var clientSecret = DecryptSecret(provider.Oidc.EncryptedClientSecret);
        if (!string.IsNullOrWhiteSpace(clientSecret))
        {
            tokenRequest["client_secret"] = clientSecret;
        }

        if (provider.Oidc.UsePkce)
        {
            tokenRequest["code_verifier"] = payload.CodeVerifier;
        }

        using var tokenResponse = await client.PostAsync(metadata.TokenEndpoint, new FormUrlEncodedContent(tokenRequest), cancellationToken);
        var tokenJson = await tokenResponse.Content.ReadAsStringAsync(cancellationToken);
        if (!tokenResponse.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"OIDC token exchange failed with status {(int)tokenResponse.StatusCode}.");
        }

        var tokenPayload = JsonSerializer.Deserialize<OidcTokenResponse>(tokenJson, JsonOptions)
            ?? throw new InvalidOperationException("OIDC token response was empty.");

        Dictionary<string, string?> claims;
        if (!string.IsNullOrWhiteSpace(metadata.UserInfoEndpoint) && !string.IsNullOrWhiteSpace(tokenPayload.AccessToken))
        {
            claims = await GetUserInfoClaimsAsync(client, metadata.UserInfoEndpoint, tokenPayload.AccessToken, cancellationToken);
        }
        else if (!string.IsNullOrWhiteSpace(tokenPayload.IdToken))
        {
            claims = ReadJwtPayload(tokenPayload.IdToken);
        }
        else
        {
            throw new InvalidOperationException("OIDC response did not include user information.");
        }

        var subject = GetClaim(claims, provider.Oidc.SubjectClaim, "sub");
        var email = GetClaim(claims, provider.Oidc.EmailClaim, "email", "preferred_username", "upn");
        if (string.IsNullOrWhiteSpace(subject) || string.IsNullOrWhiteSpace(email))
        {
            throw new InvalidOperationException("OIDC response did not contain the required subject/email claims.");
        }

        var fullName = GetClaim(claims, provider.Oidc.NameClaim, "name", "displayName");
        return new ExternalAuthResult(provider.Id, subject, email, fullName, payload.ReturnUrl);
    }

    public async Task<ExternalAuthTestResult> TestLdapAsync(AuthProviderConfig provider, CancellationToken cancellationToken = default)
    {
        if (provider.Type != AuthProviderType.Ldap)
        {
            return new ExternalAuthTestResult(false, "Provider is not LDAP.");
        }

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

    public async Task<ExternalAuthTestResult> TestOidcDiscoveryAsync(AuthProviderConfig provider, CancellationToken cancellationToken = default)
    {
        if (provider.Type != AuthProviderType.Oidc)
        {
            return new ExternalAuthTestResult(false, "Provider is not OIDC.");
        }

        try
        {
            var metadata = await GetOidcMetadataAsync(provider, cancellationToken);
            if (string.IsNullOrWhiteSpace(metadata.AuthorizationEndpoint) || string.IsNullOrWhiteSpace(metadata.TokenEndpoint))
            {
                return new ExternalAuthTestResult(false, "OIDC metadata is missing authorizationEndpoint or tokenEndpoint.");
            }

            return new ExternalAuthTestResult(true, "OIDC discovery completed successfully.");
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
        {
            throw new InvalidOperationException($"Provider '{providerId}' is unavailable.");
        }

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
        {
            throw new InvalidOperationException("LDAP user entry did not include a distinguished name.");
        }

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
        var identifier = new LdapDirectoryIdentifier(settings.Server, settings.Port);
        var connection = new LdapConnection(identifier)
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
        {
            connection.Credential = new NetworkCredential(settings.BindDn, DecryptSecret(settings.EncryptedBindPassword));
        }

        connection.Bind();
    }

    private static SearchResultEntry? SearchUser(LdapConnection connection, LdapAuthProviderConfig settings, string email, string username)
    {
        var filter = (string.IsNullOrWhiteSpace(settings.UserFilter) ? "(mail={email})" : settings.UserFilter)
            .Replace("{email}", EscapeLdapFilterValue(email), StringComparison.OrdinalIgnoreCase)
            .Replace("{username}", EscapeLdapFilterValue(username), StringComparison.OrdinalIgnoreCase);

        var request = new SearchRequest(
            settings.BaseDn,
            filter,
            SearchScope.Subtree,
            BuildSearchAttributes(settings));

        var response = (SearchResponse)connection.SendRequest(request);
        return response.Entries.Count > 0 ? response.Entries[0] : null;
    }

    private static string[] BuildSearchAttributes(LdapAuthProviderConfig settings)
    {
        return [settings.EmailAttribute, settings.FullNameAttribute];
    }

    private static string? GetLdapAttribute(SearchResultEntry entry, string attributeName)
    {
        if (string.IsNullOrWhiteSpace(attributeName) || !entry.Attributes.Contains(attributeName))
        {
            return null;
        }

        var values = entry.Attributes[attributeName]?.GetValues(typeof(string));
        return values is { Length: > 0 } ? values[0]?.ToString() : null;
    }

    private async Task<OidcMetadata> GetOidcMetadataAsync(AuthProviderConfig provider, CancellationToken cancellationToken)
    {
        var metadataUrl = !string.IsNullOrWhiteSpace(provider.Oidc.MetadataUrl)
            ? provider.Oidc.MetadataUrl.Trim()
            : BuildOidcMetadataUrl(provider.Oidc.Authority);

        if (string.IsNullOrWhiteSpace(metadataUrl))
        {
            throw new InvalidOperationException("OIDC authority or metadataUrl is required.");
        }

        using var client = httpClientFactory.CreateClient(nameof(ExternalAuthService));
        using var response = await client.GetAsync(metadataUrl, cancellationToken);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        return new OidcMetadata(
            root.TryGetProperty("authorization_endpoint", out var authorizationEndpoint) ? authorizationEndpoint.GetString() : null,
            root.TryGetProperty("token_endpoint", out var tokenEndpoint) ? tokenEndpoint.GetString() : null,
            root.TryGetProperty("userinfo_endpoint", out var userInfoEndpoint) ? userInfoEndpoint.GetString() : null);
    }

    private static string BuildOidcMetadataUrl(string? authority)
    {
        if (string.IsNullOrWhiteSpace(authority))
        {
            return string.Empty;
        }

        return authority.Trim().TrimEnd('/') + "/.well-known/openid-configuration";
    }

    private async Task<Dictionary<string, string?>> GetUserInfoClaimsAsync(HttpClient client, string userInfoEndpoint, string accessToken, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, userInfoEndpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await client.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        using var document = JsonDocument.Parse(json);
        return document.RootElement.EnumerateObject()
            .ToDictionary(x => x.Name, x => x.Value.ValueKind == JsonValueKind.String ? x.Value.GetString() : x.Value.ToString(), StringComparer.OrdinalIgnoreCase);
    }

    private static Dictionary<string, string?> ReadJwtPayload(string token)
    {
        var segments = token.Split('.');
        if (segments.Length < 2)
        {
            throw new InvalidOperationException("OIDC id_token is not a valid JWT.");
        }

        var payloadBytes = Base64UrlDecode(segments[1]);
        using var document = JsonDocument.Parse(payloadBytes);
        return document.RootElement.EnumerateObject()
            .ToDictionary(x => x.Name, x => x.Value.ValueKind == JsonValueKind.String ? x.Value.GetString() : x.Value.ToString(), StringComparer.OrdinalIgnoreCase);
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
        return $"{context.Request.Scheme}://{context.Request.Host}/internal-api/admin/auth/oidc/{Uri.EscapeDataString(providerId)}/callback";
    }

    private static string NormalizeReturnUrl(string? returnUrl)
    {
        if (string.IsNullOrWhiteSpace(returnUrl) || !returnUrl.StartsWith('/'))
        {
            return "/";
        }

        return returnUrl;
    }

    private static string GetClaim(IReadOnlyDictionary<string, string?> claims, params string[] names)
    {
        foreach (var name in names)
        {
            if (!string.IsNullOrWhiteSpace(name) && claims.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return string.Empty;
    }

    private static string GetUsername(string email)
    {
        var atIndex = email.IndexOf('@');
        return atIndex > 0 ? email[..atIndex] : email;
    }

    private static bool TryResolveRequestPassword(string? password, string? passwordBase64, out string resolvedPassword)
    {
        if (!string.IsNullOrWhiteSpace(password))
        {
            resolvedPassword = password;
            return true;
        }

        if (!string.IsNullOrWhiteSpace(passwordBase64))
        {
            try
            {
                resolvedPassword = Encoding.UTF8.GetString(Convert.FromBase64String(passwordBase64));
                return !string.IsNullOrWhiteSpace(resolvedPassword);
            }
            catch (FormatException)
            {
            }
        }

        resolvedPassword = string.Empty;
        return false;
    }

    private static string EscapeLdapFilterValue(string value)
    {
        return value
            .Replace("\\", "\\5c", StringComparison.Ordinal)
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
        return Convert.ToBase64String(bytes).TrimEnd(''='').Replace(''+'', ''-'').Replace(''/'', ''_'');
    }

    private static byte[] Base64UrlDecode(string value)
    {
        var base64 = value.Replace(''-'', ''+'').Replace(''_'', ''/'');
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

    private sealed record OidcStatePayload(string ProviderId, string ReturnUrl, string CodeVerifier, DateTimeOffset IssuedAtUtc);
    private sealed record OidcMetadata(string? AuthorizationEndpoint, string? TokenEndpoint, string? UserInfoEndpoint);
    private sealed record OidcTokenResponse(string? AccessToken, string? IdToken);
}
