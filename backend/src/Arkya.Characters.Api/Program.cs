using System.Text.Json.Serialization;
using Arkya.Characters.Api.Application;
using Arkya.Characters.Api.Endpoints;
using Arkya.Characters.Api.Infrastructure;
using Arkya.Characters.Api.Infrastructure.Identity;
using Arkya.Characters.Api.Infrastructure.Persistence;
using Arkya.ServiceDefaults;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Arkya.Characters.Api;

public partial class Program
{
    private static readonly object SecureTransport = new();

    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        PlayerAdministrationKeys.ConfigureCharacters(builder.Configuration);
        builder.Services.AddArkyaServiceDefaults(builder.Configuration);
        builder.Services.AddArkyaInternalHttp(builder.Configuration);
        builder.Services.AddArkyaProxyTrust(builder.Configuration);
        builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 16384);
        builder.Services.AddOptions<BrowserClientOptions>().BindConfiguration("WebClient")
            .Validate(value => value.IsValid(builder.Environment.IsDevelopment()), "Browser origins must be exact HTTPS origins.").ValidateOnStart();
        builder.Services.AddCors();
        builder.Services.AddOptions<Microsoft.AspNetCore.Cors.Infrastructure.CorsOptions>().Configure<IOptions<BrowserClientOptions>>((cors, web) =>
            cors.AddPolicy(BrowserClientOptions.Policy, policy => policy.WithOrigins(web.Value.AllowedOrigins)
                .WithMethods("GET", "POST").WithHeaders("Authorization", "Content-Type")));
        builder.Services.AddOptions<RealmClientOptions>().BindConfiguration("Realms")
            .Validate(value => value.IsValid(builder.Environment.IsDevelopment()), "Realm catalogue URL/key configuration is invalid.").ValidateOnStart();
        builder.Services.AddOptions<CharacterGameAccessOptions>().BindConfiguration("GameAccess")
            .Configure<IConfiguration>((value, configuration) =>
            {
                if (configuration["RealmRuntime:DedicatedKeySeed"] is null) return;
                // Options are also created by startup validation; keep derived credentials in a local copy.
                using var runtime = new ConfigurationManager();
                runtime.AddInMemoryCollection(configuration.GetSection("RealmRuntime").AsEnumerable()
                    .Concat(configuration.GetSection("GameAccess").AsEnumerable()));
                RealmRuntimeKeys.ConfigureCharacters(runtime);
                runtime.GetSection("GameAccess").Bind(value);
            })
            .Validate(value => value.IsValid(), "Dedicated identities require unique keys and realm/server bindings.").ValidateOnStart();
        builder.Services.AddSingleton<DedicatedKeyValidator>();
        builder.Services.AddOptions<CharacterAdminOptions>().BindConfiguration("AdminAuthentication")
            .Validate(options => options.Key.Length == 0 || options.Key.Length is >= 32 and <= 512,
                "AdminAuthentication:Key must be empty to disable administration, or contain 32 to 512 characters.")
            .Validate(options => options.Key.Length == 0 || options.Key != builder.Configuration["ServiceAuthentication:Key"] &&
                options.Key != builder.Configuration["Identity:ServiceKey"], "The character administrator key must differ from service keys.")
            .ValidateOnStart();
        builder.Services.AddSingleton<CharacterAdminKeyValidator>();
        builder.Services.AddSingleton<ICharacterAdminRepository, MongoCharacterAdminRepository>();
        builder.Services.AddSingleton<MongoGameLeaseStore>();
        builder.Services.AddSingleton<IGameLeaseStore>(services => services.GetRequiredService<MongoGameLeaseStore>());
        builder.Services.AddScoped<GameAdmissionService>();
        builder.Services.AddHttpClient<IGameRealmCatalog, GameRealmClient>(client => client.Timeout = TimeSpan.FromSeconds(5))
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false }).RemoveAllLoggers();
        builder.Services.ConfigureHttpJsonOptions(options =>
            options.SerializerOptions.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow);
        builder.Services.Configure<RouteHandlerOptions>(options => options.ThrowOnBadRequest = false);
        builder.Services.AddOptions<IdentityOptions>().Bind(builder.Configuration.GetSection("Identity"))
            .Validate(options => IdentityOptions.HasSafeBaseUrl(options, builder.Environment.IsDevelopment()),
                "Identity:BaseUrl requires HTTPS, Development loopback HTTP, or explicit AllowClusterHttp with a Kubernetes service FQDN.")
            .Validate(options => options.ServiceKey.Length is >= 32 and <= 512,
                "Identity:ServiceKey must contain 32 to 512 characters.")
            .ValidateOnStart();
        builder.Services.AddSingleton<MongoCharacterRepository>();
        builder.Services.AddSingleton<ICharacterRepository>(services => services.GetRequiredService<MongoCharacterRepository>());
        builder.Services.AddSingleton<MongoCharacterQuestRepository>();
        builder.Services.AddSingleton<ICharacterQuestRepository>(services => services.GetRequiredService<MongoCharacterQuestRepository>());
        builder.Services.AddHostedService<CharacterIndexInitializer>();
        builder.Services.AddScoped<CharacterService>();
        builder.Services.AddScoped<CharacterQuestService>();
        builder.Services.AddHttpClient<ISessionValidator, IdentitySessionValidator>((services, client) =>
        {
            var options = services.GetRequiredService<IOptions<IdentityOptions>>().Value;
            client.BaseAddress = new Uri(options.BaseUrl.TrimEnd('/') + "/");
            client.Timeout = TimeSpan.FromSeconds(5);
        }).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
        builder.Services.AddHttpClient<ISessionReferenceValidator, IdentitySessionValidator>((services, client) =>
        {
            var options = services.GetRequiredService<IOptions<IdentityOptions>>().Value;
            client.BaseAddress = new Uri(options.BaseUrl.TrimEnd('/') + "/");
            client.Timeout = TimeSpan.FromSeconds(5);
        }).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false }).RemoveAllLoggers();
        builder.Services.AddAuthentication(SessionAuthenticationHandler.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, SessionAuthenticationHandler>(SessionAuthenticationHandler.SchemeName, _ => { });
        builder.Services.AddAuthorization();

        var app = builder.Build();
        var gameAccess = app.Services.GetRequiredService<IOptions<CharacterGameAccessOptions>>().Value;
        app.UseArkyaInternalHttp(context => context.Request.Path.StartsWithSegments("/admin/characters") || context.Request.Path == "/health" ||
            gameAccess.AllowPrivateHttp && context.Request.Path.StartsWithSegments("/internal/game"));
        app.Use((context, next) =>
        {
            context.Items[SecureTransport] = context.Request.IsHttps;
            return next(context);
        });
        app.UseArkyaProxyTrust();
        app.UseArkyaServiceDefaults();
        app.Use(async (context, next) =>
        {
            if ((context.Request.Path.StartsWithSegments("/admin/characters") || context.Request.Path.StartsWithSegments("/internal/game")) && !app.Environment.IsDevelopment() &&
                context.Items[SecureTransport] is not true && !context.IsArkyaInternalHttpAllowed())
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }
            await next(context);
        });
        app.UseRouting();
        app.UseCors();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "characters" })).AllowAnonymous();
        app.MapCharacterEndpoints();
        app.MapCharacterQuestEndpoints();
        app.MapGameAdmissionEndpoints();
        app.MapCharacterAdminEndpoints();
        app.Run();
    }
}
