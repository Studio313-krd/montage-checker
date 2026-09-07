using MontageMonitor.Agent.Idle;
using MontageMonitor.Shared.States;
using Xunit;

namespace MontageMonitor.Agent.Tests;

public sealed class IdleStateResolverTests
{
    [Fact]
    public void Resolve_UsesThresholdBoundary()
    {
        Assert.Equal(HumanState.Active, IdleStateResolver.Resolve(299, false, 300).HumanState);
        Assert.Equal(HumanState.Idle, IdleStateResolver.Resolve(300, false, 300).HumanState);
    }

    [Fact]
    public void Resolve_LockedSessionHasPriorityOverIdle()
    {
        var result = IdleStateResolver.Resolve(1_200, true, 300);

        Assert.Equal(HumanState.Locked, result.HumanState);
        Assert.Equal(1_200, result.IdleSeconds);
    }

    [Fact]
    public void Resolve_NormalizesNegativeInput()
    {
        var result = IdleStateResolver.Resolve(-5, false, 300);

        Assert.Equal(HumanState.Active, result.HumanState);
        Assert.Equal(0, result.IdleSeconds);
    }
}
