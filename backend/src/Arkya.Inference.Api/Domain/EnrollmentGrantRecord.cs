namespace Arkya.Inference.Api.Domain;

public sealed record EnrollmentGrantRecord(string TokenHash, DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt,
    string? BindingAgentId = null, string? BindingTokenHash = null, bool Revoked = false);
