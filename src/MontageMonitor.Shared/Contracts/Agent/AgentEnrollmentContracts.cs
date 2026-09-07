namespace MontageMonitor.Shared.Contracts.Agent;

public sealed record AgentEnrollmentRequest(
    string EnrollmentToken,
    string MachineName,
    string WindowsUser,
    string OperatingSystem,
    string AgentVersion);

public sealed record AgentEnrollmentResponse(
    Guid AgentId,
    Guid EmployeeId,
    Guid ComputerId,
    string DeviceAccessToken,
    DateTimeOffset ExpiresAtUtc);
