using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MontageMonitor.Server.Domain;
using MontageMonitor.Shared.States;

namespace MontageMonitor.Server.Infrastructure.Persistence.Configurations;

public sealed class ApplicationRuleConfiguration : IEntityTypeConfiguration<ApplicationRule>
{
    private static readonly DateTimeOffset SeedTimestamp =
        new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public void Configure(EntityTypeBuilder<ApplicationRule> builder)
    {
        builder.ToTable("application_rules");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.ProcessName).HasMaxLength(255).IsRequired();
        builder.Property(x => x.NormalizedProcessName).HasMaxLength(255).IsRequired();
        builder.Property(x => x.DisplayName).HasMaxLength(255).IsRequired();
        builder.Property(x => x.Classification).HasConversion<string>().HasMaxLength(20);
        builder.Property(x => x.CreatedAtUtc).HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.Property(x => x.UpdatedAtUtc).HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.HasOne<User>().WithMany().HasForeignKey(x => x.UpdatedByUserId)
            .OnDelete(DeleteBehavior.SetNull);
        builder.HasIndex(x => x.NormalizedProcessName).IsUnique();
        builder.HasIndex(x => new { x.IsEnabled, x.Priority });

        builder.HasData(
            Rule("10000000-0000-0000-0000-000000000001", "Adobe Premiere Pro.exe", "Adobe Premiere Pro", ApplicationClassification.Productive),
            Rule("10000000-0000-0000-0000-000000000002", "Adobe Media Encoder.exe", "Adobe Media Encoder", ApplicationClassification.Productive),
            Rule("10000000-0000-0000-0000-000000000003", "AfterFX.exe", "Adobe After Effects", ApplicationClassification.Productive),
            Rule("10000000-0000-0000-0000-000000000004", "aerender.exe", "After Effects Render Engine", ApplicationClassification.Productive),
            Rule("10000000-0000-0000-0000-000000000005", "Resolve.exe", "DaVinci Resolve", ApplicationClassification.Productive),
            Rule("10000000-0000-0000-0000-000000000006", "ffmpeg.exe", "FFmpeg", ApplicationClassification.Productive),
            Rule("10000000-0000-0000-0000-000000000007", "Blender.exe", "Blender", ApplicationClassification.Productive),
            Rule("10000000-0000-0000-0000-000000000008", "chrome.exe", "Google Chrome", ApplicationClassification.Neutral),
            Rule("10000000-0000-0000-0000-000000000009", "1Password.exe", "1Password", ApplicationClassification.Ignored, true),
            Rule("10000000-0000-0000-0000-000000000010", "KeePass.exe", "KeePass", ApplicationClassification.Ignored, true));
    }

    private static ApplicationRule Rule(
        string id,
        string processName,
        string displayName,
        ApplicationClassification classification,
        bool isScreenshotExcluded = false) => new()
        {
            Id = Guid.Parse(id),
            ProcessName = processName,
            NormalizedProcessName = processName.ToUpperInvariant(),
            DisplayName = displayName,
            Classification = classification,
            IsScreenshotExcluded = isScreenshotExcluded,
            IsEnabled = true,
            CreatedAtUtc = SeedTimestamp,
            UpdatedAtUtc = SeedTimestamp,
        };
}

