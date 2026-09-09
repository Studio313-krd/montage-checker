using MontageMonitor.Server.Domain;
using MontageMonitor.Server.Features.Reports;
using MontageMonitor.Shared.States;
using Xunit;

namespace MontageMonitor.Agent.Tests;

public sealed class ReportTimeTests
{
    [Fact]
    public void DurationSeconds_ClipsSessionToRequestedRange()
    {
        var rangeStart = new DateTimeOffset(2026, 9, 4, 8, 0, 0, TimeSpan.Zero);
        var rangeEnd = rangeStart.AddHours(8);

        var duration = ReportTime.DurationSeconds(
            rangeStart.AddHours(-1),
            rangeStart.AddHours(2),
            rangeStart,
            rangeEnd);

        Assert.Equal(7_200, duration);
    }

    [Fact]
    public void UnionSeconds_DoesNotDoubleCountOverlappingProductiveIntervals()
    {
        var start = new DateTimeOffset(2026, 9, 4, 8, 0, 0, TimeSpan.Zero);
        var intervals = new[]
        {
            new ReportInterval(start, start.AddHours(2)),
            new ReportInterval(start.AddHours(1), start.AddHours(3)),
            new ReportInterval(start.AddHours(4), start.AddHours(5)),
        };

        Assert.Equal(14_400, ReportTime.UnionSeconds(intervals));
    }

    [Fact]
    public void SplitByLocalDate_UsesRequestedCompanyTimeZone()
    {
        var timeZone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Moscow");
        var start = new DateTimeOffset(2026, 9, 4, 20, 30, 0, TimeSpan.Zero);
        var end = start.AddHours(2);

        var slices = ReportTime.SplitByLocalDate(start, end, start, end, timeZone);

        Assert.Collection(
            slices,
            first =>
            {
                Assert.Equal(new DateOnly(2026, 9, 4), first.Date);
                Assert.Equal(1_800, first.DurationSeconds);
            },
            second =>
            {
                Assert.Equal(new DateOnly(2026, 9, 5), second.Date);
                Assert.Equal(5_400, second.DurationSeconds);
            });
    }

    [Fact]
    public void Clip_RejectsSessionOutsideRange()
    {
        var rangeStart = new DateTimeOffset(2026, 9, 4, 8, 0, 0, TimeSpan.Zero);

        Assert.Null(ReportTime.Clip(
            rangeStart.AddHours(-3),
            rangeStart.AddHours(-2),
            rangeStart,
            rangeStart.AddHours(8)));
    }

    [Fact]
    public void SummarizeHumanStates_DoesNotDoubleCountThreeComputers()
    {
        var start = new DateTimeOffset(2026, 9, 4, 6, 0, 0, TimeSpan.Zero);
        var firstComputer = Guid.NewGuid();
        var secondComputer = Guid.NewGuid();
        var thirdComputer = Guid.NewGuid();
        var sessions = new[]
        {
            Session(firstComputer, HumanState.Idle, start, start.AddHours(2)),
            Session(secondComputer, HumanState.Active, start.AddHours(1), start.AddHours(3)),
            Session(thirdComputer, HumanState.Offline, start, start.AddHours(3)),
        };

        var result = ReportTime.SummarizeHumanStates(sessions, start, start.AddHours(3));

        Assert.Equal(10_800, result.TotalSeconds);
        Assert.Equal(7_200, result.ActiveSeconds);
        Assert.Equal(3_600, result.IdleSeconds);
        Assert.Equal(0, result.OfflineSeconds);
        Assert.Equal(3_600, result.ParallelSeconds);
        Assert.Equal(2, result.ComputerCount);

        static HumanStateSession Session(
            Guid computerId,
            HumanState state,
            DateTimeOffset startedAtUtc,
            DateTimeOffset endedAtUtc) => new()
            {
                EmployeeId = Guid.NewGuid(),
                ComputerId = computerId,
                State = state,
                StartedAtUtc = startedAtUtc,
                EndedAtUtc = endedAtUtc,
            };
    }
}
