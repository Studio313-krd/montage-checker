using MontageMonitor.Server.Features.Reports;
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
}
