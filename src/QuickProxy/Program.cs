using System.Net.Http.Headers;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Text.Json.Serialization;
using Aditify.Identity;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using QuickProxy;
using QuickProxy.Audit;
using QuickProxy.Audit.Db;
using QuickProxy.Configuration;
using QuickProxy.Proxy.Api;
using QuickProxy.Proxy.Config.Api;
using QuickProxy.Proxy.Config.Storage;
using QuickProxy.Proxy.Config.Storage.Db;
using QuickProxy.Proxy.Containers;
using QuickProxy.Proxy.Provisioning;
using QuickProxy.Proxy.Runtime;
using QuickProxy.Proxy.Storage;
using QuickProxy.Proxy.Storage.Db;
using QuickProxy.Proxy.Web;
using QuickProxy.Shared.Auth;
using QuickProxy.Shared.Configuration;
using QuickProxy.Shared.Hosting;
using QuickProxy.Shared.Web;
using Yarp.ReverseProxy.Configuration;
using Yarp.ReverseProxy.Transforms;

if (await SelfUpdateWorker.TryRunAsync(args)) return;

var builder = WebApplication.CreateBuilder(args);
var listenSettings = builder.Configuration.GetSection("Listen").Get<ListenSettings>() ?? new ListenSettings();
var modulesConfiguration = builder.Configuration.Get<AppModulesConfiguration>() ?? new AppModulesConfiguration
{
    Proxy = new AppModuleSettings
    {
        Enabled = true,
        Storage = new StorageSettings()
    },
    Config = new AppConfigModuleSettings
    {
        Enabled = true,
        Storage = new StorageSettings
        {
            Provider = "sqlite",
            ConnectionString = "Data Source=Data/quickconfig.db"
        }
    },
    Audit = new AppModuleSettings
    {
        Enabled = true,
        Storage = new StorageSettings
        {
            Provider = "sqlite",
            ConnectionString = "Data Source=Data/quickaudit.db"
        }
    }
};
var proxySettings = modulesConfiguration.Proxy;
var configSettings = modulesConfiguration.Config;
var auditSettings = modulesConfiguration.Audit;
proxySettings.Storage.Validate("Proxy");
if (configSettings.Enabled) configSettings.Storage.Validate("Config");
if (auditSettings.Enabled) auditSettings.Storage.Validate("Audit");
var containerSettings = builder.Configuration.GetSection("Containers").Get<ContainerRuntimeSettings>() ??
                        new ContainerRuntimeSettings();
const long MaxUploadBodyBytes = 4L * 1024 * 1024 * 1024;

builder.Services.Configure<ContainerRuntimeSettings>(builder.Configuration.GetSection("Containers"));
builder.Services.Configure<ProvisioningSettings>(builder.Configuration.GetSection("Provisioning"));
builder.Services.Configure<FormOptions>(options => { options.MultipartBodyLengthLimit = MaxUploadBodyBytes; });
builder.Services.AddSingleton(listenSettings);
builder.Services.AddSingleton(modulesConfiguration);
builder.Services
    .AddDataProtection()
    .SetApplicationName("QuickProxy")
    .PersistKeysToDbContext<QuickProxyDbContext>();

TlsCertificateSelector? tlsSelector = null;
AdminTlsCertificateSelector? adminTlsSelector = null;
X509Certificate2? adminCertificate = null;

builder.WebHost.ConfigureKestrel((context, options) =>
{
    var settings = context.Configuration.GetSection("Listen").Get<ListenSettings>() ?? new ListenSettings();
    options.Limits.MaxRequestBodySize = MaxUploadBodyBytes;

    settings.ValidateUniquePorts();

    if (proxySettings.Enabled && settings.HttpPort > 0) options.ListenAnyIP(settings.HttpPort);

    if (proxySettings.Enabled && settings.HttpsPort > 0)
        options.ListenAnyIP(settings.HttpsPort,
            listen =>
            {
                listen.UseHttps(https =>
                {
                    https.ServerCertificateSelector = (_, serverName) => tlsSelector?.Select(serverName);
                });
            });

    if (settings.InternalPort > 0)
        options.ListenAnyIP(settings.InternalPort, listen =>
        {
            if (!settings.AdminUseHttps) return;

            listen.UseHttps(https =>
            {
                https.ServerCertificateSelector = (_, serverName) =>
                    adminCertificate is null
                        ? null
                        : adminTlsSelector?.Select(serverName, adminCertificate) ?? adminCertificate;
            });
        });
});

