using Arkya.ServiceDefaults;

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
        app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "arkya-gateway" }));
        app.MapReverseProxy();
        app.Run();
    }
}
