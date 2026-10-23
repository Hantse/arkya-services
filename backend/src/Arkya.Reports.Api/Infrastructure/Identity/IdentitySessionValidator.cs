using System.Net;
using System.Text.Json;
using Arkya.Reports.Api.Application;
using Arkya.ServiceDefaults;
using Microsoft.Extensions.Options;

namespace Arkya.Reports.Api.Infrastructure.Identity;

public sealed class IdentitySessionValidator(HttpClient client, IOptions<IdentityOptions> options,
    TimeProvider timeProvider) : ISessionValidator, ISessionReferenceValidator
{
    public async Task<SessionValidationResult> ValidateAsync(string sessionToken, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(sessionToken) || sessionToken.Length > 2048)
            return new(SessionValidationStatus.Invalid);

        return await IntrospectAsync("internal/sessions/introspect", new { sessionToken }, cancellationToken);
    }

    public Task<SessionValidationResult> ValidateReferenceAsync(string sessionReference, CancellationToken cancellationToken) =>
        SessionReference.IsValid(sessionReference)
            ? IntrospectAsync("internal/sessions/introspect-reference", new { sessionReference }, cancellationToken)
            : Task.FromResult(new SessionValidationResult(SessionValidationStatus.Invalid));

    private async Task<SessionValidationResult> IntrospectAsync(string path, object payload, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(5));
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(payload) };
        request.Headers.Add("X-Arkya-Service-Key", options.Value.ServiceKey);
        try
        {
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
            if (response.StatusCode == HttpStatusCode.Unauthorized)
                return new(SessionValidationStatus.Invalid);
            if (response.StatusCode != HttpStatusCode.OK)
                return new(SessionValidationStatus.Unavailable);

            await response.Content.LoadIntoBufferAsync(16384, deadline.Token);
            var session = await response.Content.ReadFromJsonAsync<PlayerSession>(deadline.Token);
            if (session is null || session.AccountId == Guid.Empty || string.IsNullOrWhiteSpace(session.DisplayName))
                return new(SessionValidationStatus.Unavailable);
            return session.ExpiresAt <= timeProvider.GetUtcNow()
                ? new(SessionValidationStatus.Invalid)
                : new(SessionValidationStatus.Valid, session);
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or NotSupportedException or IOException ||
            (exception is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            return new(SessionValidationStatus.Unavailable);
        }
    }
}