builder.Services.AddDbContextFactory<QuickProxyDbContext>(options =>
{
    if (string.Equals(proxySettings.Storage.Provider, "sqlserver", StringComparison.OrdinalIgnoreCase))
    {
        options.UseSqlServer(proxySettings.Storage.ConnectionString);
        return;
    }

    options.UseSqlite(proxySettings.Storage.ConnectionString);
});

builder.Services.AddSingleton<IProxyHostRepository, DbProxyHostRepository>();
builder.Services.AddSingleton<IDomainTranslationStore, DbDomainTranslationStore>();
builder.Services.AddSingleton<IFallbackSettingsStore, DbFallbackSettingsStore>();
builder.Services.AddSingleton<ICertificateStore, DbCertificateStore>();
builder.Services.AddSingleton<IUserStore, DbUserStore>();
builder.Services.AddSingleton<IAuthProviderStore, DbAuthProviderStore>();
builder.Services.AddSingleton<IContainerDefaultsStore, DbContainerDefaultsStore>();
builder.Services.AddSingleton<IComposeProjectStore, DbComposeProjectStore>();
builder.Services.AddSingleton<IApplicationDataStore, DbApplicationDataStore>();

builder.Services.AddSingleton<IConfigEncryptionService, ConfigEncryptionService>();
if (auditSettings.Enabled)
{
    builder.Services.AddDbContextFactory<QuickAuditDbContext>(options =>
    {
        if (string.Equals(auditSettings.Storage.Provider, "sqlserver", StringComparison.OrdinalIgnoreCase))
        {
            options.UseSqlServer(auditSettings.Storage.ConnectionString);
            return;
        }

        options.UseSqlite(auditSettings.Storage.ConnectionString);
    });

    builder.Services.AddSingleton<IAuditStore, DbAuditStore>();
}
else
{
    builder.Services.AddSingleton<IAuditStore, NoOpAuditStore>();
}

if (configSettings.Enabled)
{
    builder.Services.AddDbContextFactory<QuickConfigDbContext>(options =>
    {
        if (string.Equals(configSettings.Storage.Provider, "sqlserver", StringComparison.OrdinalIgnoreCase))
        {
            options.UseSqlServer(configSettings.Storage.ConnectionString);
            return;
        }

        options.UseSqlite(configSettings.Storage.ConnectionString);
    });

    builder.Services.AddSingleton<ILocalConfigStore, DbConfigStore>();
}

if (configSettings.Enabled)
{
    builder.Services.AddSingleton<IRemoteConfigStore, RemoteConfigStore>();
    builder.Services.AddSingleton<IConfigReadService, ConfigReadService>();
}

builder.Services.AddSingleton<ProvisioningHostedService>();
builder.Services.AddSingleton<IPasswordHashingService, PasswordHashingService>();
builder.Services.AddSingleton<IExternalAuthService, ExternalAuthService>();
builder.Services.AddHttpClient();
builder.Services.AddSingleton<ContainerInventoryCache>();
builder.Services.AddSingleton<IContainerInventory>(sp => sp.GetRequiredService<ContainerInventoryCache>());
builder.Services.AddSingleton<IContainerRuntimeClient, DockerRuntimeClient>();
builder.Services.AddSingleton<ContainerImageUpdateResolver>();
builder.Services.AddSingleton<IContainerDefaultsApplier, ContainerDefaultsApplier>();
builder.Services.AddSingleton<DockerCliBootstrapper>();
builder.Services.AddSingleton<ComposeProjectDefaultsMerger>();
builder.Services.AddSingleton<IComposeProjectRunner, ComposeProjectRunner>();
builder.Services.AddSingleton<ComposeProjectService>();
builder.Services.AddSingleton<IHostTemplateValueProvider, HostTemplateValueProvider>();
builder.Services.AddSingleton<IInternalDnsService, InternalDnsHostedService>();
builder.Services.AddSingleton<DynamicProxyConfigProvider>();
builder.Services.AddSingleton<IProxyConfigProvider>(sp => sp.GetRequiredService<DynamicProxyConfigProvider>());
builder.Services.AddSingleton<IProxyHostRuntime>(sp => sp.GetRequiredService<DynamicProxyConfigProvider>());
builder.Services.AddSingleton<IDomainTranslationRuntime, DomainTranslationRuntime>();
builder.Services.AddSingleton<IFallbackSettingsCache, FallbackSettingsCache>();
builder.Services.AddSingleton<FallbackPageResponder>();
builder.Services.AddSingleton<DevelopmentCertificateAccessor>();
builder.Services.AddSingleton<AdminCertificateAccessor>();
builder.Services.AddSingleton<TlsCertificateSelector>();
builder.Services.AddSingleton<AdminTlsCertificateSelector>();
builder.Services.AddSingleton<ICertificateRuntimeCache>(sp => sp.GetRequiredService<TlsCertificateSelector>());
builder.Services.AddSingleton<IIssuedCertificateService, IssuedCertificateService>();
if (proxySettings.Enabled && containerSettings.Enabled)
{
    builder.Services.AddHostedService(sp => (InternalDnsHostedService)sp.GetRequiredService<IInternalDnsService>());
    builder.Services.AddHostedService<ContainerInventoryHostedService>();
    builder.Services.AddHostedService<ContainerStatsHostedService>();
    builder.Services.AddHostedService<ContainerImageUpdateHostedService>();
}

