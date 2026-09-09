using MontageMonitor.Shared.Contracts.Agent;
using MontageMonitor.Shared.States;

namespace MontageMonitor.Agent.Abstractions;

internal interface IActivityMonitor
{
    ActivitySnapshot Capture();
}

internal interface IIdleMonitor : IDisposable
{
    IdleSnapshot Capture(int idleThresholdSeconds);
}

internal interface ISystemMetricsProvider
{
    SystemMetricsSnapshot Capture();
}

internal interface IProcessMonitor
{
    IReadOnlyList<ProcessMetricsSnapshot> Capture(IReadOnlyCollection<string> processNames);
}

internal interface IGpuMetricsProvider
{
    Task<GpuMetricsSnapshot> CaptureAsync(CancellationToken cancellationToken);
}

internal interface IWatchedFolderMonitor
{
    WatchedFileSnapshot? Capture(IReadOnlyList<AgentWatchedFolder> folders);
}

internal interface IRenderDetector
{
    Task<RenderDetectionResult> EvaluateAsync(
        DateTimeOffset timestampUtc,
        IReadOnlyList<AgentRenderRule> rules,
        IReadOnlyList<AgentWatchedFolder> folders,
        CancellationToken cancellationToken);
}

internal interface IProxyDetector
{
    Task<ProxyDetectionResult> EvaluateAsync(
        DateTimeOffset timestampUtc,
        IReadOnlyList<AgentProxyRule> rules,
        IReadOnlyList<AgentWatchedFolder> folders,
        CancellationToken cancellationToken);
}

internal interface IScreenshotCapture
{
    IReadOnlyList<CapturedScreenshot> Capture(
        ScreenshotCaptureMode captureMode,
        int maxWidth,
        int jpegQuality);
}

internal interface ILocalScreenshotQueue
{
    Task InitializeAsync(CancellationToken cancellationToken);
    Task EnqueueAsync(
        ScreenshotUploadMetadata metadata,
        ReadOnlyMemory<byte> jpegData,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<QueuedScreenshot>> GetReadyAsync(int limit, CancellationToken cancellationToken);
    Task MarkSentAsync(Guid eventId, CancellationToken cancellationToken);
    Task MarkFailedAsync(Guid eventId, int previousAttemptCount, CancellationToken cancellationToken);
    Task<int> CountAsync(CancellationToken cancellationToken);
    Task ClearAsync(CancellationToken cancellationToken);
}

internal interface ILocalEventQueue
{
    Task InitializeAsync(CancellationToken cancellationToken);
    Task EnqueueAsync(HeartbeatRequest heartbeat, CancellationToken cancellationToken);
    Task<IReadOnlyList<QueuedHeartbeat>> GetReadyAsync(int limit, CancellationToken cancellationToken);
    Task MarkSentAsync(Guid eventId, CancellationToken cancellationToken);
    Task MarkFailedAsync(Guid eventId, int previousAttemptCount, CancellationToken cancellationToken);
    Task<int> CountAsync(CancellationToken cancellationToken);
    Task ClearAsync(CancellationToken cancellationToken);
}

internal interface IAgentApiClient : IDisposable
{
    Task<HeartbeatSendResult> SendHeartbeatAsync(
        HeartbeatRequest heartbeat,
        CancellationToken cancellationToken);

    Task<AgentConfigurationResponse?> GetConfigurationAsync(
        Guid? operatorSessionId,
        CancellationToken cancellationToken);

    Task<AgentOperatorOptionsResponse?> GetOperatorOptionsAsync(
        Guid? operatorSessionId,
        CancellationToken cancellationToken);

    Task<AgentOperatorSessionResponse> StartOperatorSessionAsync(
        Guid employeeId,
        string pin,
        CancellationToken cancellationToken);

    Task<HeartbeatSendResult> UploadScreenshotAsync(
        QueuedScreenshot screenshot,
        CancellationToken cancellationToken);
}

internal sealed record ActivitySnapshot(
    string? ProcessName,
    string? ExecutablePath,
    string? WindowTitle);

internal sealed record IdleSnapshot(int IdleSeconds, HumanState HumanState);

internal sealed record SystemMetricsSnapshot(double CpuLoadPercent, double MemoryLoadPercent);

internal sealed record ProcessMetricsSnapshot(
    int ProcessId,
    string ProcessName,
    DateTimeOffset? StartedAtUtc,
    double CpuLoadPercent,
    long WorkingSetBytes,
    double IoReadBytesPerSecond,
    double IoWriteBytesPerSecond,
    int ChildProcessCount);

internal sealed record GpuMetricsSnapshot(double? LoadPercent);

internal sealed record WatchedFileSnapshot(
    string Folder,
    string FilePath,
    long FileSizeBytes,
    bool WasCreated,
    bool IsGrowing,
    bool HasKnownExtension);

internal sealed record RenderDetectionResult(
    MachineState MachineState,
    RenderTelemetrySnapshot? Telemetry);

internal sealed record ProxyDetectionResult(
    MachineState MachineState,
    ProxyTelemetrySnapshot? Telemetry);

internal sealed record CapturedScreenshot(
    int ScreenIndex,
    int Width,
    int Height,
    byte[] JpegData);

internal sealed record QueuedScreenshot(
    Guid EventId,
    ScreenshotUploadMetadata Metadata,
    string LocalFilePath,
    int AttemptCount);

internal sealed record QueuedHeartbeat(Guid EventId, HeartbeatRequest Payload, int AttemptCount);

internal enum HeartbeatSendResult
{
    Sent,
    RetryLater,
    Unauthorized,
    OperatorSelectionRequired,
    Rejected,
}
