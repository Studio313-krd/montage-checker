using MontageMonitor.Shared.States;

namespace MontageMonitor.Server.Domain;

public sealed class ApplicationRule : Entity
{
    public required string ProcessName { get; set; }
    public required string NormalizedProcessName { get; set; }
    public required string DisplayName { get; set; }
    public ApplicationClassification Classification { get; set; }
    public bool IsScreenshotExcluded { get; set; }
    public bool IsEnabled { get; set; } = true;
    public int Priority { get; set; }
    public Guid? UpdatedByUserId { get; set; }
}

public sealed class RenderRule : Entity
{
    public required string Name { get; set; }
    public ProcessingType Type { get; set; }
    public required string ProcessName { get; set; }
    public double CpuThresholdPercent { get; set; } = 20;
    public long DiskWriteThresholdBytesPerSecond { get; set; } = 1_048_576;
    public int ConfirmationSeconds { get; set; } = 15;
    public int FinishTimeoutSeconds { get; set; } = 30;
    public byte MinimumConfidence { get; set; } = 50;
    public string[] FileNamePatterns { get; set; } = [];
    public bool IsEnabled { get; set; } = true;
    public Guid? UpdatedByUserId { get; set; }
}

public sealed class WatchedFolder : Entity
{
    public Guid ComputerId { get; set; }
    public WatchedFolderType Type { get; set; }
    public required string PathPattern { get; set; }
    public string[] Extensions { get; set; } = [];
    public string[] FileNamePatterns { get; set; } = [];
    public bool IsEnabled { get; set; } = true;
}

public sealed class SystemSetting
{
    public required string Key { get; set; }
    public required string JsonValue { get; set; }
    public long Version { get; set; } = 1;
    public Guid? UpdatedByUserId { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
