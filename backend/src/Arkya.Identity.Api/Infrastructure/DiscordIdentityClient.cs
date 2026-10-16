using System.Net.Http.Headers;
using System.Text.Json;
using Arkya.Identity.Api.Application;
using Microsoft.Extensions.Options;

namespace Arkya.Identity.Api.Infrastructure;

public sealed class DiscordIdentityClient(HttpClient http, IOptions<DiscordOptions> options) : IDiscordIdentityClient
{
    public async Task<DiscordUser> ExchangeCodeAsync(string code, CancellationToken cancellationToken)
    {
        using var response = await http.PostAsync("https://discord.com/api/oauth2/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code", ["code"] = code,
            ["client_id"] = options.Value.ClientId, ["client_secret"] = options.Value.ClientSecret,
            ["redirect_uri"] = options.Value.RedirectUri
        }), cancellationToken);
        response.EnsureSuccessStatusCode();
        try
        {
            using var token = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            var accessToken = token.RootElement.GetProperty("access_token").GetString();
            if (string.IsNullOrWhiteSpace(accessToken))
            {
                throw new InvalidDataException("Discord returned an invalid token response.");
            }
            using var request = new HttpRequestMessage(HttpMethod.Get, "https://discord.com/api/v10/users/@me");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            using var profileResponse = await http.SendAsync(request, cancellationToken);
            profileResponse.EnsureSuccessStatusCode();
            using var profile = JsonDocument.Parse(await profileResponse.Content.ReadAsStringAsync(cancellationToken));
            var id = profile.RootElement.GetProperty("id").GetString();
            var name = profile.RootElement.TryGetProperty("global_name", out var globalName) && globalName.ValueKind == JsonValueKind.String
                ? globalName.GetString() : profile.RootElement.GetProperty("username").GetString();
            if (string.IsNullOrWhiteSpace(id) || id.Length > 32 || !id.All(char.IsAsciiDigit) || string.IsNullOrWhiteSpace(name) || name.Length > 128)
            {
                throw new InvalidDataException("Discord returned an invalid identity.");
            }
            return new(id, name);
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            throw new InvalidDataException("Discord returned an invalid response.", exception);
        }
    }
}
