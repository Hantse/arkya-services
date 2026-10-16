using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Arkya.Identity.Api.Application;
using Microsoft.Extensions.Options;

namespace Arkya.Identity.Api.Infrastructure;

public sealed class DiscordSocialClient(HttpClient http, IOptions<DiscordOptions> options) : IDiscordSocialClient
{
    public Task<DiscordSocialGrant> ExchangeAsync(string code, string codeVerifier, string redirectUri, CancellationToken cancellationToken) =>
        RequestGrantAsync(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code", ["code"] = code, ["code_verifier"] = codeVerifier, ["redirect_uri"] = redirectUri
        }, cancellationToken);

    public Task<DiscordSocialGrant> RefreshAsync(string refreshToken, CancellationToken cancellationToken) =>
        RequestGrantAsync(new Dictionary<string, string> { ["grant_type"] = "refresh_token", ["refresh_token"] = refreshToken }, cancellationToken);

    private async Task<DiscordSocialGrant> RequestGrantAsync(Dictionary<string, string> form, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "https://discord.com/api/v10/oauth2/token");
            request.Content = new FormUrlEncodedContent(form);
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(
                Encoding.UTF8.GetBytes(options.Value.ClientId + ":" + options.Value.ClientSecret)));
            using var response = await http.SendAsync(request, cancellationToken);
            EnsureSuccess(response);
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            var root = document.RootElement;
            var accessToken = root.GetProperty("access_token").GetString();
            var refreshToken = root.GetProperty("refresh_token").GetString();
            var tokenType = root.GetProperty("token_type").GetString();
            var scope = root.GetProperty("scope").GetString();
            var expiresIn = root.GetProperty("expires_in").GetInt32();
            if (!DiscordSocialService.IsBoundedSecret(accessToken, 4096) || !DiscordSocialService.IsBoundedSecret(refreshToken, 4096) ||
                !string.Equals(tokenType, "Bearer", StringComparison.OrdinalIgnoreCase) || expiresIn <= 0 ||
                string.IsNullOrWhiteSpace(scope) || scope.Length > 4096)
                throw new DiscordSocialException("invalid_discord_response", 502);

            var grantedScopes = scope.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
            if (!grantedScopes.Contains("openid") || !grantedScopes.Contains("sdk.social_layer") || !grantedScopes.Contains("identify"))
                throw new DiscordSocialException("discord_social_scope_required", 403);

            using var profileRequest = new HttpRequestMessage(HttpMethod.Get, "https://discord.com/api/v10/users/@me");
            profileRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            using var profileResponse = await http.SendAsync(profileRequest, cancellationToken);
            EnsureSuccess(profileResponse);
            using var profile = JsonDocument.Parse(await profileResponse.Content.ReadAsStringAsync(cancellationToken));
            var discordUserId = profile.RootElement.GetProperty("id").GetString();
            if (string.IsNullOrWhiteSpace(discordUserId) || discordUserId.Length > 32 || !discordUserId.All(char.IsAsciiDigit))
                throw new DiscordSocialException("invalid_discord_response", 502);
            return new(accessToken!, refreshToken!, expiresIn, scope, discordUserId);
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            throw new DiscordSocialException("invalid_discord_response", 502);
        }
        catch (HttpRequestException)
        {
            throw new DiscordSocialException("discord_unavailable", 503);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new DiscordSocialException("discord_unavailable", 503);
        }
    }

    private static void EnsureSuccess(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode) return;
        if (response.StatusCode == HttpStatusCode.BadRequest || response.StatusCode == HttpStatusCode.Unauthorized || response.StatusCode == HttpStatusCode.Forbidden)
            throw new DiscordSocialException("discord_authorization_rejected", 400);
        throw new DiscordSocialException("discord_unavailable", 503);
    }
}
