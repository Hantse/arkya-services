namespace Arkya.Reports.Api.Application;

public sealed record PlayerSession(Guid AccountId, string DisplayName, DateTimeOffset ExpiresAt);
public enum SessionValidationStatus { Valid, Invalid, Unavailable }
public sealed record SessionValidationResult(SessionValidationStatus Status, PlayerSession? Session = null);

public interface ISessionValidator
{
    Task<SessionValidationResult> ValidateAsync(string sessionToken, CancellationToken cancellationToken);
}

public interface ISessionReferenceValidator
{
    Task<SessionValidationResult> ValidateReferenceAsync(string sessionReference, CancellationToken cancellationToken);
}
