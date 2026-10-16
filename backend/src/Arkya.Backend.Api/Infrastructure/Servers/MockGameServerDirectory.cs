using Arkya.Backend.Api.Application.Servers;

using Microsoft.Extensions.Options;

namespace Arkya.Backend.Api.Infrastructure.Servers;

public sealed class MockGameServerDirectory : IGameServerDirectory
{
    private readonly IOptions<MockServerListOptions> options;

    public MockGameServerDirectory(IOptions<MockServerListOptions> options)
    {
        this.options = options;
    }

    public ValueTask<IReadOnlyCollection<GameServerSummary>> ListServersAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var localDevServer = new GameServerSummary(
            "local-dev",
            "Local Dev Server",
            options.Value.LocalDevHost,
            options.Value.LocalDevPort,
            "local",
            "online",
            0,
            options.Value.LocalDevMaxPlayers);

        return ValueTask.FromResult<IReadOnlyCollection<GameServerSummary>>(
            [localDevServer]);
    }
}
