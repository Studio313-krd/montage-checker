namespace MontageMonitor.Server.Domain;

public sealed class User : Entity
{
    public required string Login { get; set; }

    public required string NormalizedLogin { get; set; }

    public required string DisplayName { get; set; }

    public required string PasswordHash { get; set; }

    public UserRole Role { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTimeOffset? LastLoginAtUtc { get; set; }

    public int FailedLoginCount { get; set; }

    public DateTimeOffset? LockoutEndUtc { get; set; }
}

public sealed class RefreshToken : Entity
{
    public Guid UserId { get; set; }

    public required string TokenHash { get; set; }

    public DateTimeOffset ExpiresAtUtc { get; set; }

    public DateTimeOffset? RevokedAtUtc { get; set; }

    public Guid? ReplacedByTokenId { get; set; }
}

public sealed class UserEmployeeAccess
{
    public Guid UserId { get; set; }

    public Guid EmployeeId { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
