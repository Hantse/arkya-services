using System.Security.Cryptography;
using System.Text;
using Arkya.Realms.Api.Domain;
using Arkya.ServiceDefaults;
using Microsoft.Extensions.Options;

namespace Arkya.Realms.Api.Application;

public sealed class GameEntryOptions
{
    public string ServiceKey { get; set; } = "";
    public StandaloneGameEntry[] StandaloneEntries { get; set; } = [];
    public bool IsValid(bool development) => StandaloneEntries is not null && StandaloneEntries.Length <= 100 &&
        (ServiceKey.Length is >= 32 and <= 512 || ServiceKey.Length == 0 && StandaloneEntries.Length == 0) &&
        StandaloneEntries.All(value => value is not null && value.IsValid(development)) &&
        StandaloneEntries.Select(value => value.RealmId).Distinct().Count() == StandaloneEntries.Length;
}

public sealed class StandaloneGameEntry
{
    public string RealmId { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string ServerId { get; set; } = "";
    public string Endpoint { get; set; } = "";
    public bool Enabled { get; set; }
    public int ProtocolVersion { get; set; } = GameProtocol.Version;
    public string BuildId { get; set; } = GameProtocol.BuildId;
    public bool IsValid(bool development) => GameProtocol.ValidId(RealmId) && GameProtocol.ValidId(ServerId) &&
        DeploymentOptions.ValidName(DisplayName) && GameProtocol.ValidEndpoint(Endpoint, development) &&
        ProtocolVersion == GameProtocol.Version && BuildId == GameProtocol.BuildId;
}

public sealed class GameEntryService(IRealmRepository realms, IOptions<GameEntryOptions> configured)
{
    public bool Authorize(string? key) => configured.Value.ServiceKey.Length >= 32 && key is { Length: >= 32 and <= 512 } &&
        CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(key)),
            SHA256.HashData(Encoding.UTF8.GetBytes(configured.Value.ServiceKey)));

    public async Task<GameRealmCatalog> ListAsync(CancellationToken cancellationToken)
    {
        var entries = configured.Value.StandaloneEntries.Select(value => new GameRealmEntry(value.RealmId,
            value.DisplayName, value.Enabled, value.ServerId, value.Endpoint, value.ProtocolVersion, value.BuildId)).ToList();
        foreach (var realm in await realms.ListAsync(cancellationToken))
        {
            // A deployed realm must never be silently overridden by standalone configuration.
            entries.RemoveAll(value => value.RealmId == realm.RealmId);
            // The current admission contract has one destination and cannot select an island or transfer authority.
            if (realm.Profile.Topology is not null) continue;
            var destinations = realm.Profile.Servers.Where(value => value.Transport == "WebSocket" &&
                GameProtocol.ValidEndpoint(value.PublicEndpoint) && value.ProtocolVersion == GameProtocol.Version && value.BuildId == GameProtocol.BuildId).ToArray();
            var server = destinations.Length == 1 ? destinations[0] : null;
            if (server is not null) entries.Add(new(realm.RealmId, realm.DisplayName, realm.Status == RealmStatus.Ready,
                server.Id, server.PublicEndpoint!, server.ProtocolVersion, server.BuildId));
        }
        return new(entries.OrderBy(value => value.RealmId).ToArray());
    }
}
