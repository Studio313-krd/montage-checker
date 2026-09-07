using MontageMonitor.Shared.States;

namespace MontageMonitor.Server.Features.Dashboard;

public sealed record DashboardResponse(
    DateTimeOffset GeneratedAtUtc,
    int RefreshAfterSeconds,
    IReadOnlyList<DashboardEmployeeResponse> Employees);

public sealed record DashboardEmployeeResponse(
    Guid EmployeeId,
    string Name,
    string? Department,
    Guid? ComputerId,
    string? ComputerName,
    string? AgentVersion,
    bool IsOnline,
    string? CurrentApplication,
    string? CurrentProcess,
    string? CurrentWindowTitle,
    HumanState HumanState,
    DateTimeOffset? HumanStateStartedAtUtc,
    MachineState MachineState,
    DateTimeOffset? MachineStateStartedAtUtc,
    DateTimeOffset? PrimaryStateStartedAtUtc,
    DateTimeOffset? LastHeartbeatAtUtc,
    DashboardScreenshotResponse? LatestScreenshot);

public sealed record DashboardScreenshotResponse(
    Guid Id,
    DateTimeOffset TimestampUtc,
    int Width,
    int Height,
    int ScreenIndex,
    string ContentUrl);
