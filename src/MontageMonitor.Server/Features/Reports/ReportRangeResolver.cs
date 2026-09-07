namespace MontageMonitor.Server.Features.Reports;

internal readonly record struct ReportRange(
    DateTimeOffset StartUtc,
    DateTimeOffset EndUtc,
    DateTimeOffset EffectiveEndUtc,
    DateTimeOffset NowUtc,
    TimeZoneInfo TimeZone);

internal sealed record ReportRangeResult(
    ReportRange? Range,
    Dictionary<string, string[]>? Error);

internal static class ReportRangeResolver
{
    public static ReportRangeResult Resolve(
        DateTimeOffset? fromUtc,
        DateTimeOffset? toUtc,
        string? timeZoneId,
        DateTimeOffset nowUtc)
    {
        var end = (toUtc ?? nowUtc).ToUniversalTime();
        var start = (fromUtc ?? end.AddDays(-7)).ToUniversalTime();
        if (start >= end || end - start > TimeSpan.FromDays(366))
        {
            return new ReportRangeResult(null, new Dictionary<string, string[]>
            {
                ["range"] = ["Диапазон должен быть положительным и не превышать 366 дней."],
            });
        }

        if (timeZoneId is { Length: > 100 } ||
            timeZoneId?.Contains("..", StringComparison.Ordinal) is true ||
            timeZoneId?.Any(character => !(char.IsLetterOrDigit(character) ||
                                            character is ' ' or '/' or '_' or '-' or '+' or '.')) is true)
        {
            return InvalidTimeZone();
        }

        TimeZoneInfo timeZone;
        try
        {
            timeZone = TimeZoneInfo.FindSystemTimeZoneById(
                string.IsNullOrWhiteSpace(timeZoneId) ? TimeZoneInfo.Utc.Id : timeZoneId);
        }
        catch (TimeZoneNotFoundException)
        {
            return InvalidTimeZone();
        }
        catch (InvalidTimeZoneException)
        {
            return InvalidTimeZone();
        }

        var effectiveEnd = end < nowUtc ? end : nowUtc;
        return new ReportRangeResult(new ReportRange(start, end, effectiveEnd, nowUtc, timeZone), null);
    }

    private static ReportRangeResult InvalidTimeZone() => new(null, new Dictionary<string, string[]>
    {
        ["timeZone"] = ["Неизвестный часовой пояс."],
    });
}