if (proxySettings.Enabled) builder.Services.AddHostedService(sp => sp.GetRequiredService<ProvisioningHostedService>());


const string adminIdentityScheme = "QuickProxy.AdminIdentity";
builder.Services.AddAuthentication(adminIdentityScheme);
builder.Services.AddAditifyIdentity(options =>
{
    options.CookieScheme = adminIdentityScheme;
    options.CookieName = "QuickProxy.Auth";
    options.BasePath = "/admin";
    options.AdministratorPolicy = "QuickProxy.Administrator";
    options.SessionLifetime = TimeSpan.FromDays(7);
});
builder.Services.AddScoped<IAdminIdentityStore, QuickProxyAdminIdentityStore>();
builder.Services.AddSingleton<IProductRoleCatalog, QuickProxyRoleCatalog>();
builder.Services.AddSingleton<IAdminIdentityAuditSink, QuickProxyIdentityAuditSink>();
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("QuickProxy.Reader", policy => policy.RequireRole(
        QuickProxyRoles.Reader, QuickProxyRoles.Operator, QuickProxyRoles.Administrator));
    options.AddPolicy("QuickProxy.Operator", policy => policy.RequireRole(
        QuickProxyRoles.Operator, QuickProxyRoles.Administrator));
    options.AddPolicy("QuickProxy.Administrator", policy => policy.RequireRole(QuickProxyRoles.Administrator));
    options.DefaultPolicy = options.GetPolicy("QuickProxy.Reader")!;
});
builder.Services.AddReverseProxy()
    .AddTransforms(transformBuilderContext =>
    {
        transformBuilderContext.AddRequestTransform(transformContext =>
        {
            if (!TryGetProxyDebugSnapshot(transformContext.HttpContext, out var snapshot))
                return ValueTask.CompletedTask;

            snapshot.OutboundRequestHeaders = CaptureProxyHeaders(transformContext.ProxyRequest.Headers,
                transformContext.ProxyRequest.Content?.Headers);
            snapshot.OutboundRequestVersion = transformContext.ProxyRequest.Version.ToString();
            snapshot.DestinationPrefix = transformContext.DestinationPrefix;
            return ValueTask.CompletedTask;
        });

        transformBuilderContext.AddResponseTransform(transformContext =>
        {
            if (!TryGetProxyDebugSnapshot(transformContext.HttpContext, out var snapshot) ||
                transformContext.ProxyResponse is null)
                return ValueTask.CompletedTask;

            snapshot.UpstreamResponseHeaders = CaptureProxyHeaders(transformContext.ProxyResponse.Headers,
                transformContext.ProxyResponse.Content?.Headers);
            snapshot.UpstreamResponseVersion = transformContext.ProxyResponse.Version.ToString();
            return ValueTask.CompletedTask;
        });
    });
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
});

var app = builder.Build();

