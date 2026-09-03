using System.Security.Claims;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using QuickProxy.Proxy.Config.Storage;
using QuickProxy.Proxy.Models;
using QuickProxy.Proxy.Storage;
using QuickProxy.Shared.Auth;
using QuickProxy.Shared.Web;

namespace QuickProxy.Proxy.Api;

public static partial class AuthApiExtensions
{
    [GeneratedRegex("^[a-z0-9]+(?:-[a-z0-9]+)*$")]
    private static partial Regex ProviderIdRegex();

    public static IEndpointRouteBuilder MapAuthApi(this IEndpointRouteBuilder app)
    {
        var authGroup = app.MapGroup($"{InternalApiPaths.AdminRoot}/auth");

        authGroup.MapGet("/status",
            (ClaimsPrincipal principal, IUserStore store, IExternalAuthService externalAuthService) =>
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

        authGroup.MapGet("/providers",
            (IExternalAuthService externalAuthService) => Results.Ok(externalAuthService.ListLoginProviders()));

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
                return Validation(["password is required and must be valid base64 when passwordBase64 is used."]);

            if (store.AnyUsers())
                return Results.Conflict(new
                    { code = "users_exist", message = "Bootstrap is only available when no users exist." });

            var errors = UserInput.ValidateNewUser(request.Email, password, request.FullName);
            if (errors.Count > 0) return Validation(errors);

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
                return InvalidCredentials();

            var email = NormalizeEmail(request.Email);
            var user = store.GetByEmail(email);
            if (user is not null && user.Enabled && !string.IsNullOrWhiteSpace(user.PasswordHash) &&
                hasher.Verify(user.PasswordHash, password))
            {
                await SignInAsync(context, user, "local", null);
                return Results.Ok(ToResponse(user, "local", null));
            }

            var externalResult =
                await externalAuthService.AuthenticatePasswordAsync(email, password, cancellationToken);
            if (externalResult is null) return InvalidCredentials();

            var provider = externalAuthService.GetProvider(externalResult.ProviderId);
            if (provider is null) return InvalidCredentials();

            var externalUser = ResolveOrCreateExternalUser(store, provider, externalResult);
            if (externalUser is null)
                return Results.BadRequest(new
                {
                    code = "external_access_denied",
                    message =
                        $"Provider '{provider.DisplayName}' authenticated successfully, but access is not allowed."
                });

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
                var url = await externalAuthService.BuildOidcChallengeUrlAsync(context, providerId, request.ReturnUrl,
                    cancellationToken);
                return Results.Ok(new { url });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { code = "oidc_start_failed", message = ex.Message });
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
                return Results.Redirect($"/login?error={Uri.EscapeDataString(error)}");

            if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(state))
                return Results.Redirect("/login?error=oidc_callback_invalid");

            try
            {
                var externalResult =
                    await externalAuthService.CompleteOidcAsync(context, providerId, code, state, cancellationToken);
                var provider = externalAuthService.GetProvider(externalResult.ProviderId);
                if (provider is null) return Results.Redirect("/login?error=oidc_provider_missing");

                var user = ResolveOrCreateExternalUser(store, provider, externalResult);
                if (user is null) return Results.Redirect("/login?error=external_access_denied");

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

        providersGroup.MapGet("/", (IAuthProviderStore store) => Results.Ok(store.List().Select(ToProviderResponse)));
        providersGroup.MapGet("/{id}",
            (string id, IAuthProviderStore store) => store.Get(id) is { } provider
                ? Results.Ok(ToProviderResponse(provider))
                : ProviderNotFound(id));

        providersGroup.MapPost("/", (
            UpsertAuthProviderRequest request,
            IAuthProviderStore store,
            IConfigEncryptionService encryptionService) =>
        {
            var errors = ValidateProviderRequest(request, store.List(), null);
            if (errors.Count > 0) return Validation(errors);

            var provider = ToStoredProvider(request, null, encryptionService);
            store.Upsert(provider);
            return Results.Created($"{InternalApiPaths.AdminRoot}/auth-providers/{provider.Id}",
                ToProviderResponse(provider));
        });

        providersGroup.MapPut("/{id}", (
            string id,
            UpsertAuthProviderRequest request,
            IAuthProviderStore store,
            IConfigEncryptionService encryptionService) =>
        {
            var existing = store.Get(id);
            if (existing is null) return ProviderNotFound(id);

            request = request with { Id = id };
            var errors = ValidateProviderRequest(request, store.List(), id);
            if (errors.Count > 0) return Validation(errors);

            var provider = ToStoredProvider(request, existing, encryptionService);
            store.Upsert(provider);
            return Results.Ok(ToProviderResponse(provider));
        });

        providersGroup.MapDelete("/{id}",
            (string id, IAuthProviderStore store) => store.Delete(id) ? Results.NoContent() : ProviderNotFound(id));

        providersGroup.MapPost("/test/ldap", async (
            UpsertAuthProviderRequest request,
            IConfigEncryptionService encryptionService,
            IExternalAuthService externalAuthService,
            CancellationToken cancellationToken) =>
        {
            var provider = ToStoredProvider(request, null, encryptionService);
            return Results.Ok(await externalAuthService.TestLdapAsync(provider, cancellationToken));
        });

        providersGroup.MapPost("/test/oidc-discovery", async (
            UpsertAuthProviderRequest request,
            IConfigEncryptionService encryptionService,
            IExternalAuthService externalAuthService,
            CancellationToken cancellationToken) =>
        {
            var provider = ToStoredProvider(request, null, encryptionService);
            return Results.Ok(await externalAuthService.TestOidcDiscoveryAsync(provider, cancellationToken));
        });

        return app;
    }

