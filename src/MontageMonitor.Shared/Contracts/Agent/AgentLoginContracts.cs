namespace MontageMonitor.Shared.Contracts.Agent;

public sealed record AgentLoginRequest(
    string? Login,
    string Password,
    string MachineName,
    string WindowsUser,
    string OperatingSystem,
    string AgentVersion,
    Guid? EmployeeId = null);

public sealed record AgentLoginOptionsResponse(
    IReadOnlyList<AgentOperatorOption> Employees);

public sealed record AgentLoginResponse(
    Guid AgentId,
    Guid EmployeeId,
    Guid ComputerId,
    string DeviceAccessToken,
    DateTimeOffset ExpiresAtUtc,
    AgentOperatorSessionResponse OperatorSession);
