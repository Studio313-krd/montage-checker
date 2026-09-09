using MontageMonitor.Shared.States;

namespace MontageMonitor.Shared.Contracts.Agent;

public sealed record HeartbeatRequest(
    Guid EventId,
    Guid AgentId,
    Guid EmployeeId,
    Guid ComputerId,
    string AgentVersion,
    DateTimeOffset TimestampUtc,
    string WindowsUser,
    string MachineName,
    HumanState HumanState,
    MachineState MachineState,
    string? ForegroundProcess,
    string? ForegroundExecutablePath,
    string? ForegroundWindowTitle,
    int IdleSeconds,
    double CpuLoadPercent,
    double MemoryLoadPercent,
    RenderTelemetrySnapshot? RenderTelemetry = null,
    ProxyTelemetrySnapshot? ProxyTelemetry = null,
    ScreenshotSkippedEvent? ScreenshotEvent = null,
    Guid? OperatorSessionId = null);

public sealed record HeartbeatResponse(
    Guid EventId,
    DateTimeOffset AcceptedAtUtc,
    long ConfigVersion);

public sealed record ScreenshotSkippedEvent(
    Guid EventId,
    DateTimeOffset TimestampUtc,
    string EventType,
    string? ForegroundProcess,
    string? ForegroundWindowTitle);
