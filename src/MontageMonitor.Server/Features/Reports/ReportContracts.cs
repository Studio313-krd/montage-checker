using MontageMonitor.Server.Domain;
using MontageMonitor.Shared.States;

namespace MontageMonitor.Server.Features.Reports;

public sealed record SummaryReportResponse(
    DateTimeOffset FromUtc,
    DateTimeOffset ToUtc,
    string TimeZone,
    DateTimeOffset GeneratedAtUtc,
    IReadOnlyList<EmployeeSummaryResponse> Employees);

public sealed record EmployeeSummaryResponse(
    Guid EmployeeId,
    string EmployeeName,
    DateTimeOffset? FirstEventAtUtc,
    DateTimeOffset? LastEventAtUtc,
    double TotalSeconds,
    double ProductiveSeconds,
    double ActiveSeconds,
    double RenderSeconds,
    double ProxySeconds,
    double BackgroundSeconds,
    double IdleSeconds,
    double LockedSeconds,
    double OfflineSeconds);

public sealed record ApplicationReportResponse(
    DateTimeOffset FromUtc,
    DateTimeOffset ToUtc,
    string TimeZone,
    DateTimeOffset GeneratedAtUtc,
    IReadOnlyList<ApplicationReportRow> Applications);

public sealed record ApplicationReportRow(
    Guid EmployeeId,
    string EmployeeName,
    DateOnly Date,
    string Application,
    string ProcessName,
    ApplicationClassification Classification,
    double DurationSeconds);

public sealed record RenderReportResponse(
    DateTimeOffset FromUtc,
    DateTimeOffset ToUtc,
    DateTimeOffset GeneratedAtUtc,
    IReadOnlyList<RenderReportRow> Renders);

public sealed record RenderReportRow(
    Guid Id,
    Guid EmployeeId,
    string EmployeeName,
    Guid ComputerId,
    string ComputerName,
    ProcessingType Type,
    string Program,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? EndedAtUtc,
    double DurationSeconds,
    string? OutputFolder,
    string? OutputFile,
    double? MaxCpuPercent,
    double? AverageCpuPercent,
    long? FileSizeBytes,
    byte DetectionConfidence,
    string DetectionReason);

public sealed record ScreenshotGalleryResponse(
    DateTimeOffset FromUtc,
    DateTimeOffset ToUtc,
    DateTimeOffset GeneratedAtUtc,
    int Page,
    int PageSize,
    int TotalCount,
    IReadOnlyList<ScreenshotGalleryItem> Screenshots);

public sealed record ScreenshotGalleryItem(
    Guid Id,
    Guid EmployeeId,
    string EmployeeName,
    Guid ComputerId,
    string ComputerName,
    DateTimeOffset TimestampUtc,
    int Width,
    int Height,
    int ScreenIndex,
    string? Application,
    string? WindowTitle,
    HumanState HumanState,
    MachineState MachineState,
    string ContentUrl);
