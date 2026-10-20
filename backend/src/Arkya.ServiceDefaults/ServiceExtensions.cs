using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace Arkya.ServiceDefaults;

public static class ServiceExtensions
{
    public static IServiceCollection AddArkyaServiceDefaults(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<MongoOptions>().Bind(configuration.GetSection("Mongo"))
            .Validate(o => !string.IsNullOrWhiteSpace(o.ConnectionString) && !string.IsNullOrWhiteSpace(o.DatabaseName),
                "Mongo connection string and database name are required.").ValidateOnStart();
        services.AddOptions<ServiceAuthenticationOptions>().Bind(configuration.GetSection("ServiceAuthentication"))
            .Validate(o => o.Key.Length is >= 32 and <= 512, "A service key of 32 to 512 characters is required.").ValidateOnStart();
        services.AddSingleton<IMongoClient>(sp =>
        {
            var settings = MongoClientSettings.FromConnectionString(sp.GetRequiredService<IOptions<MongoOptions>>().Value.ConnectionString);
            settings.ServerSelectionTimeout = TimeSpan.FromSeconds(5);
            return new MongoClient(settings);
        });
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<ServiceKeyValidator>();
        services.AddProblemDetails();
        services.AddExceptionHandler<DependencyExceptionHandler>();
        return services;
    }

    public static WebApplication UseArkyaServiceDefaults(this WebApplication app)
    {
        app.UseExceptionHandler();
        app.Use(async (context, next) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            context.Response.Headers["Referrer-Policy"] = "no-referrer";
            context.Response.Headers["X-Content-Type-Options"] = "nosniff";
            if (!app.Environment.IsDevelopment() && !context.Request.IsHttps && !context.IsArkyaInternalHttpAllowed())
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }
            await next(context);
        });
        return app;
    }
}

public sealed class DependencyExceptionHandler : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is BadHttpRequestException badRequest)
        {
            context.Response.StatusCode = badRequest.StatusCode;
            await context.Response.WriteAsJsonAsync(new { code = "invalid_request", message = "The request is invalid." }, cancellationToken);
            return true;
        }
        if (exception is not (MongoException or TimeoutException or HttpRequestException or TaskCanceledException))
        {
            return false;
        }
        if (context.RequestAborted.IsCancellationRequested)
        {
            return false;
        }
        context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        await context.Response.WriteAsJsonAsync(new { code = "dependency_unavailable", message = "A required service is unavailable." }, cancellationToken);
        return true;
    }
}
