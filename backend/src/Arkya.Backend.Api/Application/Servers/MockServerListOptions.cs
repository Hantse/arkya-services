namespace Arkya.Backend.Api.Application.Servers;

public sealed class MockServerListOptions
{
    public const string SectionName = "MockServerList";

    public string LocalDevHost { get; init; } = "127.0.0.1";

    public int LocalDevPort { get; init; } = 7777;

    public int LocalDevMaxPlayers { get; init; } = 100;
}
