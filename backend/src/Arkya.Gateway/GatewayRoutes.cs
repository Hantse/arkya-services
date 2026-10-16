using Yarp.ReverseProxy.Configuration;

namespace Arkya.Gateway;

public static class GatewayRoutes
{
    public static IReadOnlyList<RouteConfig> Create(bool charactersEnabled)
    {
        var routes = new List<RouteConfig>
        {
            Route("identity-challenges", "identity", "/v1/auth/discord/challenges", "POST", "OPTIONS"),
            Route("identity-authorize", "identity", "/v1/auth/discord/authorize/{id}", "GET"),
            Route("identity-callback", "identity", "/v1/auth/discord/callback", "GET"),
            Route("identity-exchange", "identity", "/v1/auth/discord/exchange", "POST", "OPTIONS"),
            Route("identity-me", "identity", "/v1/auth/me", "GET", "OPTIONS"),
            Route("identity-logout", "identity", "/v1/auth/logout", "POST", "OPTIONS")
        };
        if (charactersEnabled)
        {
            routes.Add(Route("characters-list-create", "characters", "/v1/characters", "GET", "POST", "OPTIONS"));
            routes.Add(Route("characters-get", "characters", "/v1/characters/{id:guid}", "GET", "OPTIONS"));
            routes.Add(Route("characters-appearance", "characters", "/v1/characters/appearance-options", "GET", "OPTIONS"));
        }
        return routes;
    }

    private static RouteConfig Route(string id, string cluster, string path, params string[] methods) => new()
    {
        RouteId = id,
        ClusterId = cluster,
        Match = new RouteMatch { Path = path, Methods = methods }
    };
}
