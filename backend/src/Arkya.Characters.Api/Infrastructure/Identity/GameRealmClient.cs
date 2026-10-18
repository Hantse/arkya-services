using Arkya.Characters.Api.Application;
using Arkya.ServiceDefaults;
using Microsoft.Extensions.Options;

namespace Arkya.Characters.Api.Infrastructure.Identity;

public sealed class GameRealmClient(HttpClient client, IOptions<RealmClientOptions> configured, IHostEnvironment environment) : IGameRealmCatalog
{
    public async Task<GameRealmCatalog> ListAsync(CancellationToken cancellationToken)
    {
        var options = configured.Value;
        if (options.BaseUrl.Length == 0) throw new HttpRequestException("Realm catalogue is not configured.");
        using var request = new HttpRequestMessage(HttpMethod.Get, options.BaseUrl.TrimEnd('/') + "/internal/realms/entries");
        request.Headers.Add("X-Arkya-Service-Key", options.ServiceKey);
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode) throw new HttpRequestException("Realm catalogue is unavailable.");
        await response.Content.LoadIntoBufferAsync(131072, cancellationToken);
        GameRealmCatalog? catalog;
        try { catalog = await response.Content.ReadFromJsonAsync<GameRealmCatalog>(cancellationToken); }
        catch (System.Text.Json.JsonException) { throw new HttpRequestException("Realm catalogue response is invalid."); }
        if (catalog?.Realms is null || catalog.Realms.Length > 100 || catalog.Realms.Any(value => value is null ||
            !GameProtocol.ValidId(value.RealmId) || !GameProtocol.ValidId(value.ServerId) ||
            !GameProtocol.ValidEndpoint(value.Endpoint, environment.IsDevelopment()) || value.DisplayName is not { Length: >= 1 and <= 80 }) ||
            catalog.Realms.Select(value => value.RealmId).Distinct().Count() != catalog.Realms.Length)
            throw new HttpRequestException("Realm catalogue response is invalid.");
        return catalog;
    }
}
