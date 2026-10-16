using System.Net;
using System.Text.Json;
using Arkya.Characters.Api.Application;
using Microsoft.Extensions.Options;

namespace Arkya.Characters.Api.Infrastructure.Identity;

public sealed class IdentitySessionValidator(HttpClient client, IOptions<IdentityOptions> options,
    TimeProvider timeProvider) : ISessionValidator
{
    public async Task<SessionValidationResult> ValidateAsync(string sessionToken, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(sessionToken) || sessionToken.Length > 2048)
            return new(SessionValidationStatus.Invalid);

        using var request = new HttpRequestMessage(HttpMethod.Post, "internal/sessions/introspect")
        {
            Content = JsonContent.Create(new { sessionToken })
        };
        request.Headers.Add("X-Arkya-Service-Key", options.Value.ServiceKey);
        try
        {
            using var response = await client.SendAsync(request, cancellationToken);
            if (response.StatusCode == HttpStatusCode.Unauthorized)
                return new(SessionValidationStatus.Invalid);
            if (response.StatusCode != HttpStatusCode.OK)
                return new(SessionValidationStatus.Unavailable);

            var session = await response.Content.ReadFromJsonAsync<PlayerSession>(cancellationToken);
            if (session is null || session.AccountId == Guid.Empty || string.IsNullOrWhiteSpace(session.DisplayName))
                return new(SessionValidationStatus.Unavailable);
            return session.ExpiresAt <= timeProvider.GetUtcNow()
                ? new(SessionValidationStatus.Invalid)
                : new(SessionValidationStatus.Valid, session);
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or NotSupportedException ||
            (exception is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            return new(SessionValidationStatus.Unavailable);
        }
    }
}
