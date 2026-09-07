namespace MontageMonitor.Server.Features.Agents;

public sealed record CreateEnrollmentTokenRequest(int ExpiresInHours = 24);

public sealed record EnrollmentTokenResponse(
    Guid Id,
    Guid EmployeeId,
    string EnrollmentToken,
    DateTimeOffset ExpiresAtUtc);

public sealed record ComputerResponse(
    Guid Id,
    Guid EmployeeId,
    string EmployeeName,
    string Name,
    string? OperatingSystem,
    string? AgentVersion,
    DateTimeOffset? LastHeartbeatAtUtc,
    DateTimeOffset? LastOnlineAtUtc,
    bool IsRevoked,
    Guid? AgentId,
    string? AgentStatus,
    DateTimeOffset? AgentLastSeenAtUtc);
