using System.Security.Claims;

namespace MontageMonitor.Server.Infrastructure.Security;

public static class CurrentUserExtensions
{
    public static bool TryGetUserId(this ClaimsPrincipal principal, out Guid userId) =>
        Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out userId);

    public static Guid GetRequiredUserId(this ClaimsPrincipal principal) =>
        principal.TryGetUserId(out var userId)
            ? userId
            : throw new InvalidOperationException("JWT не содержит идентификатор пользователя.");
}
