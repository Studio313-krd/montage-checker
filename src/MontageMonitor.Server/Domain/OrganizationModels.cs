namespace MontageMonitor.Server.Domain;

public sealed class Employee : Entity
{
    public required string Name { get; set; }

    public required string Login { get; set; }

    public required string NormalizedLogin { get; set; }

    public string? Department { get; set; }

    public bool IsActive { get; set; } = true;

    public bool ScreenshotEnabled { get; set; } = true;

    public int ScreenshotIntervalMinutes { get; set; } = 5;

    public int IdleThresholdSeconds { get; set; } = 300;
}

public sealed class Computer : Entity
{
    public Guid EmployeeId { get; set; }

    public required string Name { get; set; }

    public required string NormalizedName { get; set; }

    public string? OperatingSystem { get; set; }

    public string? AgentVersion { get; set; }

    public string? LastIpAddress { get; set; }

    public DateTimeOffset? LastHeartbeatAtUtc { get; set; }

    public DateTimeOffset? LastOnlineAtUtc { get; set; }

    public bool IsRevoked { get; set; }
}

public sealed class AgentDevice : Entity
{
    public Guid ComputerId { get; set; }

    public AgentStatus Status { get; set; } = AgentStatus.Pending;

    public required string InstalledVersion { get; set; }

    public long ConfigVersion { get; set; }

    public DateTimeOffset EnrolledAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? LastSeenAtUtc { get; set; }

    public DateTimeOffset? RevokedAtUtc { get; set; }
}

public sealed class AgentCredential : Entity
{
    public Guid AgentId { get; set; }

    public required string SecretHash { get; set; }

    public DateTimeOffset? ExpiresAtUtc { get; set; }

    public DateTimeOffset? RevokedAtUtc { get; set; }
}

public sealed class AgentEnrollmentToken : Entity
{
    public Guid EmployeeId { get; set; }

    public required string TokenHash { get; set; }

    public DateTimeOffset ExpiresAtUtc { get; set; }

    public DateTimeOffset? UsedAtUtc { get; set; }

    public Guid? CreatedByUserId { get; set; }

    public Guid? ConsumedByAgentId { get; set; }
}
