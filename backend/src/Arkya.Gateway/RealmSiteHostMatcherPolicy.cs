using Microsoft.AspNetCore.Routing.Matching;
using Microsoft.Extensions.Options;
using Yarp.ReverseProxy.Model;

namespace Arkya.Gateway;

public sealed class RealmSiteHostMatcherPolicy(IOptions<GatewayOptions> configured) : MatcherPolicy, IEndpointSelectorPolicy
{
    public override int Order => -90;

    public bool AppliesToEndpoints(IReadOnlyList<Endpoint> endpoints) => endpoints.Any(endpoint =>
        endpoint.Metadata.GetMetadata<RouteModel>()?.Config.RouteId is "realm-admin" or "realm-frontend");

    public Task ApplyAsync(HttpContext context, CandidateSet candidates)
    {
        var options = configured.Value;
        for (var index = 0; index < candidates.Count; index++)
        {
            if (!candidates.IsValidCandidate(index)) continue;
            var route = candidates[index].Endpoint.Metadata.GetMetadata<RouteModel>()?.Config.RouteId;
            // Nested suffix wildcards share routing edges; recheck the realm boundary before selecting a route.
            if (route == "realm-admin" && options.RealmIdForHost(context.Request.Host.Host, admin: true) is null
                || route == "realm-frontend" && options.RealmIdForHost(context.Request.Host.Host) is null)
                candidates.SetValidity(index, false);
        }
        return Task.CompletedTask;
    }
}
