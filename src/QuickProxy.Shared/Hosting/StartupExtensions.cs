using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuickProxy.Shared.Auth;
using QuickProxy.Shared.Configuration;
using QuickProxy.Shared.Web;

namespace QuickProxy.Shared.Hosting;

public static class StartupExtensions
{
    public static IServiceCollection AddQuickCookieAuthentication(this IServiceCollection services, string cookieName)
    {
        services
            .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(options =>
            {
                options.Cookie.Name = cookieName;
                options.Cookie.HttpOnly = true;
                options.Cookie.SameSite = SameSiteMode.Lax;
                options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
                options.SlidingExpiration = true;
                options.ExpireTimeSpan = TimeSpan.FromDays(7);
                options.Events = new CookieAuthenticationEvents
                {
                    OnValidatePrincipal = context =>
                    {
                        try
                        {
                            var email = context.Principal?.FindFirstValue(ClaimTypes.Email)
                                        ?? context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);

                            if (string.IsNullOrWhiteSpace(email))
                            {
                                context.RejectPrincipal();
                                return Task.CompletedTask;
                            }

                            var users = context.HttpContext.RequestServices.GetRequiredService<IUserStore>();
                            var user = users.GetByEmail(email);
                            if (user is null || !user.Enabled) context.RejectPrincipal();
                        }
                        catch
                        {
                            context.RejectPrincipal();
                        }

                        return Task.CompletedTask;
                    },
                    OnRedirectToLogin = context =>
                    {
                        if (InternalApiPaths.IsInternalApi(context.Request.Path))
                        {
                            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                            return Task.CompletedTask;
                        }

                        context.Response.Redirect("/login");
                        return Task.CompletedTask;
                    },
                    OnRedirectToAccessDenied = context =>
                    {
                        if (InternalApiPaths.IsInternalApi(context.Request.Path))
                        {
                            context.Response.StatusCode = StatusCodes.Status403Forbidden;
                            return Task.CompletedTask;
                        }

                        context.Response.Redirect("/login");
                        return Task.CompletedTask;
                    }
                };
            });

        services.AddAuthorization();
        return services;
    }

    public static void EnsureDatabaseReady<TDbContext>(this IServiceProvider services, StorageSettings settings)
        where TDbContext : DbContext
    {
        if (string.Equals(settings.Provider, "sqlite", StringComparison.OrdinalIgnoreCase))
            EnsureSqliteDirectory(settings.ConnectionString);

        using var scope = services.CreateScope();
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<TDbContext>>();
        using var db = factory.CreateDbContext();
        db.Database.EnsureCreated();
    }

    private static void EnsureSqliteDirectory(string connectionString)
    {
        const string prefix = "Data Source=";
        var dataSource = connectionString
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault(x => x.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

        if (string.IsNullOrWhiteSpace(dataSource)) return;

        var path = dataSource[prefix.Length..].Trim().Trim('"');
        if (string.IsNullOrWhiteSpace(path) || path == ":memory:") return;

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
    }
}