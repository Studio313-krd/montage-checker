namespace MontageMonitor.Server.Features.Agents;

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
