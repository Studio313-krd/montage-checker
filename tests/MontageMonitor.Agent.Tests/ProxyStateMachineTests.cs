using MontageMonitor.Agent;
using MontageMonitor.Agent.Abstractions;
using MontageMonitor.Agent.Proxy;
using MontageMonitor.Shared.Contracts.Agent;
using MontageMonitor.Shared.States;
using Xunit;

namespace MontageMonitor.Agent.Tests;

public sealed class ProxyStateMachineTests
{
    private static readonly DateTimeOffset Start =
        new(2026, 9, 4, 14, 0, 0, TimeSpan.Zero);

    [Fact]
    public void BusyEncoderWithoutProxyFileDoesNotStartProxy()
    {
        var machine = new ProxyStateMachine();
        var busy = Evidence(cpu: 70, writeRate: 3_000_000, file: null);

        machine.Update(Start, [busy]);
        var result = machine.Update(Start.AddMinutes(1), [busy]);

        Assert.Equal(MachineState.Normal, result.MachineState);
        Assert.Equal(65, result.Telemetry!.DetectionConfidence);
    }

    [Fact]
    public void GrowingVideoInProxyFolderStartsProxyAfterConfirmation()
    {
        var machine = new ProxyStateMachine();
        var growing = Evidence(cpu: 0, writeRate: 0, file: GrowingFile("clip_Proxy.mov"));

        Assert.Equal(MachineState.Normal, machine.Update(Start, [growing]).MachineState);
        var confirmed = machine.Update(Start.AddSeconds(15), [growing]);

        Assert.Equal(MachineState.Proxy, confirmed.MachineState);
        Assert.True(confirmed.Telemetry!.FileNameMatched);
        Assert.True(confirmed.Telemetry.DetectionConfidence >= 70);
    }

    [Fact]
    public void MatchingNameWithoutFileGrowthDoesNotStartProxy()
    {
        var machine = new ProxyStateMachine();
        var unchanged = new WatchedFileSnapshot(
            @"D:\Proxy",
            @"D:\Proxy\clip_Proxy.mov",
            50_000_000,
            false,
            false,
            true);
        var evidence = Evidence(cpu: 80, writeRate: 3_000_000, file: unchanged);

        machine.Update(Start, [evidence]);
        var result = machine.Update(Start.AddMinutes(1), [evidence]);

        Assert.Equal(MachineState.Normal, result.MachineState);
    }

    [Fact]
    public void ProxyFinishUsesHysteresisAndProcessExitIsImmediate()
    {
        var machine = ConfirmedMachine();
        var weak = Evidence(cpu: 0, writeRate: 0, file: null);

        Assert.Equal(MachineState.Proxy, machine.Update(Start.AddSeconds(20), [weak]).MachineState);
        Assert.Equal(MachineState.Proxy, machine.Update(Start.AddSeconds(49), [weak]).MachineState);
        Assert.Equal(MachineState.Normal, machine.Update(Start.AddSeconds(50), [weak]).MachineState);

        var restarted = ConfirmedMachine();
        Assert.Equal(MachineState.Normal, restarted.Update(Start.AddSeconds(20), []).MachineState);
    }

    [Fact]
    public void RenderFolderWinsConflictAndProxyWinsCpuOnlyRender()
    {
        var renderWithFile = new RenderDetectionResult(
            MachineState.Render,
            RenderTelemetry(@"D:\Render\film.mov"));
        var proxy = new ProxyDetectionResult(MachineState.Proxy, ProxyTelemetry());

        Assert.Equal(MachineState.Render, MachineWorkDetection.Select(renderWithFile, proxy).MachineState);

        var renderWithoutFile = new RenderDetectionResult(MachineState.Render, RenderTelemetry(null));
        Assert.Equal(MachineState.Proxy, MachineWorkDetection.Select(renderWithoutFile, proxy).MachineState);
    }

    private static ProxyStateMachine ConfirmedMachine()
    {
        var machine = new ProxyStateMachine();
        var growing = Evidence(cpu: 40, writeRate: 2_000_000, file: GrowingFile("clip_Proxy.mov"));
        machine.Update(Start, [growing]);
        machine.Update(Start.AddSeconds(15), [growing]);
        return machine;
    }

    private static ProxyEvidence Evidence(
        double cpu,
        double writeRate,
        WatchedFileSnapshot? file)
    {
        var rule = new AgentProxyRule(
            Guid.Parse("21000000-0000-0000-0000-000000000001"),
            "Adobe Media Encoder — Proxy",
            "Adobe Media Encoder.exe",
            20,
            1_048_576,
            15,
            30,
            70,
            ["*_Proxy.mov", "*_Proxy.mp4"]);
        var process = new ProcessMetricsSnapshot(
            84,
            "Adobe Media Encoder.exe",
            Start.AddMinutes(-1),
            cpu,
            500_000_000,
            100_000,
            writeRate,
            1);
        return ProxyEvidence.Create(rule, process, file);
    }

    private static WatchedFileSnapshot GrowingFile(string fileName) => new(
        @"D:\Proxy",
        Path.Combine(@"D:\Proxy", fileName),
        50_000_000,
        false,
        true,
        true);

    private static RenderTelemetrySnapshot RenderTelemetry(string? outputFile) => new(
        "Adobe Media Encoder.exe",
        Start.AddMinutes(-1),
        50,
        500_000_000,
        0,
        2_000_000,
        1,
        null,
        outputFile is null ? null : @"D:\Render",
        outputFile,
        50_000_000,
        90,
        "Render подтверждён");

    private static ProxyTelemetrySnapshot ProxyTelemetry() => new(
        "Adobe Media Encoder.exe",
        Start.AddMinutes(-1),
        50,
        500_000_000,
        0,
        2_000_000,
        1,
        @"D:\Proxy",
        @"D:\Proxy\clip_Proxy.mov",
        50_000_000,
        95,
        "Proxy подтверждён",
        true);
}
