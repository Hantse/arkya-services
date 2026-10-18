using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Arkya.Characters.Api.Application;
using Arkya.ServiceDefaults;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace Arkya.Characters.Api.Infrastructure.Identity;

public sealed class SessionAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger, UrlEncoder encoder, ISessionValidator sessions)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "ArkyaSession";
    private bool identityUnavailable;

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (Context.GetEndpoint()?.Metadata.GetMetadata<IAllowAnonymous>() is not null)
            return AuthenticateResult.NoResult();
        var values = Request.Headers.Authorization;
        if (values.Count == 0)
            return AuthenticateResult.NoResult();
        if (values.Count != 1 || !AuthenticationHeaderValue.TryParse(values[0], out var header) ||
            !header.Scheme.Equals("Bearer", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(header.Parameter))
            return AuthenticateResult.Fail("A valid bearer session is required.");

        var result = await sessions.ValidateAsync(header.Parameter, Context.RequestAborted);
        if (result.Status != SessionValidationStatus.Valid)
        {
            identityUnavailable = result.Status == SessionValidationStatus.Unavailable;
            return AuthenticateResult.Fail("The session could not be validated.");
        }

        var session = result.Session!;
        var identity = new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, session.AccountId.ToString("D")),
            new Claim(ClaimTypes.Name, session.DisplayName),
            new Claim(SessionReference.ClaimType, SessionReference.Create(header.Parameter)),
            new Claim("arkya:session-expires-at", session.ExpiresAt.ToString("O", System.Globalization.CultureInfo.InvariantCulture))
        ], SchemeName);
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        if (!identityUnavailable)
            Response.Headers.WWWAuthenticate = "Bearer";
        return Results.Problem(statusCode: identityUnavailable ? 503 : 401,
            title: identityUnavailable ? "Identity service unavailable." : "A valid session is required.")
            .ExecuteAsync(Context);
    }
}
