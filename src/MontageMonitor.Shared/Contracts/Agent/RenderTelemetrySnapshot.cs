namespace MontageMonitor.Shared.Contracts.Agent;

public sealed record RenderTelemetrySnapshot(
    string Program,
    DateTimeOffset? ProcessStartedAtUtc,
    double ProcessCpuPercent,
    long ProcessWorkingSetBytes,
    double ProcessIoReadBytesPerSecond,
    double ProcessIoWriteBytesPerSecond,
    int ChildProcessCount,
    double? GpuLoadPercent,
    string? OutputFolder,
    string? OutputFile,
    long? OutputFileSizeBytes,
    byte DetectionConfidence,
    string DetectionReason);