app.Services.EnsureDatabaseReady<QuickProxyDbContext>(proxySettings.Storage);
if (configSettings.Enabled) app.Services.EnsureDatabaseReady<QuickConfigDbContext>(configSettings.Storage);
if (auditSettings.Enabled) app.Services.EnsureDatabaseReady<QuickAuditDbContext>(auditSettings.Storage);

if (listenSettings.AdminUseHttps)
    adminCertificate = app.Services.GetRequiredService<AdminCertificateAccessor>().LoadOrCreate();

ContainerTrustedRootSeeder.ImportFromDataDirectoryIfRunningInContainer(
    app.Environment,
    app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("ContainerTrustedRootSeeder"));

tlsSelector = app.Services.GetRequiredService<TlsCertificateSelector>();
adminTlsSelector = app.Services.GetRequiredService<AdminTlsCertificateSelector>();
app.Services.GetRequiredService<DynamicProxyConfigProvider>().TryReload();
app.Services.GetRequiredService<IDomainTranslationRuntime>().TryReload();

app.MapWhen(context => context.Connection.LocalPort == listenSettings.InternalPort, adminApp =>
{
    if (listenSettings.IsAdminLocalhostOnly()) adminApp.UseLocalhostAdminGuard();

    adminApp.UseDefaultFiles();
    adminApp.Use(async (context, next) =>
    {
        if (context.Request.Path.StartsWithSegments("/docs"))
            context.Response.OnStarting(() =>
            {
                context.Response.Headers["Content-Security-Policy"] =
                    "default-src 'self'; script-src 'self' 'unsafe-inline'; style-src 'self' 'unsafe-inline'; font-src 'self' data:; img-src 'self' data:; connect-src 'self'";
                return Task.CompletedTask;
            });

        context.Request.Path = DocumentationPathRewriter.Resolve(context.Request.Path,
            path => app.Environment.WebRootFileProvider.GetFileInfo(path).Exists);
        await next(context);
    });
    adminApp.UseStaticFiles();

    adminApp.UseWebSockets();
    adminApp.UseRouting();

    adminApp.UseAuthentication();
    adminApp.Use(async (context, next) =>
    {
        var path = context.Request.Path;
        if (context.User.HasClaim("aditi.must_change_password", "true") &&
            !path.StartsWithSegments("/admin/auth/status") &&
            !path.StartsWithSegments("/admin/auth/change-password") &&
            !path.StartsWithSegments("/admin/auth/logout"))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(new
            {
                code = "password_change_required",
                message = "Change the temporary password before continuing."
            });
            return;
        }

        var isMutation = !HttpMethods.IsGet(context.Request.Method) &&
                         !HttpMethods.IsHead(context.Request.Method) &&
                         !HttpMethods.IsOptions(context.Request.Method);
        if (InternalApiPaths.IsInternalAdminApi(path) && isMutation)
            try
            {
                await context.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(context);
            }
            catch (AntiforgeryValidationException)
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                await context.Response.WriteAsJsonAsync(new
                {
                    code = "antiforgery_failed",
                    message = "The request verification token is invalid."
                });
                return;
            }

        var needsOperator = InternalApiPaths.IsInternalAdminApi(path) &&
                            (isMutation ||
                             path.Value?.Contains("/shell", StringComparison.OrdinalIgnoreCase) == true ||
                             path.Value?.Contains("self-update", StringComparison.OrdinalIgnoreCase) == true);
        if (needsOperator && context.User.Identity?.IsAuthenticated == true &&
            !context.User.IsInRole(QuickProxyRoles.Operator) &&
            !context.User.IsInRole(QuickProxyRoles.Administrator))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        await next();
    });
    adminApp.UseAuthorization();
    adminApp.UseMiddleware<AuditLoggingMiddleware>();

    adminApp.UseEndpoints(endpoints =>
    {
        endpoints.MapAditifyIdentity();
        endpoints.MapSystemApi();
        if (auditSettings.Enabled) endpoints.MapAuditApi();

        if (proxySettings.Enabled)
        {
            endpoints.MapAdminApi();
            endpoints.MapDomainTranslationsApi();
            endpoints.MapFallbackSettingsApi();
            endpoints.MapCertificatesApi();
            endpoints.MapPublicCertificatesApi();

            if (containerSettings.Enabled)
            {
                endpoints.MapContainersApi();
                endpoints.MapPublicContainersApi();
            }
        }

        if (configSettings.Enabled)
        {
            endpoints.MapConfigsApi();
            endpoints.MapPublicConfigApi();
        }

        endpoints.MapGet("/", () => Results.Redirect("/admin/"));
        foreach (var legacyPath in new[]
                 {
                     "/proxy-hosts", "/containers", "/key-values", "/certificates", "/settings", "/audit",
                     "/users", "/login", "/issuers"
                 })
            endpoints.MapGet(legacyPath, (HttpContext context) =>
                Results.Redirect($"/admin{context.Request.Path}{context.Request.QueryString}"));

        endpoints.MapFallbackToFile("/admin/{*path:nonfile}", "admin/index.html");
    });
});

