using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MontageMonitor.Server.Domain;

namespace MontageMonitor.Server.Infrastructure.Persistence.Configurations;

public sealed class EmployeeConfiguration : IEntityTypeConfiguration<Employee>
{
    public void Configure(EntityTypeBuilder<Employee> builder)
    {
        builder.ToTable("employees", table =>
        {
            table.HasCheckConstraint(
                "ck_employees_screenshot_interval",
                "screenshot_interval_minutes BETWEEN 1 AND 60");
            table.HasCheckConstraint(
                "ck_employees_idle_threshold",
                "idle_threshold_seconds >= 0");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Name).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Login).HasMaxLength(100).IsRequired();
        builder.Property(x => x.NormalizedLogin).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Department).HasMaxLength(200);
        builder.Property(x => x.CreatedAtUtc).HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.Property(x => x.UpdatedAtUtc).HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.HasIndex(x => x.NormalizedLogin).IsUnique();
        builder.HasIndex(x => new { x.IsActive, x.Name });
    }
}

public sealed class ComputerConfiguration : IEntityTypeConfiguration<Computer>
{
    public void Configure(EntityTypeBuilder<Computer> builder)
    {
        builder.ToTable("computers", table =>
            table.HasCheckConstraint("ck_computers_normalized_name", "normalized_name <> ''"));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Name).HasMaxLength(255).IsRequired();
        builder.Property(x => x.NormalizedName).HasMaxLength(255).IsRequired();
        builder.Property(x => x.OperatingSystem).HasMaxLength(255);
        builder.Property(x => x.AgentVersion).HasMaxLength(50);
        builder.Property(x => x.LastIpAddress).HasMaxLength(64);
        builder.Property(x => x.CreatedAtUtc).HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.Property(x => x.UpdatedAtUtc).HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.HasOne<Employee>().WithMany().HasForeignKey(x => x.EmployeeId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.EmployeeId, x.NormalizedName }).IsUnique();
        builder.HasIndex(x => x.LastHeartbeatAtUtc);
    }
}

public sealed class AgentDeviceConfiguration : IEntityTypeConfiguration<AgentDevice>
{
    public void Configure(EntityTypeBuilder<AgentDevice> builder)
    {
        builder.ToTable("agents");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(x => x.InstalledVersion).HasMaxLength(50).IsRequired();
        builder.Property(x => x.CreatedAtUtc).HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.Property(x => x.UpdatedAtUtc).HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.HasOne<Computer>().WithOne().HasForeignKey<AgentDevice>(x => x.ComputerId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => x.ComputerId).IsUnique();
        builder.HasIndex(x => new { x.Status, x.LastSeenAtUtc });
    }
}

public sealed class AgentCredentialConfiguration : IEntityTypeConfiguration<AgentCredential>
{
    public void Configure(EntityTypeBuilder<AgentCredential> builder)
    {
        builder.ToTable("agent_credentials");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.SecretHash).HasMaxLength(512).IsRequired();
        builder.Property(x => x.RevokedAtUtc).IsConcurrencyToken();
        builder.Property(x => x.CreatedAtUtc).HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.Property(x => x.UpdatedAtUtc).HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.HasOne<AgentDevice>().WithMany().HasForeignKey(x => x.AgentId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(x => new { x.AgentId, x.RevokedAtUtc });
    }
}

public sealed class AgentEnrollmentTokenConfiguration
    : IEntityTypeConfiguration<AgentEnrollmentToken>
{
    public void Configure(EntityTypeBuilder<AgentEnrollmentToken> builder)
    {
        builder.ToTable("agent_enrollment_tokens", table =>
            table.HasCheckConstraint(
                "ck_agent_enrollment_tokens_expiry",
                "expires_at_utc > created_at_utc"));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.TokenHash).HasMaxLength(128).IsRequired();
        builder.Property(x => x.UsedAtUtc).IsConcurrencyToken();
        builder.Property(x => x.CreatedAtUtc).HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.Property(x => x.UpdatedAtUtc).HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.HasOne<Employee>().WithMany().HasForeignKey(x => x.EmployeeId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<User>().WithMany().HasForeignKey(x => x.CreatedByUserId)
            .OnDelete(DeleteBehavior.SetNull);
        builder.HasOne<AgentDevice>().WithMany().HasForeignKey(x => x.ConsumedByAgentId)
            .OnDelete(DeleteBehavior.SetNull);
        builder.HasIndex(x => x.TokenHash).IsUnique();
        builder.HasIndex(x => new { x.EmployeeId, x.ExpiresAtUtc, x.UsedAtUtc });
    }
}
