using MontageMonitor.Shared.States;
using MontageMonitor.Server.Domain;

namespace MontageMonitor.Server.Features.Activity;

public sealed record ActivityTimelineResponse(
    Guid EmployeeId,
    DateTimeOffset FromUtc,
    DateTimeOffset ToUtc,
    DateTimeOffset GeneratedAtUtc,
    IReadOnlyList<ApplicationSessionResponse> Applications,
    IReadOnlyList<HumanStateSessionResponse> HumanStates,
    IReadOnlyList<MachineStateSessionResponse> MachineStates,
    IReadOnlyList<RenderSessionResponse> Renders);

public sealed record ApplicationSessionResponse(
    Guid Id,
    Guid ComputerId,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? EndedAtUtc,
    double DurationSeconds,
    string ProcessName,
    string? ExecutablePath,
    string? WindowTitle,
    ApplicationClassification Classification);

public sealed record HumanStateSessionResponse(
    Guid Id,
    Guid ComputerId,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? EndedAtUtc,
    double DurationSeconds,
    HumanState State);

public sealed record MachineStateSessionResponse(
    Guid Id,
    Guid ComputerId,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? EndedAtUtc,
    double DurationSeconds,
    MachineState State,
    byte DetectionConfidence,
    string? DetectionReason);

public sealed record RenderSessionResponse(
    Guid Id,
    Guid ComputerId,
    ProcessingType Type,
    string Program,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? EndedAtUtc,
    double DurationSeconds,
    string? OutputFolder,
    string? OutputFile,
    double? AverageCpuPercent,
    double? MaxCpuPercent,
    long? FileSizeBytes,
    byte DetectionConfidence,
    string DetectionReason);
