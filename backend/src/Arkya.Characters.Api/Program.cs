using System.Text.Json.Serialization;
using Arkya.Characters.Api.Application;
using Arkya.Characters.Api.Endpoints;
using Arkya.Characters.Api.Infrastructure.Identity;
using Arkya.Characters.Api.Infrastructure.Persistence;
using Arkya.ServiceDefaults;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Arkya.Characters.Api;

public partial class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.Services.AddArkyaServiceDefaults(builder.Configuration);
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
            .Validate(value => value.IsValid(), "Dedicated identities require unique keys and realm/server bindings.").ValidateOnStart();
        builder.Services.AddSingleton<DedicatedKeyValidator>();
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
                "Identity:BaseUrl must use HTTPS, or loopback HTTP in Development.")
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
        app.UseArkyaProxyTrust();
        app.UseArkyaServiceDefaults();
        app.UseRouting();
        app.UseCors();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "characters" })).AllowAnonymous();
        app.MapCharacterEndpoints();
        app.MapCharacterQuestEndpoints();
        app.MapGameAdmissionEndpoints();
        app.Run();
    }
}
