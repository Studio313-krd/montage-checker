using System.Security.Claims;
using MontageMonitor.Server.Domain;
using MontageMonitor.Server.Infrastructure.Security;
using Xunit;

namespace MontageMonitor.Agent.Tests;

public sealed class UserPermissionRulesTests
{
    [Theory]
    [InlineData(UserRole.Owner, true)]
    [InlineData(UserRole.Admin, true)]
    [InlineData(UserRole.Manager, false)]
    [InlineData(UserRole.Viewer, false)]
    public void GlobalEmployeeVisibility_IsLimitedToOwnerAndAdmin(UserRole role, bool expected)
    {
        Assert.Equal(expected, UserPermissionRules.HasGlobalEmployeeVisibility(Principal(role)));
    }

    [Fact]
    public void OnlyOwnerCanAssignOwnerRole()
    {
        Assert.True(UserPermissionRules.CanAssignRole(Principal(UserRole.Owner), UserRole.Owner));
        Assert.False(UserPermissionRules.CanAssignRole(Principal(UserRole.Admin), UserRole.Owner));
        Assert.True(UserPermissionRules.CanAssignRole(Principal(UserRole.Admin), UserRole.Manager));
    }

    [Fact]
    public void AdminCannotModifyOrPromoteOwner()
    {
        var owner = User(UserRole.Owner);
        var viewer = User(UserRole.Viewer);

        Assert.False(UserPermissionRules.CanModify(Principal(UserRole.Admin), owner, UserRole.Owner));
        Assert.False(UserPermissionRules.CanModify(Principal(UserRole.Admin), viewer, UserRole.Owner));
        Assert.True(UserPermissionRules.CanModify(Principal(UserRole.Admin), viewer, UserRole.Manager));
        Assert.True(UserPermissionRules.CanModify(Principal(UserRole.Owner), owner, UserRole.Admin));
    }

    private static ClaimsPrincipal Principal(UserRole role) => new(
        new ClaimsIdentity(
            [new Claim(ClaimTypes.Role, role.ToString())],
            "tests",
            ClaimTypes.Name,
            ClaimTypes.Role));

    private static User User(UserRole role) => new()
    {
        Login = role.ToString().ToLowerInvariant(),
        NormalizedLogin = role.ToString().ToUpperInvariant(),
        DisplayName = role.ToString(),
        PasswordHash = "test-only",
        Role = role,
    };
}
