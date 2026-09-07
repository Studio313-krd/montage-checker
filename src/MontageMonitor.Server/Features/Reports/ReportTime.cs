namespace MontageMonitor.Server.Features.Reports;

internal readonly record struct ReportDateSlice(DateOnly Date, double DurationSeconds);

internal readonly record struct ReportInterval(DateTimeOffset StartUtc, DateTimeOffset EndUtc);

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
}
