using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Arkya.ServiceDefaults;

public sealed class InternalHttpOptions
{
    private static readonly IPNetwork[] PrivateNetworks =
    [
        IPNetwork.Parse("10.0.0.0/8"),
        IPNetwork.Parse("172.16.0.0/12"),
        IPNetwork.Parse("192.168.0.0/16"),
        IPNetwork.Parse("fc00::/7")
    ];

    public string[] AllowedNetworks { get; set; } = [];

    public bool IsValid() => AllowedNetworks is not null && AllowedNetworks.All(value =>
        value is not null && value == value.Trim() && IPNetwork.TryParse(value, out var network) &&
        !network.BaseAddress.IsIPv4MappedToIPv6 &&
        PrivateNetworks.Any(privateNetwork => network.BaseAddress.AddressFamily == privateNetwork.BaseAddress.AddressFamily &&
            network.PrefixLength >= privateNetwork.PrefixLength &&
            privateNetwork.Contains(network.BaseAddress)));

    public bool Allows(IPAddress? address)
    {
        if (address is null) return false;
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        return AllowedNetworks.Any(value => IPNetwork.Parse(value).Contains(address));
    }
}

public static class InternalHttpExtensions
{
    private sealed record InternalHttpConnection(bool Allowed);

    public static IServiceCollection AddArkyaInternalHttp(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<InternalHttpOptions>().Bind(configuration.GetSection("InternalHttp"))
            .Validate(options => options.IsValid(), "InternalHttp:AllowedNetworks must contain explicit private IPv4 or IPv6 CIDRs.")
            .ValidateOnStart();
        return services;
    }

    public static WebApplication UseArkyaInternalHttp(this WebApplication app, Func<HttpContext, bool>? allowedPath = null)
    {
        var options = app.Services.GetRequiredService<IOptions<InternalHttpOptions>>().Value;
        // Capture the transport peer before forwarded headers can replace RemoteIpAddress.
        app.Use((context, next) =>
        {
            context.Features.Set(new InternalHttpConnection(options.Allows(context.Connection.RemoteIpAddress) &&
                (allowedPath is null || allowedPath(context))));
            return next(context);
        });
        return app;
    }

    public static bool IsArkyaInternalHttpAllowed(this HttpContext context) =>
        context.Features.Get<InternalHttpConnection>()?.Allowed == true;
}
