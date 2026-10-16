namespace Arkya.Backend.Api.Application.Servers;

public sealed record GameServerSummary(
    string Id,
    string Name,
    string Host,
    int Port,
    string Region,
    string Status,
    int CurrentPlayers,
    int MaxPlayers);
