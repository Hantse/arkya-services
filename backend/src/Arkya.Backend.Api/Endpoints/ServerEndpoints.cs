using Arkya.Backend.Api.Application.Servers;
using Arkya.Backend.Api.Contracts.Servers;

namespace Arkya.Backend.Api.Endpoints;

public static class ServerEndpoints
{
    public static IEndpointRouteBuilder MapServerEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(
            "/servers",
            async (
                IGameServerDirectory serverDirectory,
                CancellationToken cancellationToken) =>
            {
                var servers = await serverDirectory.ListServersAsync(cancellationToken);
                var response = servers.Select(ServerResponse.FromServer).ToArray();

                return Results.Ok(response);
            })
            .WithName("ListServers");

        return endpoints;
    }
}
