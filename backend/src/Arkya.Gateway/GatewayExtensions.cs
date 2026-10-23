using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Routing.Matching;
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
        services.AddOptions<GatewayOptions>().BindConfiguration("Gateway")
            .Validate(options => !options.RealmGameEnabled || GatewayOptions.IsRealmGameOriginValid(options.RealmGameAddress),
                "Gateway:RealmGameAddress must be an HTTP Kubernetes service origin without credentials, path prefix, query or fragment.")
            .Validate(options => !options.AdminEnabled || GatewayOptions.IsWebHostValid(options.AdminHost),
                "Gateway:AdminHost must be an exact DNS hostname distinct from api.arkya.online.")
            .Validate(options => !options.AdminEnabled || GatewayOptions.IsOriginValid(options.AdminAddress),
                "Gateway:AdminAddress must be an HTTP(S) origin without credentials, path prefix, query or fragment.")
            .Validate(options => !options.FrontendEnabled || GatewayOptions.IsWebHostValid(options.FrontendHost),
                "Gateway:FrontendHost must be an exact DNS hostname distinct from api.arkya.online.")
            .Validate(options => !options.FrontendEnabled || GatewayOptions.IsOriginValid(options.FrontendAddress),
                "Gateway:FrontendAddress must be an HTTP(S) origin without credentials, path prefix, query or fragment.")
            .Validate(options => !options.PublicRealmsEnabled || GatewayOptions.IsOriginValid(options.PublicRealmsAddress),
                "Gateway:PublicRealmsAddress must be an HTTP(S) origin without credentials, path prefix, query or fragment.")
            .Validate(options => !options.RealmSitesEnabled || options.FrontendEnabled && options.AdminEnabled && options.PublicRealmsEnabled
                && GatewayOptions.IsWebHostValid(options.RealmBaseDomain)
                && options.FrontendHost.Equals(options.RealmBaseDomain, StringComparison.OrdinalIgnoreCase)
                && options.AdminHost.Equals($"admin.{options.RealmBaseDomain}", StringComparison.OrdinalIgnoreCase),
                "Realm sites require the public realm catalogue, frontend at RealmBaseDomain and admin at admin.RealmBaseDomain.")
            .Validate(options => !options.AdminEnabled || !options.FrontendEnabled
                || !string.Equals(options.AdminHost, options.FrontendHost, StringComparison.OrdinalIgnoreCase),
                "Gateway:FrontendHost and Gateway:AdminHost must be distinct when both routes are enabled.")
            .Validate<IConfiguration>((options, configuration) => (!options.AdminEnabled && !options.FrontendEnabled)
                || options.HasExplicitAllowedHosts(configuration),
                "AllowedHosts must explicitly include api.arkya.online and each enabled frontend/admin host; only enabled realm host patterns may be wildcards.")
            .ValidateOnStart();
        services.AddOptions<GatewayManagementOptions>().BindConfiguration("Management")
            .Validate(options => options.Port == 0 || options.Port is >= 1024 and <= 65535,
                "Management port must be zero (disabled) or 1024-65535.").ValidateOnStart();
        services.AddSingleton<MatcherPolicy, RealmSiteHostMatcherPolicy>();

        services.AddSingleton<IProxyConfigProvider>(provider =>
        {
            var finalConfiguration = provider.GetRequiredService<IConfiguration>();
            var options = provider.GetRequiredService<IOptions<GatewayOptions>>().Value;
            var publicHosts = options.AdminEnabled || options.FrontendEnabled
                ? options.PublicApiHosts(finalConfiguration) : null;
            var routes = GatewayRoutes.Create(options.CharactersEnabled, options.AdminEnabled ? options.AdminHost : null,
                publicHosts, options.FrontendEnabled ? options.FrontendHost : null, options.ReportsEnabled,
                options.InferenceEnabled, options.RealmGameEnabled, options.ContentEnabled,
                options.PublicRealmsEnabled, options.RealmSitesEnabled ? options.RealmBaseDomain : null);
            var configured = finalConfiguration.GetSection("ReverseProxy:Clusters").Get<Dictionary<string, ClusterConfig>>()
                ?? throw new InvalidOperationException("ReverseProxy:Clusters must be configured.");
            if (options.AdminEnabled)
                configured["admin"] = OriginCluster("admin", options.AdminAddress);
            if (options.FrontendEnabled)
                configured["frontend"] = OriginCluster("frontend", options.FrontendAddress);
            if (options.PublicRealmsEnabled)
                configured["public-realms"] = OriginCluster("public-realms", options.PublicRealmsAddress);
            if (options.RealmGameEnabled)
                configured["realm-game"] = OriginCluster("realm-game", options.RealmGameAddress) with
                {
                    HttpRequest = new() { ActivityTimeout = TimeSpan.FromMinutes(2) }
                };
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
            var anonymousRealms = context.Route.ClusterId == "public-realms";
            context.AddRequestTransform(transform =>
            {
                // Only the trusted, normalized incoming connection may identify the client.
                foreach (var header in transform.ProxyRequest.Headers.Select(header => header.Key).ToArray())
                {
                    if (header.Equals("Forwarded", StringComparison.OrdinalIgnoreCase)
                        || header.StartsWith("X-Forwarded-", StringComparison.OrdinalIgnoreCase)
                        || header.StartsWith("X-Original-", StringComparison.OrdinalIgnoreCase)
                        || header.Equals("X-Arkya-Service-Key", StringComparison.OrdinalIgnoreCase)
                        || header.Equals("X-Arkya-Admin-Key", StringComparison.OrdinalIgnoreCase)
                        || header.Equals("X-Arkya-Dedicated-Key", StringComparison.OrdinalIgnoreCase)
                        || header.Equals("X-Arkya-Reports-Admin-Key", StringComparison.OrdinalIgnoreCase)
                        || header.Equals("X-Arkya-Admin-Actor", StringComparison.OrdinalIgnoreCase))
                        transform.ProxyRequest.Headers.Remove(header);
                }
                if (anonymousRealms)
                {
                    transform.ProxyRequest.Headers.Remove("Authorization");
                    transform.ProxyRequest.Headers.Remove("Cookie");
                    transform.ProxyRequest.Headers.Host = null;
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

    private static ClusterConfig OriginCluster(string id, string address) => new()
    {
        ClusterId = id,
        Destinations = new Dictionary<string, DestinationConfig>
        {
            ["primary"] = new() { Address = new Uri(address).AbsoluteUri }
        },
        HttpRequest = new() { ActivityTimeout = TimeSpan.FromSeconds(30) }
    };

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
