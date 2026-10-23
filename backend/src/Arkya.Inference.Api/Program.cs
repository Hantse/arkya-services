using System.Text.Json.Serialization;
using Arkya.Inference.Api.Application;
using Arkya.Inference.Api.Endpoints;
using Arkya.Inference.Api.Infrastructure;
using Arkya.ServiceDefaults;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace Arkya.Inference.Api;

public partial class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 16384);
        builder.Services.ConfigureHttpJsonOptions(options =>
        {
            options.SerializerOptions.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow;
            options.SerializerOptions.MaxDepth = 8;
            options.SerializerOptions.RespectRequiredConstructorParameters = true;
        });
        builder.Services.AddArkyaProxyTrust(builder.Configuration);
        builder.Services.AddArkyaInternalHttp(builder.Configuration);
        builder.Services.AddOptions<MongoOptions>().BindConfiguration("Mongo")
            .Validate(value => !string.IsNullOrWhiteSpace(value.ConnectionString) && !string.IsNullOrWhiteSpace(value.DatabaseName),
                "Mongo connection string and database name are required.").ValidateOnStart();
        builder.Services.AddOptions<InferenceAdminOptions>().BindConfiguration("InferenceAdmin")
            .Validate(value => value.Key is { Length: >= 32 and <= 512 },
                "InferenceAdmin:Key must contain 32 to 512 characters.").ValidateOnStart();
        builder.Services.AddOptions<InferenceOptions>().BindConfiguration("Inference")
            .Validate(value => value.IsValid(), "Inference limits or heartbeat configuration are invalid.").ValidateOnStart();
        builder.Services.AddSingleton<IMongoClient>(services =>
        {
            var settings = MongoClientSettings.FromConnectionString(services.GetRequiredService<IOptions<MongoOptions>>().Value.ConnectionString);
            settings.ServerSelectionTimeout = TimeSpan.FromSeconds(5);
            settings.ConnectTimeout = TimeSpan.FromSeconds(5);
            settings.SocketTimeout = TimeSpan.FromSeconds(10);
            return new MongoClient(settings);
        });
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<InferenceAdminKeyValidator>();
        builder.Services.AddSingleton<IInferenceRepository, MongoInferenceRepository>();
        builder.Services.AddSingleton<IEnrollmentGrantRepository, MongoEnrollmentGrantRepository>();
        builder.Services.AddSingleton<InferenceService>();
        builder.Services.AddSingleton<EnrollmentService>();
        builder.Services.AddHostedService<InferenceStoreInitializer>();
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
            if (context.Request.ContentLength > 16384)
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
            catch (Exception exception) when (exception is MongoException or TimeoutException or OperationCanceledException)
            {
                context.Response.StatusCode = 503;
                if (!timeout.IsCancellationRequested)
                    await context.Response.WriteAsJsonAsync(new ErrorResponse("dependency_unavailable", "The inference store is unavailable."));
            }
            catch (Exception)
            {
                app.Logger.LogError("Inference request failed with an unexpected internal error.");
                context.Response.StatusCode = 500;
                await context.Response.WriteAsJsonAsync(new ErrorResponse("internal_error", "The inference request could not be completed."));
            }
        });
        app.MapGet("/health/live", () => Results.Ok(new { status = "ok", service = "inference" }));
        app.MapGet("/health/ready", async (IInferenceRepository repository, CancellationToken cancellationToken) =>
            await repository.IsAvailableAsync(cancellationToken) ? Results.Ok(new { status = "ready" }) : Results.StatusCode(503));
        app.MapInferenceEndpoints();
        app.Run();
    }
}
