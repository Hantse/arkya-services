using System.Threading.RateLimiting;
using Arkya.Admin.Content;
using Arkya.Admin.Realms;
using Arkya.Admin.Inference;
using Arkya.Admin.Reports;
using Arkya.Admin.Authentication;
using Arkya.Admin.Accounts;
using Arkya.Admin.Operations;
using Arkya.ServiceDefaults;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OAuth;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace Arkya.Admin;

public partial class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        PlayerAdministrationKeys.ConfigureAdmin(builder.Configuration);
        builder.Services.AddArkyaProxyTrust(builder.Configuration);
        builder.Services.AddOptions<RealmSiteOptions>().BindConfiguration("RealmSites")
            .Validate(o => o.IsValid(), "RealmSites:BaseDomain requires a lowercase DNS domain.")
            .Validate<IOptions<AdminDiscordOptions>, IOptions<AdminOptions>>((o, discord, admin) =>
                !o.Enabled || !admin.Value.DiscordEnabled || discord.Value.PublicBaseUrl.TrimEnd('/') == "https://admin." + o.BaseDomain,
                "Realm sites require the Discord callback origin to be the central admin origin.").ValidateOnStart();
        builder.Services.AddSingleton<RealmSiteLinks>();
        builder.Services.AddOptions<AdminDataProtectionOptions>().BindConfiguration("DataProtection")
            .Validate(o => string.IsNullOrEmpty(o.KeysPath) || Path.IsPathFullyQualified(o.KeysPath),
                "DataProtection:KeysPath must be an absolute path.").ValidateOnStart();
        var protection = builder.Services.AddDataProtection().SetApplicationName("Arkya.Admin");
        var keysPath = builder.Configuration["DataProtection:KeysPath"];
        if (!string.IsNullOrEmpty(keysPath)) protection.PersistKeysToFileSystem(new DirectoryInfo(keysPath));
        builder.Services.AddOptions<AdminOptions>().BindConfiguration("Admin")
            .Validate(o => o.DiscordEnabled || o.Password.Length is >= 16 and <= 512, "Admin:Password must contain 16 to 512 characters.")
            .Validate(o => !o.DiscordEnabled || o.AllowedDiscordUserIds is { Length: > 0 and <= 100 } &&
                o.AllowedDiscordUserIds.All(AdminDiscordAuthentication.IsDiscordId) &&
                o.AllowedDiscordUserIds.Distinct(StringComparer.Ordinal).Count() == o.AllowedDiscordUserIds.Length,
                "Admin:AllowedDiscordUserIds must contain distinct Discord user IDs when Discord login is enabled.")
            .ValidateOnStart();
        builder.Services.AddOptions<AdminDiscordOptions>().BindConfiguration("Discord")
            .Validate<IOptions<AdminOptions>>((o, admin) => !admin.Value.DiscordEnabled || o.IsValid(),
                "Discord requires a client ID, secret and an HTTPS public origin.")
            .ValidateOnStart();
        builder.Services.AddOptions<ContentApiOptions>().BindConfiguration("ContentApi")
            .Validate(o => !o.Enabled || o.AdminKey.Length is >= 32 and <= 512, "ContentApi:AdminKey must contain 32 to 512 characters.")
            .Validate(o => !o.Enabled || ContentApiOptions.IsSafe(o.BaseUrl, builder.Environment.IsDevelopment(), o.AllowClusterHttp),
                "ContentApi:BaseUrl requires HTTPS, Development loopback HTTP, or explicitly enabled Kubernetes service HTTP.")
            .ValidateOnStart();
        builder.Services.AddHttpClient<ContentAdminClient>((services, client) =>
        {
            var options = services.GetRequiredService<IOptions<ContentApiOptions>>().Value;
            if (options.Enabled) client.BaseAddress = new Uri(options.BaseUrl.TrimEnd('/') + "/");
            client.Timeout = TimeSpan.FromSeconds(5);
        }).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
        builder.Services.AddOptions<RealmsApiOptions>().BindConfiguration("RealmsApi")
            .Validate(o => !o.Enabled || o.AdminKey.Length is >= 32 and <= 512, "RealmsApi:AdminKey must contain 32 to 512 characters.")
            .Validate(o => !o.Enabled || ContentApiOptions.IsSafe(o.BaseUrl, builder.Environment.IsDevelopment(), o.AllowClusterHttp),
                "RealmsApi:BaseUrl requires HTTPS, Development loopback HTTP, or explicitly enabled Kubernetes service HTTP.")
            .Validate(o => !o.Enabled || string.IsNullOrEmpty(o.CertificateAuthorityFile) || Path.IsPathFullyQualified(o.CertificateAuthorityFile),
                "RealmsApi:CertificateAuthorityFile must be an absolute path.").ValidateOnStart();
        builder.Services.AddHttpClient<RealmsAdminClient>((services, client) =>
        {
            var options = services.GetRequiredService<IOptions<RealmsApiOptions>>().Value;
            if (options.Enabled) client.BaseAddress = new Uri(options.BaseUrl.TrimEnd('/') + "/");
            client.Timeout = TimeSpan.FromSeconds(10);
        }).ConfigurePrimaryHttpMessageHandler(services =>
        {
            var options = services.GetRequiredService<IOptions<RealmsApiOptions>>().Value;
            return RealmsHttpTransport.Create(options.Enabled ? options.CertificateAuthorityFile : "");
        });
        builder.Services.AddOptions<ReportsApiOptions>().BindConfiguration("ReportsApi")
            .PostConfigure(options => options.AdminKey = ReportsOperatorKeys.ResolveAdmin(builder.Configuration, options.AdminKey))
            .Validate(o => !o.Enabled || o.AdminKey.Length is >= 32 and <= 512, "ReportsApi:AdminKey must contain 32 to 512 characters.")
            .Validate(o => !o.Enabled || ContentApiOptions.IsSafe(o.BaseUrl, builder.Environment.IsDevelopment(), o.AllowClusterHttp),
                "ReportsApi:BaseUrl requires HTTPS, Development loopback HTTP, or explicitly enabled Kubernetes service HTTP.")
            .Validate(o => !o.Enabled || string.IsNullOrEmpty(o.CertificateAuthorityFile) || Path.IsPathFullyQualified(o.CertificateAuthorityFile),
                "ReportsApi:CertificateAuthorityFile must be an absolute path.").ValidateOnStart();
        builder.Services.AddHttpClient<ReportsAdminClient>((services, client) =>
        {
            var options = services.GetRequiredService<IOptions<ReportsApiOptions>>().Value;
            if (options.Enabled) client.BaseAddress = new Uri(options.BaseUrl.TrimEnd('/') + "/");
            client.Timeout = TimeSpan.FromSeconds(10);
            client.MaxResponseContentBufferSize = 8 * 1024 * 1024;
        }).ConfigurePrimaryHttpMessageHandler(services =>
        {
            var options = services.GetRequiredService<IOptions<ReportsApiOptions>>().Value;
            return RealmsHttpTransport.Create(options.Enabled ? options.CertificateAuthorityFile : "");
        });
        builder.Services.AddInferenceAdministration(builder.Configuration, builder.Environment);
        builder.Services.AddPlayerAdministration(builder.Configuration, builder.Environment);
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddOptions<AdminSessionOptions>().BindConfiguration("AdminSessions")
            .Validate(o => o.AccessLifetime >= TimeSpan.FromMinutes(5) && o.AccessLifetime <= TimeSpan.FromHours(2) &&
                o.RefreshLifetime > o.AccessLifetime && o.RefreshLifetime <= TimeSpan.FromDays(90),
                "Admin sessions require 5 minutes to 2 hours access and a longer refresh lifetime of at most 90 days.")
            .ValidateOnStart();
        builder.Services.AddOptions<MongoOptions>().BindConfiguration("Mongo")
            .Validate(o => !string.IsNullOrWhiteSpace(o.ConnectionString) && !string.IsNullOrWhiteSpace(o.DatabaseName),
                "Mongo connection string and database name are required for administrator sessions.").ValidateOnStart();
        builder.Services.AddSingleton<IMongoClient>(services =>
        {
            var settings = MongoClientSettings.FromConnectionString(services.GetRequiredService<IOptions<MongoOptions>>().Value.ConnectionString);
            settings.ServerSelectionTimeout = TimeSpan.FromSeconds(5);
            return new MongoClient(settings);
        });
        builder.Services.AddSingleton<MongoAdminSessionStore>();
        builder.Services.AddSingleton<IAdminSessionStore>(services => services.GetRequiredService<MongoAdminSessionStore>());
        builder.Services.AddHostedService<AdminSessionIndexInitializer>();
        builder.Services.AddScoped<AdminSessions>();
        builder.Services.AddScoped<OperationsDashboard>();
        builder.Services.AddHostedReportsMcp(builder.Configuration);
        var authentication = builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(options =>
        {
            options.LoginPath = "/Login";
            options.Cookie.Name = "Arkya.Admin";
            var realmSites = builder.Configuration.GetSection("RealmSites").Get<RealmSiteOptions>() ?? new();
            if (realmSites.Enabled)
            {
                options.Cookie.Name = "Arkya.Admin.RealmSites";
                options.Cookie.Domain = "admin." + realmSites.BaseDomain;
            }
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = builder.Configuration.GetValue<bool>("Admin:DiscordEnabled") ? SameSiteMode.Lax : SameSiteMode.Strict;
            options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
                ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
            options.ExpireTimeSpan = TimeSpan.FromMinutes(30);
            options.SlidingExpiration = false;
            options.Events.OnSigningIn = context => context.HttpContext.RequestServices.GetRequiredService<AdminSessions>().SigningInAsync(context);
            options.Events.OnValidatePrincipal = context => context.HttpContext.RequestServices.GetRequiredService<AdminSessions>().ValidatePrincipalAsync(context);
            options.Events.OnSigningOut = context => context.HttpContext.RequestServices.GetRequiredService<AdminSessions>().SigningOutAsync(context);
            options.Events.OnRedirectToLogin = context =>
            {
                var links = context.HttpContext.RequestServices.GetRequiredService<RealmSiteLinks>();
                context.Response.Redirect(links.RealmId(context.HttpContext) is not null ? links.LoginUrl(context.HttpContext) : context.RedirectUri);
                return Task.CompletedTask;
            };
        });
        builder.Services.AddOptions<CookieAuthenticationOptions>(CookieAuthenticationDefaults.AuthenticationScheme)
            .Configure<TimeProvider>((options, time) => options.TimeProvider = time);
        authentication.AddOAuth<OAuthOptions, AdminDiscordHandler>(AdminDiscordAuthentication.Scheme, options =>
        {
            var enabled = builder.Configuration.GetValue<bool>("Admin:DiscordEnabled");
            var discord = builder.Configuration.GetSection("Discord").Get<AdminDiscordOptions>() ?? new();
            options.ClientId = enabled ? discord.ClientId : "disabled";
            options.ClientSecret = enabled ? discord.ClientSecret : "disabled";
            options.SignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
            options.CallbackPath = AdminDiscordAuthentication.CallbackPath;
            options.AuthorizationEndpoint = "https://discord.com/oauth2/authorize";
            options.TokenEndpoint = "https://discord.com/api/oauth2/token";
            options.UserInformationEndpoint = "https://discord.com/api/v10/users/@me";
            options.Scope.Add("identify");
            options.SaveTokens = false;
            options.RemoteAuthenticationTimeout = TimeSpan.FromMinutes(5);
            options.BackchannelTimeout = TimeSpan.FromSeconds(10);
            options.Backchannel = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
            {
                Timeout = TimeSpan.FromSeconds(10),
                MaxResponseContentBufferSize = 16 * 1024
            };
            options.CorrelationCookie.SecurePolicy = CookieSecurePolicy.Always;
            options.CorrelationCookie.SameSite = SameSiteMode.None;
            options.Events.OnRemoteFailure = context =>
            {
                context.HandleResponse();
                context.Response.Redirect("/Login?error=discord");
                return Task.CompletedTask;
            };
        });
        builder.Services.AddAntiforgery(options =>
        {
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
                ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
        });
        builder.Services.AddAuthorization(options => options.AddPolicy("ContentAdmin", policy => policy.RequireRole("Administrator")));
        builder.Services.AddRazorPages(options =>
        {
            options.Conventions.ConfigureFilter(new RealmSitePageFilter());
            options.Conventions.AuthorizeFolder("/", "ContentAdmin");
            options.Conventions.AllowAnonymousToPage("/Login");
            options.Conventions.AllowAnonymousToPage("/Error");
        }).AddCookieTempDataProvider(options =>
        {
            var realmSites = builder.Configuration.GetSection("RealmSites").Get<RealmSiteOptions>() ?? new();
            if (!realmSites.Enabled) return;
            options.Cookie.Name = "Arkya.Admin.TempData.RealmSites";
            options.Cookie.Domain = "admin." + realmSites.BaseDomain;
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
                ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
        });
        builder.Services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
                context.Request.Path.Equals("/Login", StringComparison.OrdinalIgnoreCase) && HttpMethods.IsPost(context.Request.Method)
                    ? RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "local", _ =>
                        new FixedWindowRateLimiterOptions { PermitLimit = 8, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 })
                    : RateLimitPartition.GetNoLimiter("other"));
        });
        var app = builder.Build();
        app.UseArkyaProxyTrust();
        app.UseExceptionHandler("/Error");
        app.Use(async (context, next) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            context.Response.Headers["Referrer-Policy"] = "no-referrer";
            context.Response.Headers["X-Content-Type-Options"] = "nosniff";
            var scriptSource = string.Equals(context.Request.Path.Value?.TrimEnd('/'), "/CreateRealm", StringComparison.OrdinalIgnoreCase) ? "'self'" : "'none'";
            context.Response.Headers["Content-Security-Policy"] = "default-src 'self'; style-src 'self'; script-src " + scriptSource + "; frame-ancestors 'none'; base-uri 'self'; form-action 'self'" +
                (context.RequestServices.GetRequiredService<IOptions<AdminOptions>>().Value.DiscordEnabled ? " https://discord.com" : "");
            if (!app.Environment.IsDevelopment() && !context.Request.IsHttps)
            {
                context.Response.StatusCode = 400;
                return;
            }
            await next();
        });
        app.UseRealmSites();
        app.UseStaticFiles();
        app.UseHostedReportsMcpAccess();
        app.UseRouting();
        app.UseRateLimiter();
        app.Use(async (context, next) =>
        {
            try { await next(); }
            catch (Exception exception) when (exception is MongoException or TimeoutException && !context.Response.HasStarted)
            {
                context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                await context.Response.WriteAsync("Le service de session est temporairement indisponible.", context.RequestAborted);
            }
        });
        app.UseAuthentication();
        app.Use(async (context, next) =>
        {
            await context.RequestServices.GetRequiredService<AdminSessions>().RenewAsync(context);
            await next();
        });
        app.UseAuthorization();
        app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "admin" })).AllowAnonymous();
        app.MapRazorPages();
        app.MapHostedReportsMcp();
        app.Run();
    }
}

public sealed class AdminOptions
{
    public string Password { get; init; } = "";
    public bool DiscordEnabled { get; init; }
    public string[] AllowedDiscordUserIds { get; init; } = [];
}

public sealed class AdminDataProtectionOptions
{
    public string KeysPath { get; init; } = "";
}
