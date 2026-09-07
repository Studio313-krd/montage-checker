using MontageMonitor.Server.Domain;
using MontageMonitor.Server.Features.Dashboard;
using MontageMonitor.Shared.States;
using Xunit;

namespace MontageMonitor.Agent.Tests;

public sealed class DashboardStateResolverTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 4, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void OnlineAgentWithFreshHeartbeatIsOnline()
    {
        var result = DashboardStateResolver.IsOnline(
            AgentStatus.Online,
            Now.AddSeconds(-89),
            Now,
            90);

        Assert.True(result);
    }

    [Theory]
    [InlineData(AgentStatus.Offline, -10)]
    [InlineData(AgentStatus.Revoked, -10)]
    [InlineData(AgentStatus.Online, -91)]
    public void OfflineRevokedOrStaleAgentIsOffline(AgentStatus status, int lastSeenSeconds)
    {
        var result = DashboardStateResolver.IsOnline(
            status,
            Now.AddSeconds(lastSeenSeconds),
            Now,
            90);

        Assert.False(result);
    }

    [Fact]
    public void MachineWorkControlsPrimaryDurationWhileOnline()
    {
        var humanStarted = Now.AddMinutes(-40);
        var renderStarted = Now.AddMinutes(-12);

        Assert.Equal(
            renderStarted,
            DashboardStateResolver.PrimaryStateStartedAt(
                true,
                MachineState.Render,
                humanStarted,
                renderStarted));
        Assert.Equal(
            humanStarted,
            DashboardStateResolver.PrimaryStateStartedAt(
                true,
                MachineState.Normal,
                humanStarted,
                renderStarted));
    }
}
