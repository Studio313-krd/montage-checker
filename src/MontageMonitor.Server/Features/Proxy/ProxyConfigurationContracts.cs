namespace MontageMonitor.Server.Features.Proxy;

public sealed record ProxyRuleResponse(
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

public sealed record SaveProxyRuleRequest(
    string Name,
    string ProcessName,
    double CpuThresholdPercent,
    long DiskWriteThresholdBytesPerSecond,
    int ConfirmationSeconds,
    int FinishTimeoutSeconds,
    byte MinimumConfidence,
    IReadOnlyList<string>? FileNamePatterns,
    bool IsEnabled = true);

public sealed record ProxyFolderResponse(
    Guid Id,
    Guid ComputerId,
    string PathPattern,
    IReadOnlyList<string> Extensions,
    IReadOnlyList<string> FileNamePatterns,
    bool IsEnabled,
    DateTimeOffset UpdatedAtUtc);

public sealed record SaveProxyFolderRequest(
    string PathPattern,
    IReadOnlyList<string>? Extensions,
    IReadOnlyList<string>? FileNamePatterns,
    bool IsEnabled = true);
