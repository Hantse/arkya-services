using Arkya.ServiceDefaults;
using Microsoft.Extensions.Options;
using Yarp.ReverseProxy.Model;

namespace Arkya.Gateway;

public partial class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        // OAuth callback queries and session headers must never become request logs.
        builder.Logging.AddFilter("Microsoft.AspNetCore.Hosting.Diagnostics", LogLevel.Warning);
        builder.Logging.AddFilter("Yarp.ReverseProxy", LogLevel.Warning);
        builder.Services.AddArkyaProxyTrust(builder.Configuration);
        builder.Services.AddGateway();

        var app = builder.Build();
        app.UseArkyaProxyTrust();
        app.UseGatewayManagement();
        app.UseRouting();
        var gateway = app.Services.GetRequiredService<IOptions<GatewayOptions>>().Value;
        app.Use(async (context, next) =>
        {
            // Traefik reports secure upgrades as wss; proxy trust has already been applied.
            if (context.Request.Scheme.Equals("wss", StringComparison.OrdinalIgnoreCase))
                context.Request.Scheme = "https";
            if (gateway.RealmSitesEnabled && !gateway.IsIncomingHostAllowed(context.Request.Host.Host, app.Configuration))
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }
            var route = context.GetEndpoint()?.Metadata.GetMetadata<RouteModel>()?.Config.RouteId;
            if (route == "realm-frontend" && gateway.RealmIdForHost(context.Request.Host.Host) is null
                || route == "realm-admin" && gateway.RealmIdForHost(context.Request.Host.Host, admin: true) is null)
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }
            if (gateway.PublicRealmsEnabled && gateway.RealmIdForHost(context.Request.Host.Host) is { } realmId
                && context.Request.Path.StartsWithSegments("/v1/public/realms", out var remaining)
                && remaining.HasValue && remaining.Value != "/"
                && !remaining.Value.TrimEnd('/').Equals($"/{realmId}", StringComparison.Ordinal))
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }
            // The frontend owns caching for its static assets and deployment configuration.
            if (!gateway.IsFrontendHost(context.Request.Host.Host)
                || context.Request.Path.StartsWithSegments("/v1/public/realms"))
                context.Response.Headers.CacheControl = "no-store";
            context.Response.Headers["Referrer-Policy"] = "no-referrer";
            context.Response.Headers["X-Content-Type-Options"] = "nosniff";
            if (!app.Environment.IsDevelopment() && !context.Request.IsHttps)
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }
            await next(context);
        });
        var health = app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "arkya-gateway" }));
        if (gateway.AdminEnabled || gateway.FrontendEnabled)
            health.RequireHost(gateway.PublicApiHosts(app.Configuration));
        app.MapReverseProxy();
        app.Run();
    }
}
