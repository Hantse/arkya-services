using Arkya.Identity.Api.Application;
using Arkya.ServiceDefaults;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Arkya.Identity.Api.Infrastructure;

public static class RuntimeExtensions
{
    public static IServiceCollection AddIdentityRuntime(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddArkyaInternalHttp(configuration);
        services.AddArkyaProxyTrust(configuration, "ReverseProxy");
        services.AddOptions<ManagementOptions>().BindConfiguration("Management")
            .Validate(o => o.IsValid(), "Management port must be zero (disabled) or 1024-65535, with a readiness timeout between 100 milliseconds and ten seconds.")
            .ValidateOnStart();
        services.AddHealthChecks().AddCheck<MongoReadinessCheck>("mongodb", tags: ["ready"]);
        services.AddOptions<KeyPersistenceOptions>().BindConfiguration("DataProtection")
            .Validate(o => o.IsValid(), "Data Protection KeysPath must be empty or an absolute filesystem path.").ValidateOnStart();
        services.AddOptions<KeyManagementOptions>().Configure<IOptions<KeyPersistenceOptions>, ILoggerFactory>((keys, configured, logs) =>
        {
            if (configured.Value.KeysPath.Length > 0)
                keys.XmlRepository = new FileSystemXmlRepository(new DirectoryInfo(configured.Value.KeysPath), logs);
        });
        return services;
    }

    public static WebApplication UseIdentityRuntime(this WebApplication app)
    {
        var administration = app.Services.GetRequiredService<IOptions<AdministrationOptions>>().Value;
        app.UseArkyaInternalHttp(context => context.Request.Path.StartsWithSegments("/internal/sessions") ||
            (administration.Enabled && administration.AllowClusterHttp && context.Request.Path.StartsWithSegments("/admin/accounts")));
        app.UseArkyaProxyTrust();

        var management = app.Services.GetRequiredService<IOptions<ManagementOptions>>().Value;
        if (management.Port != 0)
        {
            // The transport's local port cannot be forged through a Host or forwarded header.
            app.MapWhen(context => context.Connection.LocalPort == management.Port, branch => branch.Run(async context =>
            {
                context.Response.Headers.CacheControl = "no-store";
                if (!HttpMethods.IsGet(context.Request.Method))
                {
                    context.Response.StatusCode = StatusCodes.Status404NotFound;
                    return;
                }
                if (context.Request.Path == "/health/live")
                {
                    await context.Response.WriteAsJsonAsync(new { status = "live" }, context.RequestAborted);
                    return;
                }
                if (context.Request.Path == "/health/ready")
                {
                    var checks = context.RequestServices.GetRequiredService<HealthCheckService>();
                    var result = await checks.CheckHealthAsync(check => check.Tags.Contains("ready"), context.RequestAborted);
                    var ready = result.Status == HealthStatus.Healthy;
                    context.Response.StatusCode = ready ? StatusCodes.Status200OK : StatusCodes.Status503ServiceUnavailable;
                    await context.Response.WriteAsJsonAsync(new { status = ready ? "ready" : "not_ready" }, context.RequestAborted);
                    return;
                }
                context.Response.StatusCode = StatusCodes.Status404NotFound;
            }));
        }
        return app;
    }
}
