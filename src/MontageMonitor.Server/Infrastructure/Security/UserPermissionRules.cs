using System.Security.Claims;
using MontageMonitor.Server.Domain;

namespace MontageMonitor.Server.Infrastructure.Security;

internal static class UserPermissionRules
{
    public static bool HasGlobalEmployeeVisibility(ClaimsPrincipal principal) =>
        principal.IsInRole(nameof(UserRole.Owner)) || principal.IsInRole(nameof(UserRole.Admin));

    public static bool CanAssignRole(ClaimsPrincipal principal, UserRole role) =>
        principal.IsInRole(nameof(UserRole.Owner)) || role != UserRole.Owner;

    public static bool CanModify(ClaimsPrincipal principal, User target, UserRole requestedRole) =>
        principal.IsInRole(nameof(UserRole.Owner)) ||
        target.Role != UserRole.Owner && requestedRole != UserRole.Owner;
}
