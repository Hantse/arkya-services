using System.Globalization;
using System.Security.Claims;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Arkya.Reports.Api.Application;
using Arkya.Reports.Api.Endpoints;
using Arkya.Reports.Api.Infrastructure;
using Arkya.Reports.Api.Infrastructure.Identity;
using Arkya.Reports.Api.Infrastructure.Persistence;
using Arkya.ServiceDefaults;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Arkya.Reports.Api;

public partial class Program
{
    public const long MaximumRequestBodyBytes = 3 * 1024 * 1024;
    private static readonly object SecureTransport = new();

    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = MaximumRequestBodyBytes);
        builder.Services.AddArkyaServiceDefaults(builder.Configuration);
        builder.Services.PostConfigure<ServiceAuthenticationOptions>(options =>
            options.Key = ReportsOperatorKeys.ResolveService(builder.Configuration, options.Key));
        builder.Services.AddArkyaInternalHttp(builder.Configuration);
        builder.Services.AddArkyaProxyTrust(builder.Configuration);
        builder.Services.ConfigureHttpJsonOptions(options =>
        {
            options.SerializerOptions.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow;
            options.SerializerOptions.MaxDepth = 16;
        });
        builder.Services.Configure<RouteHandlerOptions>(options => options.ThrowOnBadRequest = false);
        builder.Services.AddOptions<BrowserClientOptions>().BindConfiguration("WebClient")
            .Validate(options => options.IsValid(builder.Environment.IsDevelopment()), "Browser origins must be exact HTTPS origins.")
            .ValidateOnStart();
        builder.Services.AddCors();
        builder.Services.AddOptions<Microsoft.AspNetCore.Cors.Infrastructure.CorsOptions>()
            .Configure<IOptions<BrowserClientOptions>>((cors, web) =>
                cors.AddPolicy(BrowserClientOptions.Policy, policy => policy.WithOrigins(web.Value.AllowedOrigins)
                    .WithMethods("GET", "POST").WithHeaders("Authorization", "Content-Type").WithExposedHeaders("Retry-After")));
        builder.Services.AddOptions<IdentityOptions>().BindConfiguration("Identity")
            .Validate(options => IdentityOptions.HasSafeBaseUrl(options, builder.Environment.IsDevelopment()),
                "Identity:BaseUrl requires HTTPS, loopback HTTP in Development, or explicit AllowClusterHttp with a Kubernetes service FQDN.")
            .Validate(options => options.ServiceKey.Length is >= 32 and <= 512, "Identity:ServiceKey requires 32 to 512 characters.")
            .ValidateOnStart();
        builder.Services.AddOptions<ReportsAdminOptions>().BindConfiguration("ReportsAdmin")
            .PostConfigure(options => options.Key = ReportsOperatorKeys.ResolveReports(builder.Configuration, options.Key))
            .Validate(options => options.Key.Length is >= 32 and <= 512, "ReportsAdmin:Key requires 32 to 512 characters.")
            .Validate<IOptions<ServiceAuthenticationOptions>>((options, service) => options.Key != service.Value.Key &&
                options.Key != builder.Configuration["Identity:ServiceKey"], "The Reports administrator key must be distinct from service keys.")
            .Validate(options => ReportService.ValidActor(options.Actor), "ReportsAdmin:Actor must be printable and at most 128 characters.")
            .ValidateOnStart();
        builder.Services.AddSingleton<ReportsAdminKeyValidator>();
        builder.Services.AddSingleton<MongoReportRepository>();
        builder.Services.AddSingleton<IReportRepository>(services => services.GetRequiredService<MongoReportRepository>());
        builder.Services.AddHostedService<ReportStoreInitializer>();
        builder.Services.AddScoped<ReportService>();
        builder.Services.AddHttpClient<ISessionValidator, IdentitySessionValidator>((services, client) =>
        {
            var options = services.GetRequiredService<IOptions<IdentityOptions>>().Value;
            client.BaseAddress = new Uri(options.BaseUrl.TrimEnd('/') + "/");
            client.Timeout = TimeSpan.FromSeconds(5);
        }).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false }).RemoveAllLoggers();
        builder.Services.AddAuthentication(SessionAuthenticationHandler.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, SessionAuthenticationHandler>(SessionAuthenticationHandler.SchemeName, _ => { });
        builder.Services.AddAuthorization();
        builder.Services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy("report-submission", context => RateLimitPartition.GetFixedWindowLimiter(
                context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "unauthenticated",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = 5, Window = TimeSpan.FromMinutes(10), QueueLimit = 0 }));
            options.OnRejected = async (context, cancellationToken) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retry))
                    context.HttpContext.Response.Headers.RetryAfter = Math.Ceiling(retry.TotalSeconds).ToString(CultureInfo.InvariantCulture);
                await Results.Problem(statusCode: 429, title: "Too many bug reports. Please retry later.")
                    .ExecuteAsync(context.HttpContext);
            };
        });

        var app = builder.Build();
        app.UseArkyaInternalHttp();
        app.Use((context, next) =>
        {
            context.Items[SecureTransport] = context.Request.IsHttps;
            return next(context);
        });
        app.UseArkyaProxyTrust();
        app.UseArkyaServiceDefaults();
        app.Use(async (context, next) =>
        {
            if (context.Request.ContentLength > MaximumRequestBodyBytes)
            {
                context.Response.StatusCode = 413;
                return;
            }
            if (context.Request.Path.StartsWithSegments("/internal"))
            {
                if (!app.Environment.IsDevelopment() && context.Items[SecureTransport] is not true && !context.IsArkyaInternalHttpAllowed())
                {
                    context.Response.StatusCode = 400;
                    return;
                }
                var keys = context.Request.Headers[ReportsAdminKeyValidator.HeaderName];
                if (keys.Count != 1 || !context.RequestServices.GetRequiredService<ReportsAdminKeyValidator>().IsValid(keys[0]))
                {
                    await Results.Problem(statusCode: 403, title: "Reports administrator authentication failed.").ExecuteAsync(context);
                    return;
                }
            }
            await next(context);
        });
        app.UseRouting();
        app.UseCors();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseRateLimiter();
        app.Use(async (context, next) =>
        {
            var original = context.Request.Body;
            context.Request.Body = new LimitedRequestStream(original, MaximumRequestBodyBytes);
            try { await next(context); }
            finally { context.Request.Body = original; }
        });
        app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "reports" })).AllowAnonymous();
        app.MapGet("/health/ready", async (IMongoClient mongo, IOptions<MongoOptions> configured, CancellationToken cancellationToken) =>
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(TimeSpan.FromSeconds(2));
            try
            {
                await mongo.GetDatabase(configured.Value.DatabaseName).RunCommandAsync<BsonDocument>(
                    new BsonDocument("ping", 1), cancellationToken: deadline.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return Results.Json(new { status = "not_ready", service = "reports" }, statusCode: 503);
            }
            return Results.Ok(new { status = "ready", service = "reports" });
        }).AllowAnonymous();
        app.MapReportEndpoints();
        app.Run();
    }
}
