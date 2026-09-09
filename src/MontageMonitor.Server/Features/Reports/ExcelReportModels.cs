using MontageMonitor.Server.Domain;
using MontageMonitor.Shared.States;

namespace MontageMonitor.Server.Features.Reports;

internal sealed record ExcelReportData(
    ReportRange Range,
    IReadOnlyList<ExcelSummaryRow> Summary,
    IReadOnlyList<ExcelTimelineRow> Timeline,
    IReadOnlyList<ExcelApplicationRow> Applications,
    IReadOnlyList<ExcelRenderRow> Renders,
    IReadOnlyList<ExcelIdleRow> Idle);

internal sealed record ExcelSummaryRow(
    Guid EmployeeId,
    string EmployeeName,
    DateOnly Date,
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
    double OfflineSeconds,
    int ComputerCount,
    double ParallelSeconds,
    int ScreenshotCount);

internal sealed record ExcelTimelineRow(
    Guid EmployeeId,
    string EmployeeName,
    Guid ComputerId,
    string ComputerName,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset EndedAtUtc,
    HumanState? HumanState,
    MachineState? MachineState,
    string? Application,
    string? WindowTitle)
{
    public double DurationSeconds => (EndedAtUtc - StartedAtUtc).TotalSeconds;
}

internal sealed record ExcelApplicationRow(
    Guid EmployeeId,
    string EmployeeName,
    Guid ComputerId,
    string ComputerName,
    DateOnly Date,
    string Application,
    ApplicationClassification Classification,
    double DurationSeconds);

internal sealed record ExcelRenderRow(
    Guid EmployeeId,
    string EmployeeName,
    Guid ComputerId,
    string ComputerName,
    ProcessingType Type,
    string Application,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset EndedAtUtc,
    string? Output,
    byte DetectionConfidence,
    string DetectionReason)
{
    public double DurationSeconds => (EndedAtUtc - StartedAtUtc).TotalSeconds;
}

internal sealed record ExcelIdleRow(
    Guid EmployeeId,
    string EmployeeName,
    Guid ComputerId,
    string ComputerName,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset EndedAtUtc)
{
    public double DurationSeconds => (EndedAtUtc - StartedAtUtc).TotalSeconds;
}

internal sealed record ExcelReportBuildResult(ExcelReportData? Data, bool NotFound);
