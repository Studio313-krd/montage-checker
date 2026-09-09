using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MontageMonitor.Server.Domain;
using MontageMonitor.Server.Infrastructure.Persistence;

namespace MontageMonitor.Server.Features.Agents;

public sealed class OperatorSelectionOptions
{
    public const string SectionName = "Company";

    public string TimeZone { get; set; } = "Europe/Moscow";

    public int DailySelectionHour { get; set; } = 6;
}

public sealed class OperatorSessionService(
    MonitoringDbContext dbContext,
    IOptions<OperatorSelectionOptions> options)
{
    private readonly TimeZoneInfo _timeZone = ResolveTimeZone(options.Value.TimeZone);
    private readonly int _selectionHour = Math.Clamp(options.Value.DailySelectionHour, 0, 23);

    public DateTimeOffset GetNextSelectionBoundaryUtc(DateTimeOffset nowUtc)
    {
        var localNow = TimeZoneInfo.ConvertTime(nowUtc, _timeZone);
        var boundaryDate = localNow.TimeOfDay < TimeSpan.FromHours(_selectionHour)
            ? localNow.Date
            : localNow.Date.AddDays(1);
        var localBoundary = DateTime.SpecifyKind(
            boundaryDate.AddHours(_selectionHour),
            DateTimeKind.Unspecified);
        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(localBoundary, _timeZone));
    }

    public async Task<AgentOperatorSession?> FindValidSessionAsync(
        Guid sessionId,
        Guid agentId,
        Guid computerId,
        DateTimeOffset occurredAtUtc,
        CancellationToken cancellationToken) =>
        await dbContext.AgentOperatorSessions.AsNoTracking()
            .SingleOrDefaultAsync(item =>
                    item.Id == sessionId &&
                    item.AgentId == agentId &&
                    item.ComputerId == computerId &&
                    item.StartedAtUtc <= occurredAtUtc &&
                    item.ExpiresAtUtc > occurredAtUtc &&
                    (item.EndedAtUtc == null || item.EndedAtUtc > occurredAtUtc),
                cancellationToken);

    public static bool RequiresOperatorSession(string agentVersion) =>
        Version.TryParse(agentVersion, out var version) && version >= new Version(0, 9, 0);

    private static TimeZoneInfo ResolveTimeZone(string value)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(value);
        }
        catch (TimeZoneNotFoundException)
        {
            throw new InvalidOperationException($"Неизвестный часовой пояс Company:TimeZone: {value}");
        }
        catch (InvalidTimeZoneException)
        {
            throw new InvalidOperationException($"Некорректный часовой пояс Company:TimeZone: {value}");
        }
    }
}
