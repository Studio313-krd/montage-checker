using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MontageMonitor.Server.Domain;

namespace MontageMonitor.Server.Infrastructure.Persistence.Configurations;

public sealed class HeartbeatConfiguration : IEntityTypeConfiguration<Heartbeat>
{
    public void Configure(EntityTypeBuilder<Heartbeat> builder)
    {
        builder.ToTable("heartbeats", table =>
        {
            table.HasCheckConstraint("ck_heartbeats_idle", "idle_seconds >= 0");
            table.HasCheckConstraint("ck_heartbeats_cpu", "cpu_load_percent BETWEEN 0 AND 100");
            table.HasCheckConstraint("ck_heartbeats_memory", "memory_load_percent BETWEEN 0 AND 100");
            table.HasCheckConstraint(
                "ck_heartbeats_identity_text",
                "agent_version <> '' AND windows_user <> '' AND machine_name <> ''");
            table.HasCheckConstraint(
                "ck_heartbeats_render_metrics",
                "(render_process_cpu_percent IS NULL OR render_process_cpu_percent BETWEEN 0 AND 100) " +
                "AND (render_gpu_load_percent IS NULL OR render_gpu_load_percent BETWEEN 0 AND 100) " +
                "AND (render_process_working_set_bytes IS NULL OR render_process_working_set_bytes >= 0) " +
                "AND (render_process_io_read_bytes_per_second IS NULL OR render_process_io_read_bytes_per_second >= 0) " +
                "AND (render_process_io_write_bytes_per_second IS NULL OR render_process_io_write_bytes_per_second >= 0) " +
                "AND (render_child_process_count IS NULL OR render_child_process_count >= 0) " +
                "AND (render_output_file_size_bytes IS NULL OR render_output_file_size_bytes >= 0) " +
                "AND (render_detection_confidence IS NULL OR render_detection_confidence BETWEEN 0 AND 100)");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.AgentVersion).HasMaxLength(50).IsRequired();
        builder.Property(x => x.WindowsUser).HasMaxLength(255).IsRequired();
        builder.Property(x => x.MachineName).HasMaxLength(255).IsRequired();
        builder.Property(x => x.HumanState).HasConversion<string>().HasMaxLength(20);
        builder.Property(x => x.MachineState).HasConversion<string>().HasMaxLength(30);
        builder.Property(x => x.ForegroundProcess).HasMaxLength(255);
        builder.Property(x => x.ForegroundExecutablePath).HasMaxLength(2_000);
        builder.Property(x => x.ForegroundWindowTitle).HasMaxLength(2_000);
        builder.Property(x => x.RenderProgram).HasMaxLength(255);
        builder.Property(x => x.RenderOutputFolder).HasMaxLength(2_000);
        builder.Property(x => x.RenderOutputFile).HasMaxLength(2_000);
        builder.Property(x => x.RenderDetectionReason).HasMaxLength(2_000);
        builder.Property(x => x.CreatedAtUtc).HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.Property(x => x.UpdatedAtUtc).HasDefaultValueSql("CURRENT_TIMESTAMP");
        ConfigureTelemetryRelations(builder);
        builder.HasIndex(x => x.EventId).IsUnique();
        builder.HasIndex(x => new { x.EmployeeId, x.TimestampUtc });
        builder.HasIndex(x => new { x.ComputerId, x.TimestampUtc });
        builder.HasIndex(x => new { x.ComputerId, x.MachineState, x.TimestampUtc });
    }

    private static void ConfigureTelemetryRelations(EntityTypeBuilder<Heartbeat> builder)
    {
        builder.HasOne<AgentDevice>().WithMany().HasForeignKey(x => x.AgentId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Employee>().WithMany().HasForeignKey(x => x.EmployeeId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Computer>().WithMany().HasForeignKey(x => x.ComputerId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class ActivityEventConfiguration : IEntityTypeConfiguration<ActivityEvent>
{
    public void Configure(EntityTypeBuilder<ActivityEvent> builder)
    {
        builder.ToTable("activity_events", table =>
            table.HasCheckConstraint("ck_activity_events_schema_version", "schema_version > 0"));
        builder.HasKey(x => x.EventId);
        builder.Property(x => x.EventType).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Payload).HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.ReceivedAtUtc).HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.HasOne<AgentDevice>().WithMany().HasForeignKey(x => x.AgentId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Employee>().WithMany().HasForeignKey(x => x.EmployeeId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Computer>().WithMany().HasForeignKey(x => x.ComputerId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.EmployeeId, x.OccurredAtUtc });
        builder.HasIndex(x => new { x.ComputerId, x.OccurredAtUtc });
        builder.HasIndex(x => new { x.AgentId, x.ReceivedAtUtc });
    }
}

public sealed class ApplicationSessionConfiguration : IEntityTypeConfiguration<ApplicationSession>
{
    public void Configure(EntityTypeBuilder<ApplicationSession> builder)
    {
        builder.ToTable("application_sessions", table =>
            table.HasCheckConstraint(
                "ck_application_sessions_time",
                "ended_at_utc IS NULL OR ended_at_utc >= started_at_utc"));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.ProcessName).HasMaxLength(255).IsRequired();
        builder.Property(x => x.ExecutablePath).HasMaxLength(2_000);
        builder.Property(x => x.WindowTitle).HasMaxLength(2_000);
        builder.Property(x => x.Classification).HasConversion<string>().HasMaxLength(20);
        ConfigureSession(builder);
    }

    private static void ConfigureSession(EntityTypeBuilder<ApplicationSession> builder)
    {
        builder.Property(x => x.CreatedAtUtc).HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.Property(x => x.UpdatedAtUtc).HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.HasOne<Employee>().WithMany().HasForeignKey(x => x.EmployeeId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Computer>().WithMany().HasForeignKey(x => x.ComputerId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.EmployeeId, x.StartedAtUtc });
        builder.HasIndex(x => new { x.ComputerId, x.StartedAtUtc });
        builder.HasIndex(x => new { x.EmployeeId, x.ProcessName, x.StartedAtUtc });
        builder.HasIndex(x => x.ComputerId)
            .HasFilter("ended_at_utc IS NULL")
            .IsUnique();
    }
}

public sealed class HumanStateSessionConfiguration : IEntityTypeConfiguration<HumanStateSession>
{
    public void Configure(EntityTypeBuilder<HumanStateSession> builder)
    {
        builder.ToTable("human_state_sessions", table =>
            table.HasCheckConstraint(
                "ck_human_state_sessions_time",
                "ended_at_utc IS NULL OR ended_at_utc >= started_at_utc"));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.State).HasConversion<string>().HasMaxLength(20);
        ConfigureSession(builder);
        builder.HasIndex(x => new { x.EmployeeId, x.State, x.StartedAtUtc });
        builder.HasIndex(x => x.ComputerId)
            .HasFilter("ended_at_utc IS NULL")
            .IsUnique();
    }

    private static void ConfigureSession(EntityTypeBuilder<HumanStateSession> builder)
    {
        builder.Property(x => x.CreatedAtUtc).HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.Property(x => x.UpdatedAtUtc).HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.HasOne<Employee>().WithMany().HasForeignKey(x => x.EmployeeId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Computer>().WithMany().HasForeignKey(x => x.ComputerId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.EmployeeId, x.StartedAtUtc });
        builder.HasIndex(x => new { x.ComputerId, x.StartedAtUtc });
    }
}

public sealed class MachineStateSessionConfiguration : IEntityTypeConfiguration<MachineStateSession>
{
    public void Configure(EntityTypeBuilder<MachineStateSession> builder)
    {
        builder.ToTable("machine_state_sessions", table =>
        {
            table.HasCheckConstraint(
                "ck_machine_state_sessions_time",
                "ended_at_utc IS NULL OR ended_at_utc >= started_at_utc");
            table.HasCheckConstraint(
                "ck_machine_state_sessions_confidence",
                "detection_confidence BETWEEN 0 AND 100");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.State).HasConversion<string>().HasMaxLength(30);
        builder.Property(x => x.DetectionReason).HasMaxLength(1_000);
        builder.Property(x => x.CreatedAtUtc).HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.Property(x => x.UpdatedAtUtc).HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.HasOne<Employee>().WithMany().HasForeignKey(x => x.EmployeeId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Computer>().WithMany().HasForeignKey(x => x.ComputerId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.EmployeeId, x.StartedAtUtc });
        builder.HasIndex(x => new { x.ComputerId, x.StartedAtUtc });
        builder.HasIndex(x => new { x.EmployeeId, x.State, x.StartedAtUtc });
        builder.HasIndex(x => x.ComputerId)
            .HasFilter("ended_at_utc IS NULL")
            .IsUnique();
    }
}

public sealed class RenderSessionConfiguration : IEntityTypeConfiguration<RenderSession>
{
    public void Configure(EntityTypeBuilder<RenderSession> builder)
    {
        builder.ToTable("render_sessions", table =>
        {
            table.HasCheckConstraint(
                "ck_render_sessions_time",
                "ended_at_utc IS NULL OR ended_at_utc >= started_at_utc");
            table.HasCheckConstraint(
                "ck_render_sessions_confidence",
                "detection_confidence BETWEEN 0 AND 100");
            table.HasCheckConstraint(
                "ck_render_sessions_cpu",
                "(average_cpu_percent IS NULL OR average_cpu_percent BETWEEN 0 AND 100) " +
                "AND (max_cpu_percent IS NULL OR max_cpu_percent BETWEEN 0 AND 100)");
            table.HasCheckConstraint(
                "ck_render_sessions_file_size",
                "file_size_bytes IS NULL OR file_size_bytes >= 0");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Type).HasConversion<string>().HasMaxLength(20);
        builder.Property(x => x.Program).HasMaxLength(255).IsRequired();
        builder.Property(x => x.OutputFolder).HasMaxLength(2_000);
        builder.Property(x => x.OutputFile).HasMaxLength(2_000);
        builder.Property(x => x.DetectionReason).HasMaxLength(2_000).IsRequired();
        builder.Property(x => x.CreatedAtUtc).HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.Property(x => x.UpdatedAtUtc).HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.HasOne<Employee>().WithMany().HasForeignKey(x => x.EmployeeId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Computer>().WithMany().HasForeignKey(x => x.ComputerId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.EmployeeId, x.StartedAtUtc });
        builder.HasIndex(x => new { x.ComputerId, x.StartedAtUtc });
        builder.HasIndex(x => new { x.Type, x.StartedAtUtc });
        builder.HasIndex(x => x.ComputerId)
            .HasFilter("ended_at_utc IS NULL")
            .IsUnique();
    }
}

public sealed class ScreenshotConfiguration : IEntityTypeConfiguration<Screenshot>
{
    public void Configure(EntityTypeBuilder<Screenshot> builder)
    {
        builder.ToTable("screenshots", table =>
        {
            table.HasCheckConstraint("ck_screenshots_file_size", "file_size_bytes >= 0");
            table.HasCheckConstraint("ck_screenshots_dimensions", "width > 0 AND height > 0");
            table.HasCheckConstraint("ck_screenshots_screen_index", "screen_index >= 0");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.StoragePath).HasMaxLength(1_000).IsRequired();
        builder.Property(x => x.MimeType).HasMaxLength(100).IsRequired();
        builder.Property(x => x.ForegroundProcess).HasMaxLength(255);
        builder.Property(x => x.ForegroundWindowTitle).HasMaxLength(2_000);
        builder.Property(x => x.HumanState).HasConversion<string>().HasMaxLength(20);
        builder.Property(x => x.MachineState).HasConversion<string>().HasMaxLength(30);
        builder.Property(x => x.CreatedAtUtc).HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.Property(x => x.UpdatedAtUtc).HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.HasOne<Employee>().WithMany().HasForeignKey(x => x.EmployeeId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Computer>().WithMany().HasForeignKey(x => x.ComputerId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => x.EventId).IsUnique();
        builder.HasIndex(x => x.StoragePath).IsUnique();
        builder.HasIndex(x => new { x.EmployeeId, x.TimestampUtc });
        builder.HasIndex(x => new { x.ComputerId, x.TimestampUtc });
        builder.HasIndex(x => new { x.TimestampUtc, x.FileDeletedAtUtc });
    }
}

public sealed class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.ToTable("audit_logs");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedOnAdd();
        builder.Property(x => x.Action).HasMaxLength(100).IsRequired();
        builder.Property(x => x.EntityName).HasMaxLength(100).IsRequired();
        builder.Property(x => x.EntityId).HasMaxLength(100);
        builder.Property(x => x.IpAddress).HasMaxLength(64);
        builder.Property(x => x.Details).HasColumnType("jsonb");
        builder.Property(x => x.TimestampUtc).HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.HasOne<User>().WithMany().HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.SetNull);
        builder.HasIndex(x => x.TimestampUtc);
        builder.HasIndex(x => new { x.UserId, x.TimestampUtc });
        builder.HasIndex(x => new { x.EntityName, x.EntityId });
    }
}
