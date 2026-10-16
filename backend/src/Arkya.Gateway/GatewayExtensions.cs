using Microsoft.Extensions.Options;
using Yarp.ReverseProxy.Configuration;
using Yarp.ReverseProxy.Transforms;

namespace Arkya.Gateway;

public sealed class GatewayManagementOptions
{
    public int Port { get; set; }
}

public static class GatewayExtensions
{
    public static IServiceCollection AddGateway(this IServiceCollection services)
    {
        services.AddOptions<GatewayManagementOptions>().BindConfiguration("Management")
            .Validate(options => options.Port == 0 || options.Port is >= 1024 and <= 65535,
                "Management port must be zero (disabled) or 1024-65535.").ValidateOnStart();

        services.AddSingleton<IProxyConfigProvider>(provider =>
        {
            var finalConfiguration = provider.GetRequiredService<IConfiguration>();
            var routes = GatewayRoutes.Create(finalConfiguration.GetValue<bool>("Gateway:CharactersEnabled"));
            var configured = finalConfiguration.GetSection("ReverseProxy:Clusters").Get<Dictionary<string, ClusterConfig>>()
                ?? throw new InvalidOperationException("ReverseProxy:Clusters must be configured.");
            var clusters = routes.Select(route => route.ClusterId!).Distinct(StringComparer.Ordinal)
                .Select(id => configured.TryGetValue(id, out var cluster)
                    ? cluster with { ClusterId = id }
                    : throw new InvalidOperationException($"ReverseProxy cluster '{id}' must be configured."))
                .ToArray();
            return new InMemoryConfigProvider(routes, clusters);
        });
        services.AddReverseProxy().AddTransforms(context =>
        {
            context.AddOriginalHost(true);
            context.UseDefaultForwarders = false;
            context.AddRequestTransform(transform =>
            {
                // Only the trusted, normalized incoming connection may identify the client.
                foreach (var header in transform.ProxyRequest.Headers.Select(header => header.Key).ToArray())
                {
                    if (header.Equals("Forwarded", StringComparison.OrdinalIgnoreCase)
                        || header.StartsWith("X-Forwarded-", StringComparison.OrdinalIgnoreCase)
                        || header.StartsWith("X-Original-", StringComparison.OrdinalIgnoreCase)
                        || header.Equals("X-Arkya-Service-Key", StringComparison.OrdinalIgnoreCase))
                        transform.ProxyRequest.Headers.Remove(header);
                }
                var request = transform.HttpContext.Request;
                transform.ProxyRequest.Headers.TryAddWithoutValidation("X-Forwarded-Proto", request.Scheme);
                if (transform.HttpContext.Connection.RemoteIpAddress is { } clientIp)
                    transform.ProxyRequest.Headers.TryAddWithoutValidation("X-Forwarded-For", clientIp.ToString());
                return ValueTask.CompletedTask;
            });
        });
        return services;
    }

    public static WebApplication UseGatewayManagement(this WebApplication app)
    {
        var port = app.Services.GetRequiredService<IOptions<GatewayManagementOptions>>().Value.Port;
        if (port == 0) return app;

        // Use the real listener port, never the client-controlled Host header.
        app.MapWhen(context => context.Connection.LocalPort == port, branch => branch.Run(async context =>
        {
            context.Response.Headers.CacheControl = "no-store";
            if (!HttpMethods.IsGet(context.Request.Method)
                || (context.Request.Path != "/health/live" && context.Request.Path != "/health/ready"))
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }
            // Readiness describes this router; a failed service must not remove healthy routes.
            await context.Response.WriteAsJsonAsync(new
            {
                status = context.Request.Path == "/health/live" ? "live" : "ready"
            }, context.RequestAborted);
        }));
        return app;
    }
}
