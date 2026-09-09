using MontageMonitor.Server.Domain;
using MontageMonitor.Server.Features.Activity;
using MontageMonitor.Shared.States;
using Xunit;

namespace MontageMonitor.Agent.Tests;

public sealed class ActivitySessionBuilderTests
{
    private static readonly Guid EmployeeId = Guid.Parse("31000000-0000-0000-0000-000000000001");
    private static readonly Guid ComputerId = Guid.Parse("31000000-0000-0000-0000-000000000002");
    private static readonly DateTimeOffset Start = new(2026, 9, 7, 8, 0, 0, TimeSpan.Zero);
    private static readonly HeartbeatTiming Timing = new(30, 90);

    [Fact]
    public void Build_SplitsForegroundApplicationAndAppliesClassification()
    {
        var sessions = ActivitySessionBuilder.Build(
            EmployeeId,
            ComputerId,
            [
                Sample(Start, process: "Adobe Premiere Pro.exe", title: "Проект"),
                Sample(Start.AddSeconds(30), process: "chrome.exe", title: "Документация"),
            ],
            new Dictionary<string, ApplicationClassification>
            {
                ["ADOBE PREMIERE PRO.EXE"] = ApplicationClassification.Productive,
            },
            Timing,
            Start);

        Assert.Collection(
            sessions.Applications,
            premiere =>
            {
                Assert.Equal(ApplicationClassification.Productive, premiere.Classification);
                Assert.Equal(Start.AddSeconds(30), premiere.EndedAtUtc);
            },
            chrome =>
            {
                Assert.Equal(ApplicationClassification.Neutral, chrome.Classification);
                Assert.Null(chrome.EndedAtUtc);
            });
    }

    [Fact]
    public void Build_InsertsOfflineSessionForHeartbeatGap()
    {
        var sessions = ActivitySessionBuilder.Build(
            EmployeeId,
            ComputerId,
            [
                Sample(Start, process: "premiere.exe"),
                Sample(Start.AddMinutes(5), process: "premiere.exe"),
            ],
            new Dictionary<string, ApplicationClassification>(),
            Timing,
            Start);

        var offline = Assert.Single(sessions.HumanStates, item => item.State == HumanState.Offline);
        Assert.Equal(Start.AddSeconds(30), offline.StartedAtUtc);
        Assert.Equal(Start.AddMinutes(5), offline.EndedAtUtc);
        Assert.Equal(Start.AddSeconds(30), sessions.Applications[0].EndedAtUtc);
        Assert.Null(sessions.Applications[1].EndedAtUtc);
    }

    [Fact]
    public void Build_KeepsHumanIdleSeparateFromRenderAndAggregatesCpu()
    {
        var sessions = ActivitySessionBuilder.Build(
            EmployeeId,
            ComputerId,
            [
                Sample(Start, HumanState.Idle, MachineState.Render, renderCpu: 30),
                Sample(Start.AddSeconds(30), HumanState.Idle, MachineState.Render, renderCpu: 70),
                Sample(Start.AddSeconds(60), HumanState.Active, MachineState.Normal),
            ],
            new Dictionary<string, ApplicationClassification>(),
            Timing,
            Start);

        var idle = Assert.Single(sessions.HumanStates, item => item.State == HumanState.Idle);
        var renderState = Assert.Single(sessions.MachineStates, item => item.State == MachineState.Render);
        var render = Assert.Single(sessions.Renders);

        Assert.Equal(Start.AddSeconds(60), idle.EndedAtUtc);
        Assert.Equal(Start.AddSeconds(60), renderState.EndedAtUtc);
        Assert.Equal(Start.AddSeconds(60), render.EndedAtUtc);
        Assert.Equal(50, render.AverageCpuPercent);
        Assert.Equal(70, render.MaxCpuPercent);
    }

    [Fact]
    public void Build_ClosesSessionsWhenOperatorChangesOnSameComputer()
    {
        var secondEmployee = Guid.NewGuid();
        var sessions = ActivitySessionBuilder.Build(
            ComputerId,
            [
                Sample(Start, process: "premiere.exe") with { EmployeeId = EmployeeId },
                Sample(Start.AddSeconds(30), process: "premiere.exe") with { EmployeeId = secondEmployee },
            ],
            new Dictionary<string, ApplicationClassification>(),
            Timing,
            Start);

        Assert.Collection(
            sessions.HumanStates,
            first =>
            {
                Assert.Equal(EmployeeId, first.EmployeeId);
                Assert.Equal(Start.AddSeconds(30), first.EndedAtUtc);
            },
            second =>
            {
                Assert.Equal(secondEmployee, second.EmployeeId);
                Assert.Null(second.EndedAtUtc);
            });
        Assert.Collection(
            sessions.Applications,
            first => Assert.Equal(EmployeeId, first.EmployeeId),
            second => Assert.Equal(secondEmployee, second.EmployeeId));
    }

    private static ActivitySample Sample(
        DateTimeOffset timestamp,
        HumanState humanState = HumanState.Active,
        MachineState machineState = MachineState.Normal,
        string? process = null,
        string? title = null,
        double? renderCpu = null) => new(
            timestamp,
            humanState,
            machineState,
            process,
            process is null ? null : @"C:\Apps\" + process,
            title,
            machineState == MachineState.Render ? "Adobe Media Encoder.exe" : null,
            machineState == MachineState.Render ? Start.AddMinutes(-1) : null,
            renderCpu,
            machineState == MachineState.Render ? 500_000_000 : null,
            null,
            machineState == MachineState.Render ? 2_000_000 : null,
            machineState == MachineState.Render ? 1 : null,
            null,
            machineState == MachineState.Render ? @"D:\Render" : null,
            machineState == MachineState.Render ? @"D:\Render\film.mp4" : null,
            machineState == MachineState.Render ? 10_000_000 : null,
            machineState == MachineState.Render ? (byte)90 : null,
            machineState == MachineState.Render ? "Encoder + рост выходного файла" : null);
}
