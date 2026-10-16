using Arkya.ServiceDefaults;
using Microsoft.Extensions.Options;

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
        app.Use(async (context, next) =>
        {
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
        var gateway = app.Services.GetRequiredService<IOptions<GatewayOptions>>().Value;
        if (gateway.AdminEnabled)
            health.RequireHost(GatewayOptions.AllowedHosts(app.Configuration)
                .Where(host => !host.Equals(gateway.AdminHost, StringComparison.OrdinalIgnoreCase)).ToArray());
        app.MapReverseProxy();
        app.Run();
    }
}
