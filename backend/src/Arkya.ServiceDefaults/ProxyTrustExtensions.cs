using System.Net;
using System.Net.Sockets;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Arkya.ServiceDefaults;

public sealed class ProxyTrustOptions
{
    public string[] KnownProxies { get; set; } = [];
    public string[] KnownNetworks { get; set; } = [];
    public int ForwardLimit { get; set; } = 1;
    public bool Enabled => KnownProxies.Length > 0 || KnownNetworks.Length > 0;

    public bool IsValid() => KnownProxies is not null && KnownNetworks is not null &&
        ForwardLimit is >= 1 and <= 4 && KnownProxies.All(IsValidProxy) && KnownNetworks.All(IsValidNetwork);

    private static bool IsValidProxy(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value != value.Trim() ||
            !(value.Contains('.') || value.Contains(':')) || !IPAddress.TryParse(value, out var address)) return false;
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        return !address.Equals(IPAddress.Any) && !address.Equals(IPAddress.IPv6Any) &&
            !address.Equals(IPAddress.Broadcast) && !address.IsIPv6Multicast &&
            (address.AddressFamily != AddressFamily.InterNetwork || address.GetAddressBytes()[0] is < 224 or > 239);
    }

    private static bool IsValidNetwork(string value) => !string.IsNullOrWhiteSpace(value) && value == value.Trim() &&
        System.Net.IPNetwork.TryParse(value, out var network) && network.PrefixLength > 0 &&
        !(network.BaseAddress.AddressFamily == AddressFamily.InterNetworkV6 && network.PrefixLength <= 96 &&
          network.Contains(IPAddress.Parse("::ffff:0.0.0.0")));
}

public static class ProxyTrustExtensions
{
    public static IServiceCollection AddArkyaProxyTrust(this IServiceCollection services,
        IConfiguration configuration, string sectionName = "ProxyTrust")
    {
        services.AddOptions<ProxyTrustOptions>().Bind(configuration.GetSection(sectionName))
            .Validate(o => o.IsValid(), "Proxy trust requires explicit IP addresses or CIDRs, never /0; ForwardLimit must be between one and four.")
            .ValidateOnStart();
        services.AddOptions<ForwardedHeadersOptions>().Configure<IOptions<ProxyTrustOptions>>((forwarded, configured) =>
        {
            var proxy = configured.Value;
            forwarded.KnownProxies.Clear();
            forwarded.KnownIPNetworks.Clear();
            foreach (var address in proxy.KnownProxies) forwarded.KnownProxies.Add(IPAddress.Parse(address));
            foreach (var network in proxy.KnownNetworks) forwarded.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(network));
            forwarded.ForwardLimit = proxy.ForwardLimit;
            forwarded.ForwardedHeaders = proxy.Enabled
                ? ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto : ForwardedHeaders.None;
        });
        return services;
    }

    public static WebApplication UseArkyaProxyTrust(this WebApplication app)
    {
        if (app.Services.GetRequiredService<IOptions<ProxyTrustOptions>>().Value.Enabled)
            app.UseForwardedHeaders();
        return app;
    }
}