    private static AdminUserRecord? ResolveOrCreateExternalUser(IUserStore store, AuthProviderConfig provider,
        ExternalAuthResult result)
    {
        var existingByIdentity = store.List().FirstOrDefault(x => x.ExternalIdentities.Any(identity =>
            string.Equals(identity.ProviderId, provider.Id, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(identity.Subject, result.Subject, StringComparison.Ordinal)));
        var user = existingByIdentity ?? store.GetByEmail(NormalizeEmail(result.Email));
        if (user is not null && !user.Enabled) return null;

        if (user is null)
        {
            if (!provider.AllowAutoAccess) return null;

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
            user.ExternalIdentities.Add(new AdminUserExternalIdentity
            {
                ProviderId = provider.Id,
                Subject = result.Subject
            });

        store.Upsert(user);
        return store.GetByEmail(user.Email);
    }

    private static AdminUserResponse? BuildUserFromPrincipal(ClaimsPrincipal principal, IUserStore store)
    {
        if (principal.Identity?.IsAuthenticated != true) return null;

        var email = principal.FindFirstValue(ClaimTypes.Email) ?? principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(email)) return null;

        var user = store.GetByEmail(email);
        return user is null || !user.Enabled
            ? null
            : ToResponse(user, principal.FindFirstValue("quickproxy_auth_type") ?? "local",
                principal.FindFirstValue("quickproxy_auth_provider"));
    }

    private static async Task SignInAsync(HttpContext context, AdminUserRecord user, string authType,
        string? providerId)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Email),
            new(ClaimTypes.Email, user.Email),
            new(ClaimTypes.Name, string.IsNullOrWhiteSpace(user.FullName) ? user.Email : user.FullName),
            new("quickproxy_auth_type", authType)
        };

        if (!string.IsNullOrWhiteSpace(providerId)) claims.Add(new Claim("quickproxy_auth_provider", providerId));

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        await context.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identity),
            new AuthenticationProperties
            {
                IsPersistent = true,
                AllowRefresh = true,
                ExpiresUtc = DateTimeOffset.UtcNow.AddDays(7)
            });
    }

    private static AdminUserResponse ToResponse(AdminUserRecord user, string authType, string? providerId)
    {
        return new AdminUserResponse(user.Email, user.FullName, user.Enabled, authType, providerId,
            !string.IsNullOrWhiteSpace(user.PasswordHash), user.ExternalIdentities.Count);
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
                string.Empty,
                provider.Oidc.MetadataUrl,
                provider.Oidc.ClientId,
                !string.IsNullOrWhiteSpace(provider.Oidc.EncryptedClientSecret),
                provider.Oidc.Scopes,
                provider.Oidc.EmailClaim,
                provider.Oidc.NameClaim,
                provider.Oidc.SubjectClaim,
                provider.Oidc.UsePkce));
    }

    private static AuthProviderConfig ToStoredProvider(UpsertAuthProviderRequest request, AuthProviderConfig? existing,
        IConfigEncryptionService encryptionService)
    {
        var normalizedOidcMetadataUrl =
            NormalizeOidcDiscoveryEndpoint(request.Oidc.MetadataUrl, request.Oidc.Authority);

        return new AuthProviderConfig
        {
            Id = (request.Id ?? string.Empty).Trim(),
            DisplayName = string.IsNullOrWhiteSpace(request.DisplayName)
                ? (request.Id ?? string.Empty).Trim()
                : request.DisplayName.Trim(),
            Enabled = request.Enabled,
            AllowAutoAccess = request.AllowAutoAccess,
            Type = request.Type,
            Ldap = new LdapAuthProviderConfig
            {
                Server = request.Ldap.Server?.Trim() ?? string.Empty,
                Port = request.Ldap.Port <= 0 ? 389 : request.Ldap.Port,
                UseSsl = request.Ldap.UseSsl,
                BindDn = request.Ldap.BindDn?.Trim() ?? string.Empty,
                EncryptedBindPassword = ResolveEncryptedSecret(request.Ldap.BindPassword,
                    request.Ldap.ClearBindPassword, existing?.Ldap.EncryptedBindPassword, encryptionService),
                BaseDn = request.Ldap.BaseDn?.Trim() ?? string.Empty,
                UserFilter = string.IsNullOrWhiteSpace(request.Ldap.UserFilter)
                    ? "(mail={email})"
                    : request.Ldap.UserFilter.Trim(),
                EmailAttribute = string.IsNullOrWhiteSpace(request.Ldap.EmailAttribute)
                    ? "mail"
                    : request.Ldap.EmailAttribute.Trim(),
                FullNameAttribute = string.IsNullOrWhiteSpace(request.Ldap.FullNameAttribute)
                    ? "displayName"
                    : request.Ldap.FullNameAttribute.Trim()
            },
            Oidc = new OidcAuthProviderConfig
            {
                Authority = string.Empty,
                MetadataUrl = normalizedOidcMetadataUrl,
                ClientId = request.Oidc.ClientId?.Trim() ?? string.Empty,
                EncryptedClientSecret = ResolveEncryptedSecret(request.Oidc.ClientSecret,
                    request.Oidc.ClearClientSecret, existing?.Oidc.EncryptedClientSecret, encryptionService),
                Scopes = string.IsNullOrWhiteSpace(request.Oidc.Scopes)
                    ? "openid profile email"
                    : request.Oidc.Scopes.Trim(),
                EmailClaim = string.IsNullOrWhiteSpace(request.Oidc.EmailClaim)
                    ? "email"
                    : request.Oidc.EmailClaim.Trim(),
                NameClaim = string.IsNullOrWhiteSpace(request.Oidc.NameClaim) ? "name" : request.Oidc.NameClaim.Trim(),
                SubjectClaim = string.IsNullOrWhiteSpace(request.Oidc.SubjectClaim)
                    ? "sub"
                    : request.Oidc.SubjectClaim.Trim(),
                UsePkce = request.Oidc.UsePkce
            }
        };
    }

    private static string ResolveEncryptedSecret(string? plaintext, bool clear, string? existingCiphertext,
        IConfigEncryptionService encryptionService)
    {
        if (clear) return string.Empty;

        return !string.IsNullOrWhiteSpace(plaintext)
            ? encryptionService.EncryptString(plaintext)
            : existingCiphertext ?? string.Empty;
    }

    private static List<string> ValidateProviderRequest(UpsertAuthProviderRequest request,
        IReadOnlyList<AuthProviderConfig> currentProviders, string? replaceId)
    {
        var errors = new List<string>();
        var id = (request.Id ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(id) || !ProviderIdRegex().IsMatch(id))
            errors.Add("id is required and must be lowercase kebab-case.");

        if (currentProviders.Any(x =>
                (replaceId is null || !string.Equals(x.Id, replaceId, StringComparison.OrdinalIgnoreCase)) &&
                string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase)))
            errors.Add($"Provider '{id}' already exists.");

        if (request.Type == AuthProviderType.Ldap)
        {
            if (string.IsNullOrWhiteSpace(request.Ldap.Server)) errors.Add("ldap.server is required.");

            if (string.IsNullOrWhiteSpace(request.Ldap.BaseDn)) errors.Add("ldap.baseDn is required.");
        }

        if (request.Type == AuthProviderType.Oidc)
        {
            if (string.IsNullOrWhiteSpace(NormalizeOidcDiscoveryEndpoint(request.Oidc.MetadataUrl,
                    request.Oidc.Authority))) errors.Add("oidc.metadataUrl is required.");

            if (string.IsNullOrWhiteSpace(request.Oidc.ClientId)) errors.Add("oidc.clientId is required.");
        }

        return errors;
    }

    private static string NormalizeOidcDiscoveryEndpoint(string? metadataUrl, string? authority)
    {
        var normalizedMetadataUrl = metadataUrl?.Trim() ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(normalizedMetadataUrl)) return normalizedMetadataUrl;

        var normalizedAuthority = authority?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalizedAuthority)) return string.Empty;

        return normalizedAuthority.TrimEnd('/') + "/.well-known/openid-configuration";
    }

    private static string NormalizeEmail(string value)
    {
        return UserInput.NormalizeEmail(value);
    }

    private static IResult Validation(List<string> details)
    {
        return Results.BadRequest(new { code = "validation_error", message = "Validation failed.", details });
    }

    private static IResult ProviderNotFound(string id)
    {
        return Results.NotFound(new { code = "not_found", message = $"Auth provider '{id}' was not found." });
    }

    private static IResult InvalidCredentials()
    {
        return Results.BadRequest(new { code = "invalid_credentials", message = "Invalid email or password." });
    }

    private static bool TryResolveRequestPassword(string? password, string? passwordBase64, out string resolvedPassword)
    {
        if (!string.IsNullOrWhiteSpace(password))
        {
            resolvedPassword = password;
            return true;
        }

        if (!string.IsNullOrWhiteSpace(passwordBase64))
            try
            {
                resolvedPassword = Encoding.UTF8.GetString(Convert.FromBase64String(passwordBase64));
                return !string.IsNullOrWhiteSpace(resolvedPassword);
            }
            catch (FormatException)
            {
            }

        resolvedPassword = string.Empty;
        return false;
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