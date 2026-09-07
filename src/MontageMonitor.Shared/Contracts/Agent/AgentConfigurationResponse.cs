namespace MontageMonitor.Shared.Contracts.Agent;

public sealed record AgentConfigurationResponse(
    long ConfigVersion,
    int HeartbeatIntervalSeconds,
    int IdleThresholdSeconds,
    IReadOnlyList<AgentRenderRule>? RenderRules = null,
    IReadOnlyList<AgentWatchedFolder>? RenderFolders = null,
    IReadOnlyList<AgentProxyRule>? ProxyRules = null,
    IReadOnlyList<AgentWatchedFolder>? ProxyFolders = null,
    AgentScreenshotConfiguration? Screenshots = null);

public sealed record AgentRenderRule(
    Guid Id,
    string Name,
    string ProcessName,
    double CpuThresholdPercent,
    long DiskWriteThresholdBytesPerSecond,
    int ConfirmationSeconds,
    int FinishTimeoutSeconds,
    byte MinimumConfidence,
    IReadOnlyList<string> FileNamePatterns);

public sealed record AgentWatchedFolder(
    Guid Id,
    string PathPattern,
    IReadOnlyList<string> Extensions,
    IReadOnlyList<string> FileNamePatterns);

public sealed record AgentProxyRule(
    Guid Id,
    string Name,
    string ProcessName,
    double CpuThresholdPercent,
    long DiskWriteThresholdBytesPerSecond,
    int ConfirmationSeconds,
    int FinishTimeoutSeconds,
    byte MinimumConfidence,
    IReadOnlyList<string> FileNamePatterns);

public sealed record AgentScreenshotConfiguration(
    bool Enabled,
    int IntervalMinutes,
    ScreenshotCaptureMode CaptureMode,
    int MaxWidth,
    int JpegQuality,
    IReadOnlyList<string> ExcludedProcesses);

public enum ScreenshotCaptureMode
{
    PrimaryMonitor = 0,
    AllMonitors = 1,
}
