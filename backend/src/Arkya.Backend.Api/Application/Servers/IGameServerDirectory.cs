namespace Arkya.Backend.Api.Application.Servers;

public interface IGameServerDirectory
{
    ValueTask<IReadOnlyCollection<GameServerSummary>> ListServersAsync(
        CancellationToken cancellationToken);
}
