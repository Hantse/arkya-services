using Yarp.ReverseProxy.Configuration;

namespace Arkya.Gateway;

public static class GatewayRoutes
{
    public static IReadOnlyList<RouteConfig> Create(bool charactersEnabled, string? adminHost = null,
        IReadOnlyList<string>? publicHosts = null, string? frontendHost = null, bool realmGameEnabled = false,
        bool inferenceEnabled = false, bool reportsEnabled = false, bool contentEnabled = false,
        IReadOnlyList<RealmGamePartition>? realmGamePartitions = null,
        bool publicRealmsEnabled = false, string? realmBaseDomain = null)
    {
        var routes = new List<RouteConfig>
        {
            Route("identity-challenges", "identity", "/v1/auth/discord/challenges", "POST", "OPTIONS"),
            Route("identity-authorize", "identity", "/v1/auth/discord/authorize/{id}", "GET"),
            Route("identity-callback", "identity", "/v1/auth/discord/callback", "GET"),
            Route("identity-exchange", "identity", "/v1/auth/discord/exchange", "POST", "OPTIONS"),
            Route("identity-me", "identity", "/v1/auth/me", "GET", "OPTIONS"),
            Route("identity-refresh", "identity", "/v1/auth/refresh", "POST", "OPTIONS"),
            Route("identity-refresh-native", "identity", "/v1/auth/refresh/native", "POST"),
            Route("identity-logout-native", "identity", "/v1/auth/logout/native", "POST"),
            Route("identity-logout", "identity", "/v1/auth/logout", "POST", "OPTIONS"),
            Route("beta-registration", "identity", "/v1/beta/registration", "GET", "POST", "OPTIONS")
        };
        if (charactersEnabled)
        {
            routes.Add(Route("characters-list-create", "characters", "/v1/characters", "GET", "POST", "OPTIONS"));
            routes.Add(Route("characters-entry-context", "characters", "/v1/characters/entry-context", "GET", "OPTIONS"));
            routes.Add(Route("characters-get", "characters", "/v1/characters/{id:guid}", "GET", "OPTIONS"));
            routes.Add(Route("characters-appearance", "characters", "/v1/characters/appearance-options", "GET", "OPTIONS"));
            routes.Add(Route("game-realms", "characters", "/v1/realms", "GET", "OPTIONS"));
            routes.Add(Route("character-admission", "characters", "/v1/characters/{id:guid}/admissions", "POST", "OPTIONS"));
        }
        if (contentEnabled)
        {
            routes.Add(Route("content-posts", "content", "/v1/content/posts", "GET", "OPTIONS"));
            routes.Add(Route("content-post", "content", "/v1/content/posts/{id:guid}", "GET", "OPTIONS"));
        }
        if (reportsEnabled)
        {
            routes.Add(Route("reports-list-create", "reports", "/v1/bug-reports", "GET", "POST", "OPTIONS"));
            routes.Add(Route("reports-get", "reports", "/v1/bug-reports/{id:guid}", "GET", "OPTIONS"));
        }
        if (inferenceEnabled)
        {
            routes.Add(Route("inference-agent-heartbeat", "inference", "/agent/inference/heartbeat", "POST"));
            routes.Add(Route("inference-agent-enroll", "inference", "/agent/inference/enroll", "POST"));
            routes.Add(Route("inference-agent-dialogue-claim", "inference", "/agent/inference/dialogues/claim", "POST"));
            routes.Add(Route("inference-agent-dialogue-complete", "inference", "/agent/inference/dialogues/complete", "POST"));
        }
        if (adminHost is not null || frontendHost is not null)
            routes = routes.Select(route => route with { Match = route.Match with { Hosts = publicHosts } }).ToList();
        if (realmGameEnabled)
        {
            foreach (var partition in realmGamePartitions ?? [])
                routes.Add(new RouteConfig
                {
                    RouteId = partition.ClusterId,
                    ClusterId = partition.ClusterId,
                    Order = -10,
                    Match = new RouteMatch { Hosts = ["api.arkya.online"], Path = partition.PublicPath, Methods = ["GET"] },
                    Transforms = [new Dictionary<string, string> { ["PathSet"] = "/" }]
                });
            routes.Add(new RouteConfig
            {
                RouteId = "realm-game-beta-web",
                ClusterId = "realm-game",
                Match = new RouteMatch { Hosts = ["api.arkya.online"], Path = "/game/beta-web/{**path}", Methods = ["GET"] },
                Transforms = [new Dictionary<string, string> { ["PathRemovePrefix"] = "/game/beta-web" }]
            });
        }
        if (publicRealmsEnabled)
        {
            var hosts = new List<string> { "api.arkya.online" };
            if (frontendHost is not null) hosts.Add(frontendHost);
            if (realmBaseDomain is not null) hosts.Add($"*.{realmBaseDomain}");
            foreach (var (id, path) in new[]
            {
                ("public-realms", "/v1/public/realms"),
                ("public-realm", "/v1/public/realms/{realmId}")
            })
                routes.Add(new RouteConfig
                {
                    RouteId = id,
                    ClusterId = "public-realms",
                    Order = -50,
                    Match = new RouteMatch { Hosts = hosts, Path = path, Methods = ["GET", "OPTIONS"] }
                });
        }
        if (adminHost is not null)
        {
            routes.Add(new RouteConfig
            {
                RouteId = "admin",
                ClusterId = "admin",
                Order = -200,
                Match = new RouteMatch { Hosts = [adminHost], Path = "/{**path}" }
            });
            if (realmBaseDomain is not null)
                routes.Add(new RouteConfig
                {
                    RouteId = "realm-admin",
                    ClusterId = "admin",
                    Order = -100,
                    Match = new RouteMatch { Hosts = [$"*.admin.{realmBaseDomain}"], Path = "/{**path}" }
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
            if (realmBaseDomain is not null)
                routes.Add(new RouteConfig
                {
                    RouteId = "realm-frontend",
                    ClusterId = "frontend",
                    Order = 10,
                    Match = new RouteMatch { Hosts = [$"*.{realmBaseDomain}"], Path = "/{**path}" }
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
