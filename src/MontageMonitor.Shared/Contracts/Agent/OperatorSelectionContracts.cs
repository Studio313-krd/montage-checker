namespace MontageMonitor.Shared.Contracts.Agent;

public sealed record AgentOperatorOption(Guid EmployeeId, string Name);

public sealed record AgentOperatorOptionsResponse(
    IReadOnlyList<AgentOperatorOption> Employees,
    AgentOperatorSessionResponse? CurrentSession,
    DateTimeOffset ServerTimeUtc);

public sealed record StartAgentOperatorSessionRequest(
    string? Login,
    string? Password,
    Guid? EmployeeId = null,
    string? Pin = null);

public sealed record AgentOperatorSessionResponse(
    Guid SessionId,
    Guid EmployeeId,
    string EmployeeName,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset ExpiresAtUtc);
