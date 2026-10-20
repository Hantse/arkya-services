using Yarp.ReverseProxy.Configuration;

namespace Arkya.Gateway;

public static class GatewayRoutes
{
    public static IReadOnlyList<RouteConfig> Create(bool charactersEnabled, string? adminHost = null,
        IReadOnlyList<string>? publicHosts = null, string? frontendHost = null, bool realmGameEnabled = false,
        bool inferenceEnabled = false)
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
            routes.Add(Route("game-realms", "characters", "/v1/realms", "GET", "OPTIONS"));
            routes.Add(Route("character-admission", "characters", "/v1/characters/{id:guid}/admissions", "POST", "OPTIONS"));
        }
        if (inferenceEnabled)
        {
            routes.Add(Route("inference-agent-heartbeat", "inference", "/agent/inference/heartbeat", "POST"));
            routes.Add(Route("inference-agent-enroll", "inference", "/agent/inference/enroll", "POST"));
        }
        if (adminHost is not null || frontendHost is not null)
            routes = routes.Select(route => route with { Match = route.Match with { Hosts = publicHosts } }).ToList();
        if (realmGameEnabled)
        {
            routes.Add(new RouteConfig
            {
                RouteId = "realm-game-beta-web",
                ClusterId = "realm-game",
                Match = new RouteMatch { Hosts = ["api.arkya.online"], Path = "/game/beta-web/{**path}", Methods = ["GET"] },
                Transforms = [new Dictionary<string, string> { ["PathRemovePrefix"] = "/game/beta-web" }]
            });
        }
        if (adminHost is not null)
        {
            routes.Add(new RouteConfig
            {
                RouteId = "admin",
                ClusterId = "admin",
                Match = new RouteMatch { Hosts = [adminHost], Path = "/{**path}" }
            });
        }
        if (frontendHost is not null)
        {
            routes.Add(new RouteConfig
            {
                RouteId = "frontend",
                ClusterId = "frontend",
                Match = new RouteMatch { Hosts = [frontendHost], Path = "/{**path}" }
            });
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
