using MontageMonitor.Shared.States;

namespace MontageMonitor.Shared.Contracts.Agent;

public sealed record ScreenshotUploadMetadata(
    Guid EventId,
    Guid AgentId,
    Guid EmployeeId,
    Guid ComputerId,
    DateTimeOffset TimestampUtc,
    int ScreenIndex,
    int Width,
    int Height,
    string? ForegroundProcess,
    string? ForegroundWindowTitle,
    HumanState HumanState,
    MachineState MachineState);

public sealed record ScreenshotUploadResponse(
    Guid EventId,
    Guid ScreenshotId,
    DateTimeOffset AcceptedAtUtc);
