namespace MontageMonitor.Server.Features.Employees;

public sealed record EmployeeResponse(
    Guid Id,
    string Name,
    string Login,
    string? Department,
    bool IsActive,
    bool ScreenshotEnabled,
    int ScreenshotIntervalMinutes,
    int IdleThresholdSeconds,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record CreateEmployeeRequest(
    string Name,
    string Login,
    string? Department,
    bool ScreenshotEnabled = true,
    int ScreenshotIntervalMinutes = 5,
    int IdleThresholdSeconds = 300);

public sealed record UpdateEmployeeRequest(
    string Name,
    string Login,
    string? Department,
    bool IsActive,
    bool ScreenshotEnabled,
    int ScreenshotIntervalMinutes,
    int IdleThresholdSeconds);

public sealed record EmployeeOperatorPinResponse(Guid EmployeeId, string OperatorPin);
