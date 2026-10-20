using System.Text.Json.Serialization;
using Arkya.Realms.Api.Application;
using Arkya.Realms.Api.Contracts;
using Arkya.Realms.Api.Endpoints;
using Arkya.Realms.Api.Infrastructure;
using Arkya.ServiceDefaults;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace Arkya.Realms.Api;

public partial class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 8192);
        builder.Services.ConfigureHttpJsonOptions(options =>
        {
            options.SerializerOptions.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow;
            options.SerializerOptions.MaxDepth = 8;
            options.SerializerOptions.RespectRequiredConstructorParameters = true;
        });
        builder.Services.AddArkyaProxyTrust(builder.Configuration);
        builder.Services.AddArkyaInternalHttp(builder.Configuration);
        builder.Services.AddOptions<MongoOptions>().Bind(builder.Configuration.GetSection("Mongo"))
            .Validate(value => !string.IsNullOrWhiteSpace(value.ConnectionString) && !string.IsNullOrWhiteSpace(value.DatabaseName),
                "Mongo connection string and database name are required.").ValidateOnStart();
        builder.Services.AddOptions<RealmsAdminOptions>().Bind(builder.Configuration.GetSection("RealmsAdmin"))
            .Validate(value => value.Key is { Length: >= 32 and <= 512 }, "RealmsAdmin:Key must contain 32 to 512 characters.").ValidateOnStart();
        builder.Services.AddOptions<DeploymentOptions>().Bind(builder.Configuration.GetSection("Deployment"))
            .Validate(value => value.IsValid(), "Deployment configuration requires unique allowlisted namespaces, valid profiles and digest-pinned images.").ValidateOnStart();
        builder.Services.AddOptions<RealmRuntimeOptions>().BindConfiguration("RealmRuntime")
            .Validate(value => value.IsValid(), "Realm runtime requires a dedicated key seed and an explicitly allowed Characters endpoint.").ValidateOnStart();
        builder.Services.AddSingleton<IMongoClient>(services =>
        {
            var settings = MongoClientSettings.FromConnectionString(services.GetRequiredService<IOptions<MongoOptions>>().Value.ConnectionString);
            settings.ServerSelectionTimeout = TimeSpan.FromSeconds(5);
            settings.ConnectTimeout = TimeSpan.FromSeconds(5);
            settings.SocketTimeout = TimeSpan.FromSeconds(10);
            return new MongoClient(settings);
        });
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<RealmsAdminKeyValidator>();
        builder.Services.AddSingleton<IRealmRepository, MongoRealmRepository>();
        builder.Services.AddSingleton<RealmService>();
        builder.Services.AddOptions<GameEntryOptions>().BindConfiguration("GameAccess")
            .Validate(value => value.IsValid(builder.Environment.IsDevelopment()), "Game entry configuration is invalid.").ValidateOnStart();
        builder.Services.AddSingleton<GameEntryService>();
        builder.Services.AddHostedService<RealmStoreInitializer>();
        builder.Services.AddHttpClient<IRealmDeployer, KubernetesRealmDeployer>(client => client.Timeout = TimeSpan.FromSeconds(10))
            .ConfigurePrimaryHttpMessageHandler(services =>
            {
                var options = services.GetRequiredService<IOptions<DeploymentOptions>>().Value;
                return options.Enabled ? KubernetesTransport.CreateHandler(options) : new SocketsHttpHandler { AllowAutoRedirect = false };
            });
        builder.Services.AddSingleton<RealmReconciler>();
        builder.Services.AddHostedService<RealmDeploymentWorker>();
        var app = builder.Build();
        app.UseArkyaInternalHttp();
        app.UseArkyaProxyTrust();
        app.Use(async (context, next) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            context.Response.Headers["X-Content-Type-Options"] = "nosniff";
            if (!app.Environment.IsDevelopment() && !context.Request.IsHttps && !context.IsArkyaInternalHttpAllowed())
            {
                context.Response.StatusCode = 400;
                return;
            }
            if (context.Request.ContentLength > 8192)
            {
                context.Response.StatusCode = 413;
                return;
            }
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
            timeout.CancelAfter(TimeSpan.FromSeconds(15));
            context.RequestAborted = timeout.Token;
            try { await next(context); }
            catch (BadHttpRequestException)
            {
                context.Response.StatusCode = 400;
                await context.Response.WriteAsJsonAsync(new ErrorResponse("invalid_request", "The request is invalid."));
            }
            catch (Exception exception) when (exception is MongoException or TimeoutException or HttpRequestException or OperationCanceledException)
            {
                context.Response.StatusCode = 503;
                if (!timeout.IsCancellationRequested)
                    await context.Response.WriteAsJsonAsync(new ErrorResponse("dependency_unavailable", "The realm store is unavailable."));
            }
            catch (Exception)
            {
                app.Logger.LogError("Realm request failed with an unexpected internal error.");
                context.Response.StatusCode = 500;
                await context.Response.WriteAsJsonAsync(new ErrorResponse("internal_error", "The realm request could not be completed."));
            }
        });
        app.MapGet("/health/live", () => Results.Ok(new { status = "ok", service = "realms" }));
        app.MapGet("/health/ready", async (IRealmRepository repository, CancellationToken cancellationToken) =>
            await repository.IsAvailableAsync(cancellationToken) ? Results.Ok(new { status = "ready" }) : Results.StatusCode(503));
        app.MapRealmEndpoints();
        app.MapGet("/internal/realms/entries", async (HttpContext context, GameEntryService entries, CancellationToken cancellationToken) =>
        {
            var key = context.Request.Headers["X-Arkya-Service-Key"];
            return key.Count == 1 && entries.Authorize(key[0])
                ? Results.Ok(await entries.ListAsync(cancellationToken)) : Results.StatusCode(403);
        });
        app.Run();
    }
}