app.MapWhen(context => context.Connection.LocalPort != listenSettings.InternalPort, proxyApp =>
{
    if (proxySettings.Enabled)
    {
        proxyApp.UseMiddleware<ProxyPolicyMiddleware>();
        proxyApp.UseMiddleware<DomainTranslationMiddleware>();
        proxyApp.UseMiddleware<UnknownHostFallbackMiddleware>();
        proxyApp.UseMiddleware<ProxyErrorFallbackMiddleware>();
        proxyApp.UseMiddleware<ProxyDebugLoggingMiddleware>();
        proxyApp.UseWebSockets();
    }

    proxyApp.UseRouting();

    proxyApp.UseEndpoints(endpoints =>
    {
        if (proxySettings.Enabled)
            endpoints.MapReverseProxy();
        else
            endpoints.MapFallback(() => Results.NotFound(new
            {
                code = "not_found",
                message = "Proxy module is disabled."
            }));
    });
});

app.Run();

static Dictionary<string, string[]> CaptureProxyHeaders(HttpHeaders headers, HttpContentHeaders? contentHeaders)
{
    var result = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

    foreach (var header in headers) result[header.Key] = [.. header.Value];

    if (contentHeaders is not null)
        foreach (var header in contentHeaders)
            result[header.Key] = [.. header.Value];

    return result;
}

static bool TryGetProxyDebugSnapshot(HttpContext httpContext, out ProxyDebugSnapshot snapshot)
{
    if (httpContext.Items.TryGetValue(ProxyDebugLoggingMiddleware.ProxyDebugSnapshotItemKey, out var value) &&
        value is ProxyDebugSnapshot typed)
    {
        snapshot = typed;
        return true;
    }

    snapshot = null!;
    return false;
}

file static class SelfUpdateWorker
{
    public static async Task<bool> TryRunAsync(string[] args)
    {
        if (args.Length < 2 ||
            !string.Equals(args[0], "--self-update-worker", StringComparison.OrdinalIgnoreCase)) return false;

        var targetContainerName = args[1].Trim();
        if (string.IsNullOrWhiteSpace(targetContainerName))
            throw new InvalidOperationException("Self-update worker requires a target container name.");

        var imageReference = args.Length >= 3 && !string.IsNullOrWhiteSpace(args[2])
            ? args[2].Trim()
            : null;

        var endpoint = Environment.GetEnvironmentVariable("Containers__Endpoint") ?? string.Empty;
        var settings = new ContainerRuntimeSettings
        {
            Endpoint = endpoint
        };

        Console.WriteLine(
            $"[self-update] Worker started for target '{targetContainerName}' using image '{imageReference ?? "<current>"}'.");

        try
        {
            using var runtimeClient =
                new DockerRuntimeClient(Options.Create(settings), NoOpHostTemplateValueProvider.Instance);
            await runtimeClient.RunSelfUpdateWorkerAsync(targetContainerName, imageReference, CancellationToken.None);
            Console.WriteLine($"[self-update] Worker finished successfully for target '{targetContainerName}'.");
            return true;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[self-update] Worker failed for target '{targetContainerName}': {ex}");
            throw;
        }
    }
}

file sealed class NoOpHostTemplateValueProvider : IHostTemplateValueProvider
{
    public static NoOpHostTemplateValueProvider Instance { get; } = new();

    public IReadOnlyDictionary<string, string> TemplateValues { get; } = new Dictionary<string, string>();

    public string ReplacePlaceholders(string input)
    {
        return input;
    }

    public string ReplaceKvPlaceholders(string input)
    {
        return input;
    }
}