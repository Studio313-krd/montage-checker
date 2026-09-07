using MontageMonitor.Server.Domain;

namespace MontageMonitor.Server.Features.Auth;

public sealed record LoginRequest(string? Login, string? Password);

public sealed record AuthUserResponse(
    Guid Id,
    string Login,
    string DisplayName,
    UserRole Role);

public sealed record AccessTokenResponse(
    string AccessToken,
    string TokenType,
    DateTimeOffset ExpiresAtUtc,
    AuthUserResponse User);
