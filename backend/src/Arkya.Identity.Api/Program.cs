using System.Threading.RateLimiting;
using Arkya.Identity.Api.Application;
using Arkya.Identity.Api.Endpoints;
using Arkya.Identity.Api.Infrastructure;
using Arkya.ServiceDefaults;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);
PlayerAdministrationKeys.ConfigureIdentity(builder.Configuration);
builder.Services.AddArkyaServiceDefaults(builder.Configuration);
builder.Services.AddIdentityRuntime(builder.Configuration);
builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow);
builder.Services.AddOptions<DiscordOptions>().BindConfiguration("Discord")
    .Validate(o => !string.IsNullOrWhiteSpace(o.ClientId) && !string.IsNullOrWhiteSpace(o.ClientSecret), "Discord credentials are required.")
    .Validate(o => o.HasValidEndpoints(),
        "Discord public URL and callback must use the same HTTPS origin, with no query, fragment or credentials.").ValidateOnStart();
builder.Services.AddOptions<Arkya.Identity.Api.Application.SessionOptions>().BindConfiguration("Sessions")
    .Validate(o => o.Lifetime > TimeSpan.Zero && o.Lifetime <= TimeSpan.FromDays(1), "Session lifetime must be greater than zero and at most one day.")
    .Validate(o => o.RefreshAccessLifetime > TimeSpan.Zero && o.RefreshAccessLifetime <= TimeSpan.FromDays(1), "Refresh access lifetime must be greater than zero and at most one day.")
    .Validate(o => o.RefreshLifetime >= o.RefreshAccessLifetime && o.RefreshLifetime <= TimeSpan.FromDays(90), "Refresh lifetime must cover access lifetime and be at most 90 days.").ValidateOnStart();
builder.Services.AddOptions<WebClientOptions>().BindConfiguration("WebClient")
    .Validate(o => o.HasValidOrigins(builder.Environment.IsDevelopment()),
        "Web client origins must be exact HTTPS origins without a trailing slash, path, credentials, query or fragment. HTTP localhost/127.0.0.1 is allowed only in Development.")
    .ValidateOnStart();
builder.Services.AddCors();
builder.Services.AddOptions<CorsOptions>().Configure<IOptions<WebClientOptions>>((cors, webClient) =>
    cors.AddPolicy(WebClientOptions.CorsPolicyName, policy => policy
        .WithOrigins(webClient.Value.AllowedOrigins)
        .WithMethods("GET", "POST")
        .WithHeaders("Content-Type", "Authorization", RefreshCookie.HeaderName)
        .AllowCredentials()
        .WithExposedHeaders("Retry-After")));
builder.Services.AddSingleton<MongoIdentityStore>();
builder.Services.AddSingleton<IIdentityStore>(sp => sp.GetRequiredService<MongoIdentityStore>());
builder.Services.AddSingleton<IBetaAccessStore>(sp => sp.GetRequiredService<MongoIdentityStore>());
builder.Services.AddHostedService<IdentityIndexInitializer>();
builder.Services.AddHostedService<BetaGrantRecoveryService>();
builder.Services.AddScoped<BetaAccessService>();
builder.Services.AddHttpClient<IDiscordIdentityClient, DiscordIdentityClient>(client => client.Timeout = TimeSpan.FromSeconds(15))
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
builder.Services.AddScoped<IdentityService>();
builder.Services.AddOptions<AdministrationOptions>().BindConfiguration("Administration")
    .Validate(options => options.IsValid(), "Administration:AdminKey must contain 32 to 512 characters when enabled.").ValidateOnStart();
builder.Services.AddSingleton<AccountAdminKeyValidator>();
builder.Services.AddScoped<AccountAdministration>();
builder.Services.AddDataProtection().SetApplicationName("Arkya.Identity");
builder.Services.AddHttpClient<IDiscordSocialClient, DiscordSocialClient>(client => client.Timeout = TimeSpan.FromSeconds(15))
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false })
    .RemoveAllLoggers();
builder.Services.AddScoped<DiscordSocialService>();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("auth", context => RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 120, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});
var app = builder.Build();
app.UseIdentityRuntime();
app.UseArkyaServiceDefaults();
app.UseRouting();
app.UseCors();
app.UseRateLimiter();
app.MapIdentityEndpoints();
app.MapAccountAdminEndpoints();
app.MapBetaAccessEndpoints();
app.MapDiscordSocialEndpoints();
app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "arkya-identity" }));
app.Run();

public partial class Program;
