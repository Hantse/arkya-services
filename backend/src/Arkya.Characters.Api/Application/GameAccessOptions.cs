using System.Security.Cryptography;
using System.Text;
using Arkya.ServiceDefaults;
using Microsoft.Extensions.Options;

namespace Arkya.Characters.Api.Application;

public sealed class RealmClientOptions
{
    public string BaseUrl { get; set; } = "";
    public string ServiceKey { get; set; } = "";
    public bool IsValid(bool development) => BaseUrl.Length == 0 && ServiceKey.Length == 0 ||
        ServiceKey.Length is >= 32 and <= 512 && Uri.TryCreate(BaseUrl, UriKind.Absolute, out var uri) &&
        uri.AbsolutePath == "/" && uri.Query.Length == 0 && uri.Fragment.Length == 0 && uri.UserInfo.Length == 0 &&
        (uri.Scheme == "https" || development && uri.Scheme == "http" && uri.Host is "localhost" or "127.0.0.1");
}

public sealed class DedicatedServerIdentity
{
    public string RealmId { get; set; } = "";
    public string ServerId { get; set; } = "";
    public string Key { get; set; } = "";
}

public sealed class CharacterGameAccessOptions
{
    public bool RequireBetaAccess { get; set; }
    public DedicatedServerIdentity[] DedicatedServers { get; set; } = [];
    public bool IsValid() => DedicatedServers is not null && DedicatedServers.Length <= 100 &&
        DedicatedServers.All(value => value is not null && GameProtocol.ValidId(value.RealmId) && GameProtocol.ValidId(value.ServerId) && value.Key.Length is >= 32 and <= 512) &&
        DedicatedServers.Select(value => value.Key).Distinct().Count() == DedicatedServers.Length &&
        DedicatedServers.Select(value => (value.RealmId, value.ServerId)).Distinct().Count() == DedicatedServers.Length;
}

public sealed class DedicatedKeyValidator(IOptions<CharacterGameAccessOptions> options)
{
    public DedicatedServerIdentity? Resolve(string? key)
    {
        if (key is not { Length: >= 32 and <= 512 }) return null;
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        return options.Value.DedicatedServers.FirstOrDefault(value =>
            CryptographicOperations.FixedTimeEquals(hash, SHA256.HashData(Encoding.UTF8.GetBytes(value.Key))));
    }
    public bool IsConfigured(string realmId, string serverId) =>
        options.Value.DedicatedServers.Any(value => value.RealmId == realmId && value.ServerId == serverId);
}

public interface IGameRealmCatalog
{
    Task<GameRealmCatalog> ListAsync(CancellationToken cancellationToken);
}
