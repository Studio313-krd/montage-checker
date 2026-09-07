namespace MontageMonitor.Server.Features.Rendering;

public sealed record RenderRuleResponse(
    Guid Id,
    string Name,
    string ProcessName,
    double CpuThresholdPercent,
    long DiskWriteThresholdBytesPerSecond,
    int ConfirmationSeconds,
    int FinishTimeoutSeconds,
    byte MinimumConfidence,
    IReadOnlyList<string> FileNamePatterns,
    bool IsEnabled,
    DateTimeOffset UpdatedAtUtc);

public sealed record SaveRenderRuleRequest(
    string Name,
    string ProcessName,
    double CpuThresholdPercent,
    long DiskWriteThresholdBytesPerSecond,
    int ConfirmationSeconds,
    int FinishTimeoutSeconds,
    byte MinimumConfidence,
    IReadOnlyList<string>? FileNamePatterns,
    bool IsEnabled = true);

public sealed record RenderFolderResponse(
    Guid Id,
    Guid ComputerId,
    string PathPattern,
    IReadOnlyList<string> Extensions,
    IReadOnlyList<string> FileNamePatterns,
    bool IsEnabled,
    DateTimeOffset UpdatedAtUtc);

public sealed record SaveRenderFolderRequest(
    string PathPattern,
    IReadOnlyList<string>? Extensions,
    IReadOnlyList<string>? FileNamePatterns,
    bool IsEnabled = true);
