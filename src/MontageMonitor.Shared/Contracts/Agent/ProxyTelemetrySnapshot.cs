namespace MontageMonitor.Shared.Contracts.Agent;

public sealed record ProxyTelemetrySnapshot(
    string Program,
    DateTimeOffset? ProcessStartedAtUtc,
    double ProcessCpuPercent,
    long ProcessWorkingSetBytes,
    double ProcessIoReadBytesPerSecond,
    double ProcessIoWriteBytesPerSecond,
    int ChildProcessCount,
    string? OutputFolder,
    string? OutputFile,
    long? OutputFileSizeBytes,
    byte DetectionConfidence,
    string DetectionReason,
    bool FileNameMatched);
