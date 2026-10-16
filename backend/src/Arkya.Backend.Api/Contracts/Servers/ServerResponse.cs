using Arkya.Backend.Api.Application.Servers;

namespace Arkya.Backend.Api.Contracts.Servers;

public sealed record ServerResponse(
    string Id,
    string Name,
    string Host,
    int Port,
    string Region,
    string Status,
    int CurrentPlayers,
    int MaxPlayers)
{
    public static ServerResponse FromServer(GameServerSummary server)
    {
        return new ServerResponse(
            server.Id,
            server.Name,
            server.Host,
            server.Port,
            server.Region,
            server.Status,
            server.CurrentPlayers,
            server.MaxPlayers);
    }
}
