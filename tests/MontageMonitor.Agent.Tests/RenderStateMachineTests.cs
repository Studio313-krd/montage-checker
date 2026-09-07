using MontageMonitor.Agent.Abstractions;
using MontageMonitor.Agent.Rendering;
using MontageMonitor.Shared.Contracts.Agent;
using MontageMonitor.Shared.States;
using Xunit;

namespace MontageMonitor.Agent.Tests;

public sealed class RenderStateMachineTests
{
    private static readonly DateTimeOffset Start =
        new(2026, 9, 4, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void OpenRenderApplicationWithoutLoadDoesNotStartRender()
    {
        var machine = new RenderStateMachine();

        var result = machine.Update(Start, [Evidence(cpu: 0, writeRate: 0)]);
        var later = machine.Update(Start.AddMinutes(2), [Evidence(cpu: 0, writeRate: 0)]);

        Assert.Equal(MachineState.Normal, result.MachineState);
        Assert.Equal(MachineState.Normal, later.MachineState);
        Assert.Equal(20, later.Telemetry!.DetectionConfidence);
    }

    [Fact]
    public void CpuAndIoWithoutGpuStartRenderAfterConfirmation()
    {
        var machine = new RenderStateMachine();
        var strong = Evidence(cpu: 65, writeRate: 2_000_000, gpu: null);

        Assert.Equal(MachineState.Normal, machine.Update(Start, [strong]).MachineState);
        Assert.Equal(MachineState.Normal, machine.Update(Start.AddSeconds(10), [strong]).MachineState);
        var confirmed = machine.Update(Start.AddSeconds(15), [strong]);

        Assert.Equal(MachineState.Render, confirmed.MachineState);
        Assert.True(confirmed.Telemetry!.DetectionConfidence >= 70);
        Assert.Null(confirmed.Telemetry.GpuLoadPercent);
    }

    [Fact]
    public void CandidateMustRemainStrongForWholeConfirmationWindow()
    {
        var machine = new RenderStateMachine();

        machine.Update(Start, [Evidence(cpu: 65, writeRate: 2_000_000)]);
        machine.Update(Start.AddSeconds(10), [Evidence(cpu: 0, writeRate: 0)]);
        var result = machine.Update(Start.AddSeconds(20), [Evidence(cpu: 65, writeRate: 2_000_000)]);

        Assert.Equal(MachineState.Normal, result.MachineState);
    }

    [Fact]
    public void LowActivityUsesFinishHysteresis()
    {
        var machine = ConfirmedMachine();
        var weak = Evidence(cpu: 0, writeRate: 0);

        Assert.Equal(MachineState.Render, machine.Update(Start.AddSeconds(20), [weak]).MachineState);
        Assert.Equal(MachineState.Render, machine.Update(Start.AddSeconds(49), [weak]).MachineState);
        Assert.Equal(MachineState.Normal, machine.Update(Start.AddSeconds(50), [weak]).MachineState);
    }

    [Fact]
    public void ProcessExitEndsRenderImmediately()
    {
        var machine = ConfirmedMachine();

        var result = machine.Update(Start.AddSeconds(20), []);

        Assert.Equal(MachineState.Normal, result.MachineState);
    }

    private static RenderStateMachine ConfirmedMachine()
    {
        var machine = new RenderStateMachine();
        var strong = Evidence(cpu: 65, writeRate: 2_000_000);
        machine.Update(Start, [strong]);
        machine.Update(Start.AddSeconds(15), [strong]);
        return machine;
    }

    private static RenderEvidence Evidence(double cpu, double writeRate, double? gpu = null)
    {
        var rule = new AgentRenderRule(
            Guid.Parse("20000000-0000-0000-0000-000000000002"),
            "Adobe Media Encoder",
            "Adobe Media Encoder.exe",
            20,
            1_048_576,
            15,
            30,
            70,
            []);
        var process = new ProcessMetricsSnapshot(
            42,
            "Adobe Media Encoder.exe",
            Start.AddMinutes(-1),
            cpu,
            500_000_000,
            100_000,
            writeRate,
            1);
        return RenderEvidence.Create(rule, process, null, new GpuMetricsSnapshot(gpu));
    }
}
