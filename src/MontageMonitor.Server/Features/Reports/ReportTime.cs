using MontageMonitor.Server.Domain;
using MontageMonitor.Shared.States;

namespace MontageMonitor.Server.Features.Reports;

internal readonly record struct ReportDateSlice(DateOnly Date, double DurationSeconds);

internal readonly record struct ReportInterval(DateTimeOffset StartUtc, DateTimeOffset EndUtc);

internal readonly record struct EmployeeHumanTime(
    double TotalSeconds,
    double ActiveSeconds,
    double IdleSeconds,
    double LockedSeconds,
    double OfflineSeconds,
    double ParallelSeconds,
    int ComputerCount);

internal static class ReportTime
{
    public static ReportInterval? Clip(
        DateTimeOffset startedAtUtc,
        DateTimeOffset? endedAtUtc,
        DateTimeOffset rangeStartUtc,
        DateTimeOffset rangeEndUtc)
    {
        var start = startedAtUtc > rangeStartUtc ? startedAtUtc : rangeStartUtc;
        var end = endedAtUtc.HasValue && endedAtUtc.Value < rangeEndUtc
            ? endedAtUtc.Value
            : rangeEndUtc;
        return end > start ? new ReportInterval(start, end) : null;
    }

    public static double DurationSeconds(
        DateTimeOffset startedAtUtc,
        DateTimeOffset? endedAtUtc,
        DateTimeOffset rangeStartUtc,
        DateTimeOffset rangeEndUtc) =>
        Clip(startedAtUtc, endedAtUtc, rangeStartUtc, rangeEndUtc) is { } interval
            ? (interval.EndUtc - interval.StartUtc).TotalSeconds
            : 0;

    public static IReadOnlyList<ReportDateSlice> SplitByLocalDate(
        DateTimeOffset startedAtUtc,
        DateTimeOffset? endedAtUtc,
        DateTimeOffset rangeStartUtc,
        DateTimeOffset rangeEndUtc,
        TimeZoneInfo timeZone)
    {
        var clipped = Clip(startedAtUtc, endedAtUtc, rangeStartUtc, rangeEndUtc);
        if (clipped is null)
        {
            return [];
        }

        var result = new List<ReportDateSlice>();
        var cursor = clipped.Value.StartUtc;
        while (cursor < clipped.Value.EndUtc)
        {
            var localDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(cursor, timeZone).DateTime);
            var nextLocalMidnight = localDate.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
            var nextUtc = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(nextLocalMidnight, timeZone));
            var sliceEnd = nextUtc < clipped.Value.EndUtc ? nextUtc : clipped.Value.EndUtc;
            result.Add(new ReportDateSlice(localDate, (sliceEnd - cursor).TotalSeconds));
            cursor = sliceEnd;
        }

        return result;
    }

    public static double UnionSeconds(IEnumerable<ReportInterval> intervals)
    {
        var ordered = intervals.OrderBy(item => item.StartUtc).ThenBy(item => item.EndUtc).ToList();
        if (ordered.Count == 0)
        {
            return 0;
        }

        var total = TimeSpan.Zero;
        var currentStart = ordered[0].StartUtc;
        var currentEnd = ordered[0].EndUtc;
        foreach (var interval in ordered.Skip(1))
        {
            if (interval.StartUtc <= currentEnd)
            {
                if (interval.EndUtc > currentEnd)
                {
                    currentEnd = interval.EndUtc;
                }

                continue;
            }

            total += currentEnd - currentStart;
            currentStart = interval.StartUtc;
            currentEnd = interval.EndUtc;
        }

        total += currentEnd - currentStart;
        return total.TotalSeconds;
    }

    public static EmployeeHumanTime SummarizeHumanStates(
        IEnumerable<HumanStateSession> sessions,
        DateTimeOffset rangeStartUtc,
        DateTimeOffset rangeEndUtc)
    {
        var intervals = sessions
            .Select(item => (Session: item, Interval: Clip(
                item.StartedAtUtc,
                item.EndedAtUtc,
                rangeStartUtc,
                rangeEndUtc)))
            .Where(item => item.Interval.HasValue)
            .Select(item => (item.Session.ComputerId, item.Session.State, Interval: item.Interval!.Value))
            .ToList();
        if (intervals.Count == 0)
        {
            return new EmployeeHumanTime(0, 0, 0, 0, 0, 0, 0);
        }

        var boundaries = intervals
            .SelectMany(item => new[] { item.Interval.StartUtc, item.Interval.EndUtc })
            .Distinct()
            .Order()
            .ToList();
        double active = 0;
        double idle = 0;
        double locked = 0;
        double offline = 0;
        double parallel = 0;
        for (var index = 0; index < boundaries.Count - 1; index++)
        {
            var start = boundaries[index];
            var end = boundaries[index + 1];
            if (end <= start)
            {
                continue;
            }

            var midpoint = start + TimeSpan.FromTicks((end - start).Ticks / 2);
            var current = intervals.Where(item =>
                    item.Interval.StartUtc <= midpoint && item.Interval.EndUtc > midpoint)
                .ToList();
            if (current.Count == 0)
            {
                continue;
            }

            var seconds = (end - start).TotalSeconds;
            if (current.Any(item => item.State == HumanState.Active))
            {
                active += seconds;
            }
            else if (current.Any(item => item.State == HumanState.Idle))
            {
                idle += seconds;
            }
            else if (current.Any(item => item.State == HumanState.Locked))
            {
                locked += seconds;
            }
            else
            {
                offline += seconds;
            }

            if (current.Where(item => item.State != HumanState.Offline)
                    .Select(item => item.ComputerId)
                    .Distinct()
                    .Take(2)
                    .Count() > 1)
            {
                parallel += seconds;
            }
        }

        return new EmployeeHumanTime(
            active + idle + locked,
            active,
            idle,
            locked,
            offline,
            parallel,
            intervals
                .Where(item => item.State != HumanState.Offline)
                .Select(item => item.ComputerId)
                .Distinct()
                .Count());
    }
}
