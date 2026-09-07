using Microsoft.EntityFrameworkCore;
using MontageMonitor.Server.Domain;

namespace MontageMonitor.Server.Infrastructure.Persistence;

public sealed class MonitoringDbContext(DbContextOptions<MonitoringDbContext> options)
    : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<UserEmployeeAccess> UserEmployeeAccess => Set<UserEmployeeAccess>();
    public DbSet<Employee> Employees => Set<Employee>();
    public DbSet<Computer> Computers => Set<Computer>();
    public DbSet<AgentDevice> Agents => Set<AgentDevice>();
    public DbSet<AgentCredential> AgentCredentials => Set<AgentCredential>();
    public DbSet<AgentEnrollmentToken> AgentEnrollmentTokens => Set<AgentEnrollmentToken>();
    public DbSet<Heartbeat> Heartbeats => Set<Heartbeat>();
    public DbSet<ActivityEvent> ActivityEvents => Set<ActivityEvent>();
    public DbSet<ApplicationSession> ApplicationSessions => Set<ApplicationSession>();
    public DbSet<HumanStateSession> HumanStateSessions => Set<HumanStateSession>();
    public DbSet<MachineStateSession> MachineStateSessions => Set<MachineStateSession>();
    public DbSet<RenderSession> RenderSessions => Set<RenderSession>();
    public DbSet<Screenshot> Screenshots => Set<Screenshot>();
    public DbSet<ApplicationRule> ApplicationRules => Set<ApplicationRule>();
    public DbSet<RenderRule> RenderRules => Set<RenderRule>();
    public DbSet<WatchedFolder> WatchedFolders => Set<WatchedFolder>();
    public DbSet<SystemSetting> SystemSettings => Set<SystemSetting>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(MonitoringDbContext).Assembly);
    }
}
