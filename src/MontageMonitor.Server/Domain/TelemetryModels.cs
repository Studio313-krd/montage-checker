using System.Text.Json;
using MontageMonitor.Shared.States;

namespace MontageMonitor.Server.Domain;

public sealed class Heartbeat : Entity
{
    public Guid EventId { get; set; }
    public Guid AgentId { get; set; }
    public Guid EmployeeId { get; set; }
    public Guid ComputerId { get; set; }
    public required string AgentVersion { get; set; }
    public required string WindowsUser { get; set; }
    public required string MachineName { get; set; }
    public DateTimeOffset TimestampUtc { get; set; }
    public HumanState HumanState { get; set; }
    public MachineState MachineState { get; set; }
    public string? ForegroundProcess { get; set; }
    public string? ForegroundExecutablePath { get; set; }
    public string? ForegroundWindowTitle { get; set; }
    public int IdleSeconds { get; set; }
    public double CpuLoadPercent { get; set; }
    public double MemoryLoadPercent { get; set; }
    public string? RenderProgram { get; set; }
    public DateTimeOffset? RenderProcessStartedAtUtc { get; set; }
    public double? RenderProcessCpuPercent { get; set; }
    public long? RenderProcessWorkingSetBytes { get; set; }
    public double? RenderProcessIoReadBytesPerSecond { get; set; }
    public double? RenderProcessIoWriteBytesPerSecond { get; set; }
    public int? RenderChildProcessCount { get; set; }
    public double? RenderGpuLoadPercent { get; set; }
    public string? RenderOutputFolder { get; set; }
    public string? RenderOutputFile { get; set; }
    public long? RenderOutputFileSizeBytes { get; set; }
    public byte? RenderDetectionConfidence { get; set; }
    public string? RenderDetectionReason { get; set; }
}

public sealed class ActivityEvent
{
    public Guid EventId { get; set; }
    public Guid AgentId { get; set; }
    public Guid EmployeeId { get; set; }
    public Guid ComputerId { get; set; }
    public DateTimeOffset OccurredAtUtc { get; set; }
    public DateTimeOffset ReceivedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public required string EventType { get; set; }
    public int SchemaVersion { get; set; } = 1;
    public required JsonDocument Payload { get; set; }
}

public sealed class ApplicationSession : Entity
{
    public Guid EmployeeId { get; set; }
    public Guid ComputerId { get; set; }
    public DateTimeOffset StartedAtUtc { get; set; }
    public DateTimeOffset? EndedAtUtc { get; set; }
    public required string ProcessName { get; set; }
    public string? ExecutablePath { get; set; }
    public string? WindowTitle { get; set; }
    public ApplicationClassification Classification { get; set; }
}

public sealed class HumanStateSession : Entity
{
    public Guid EmployeeId { get; set; }
    public Guid ComputerId { get; set; }
    public DateTimeOffset StartedAtUtc { get; set; }
    public DateTimeOffset? EndedAtUtc { get; set; }
    public HumanState State { get; set; }
}

public sealed class MachineStateSession : Entity
{
    public Guid EmployeeId { get; set; }
    public Guid ComputerId { get; set; }
    public DateTimeOffset StartedAtUtc { get; set; }
    public DateTimeOffset? EndedAtUtc { get; set; }
    public MachineState State { get; set; }
    public byte DetectionConfidence { get; set; }
    public string? DetectionReason { get; set; }
}

public sealed class RenderSession : Entity
{
    public Guid EmployeeId { get; set; }
    public Guid ComputerId { get; set; }
    public ProcessingType Type { get; set; }
    public required string Program { get; set; }
    public DateTimeOffset StartedAtUtc { get; set; }
    public DateTimeOffset? EndedAtUtc { get; set; }
    public string? OutputFolder { get; set; }
    public string? OutputFile { get; set; }
    public double? AverageCpuPercent { get; set; }
    public double? MaxCpuPercent { get; set; }
    public long? FileSizeBytes { get; set; }
    public byte DetectionConfidence { get; set; }
    public required string DetectionReason { get; set; }
}

public sealed class Screenshot : Entity
{
    public Guid EventId { get; set; }
    public Guid EmployeeId { get; set; }
    public Guid ComputerId { get; set; }
    public DateTimeOffset TimestampUtc { get; set; }
    public required string StoragePath { get; set; }
    public required string MimeType { get; set; }
    public long FileSizeBytes { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public int ScreenIndex { get; set; }
    public string? ForegroundProcess { get; set; }
    public string? ForegroundWindowTitle { get; set; }
    public HumanState HumanState { get; set; }
    public MachineState MachineState { get; set; }
    public DateTimeOffset? FileDeletedAtUtc { get; set; }
}

public sealed class AuditLog
{
    public long Id { get; set; }
    public Guid? UserId { get; set; }
    public required string Action { get; set; }
    public required string EntityName { get; set; }
    public string? EntityId { get; set; }
    public DateTimeOffset TimestampUtc { get; set; } = DateTimeOffset.UtcNow;
    public string? IpAddress { get; set; }
    public JsonDocument? Details { get; set; }
}
