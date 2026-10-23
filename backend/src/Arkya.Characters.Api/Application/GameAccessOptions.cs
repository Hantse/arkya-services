using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Arkya.ServiceDefaults;
using Microsoft.Extensions.Options;

namespace Arkya.Characters.Api.Application;

public sealed class RealmClientOptions
{
    public string BaseUrl { get; set; } = "";
    public string ServiceKey { get; set; } = "";
    public bool AllowClusterHttp { get; set; }
    public bool IsValid(bool development) => BaseUrl.Length == 0 && ServiceKey.Length == 0 ||
        ServiceKey.Length is >= 32 and <= 512 && Uri.TryCreate(BaseUrl, UriKind.Absolute, out var uri) &&
        uri.AbsolutePath == "/" && uri.Query.Length == 0 && uri.Fragment.Length == 0 && uri.UserInfo.Length == 0 &&
        (uri.Scheme == "https" || development && uri.Scheme == "http" && uri.IsLoopback ||
            AllowClusterHttp && uri.Scheme == "http" && Regex.IsMatch(BaseUrl,
                @"\Ahttp://[a-z](?:[a-z0-9-]{0,61}[a-z0-9])?\.[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?\.svc\.cluster\.local(?::[1-9][0-9]{0,4})?/?\z",
                RegexOptions.CultureInvariant));
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
    public bool AllowPrivateHttp { get; set; }
    public DedicatedServerIdentity[] DedicatedServers { get; set; } = [];
    public bool IsValid() => DedicatedServers is not null && DedicatedServers.Length <= 256 &&
        DedicatedServers.All(value => value is not null && GameProtocol.ValidId(value.RealmId) && GameProtocol.ValidId(value.ServerId) && value.Key.Length is >= 32 and <= 512) &&
        DedicatedServers.Select(value => value.Key).Distinct().Count() == DedicatedServers.Length &&
        DedicatedServers.Select(value => (value.RealmId, value.ServerId)).Distinct().Count() == DedicatedServers.Length;
}

public sealed class CharacterRealmRuntimeOptions
{
    public bool DynamicRealmsEnabled { get; set; }
    public string DedicatedKeySeed { get; set; } = "";

    public bool IsValid() => !DynamicRealmsEnabled || DedicatedKeySeed is { Length: >= 32 and <= 512 } &&
        DedicatedKeySeed == DedicatedKeySeed.Trim() && !DedicatedKeySeed.Any(char.IsControl);
}

public sealed class DedicatedKeyValidator(IOptions<CharacterGameAccessOptions> options,
    IOptions<CharacterRealmRuntimeOptions>? runtime = null, IGameRealmCatalog? realms = null)
{
    private readonly CharacterRealmRuntimeOptions runtimeOptions = runtime?.Value ?? new();

    public DedicatedServerIdentity? Resolve(string? key)
    {
        if (runtimeOptions.DynamicRealmsEnabled || key is not { Length: >= 32 and <= 512 }) return null;
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        return options.Value.DedicatedServers.FirstOrDefault(value =>
            CryptographicOperations.FixedTimeEquals(hash, SHA256.HashData(Encoding.UTF8.GetBytes(value.Key))));
    }

    public async Task<DedicatedServerIdentity?> ResolveAsync(string? key, CancellationToken cancellationToken)
    {
        if (!runtimeOptions.DynamicRealmsEnabled) return Resolve(key);
        if (!runtimeOptions.IsValid() || key is not { Length: >= 32 and <= 512 }) return null;
        if (realms is null) throw new HttpRequestException("Realm catalogue is unavailable.");
        var catalog = await realms.ListAsync(cancellationToken);
        if (catalog?.Realms is null || catalog.Realms.Length > 100 || catalog.Realms.Any(value => value is null ||
            !GameProtocol.ValidId(value.RealmId) || !GameProtocol.ValidId(value.ServerId) ||
            !GameProtocol.ValidEndpoint(value.Endpoint, true) ||
            !GameTopology.IsValid(value, true)) ||
            catalog.Realms.Select(value => value.RealmId).Distinct(StringComparer.Ordinal).Count() != catalog.Realms.Length)
            throw new HttpRequestException("Realm catalogue response is invalid.");
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        foreach (var realm in catalog.Realms.Where(value => GameProtocol.IsSupported(value.ProtocolVersion, value.BuildId)))
        foreach (var serverId in realm.Partitions.Select(partition => partition.ServerId).Prepend(realm.ServerId).Distinct(StringComparer.Ordinal))
        {
            // Starting dedicateds need credentials before their realm becomes ready.
            var expected = RealmRuntimeKeys.DeriveDedicatedKey(runtimeOptions.DedicatedKeySeed, realm.RealmId, serverId);
            if (CryptographicOperations.FixedTimeEquals(hash, SHA256.HashData(Encoding.UTF8.GetBytes(expected))))
                return new() { RealmId = realm.RealmId, ServerId = serverId, Key = expected };
        }
        return null;
    }

    public bool IsConfigured(string realmId, string serverId) =>
        runtimeOptions.DynamicRealmsEnabled
            ? runtimeOptions.IsValid() && GameProtocol.ValidId(realmId) && GameProtocol.ValidId(serverId)
            : options.Value.DedicatedServers.Any(value => value.RealmId == realmId && value.ServerId == serverId);
}

public interface IGameRealmCatalog
{
    Task<GameRealmCatalog> ListAsync(CancellationToken cancellationToken);
}
