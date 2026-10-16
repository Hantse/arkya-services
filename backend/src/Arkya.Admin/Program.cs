using System.Threading.RateLimiting;
using Arkya.Admin.Content;
using Arkya.Admin.Realms;
using Arkya.ServiceDefaults;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace Arkya.Admin;

public partial class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.Services.AddArkyaProxyTrust(builder.Configuration);
        builder.Services.AddOptions<AdminDataProtectionOptions>().BindConfiguration("DataProtection")
            .Validate(o => string.IsNullOrEmpty(o.KeysPath) || Path.IsPathFullyQualified(o.KeysPath),
                "DataProtection:KeysPath must be an absolute path.").ValidateOnStart();
        var protection = builder.Services.AddDataProtection().SetApplicationName("Arkya.Admin");
        var keysPath = builder.Configuration["DataProtection:KeysPath"];
        if (!string.IsNullOrEmpty(keysPath)) protection.PersistKeysToFileSystem(new DirectoryInfo(keysPath));
        builder.Services.AddOptions<AdminOptions>().BindConfiguration("Admin")
            .Validate(o => o.Password.Length is >= 16 and <= 512, "Admin:Password must contain 16 to 512 characters.")
            .ValidateOnStart();
        builder.Services.AddOptions<ContentApiOptions>().BindConfiguration("ContentApi")
            .Validate(o => !o.Enabled || o.AdminKey.Length is >= 32 and <= 512, "ContentApi:AdminKey must contain 32 to 512 characters.")
            .Validate(o => !o.Enabled || ContentApiOptions.IsSafe(o.BaseUrl, builder.Environment.IsDevelopment()),
                "ContentApi:BaseUrl must use HTTPS, or loopback HTTP in Development.")
            .ValidateOnStart();
        builder.Services.AddHttpClient<ContentAdminClient>((services, client) =>
        {
            var options = services.GetRequiredService<IOptions<ContentApiOptions>>().Value;
            if (options.Enabled) client.BaseAddress = new Uri(options.BaseUrl.TrimEnd('/') + "/");
            client.Timeout = TimeSpan.FromSeconds(5);
        }).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
        builder.Services.AddOptions<RealmsApiOptions>().BindConfiguration("RealmsApi")
            .Validate(o => !o.Enabled || o.AdminKey.Length is >= 32 and <= 512, "RealmsApi:AdminKey must contain 32 to 512 characters.")
            .Validate(o => !o.Enabled || ContentApiOptions.IsSafe(o.BaseUrl, builder.Environment.IsDevelopment()),
                "RealmsApi:BaseUrl must use HTTPS, or loopback HTTP in Development.")
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
        builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(options =>
        {
            options.LoginPath = "/Login";
            options.Cookie.Name = "Arkya.Admin";
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
                ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
            options.ExpireTimeSpan = TimeSpan.FromHours(2);
            options.SlidingExpiration = false;
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
            options.Conventions.AuthorizeFolder("/", "ContentAdmin");
            options.Conventions.AllowAnonymousToPage("/Login");
            options.Conventions.AllowAnonymousToPage("/Error");
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
            context.Response.Headers["Content-Security-Policy"] = "default-src 'self'; style-src 'self'; script-src 'none'; frame-ancestors 'none'; base-uri 'self'; form-action 'self'";
            if (!app.Environment.IsDevelopment() && !context.Request.IsHttps)
            {
                context.Response.StatusCode = 400;
                return;
            }
            await next();
        });
        app.UseStaticFiles();
        app.UseRouting();
        app.UseRateLimiter();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "admin" })).AllowAnonymous();
        app.MapRazorPages();
        app.Run();
    }
}

public sealed class AdminOptions
{
    public string Password { get; init; } = "";
}

public sealed class AdminDataProtectionOptions
{
    public string KeysPath { get; init; } = "";
}