public sealed class RenderRuleConfiguration : IEntityTypeConfiguration<RenderRule>
{
    private static readonly DateTimeOffset SeedTimestamp =
        new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public void Configure(EntityTypeBuilder<RenderRule> builder)
    {
        builder.ToTable("render_rules", table =>
        {
            table.HasCheckConstraint("ck_render_rules_cpu", "cpu_threshold_percent BETWEEN 0 AND 100");
            table.HasCheckConstraint("ck_render_rules_disk", "disk_write_threshold_bytes_per_second >= 0");
            table.HasCheckConstraint("ck_render_rules_confirmation", "confirmation_seconds > 0");
            table.HasCheckConstraint("ck_render_rules_finish", "finish_timeout_seconds > 0");
            table.HasCheckConstraint("ck_render_rules_confidence", "minimum_confidence BETWEEN 0 AND 100");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Name).HasMaxLength(255).IsRequired();
        builder.Property(x => x.Type).HasConversion<string>().HasMaxLength(20);
        builder.Property(x => x.ProcessName).HasMaxLength(255).IsRequired();
        builder.Property(x => x.FileNamePatterns).HasColumnType("text[]");
        builder.Property(x => x.CreatedAtUtc).HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.Property(x => x.UpdatedAtUtc).HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.HasOne<User>().WithMany().HasForeignKey(x => x.UpdatedByUserId)
            .OnDelete(DeleteBehavior.SetNull);
        builder.HasIndex(x => new { x.IsEnabled, x.Type, x.ProcessName });

        builder.HasData(
            Rule("20000000-0000-0000-0000-000000000001", "Adobe Premiere Pro", "Adobe Premiere Pro.exe", 35),
            Rule("20000000-0000-0000-0000-000000000002", "Adobe Media Encoder", "Adobe Media Encoder.exe", 20),
            Rule("20000000-0000-0000-0000-000000000003", "Adobe After Effects", "AfterFX.exe", 40),
            Rule("20000000-0000-0000-0000-000000000004", "After Effects Render Engine", "aerender.exe", 30),
            Rule("20000000-0000-0000-0000-000000000005", "DaVinci Resolve", "Resolve.exe", 25),
            Rule("20000000-0000-0000-0000-000000000006", "FFmpeg", "ffmpeg.exe", 25),
            Rule("20000000-0000-0000-0000-000000000007", "Blender", "Blender.exe", 40),
            ProxyRule(
                "21000000-0000-0000-0000-000000000001",
                "Adobe Media Encoder — Proxy",
                "Adobe Media Encoder.exe",
                20),
            ProxyRule(
                "21000000-0000-0000-0000-000000000002",
                "DaVinci Resolve — Proxy",
                "Resolve.exe",
                25),
            ProxyRule(
                "21000000-0000-0000-0000-000000000003",
                "FFmpeg — Proxy",
                "ffmpeg.exe",
                25));
    }

    private static RenderRule Rule(string id, string name, string processName, double cpuThreshold) => new()
    {
        Id = Guid.Parse(id),
        Name = name,
        Type = ProcessingType.Render,
        ProcessName = processName,
        CpuThresholdPercent = cpuThreshold,
        DiskWriteThresholdBytesPerSecond = 1_048_576,
        ConfirmationSeconds = 15,
        FinishTimeoutSeconds = 30,
        MinimumConfidence = 70,
        FileNamePatterns = [],
        IsEnabled = true,
        CreatedAtUtc = SeedTimestamp,
        UpdatedAtUtc = SeedTimestamp,
    };

    private static RenderRule ProxyRule(string id, string name, string processName, double cpuThreshold) => new()
    {
        Id = Guid.Parse(id),
        Name = name,
        Type = ProcessingType.Proxy,
        ProcessName = processName,
        CpuThresholdPercent = cpuThreshold,
        DiskWriteThresholdBytesPerSecond = 1_048_576,
        ConfirmationSeconds = 15,
        FinishTimeoutSeconds = 30,
        MinimumConfidence = 70,
        FileNamePatterns = ["*_Proxy.mov", "*_Proxy.mp4", "*proxy*.mxf"],
        IsEnabled = true,
        CreatedAtUtc = SeedTimestamp,
        UpdatedAtUtc = SeedTimestamp,
    };
}

public sealed class WatchedFolderConfiguration : IEntityTypeConfiguration<WatchedFolder>
{
    public void Configure(EntityTypeBuilder<WatchedFolder> builder)
    {
        builder.ToTable("watched_folders");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Type).HasConversion<string>().HasMaxLength(20);
        builder.Property(x => x.PathPattern).HasMaxLength(2_000).IsRequired();
        builder.Property(x => x.Extensions).HasColumnType("text[]");
        builder.Property(x => x.FileNamePatterns).HasColumnType("text[]");
        builder.Property(x => x.CreatedAtUtc).HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.Property(x => x.UpdatedAtUtc).HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.HasOne<Computer>().WithMany().HasForeignKey(x => x.ComputerId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(x => new { x.ComputerId, x.Type, x.IsEnabled });
    }
}

public sealed class SystemSettingConfiguration : IEntityTypeConfiguration<SystemSetting>
{
    private static readonly DateTimeOffset SeedTimestamp =
        new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public void Configure(EntityTypeBuilder<SystemSetting> builder)
    {
        builder.ToTable("system_settings", table =>
            table.HasCheckConstraint("ck_system_settings_version", "version > 0"));
        builder.HasKey(x => x.Key);
        builder.Property(x => x.Key).HasMaxLength(200);
        builder.Property(x => x.JsonValue).HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.UpdatedAtUtc).HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.HasOne<User>().WithMany().HasForeignKey(x => x.UpdatedByUserId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasData(
            Setting("company.timeZone", "\"Europe/Moscow\""),
            Setting("agent.heartbeatIntervalSeconds", "30"),
            Setting("agent.syncIntervalSeconds", "45"),
            Setting("agent.latestVersion", "\"0.1.0\""),
            Setting("activity.idleThresholdSeconds", "300"),
            Setting("screenshots.intervalMinutes", "5"),
            Setting("screenshots.captureMode", "\"PrimaryMonitor\""),
            Setting("screenshots.maxWidth", "1600"),
            Setting("screenshots.jpegQuality", "60"),
            Setting("screenshots.retentionDays", "30"),
            Setting("telemetry.retentionDays", "365"),
            Setting("backup.retentionDays", "14"));
    }

    private static SystemSetting Setting(string key, string jsonValue) => new()
    {
        Key = key,
        JsonValue = jsonValue,
        Version = 1,
        UpdatedAtUtc = SeedTimestamp,
    };
}
