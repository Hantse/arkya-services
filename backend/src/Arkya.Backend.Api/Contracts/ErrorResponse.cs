namespace Arkya.Backend.Api.Contracts;

public sealed record ErrorResponse(
    string Code,
    string Message);
