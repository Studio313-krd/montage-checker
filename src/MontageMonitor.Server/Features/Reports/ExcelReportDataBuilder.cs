using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using MontageMonitor.Server.Domain;
using MontageMonitor.Server.Features.Employees;
using MontageMonitor.Server.Infrastructure.Persistence;
using MontageMonitor.Shared.States;

namespace MontageMonitor.Server.Features.Reports;

internal sealed class ExcelReportDataBuilder(
    MonitoringDbContext dbContext,
    EmployeeAccessService employeeAccessService)
{
    public async Task<ExcelReportBuildResult> BuildAsync(
        ReportRange range,
        IReadOnlyCollection<Guid> requestedEmployeeIds,
        ClaimsPrincipal principal,
        CancellationToken cancellationToken)
    {
        var visibleEmployees = await employeeAccessService.ApplyVisibility(
                dbContext.Employees.AsNoTracking(), principal)
            .OrderBy(item => item.Name)
            .ToListAsync(cancellationToken);
        var requested = requestedEmployeeIds.Distinct().ToHashSet();
        if (requested.Count > 0 && requested.Any(id => visibleEmployees.All(employee => employee.Id != id)))
        {
            return new ExcelReportBuildResult(null, true);
        }

        var employees = requested.Count == 0
            ? visibleEmployees
            : visibleEmployees.Where(item => requested.Contains(item.Id)).ToList();
        var employeeIds = employees.Select(item => item.Id).ToArray();
        var applications = await dbContext.ApplicationSessions.AsNoTracking()
            .Where(item => employeeIds.Contains(item.EmployeeId) && item.StartedAtUtc < range.EndUtc &&
                           (item.EndedAtUtc == null || item.EndedAtUtc > range.StartUtc))
            .ToListAsync(cancellationToken);
        var human = await dbContext.HumanStateSessions.AsNoTracking()
            .Where(item => employeeIds.Contains(item.EmployeeId) && item.StartedAtUtc < range.EndUtc &&
                           (item.EndedAtUtc == null || item.EndedAtUtc > range.StartUtc))
            .ToListAsync(cancellationToken);
        var machine = await dbContext.MachineStateSessions.AsNoTracking()
            .Where(item => employeeIds.Contains(item.EmployeeId) && item.StartedAtUtc < range.EndUtc &&
                           (item.EndedAtUtc == null || item.EndedAtUtc > range.StartUtc))
            .ToListAsync(cancellationToken);
        var renders = await dbContext.RenderSessions.AsNoTracking()
            .Where(item => employeeIds.Contains(item.EmployeeId) && item.StartedAtUtc < range.EndUtc &&
                           (item.EndedAtUtc == null || item.EndedAtUtc > range.StartUtc))
            .ToListAsync(cancellationToken);
        var screenshots = await dbContext.Screenshots.AsNoTracking()
            .Where(item => employeeIds.Contains(item.EmployeeId) &&
                           item.TimestampUtc >= range.StartUtc && item.TimestampUtc < range.EndUtc)
            .ToListAsync(cancellationToken);
        var computerIds = applications.Select(item => item.ComputerId)
            .Concat(human.Select(item => item.ComputerId))
            .Concat(machine.Select(item => item.ComputerId))
            .Distinct()
            .ToArray();
        var computers = await dbContext.Computers.AsNoTracking()
            .Where(item => computerIds.Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, item => item.Name, cancellationToken);
        var applicationRules = await dbContext.ApplicationRules.AsNoTracking()
            .Where(item => item.IsEnabled)
            .ToDictionaryAsync(item => item.NormalizedProcessName, item => item.DisplayName, cancellationToken);
        var employeeNames = employees.ToDictionary(item => item.Id, item => item.Name);

        string ApplicationName(string processName) =>
            applicationRules.GetValueOrDefault(processName.ToUpperInvariant()) ??
            Path.GetFileNameWithoutExtension(processName);

        var data = new ExcelReportData(
            range,
            BuildSummary(employees, applications, human, machine, screenshots, range),
            BuildTimeline(applications, human, machine, employeeNames, computers, range, ApplicationName),
            BuildApplications(applications, employeeNames, computers, range, ApplicationName),
            BuildRenders(renders, employeeNames, computers, range),
            BuildIdle(human, employeeNames, computers, range));
        return new ExcelReportBuildResult(data, false);
    }

    private static IReadOnlyList<ExcelSummaryRow> BuildSummary(
        IReadOnlyCollection<Employee> employees,
        IReadOnlyCollection<ApplicationSession> applications,
        IReadOnlyCollection<HumanStateSession> human,
        IReadOnlyCollection<MachineStateSession> machine,
        IReadOnlyCollection<Screenshot> screenshots,
        ReportRange range)
    {
        var result = new List<ExcelSummaryRow>();
        foreach (var employee in employees)
        {
            var employeeApplications = applications.Where(item => item.EmployeeId == employee.Id).ToList();
            var employeeHuman = human.Where(item => item.EmployeeId == employee.Id).ToList();
            var employeeMachine = machine.Where(item => item.EmployeeId == employee.Id).ToList();
            var employeeScreenshots = screenshots.Where(item => item.EmployeeId == employee.Id).ToList();
            var dates = employeeApplications.SelectMany(item => Dates(item.StartedAtUtc, item.EndedAtUtc))
                .Concat(employeeHuman.SelectMany(item => Dates(item.StartedAtUtc, item.EndedAtUtc)))
                .Concat(employeeMachine.SelectMany(item => Dates(item.StartedAtUtc, item.EndedAtUtc)))
                .Concat(employeeScreenshots.Select(item =>
                    DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(item.TimestampUtc, range.TimeZone).DateTime)))
                .Distinct()
                .Order()
                .ToList();
            foreach (var date in dates)
            {
                var (dayStartUtc, dayEndUtc) = DayBounds(date, range.TimeZone);
                var start = dayStartUtc > range.StartUtc ? dayStartUtc : range.StartUtc;
                var end = dayEndUtc < range.EffectiveEndUtc ? dayEndUtc : range.EffectiveEndUtc;
                if (end <= start)
                {
                    continue;
                }

                var tracked = employeeHuman.Select(item => ReportTime.Clip(
                        item.StartedAtUtc, item.EndedAtUtc, start, end))
                    .Where(item => item.HasValue).Select(item => item!.Value).ToList();
                var machineIntervals = employeeMachine.Select(item => ReportTime.Clip(
                        item.StartedAtUtc, item.EndedAtUtc, start, end))
                    .Where(item => item.HasValue).Select(item => item!.Value).ToList();
                var applicationIntervals = employeeApplications.Select(item => ReportTime.Clip(
                        item.StartedAtUtc, item.EndedAtUtc, start, end))
                    .Where(item => item.HasValue).Select(item => item!.Value).ToList();
                var observed = tracked.Concat(machineIntervals).Concat(applicationIntervals).ToList();
                var productive = employeeHuman.Where(item => item.State == HumanState.Active)
                    .Select(item => ReportTime.Clip(item.StartedAtUtc, item.EndedAtUtc, start, end))
                    .Concat(employeeMachine.Where(item => item.State != MachineState.Normal)
                        .Select(item => ReportTime.Clip(item.StartedAtUtc, item.EndedAtUtc, start, end)))
                    .Where(item => item.HasValue).Select(item => item!.Value);
                var humanTime = ReportTime.SummarizeHumanStates(employeeHuman, start, end);
                result.Add(new ExcelSummaryRow(
                    employee.Id,
                    employee.Name,
                    date,
                    observed.Count == 0 ? null : observed.Min(item => item.StartUtc),
                    observed.Count == 0 ? null : observed.Max(item => item.EndUtc),
                    humanTime.TotalSeconds,
                    ReportTime.UnionSeconds(productive),
                    humanTime.ActiveSeconds,
                    StateSeconds(employeeMachine, MachineState.Render, start, end),
                    StateSeconds(employeeMachine, MachineState.Proxy, start, end),
                    StateSeconds(employeeMachine, MachineState.BackgroundProcessing, start, end),
                    humanTime.IdleSeconds,
                    humanTime.LockedSeconds,
                    humanTime.OfflineSeconds,
                    humanTime.ComputerCount,
                    humanTime.ParallelSeconds,
                    employeeScreenshots.Count(item => item.TimestampUtc >= start && item.TimestampUtc < end)));
            }

            IEnumerable<DateOnly> Dates(DateTimeOffset sessionStart, DateTimeOffset? sessionEnd) =>
                ReportTime.SplitByLocalDate(
                    sessionStart,
                    sessionEnd,
                    range.StartUtc,
                    range.EffectiveEndUtc,
                    range.TimeZone).Select(item => item.Date);
        }

        return result;
    }

    private static IReadOnlyList<ExcelTimelineRow> BuildTimeline(
        IReadOnlyCollection<ApplicationSession> applications,
        IReadOnlyCollection<HumanStateSession> human,
        IReadOnlyCollection<MachineStateSession> machine,
        IReadOnlyDictionary<Guid, string> employeeNames,
        IReadOnlyDictionary<Guid, string> computerNames,
        ReportRange range,
        Func<string, string> applicationName)
    {
        var result = new List<ExcelTimelineRow>();
        var keys = applications.Select(item => (item.EmployeeId, item.ComputerId))
            .Concat(human.Select(item => (item.EmployeeId, item.ComputerId)))
            .Concat(machine.Select(item => (item.EmployeeId, item.ComputerId)))
            .Distinct();
        foreach (var key in keys)
        {
            var keyApplications = applications.Where(item =>
                item.EmployeeId == key.EmployeeId && item.ComputerId == key.ComputerId).ToList();
            var keyHuman = human.Where(item =>
                item.EmployeeId == key.EmployeeId && item.ComputerId == key.ComputerId).ToList();
            var keyMachine = machine.Where(item =>
                item.EmployeeId == key.EmployeeId && item.ComputerId == key.ComputerId).ToList();
            var boundaries = keyApplications.SelectMany(item => Boundaries(item.StartedAtUtc, item.EndedAtUtc))
                .Concat(keyHuman.SelectMany(item => Boundaries(item.StartedAtUtc, item.EndedAtUtc)))
                .Concat(keyMachine.SelectMany(item => Boundaries(item.StartedAtUtc, item.EndedAtUtc)))
                .Distinct()
                .Order()
                .ToList();
            for (var index = 0; index < boundaries.Count - 1; index++)
            {
                var start = boundaries[index];
                var end = boundaries[index + 1];
                if (end <= start)
                {
                    continue;
                }

                var midpoint = start + TimeSpan.FromTicks((end - start).Ticks / 2);
                var application = keyApplications.FirstOrDefault(item => Contains(item, midpoint));
                var humanState = keyHuman.FirstOrDefault(item => Contains(item, midpoint));
                var machineState = keyMachine.FirstOrDefault(item => Contains(item, midpoint));
                if (application is null && humanState is null && machineState is null)
                {
                    continue;
                }

                var row = new ExcelTimelineRow(
                    key.EmployeeId,
                    employeeNames[key.EmployeeId],
                    key.ComputerId,
                    computerNames.GetValueOrDefault(key.ComputerId) ?? "Неизвестный компьютер",
                    start,
                    end,
                    humanState?.State,
                    machineState?.State,
                    application is null ? null : applicationName(application.ProcessName),
                    application?.WindowTitle);
                if (result.LastOrDefault() is { } previous && CanMerge(previous, row))
                {
                    result[^1] = previous with { EndedAtUtc = row.EndedAtUtc };
                }
                else
                {
                    result.Add(row);
                }
            }

            IEnumerable<DateTimeOffset> Boundaries(DateTimeOffset sessionStart, DateTimeOffset? sessionEnd)
            {
                var clipped = ReportTime.Clip(
                    sessionStart, sessionEnd, range.StartUtc, range.EffectiveEndUtc);
                return clipped is null ? [] : [clipped.Value.StartUtc, clipped.Value.EndUtc];
            }
        }

        return result.OrderBy(item => item.EmployeeName)
            .ThenBy(item => item.ComputerName)
            .ThenBy(item => item.StartedAtUtc)
            .ToList();
    }

    private static IReadOnlyList<ExcelApplicationRow> BuildApplications(
        IReadOnlyCollection<ApplicationSession> applications,
        IReadOnlyDictionary<Guid, string> employeeNames,
        IReadOnlyDictionary<Guid, string> computerNames,
        ReportRange range,
        Func<string, string> applicationName) =>
        applications.SelectMany(session => ReportTime.SplitByLocalDate(
                session.StartedAtUtc,
                session.EndedAtUtc,
                range.StartUtc,
                range.EffectiveEndUtc,
                range.TimeZone)
            .Select(slice => new
            {
                session.EmployeeId,
                EmployeeName = employeeNames[session.EmployeeId],
                session.ComputerId,
                ComputerName = computerNames.GetValueOrDefault(session.ComputerId) ?? "Неизвестный компьютер",
                slice.Date,
                Application = applicationName(session.ProcessName),
                session.Classification,
                slice.DurationSeconds,
            }))
            .GroupBy(item => new
            {
                item.EmployeeId,
                item.EmployeeName,
                item.ComputerId,
                item.ComputerName,
                item.Date,
                item.Application,
                item.Classification,
            })
            .Select(group => new ExcelApplicationRow(
                group.Key.EmployeeId,
                group.Key.EmployeeName,
                group.Key.ComputerId,
                group.Key.ComputerName,
                group.Key.Date,
                group.Key.Application,
                group.Key.Classification,
                group.Sum(item => item.DurationSeconds)))
            .OrderBy(item => item.EmployeeName)
            .ThenBy(item => item.ComputerName)
            .ThenBy(item => item.Date)
            .ThenByDescending(item => item.DurationSeconds)
            .ToList();

    private static IReadOnlyList<ExcelRenderRow> BuildRenders(
        IReadOnlyCollection<RenderSession> renders,
        IReadOnlyDictionary<Guid, string> employeeNames,
        IReadOnlyDictionary<Guid, string> computerNames,
        ReportRange range) => renders
        .Select(item => (Session: item, Interval: ReportTime.Clip(
            item.StartedAtUtc, item.EndedAtUtc, range.StartUtc, range.EffectiveEndUtc)))
        .Where(item => item.Interval.HasValue)
        .Select(item => new ExcelRenderRow(
            item.Session.EmployeeId,
            employeeNames[item.Session.EmployeeId],
            item.Session.ComputerId,
            computerNames.GetValueOrDefault(item.Session.ComputerId) ?? "Неизвестный компьютер",
            item.Session.Type,
            item.Session.Program,
            item.Interval!.Value.StartUtc,
            item.Interval.Value.EndUtc,
            item.Session.OutputFile ?? item.Session.OutputFolder,
            item.Session.DetectionConfidence,
            item.Session.DetectionReason))
        .OrderBy(item => item.EmployeeName)
        .ThenBy(item => item.ComputerName)
        .ThenBy(item => item.StartedAtUtc)
        .ToList();

    private static IReadOnlyList<ExcelIdleRow> BuildIdle(
        IReadOnlyCollection<HumanStateSession> human,
        IReadOnlyDictionary<Guid, string> employeeNames,
        IReadOnlyDictionary<Guid, string> computerNames,
        ReportRange range) => human.Where(item => item.State == HumanState.Idle)
        .Select(item => (Session: item, Interval: ReportTime.Clip(
            item.StartedAtUtc, item.EndedAtUtc, range.StartUtc, range.EffectiveEndUtc)))
        .Where(item => item.Interval.HasValue)
        .Select(item => new ExcelIdleRow(
            item.Session.EmployeeId,
            employeeNames[item.Session.EmployeeId],
            item.Session.ComputerId,
            computerNames.GetValueOrDefault(item.Session.ComputerId) ?? "Неизвестный компьютер",
            item.Interval!.Value.StartUtc,
            item.Interval.Value.EndUtc))
        .OrderBy(item => item.EmployeeName)
        .ThenBy(item => item.ComputerName)
        .ThenBy(item => item.StartedAtUtc)
        .ToList();

    private static double StateSeconds<TSession, TState>(
        IEnumerable<TSession> sessions,
        TState state,
        DateTimeOffset start,
        DateTimeOffset end)
        where TState : struct, Enum => sessions.Sum(session => session switch
        {
            HumanStateSession item when EqualityComparer<TState>.Default.Equals(
                (TState)(object)item.State, state) => ReportTime.DurationSeconds(
                    item.StartedAtUtc, item.EndedAtUtc, start, end),
            MachineStateSession item when EqualityComparer<TState>.Default.Equals(
                (TState)(object)item.State, state) => ReportTime.DurationSeconds(
                    item.StartedAtUtc, item.EndedAtUtc, start, end),
            _ => 0,
        });

    private static (DateTimeOffset StartUtc, DateTimeOffset EndUtc) DayBounds(
        DateOnly date,
        TimeZoneInfo timeZone)
    {
        var start = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        var end = date.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        return (
            new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(start, timeZone)),
            new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(end, timeZone)));
    }

    private static bool Contains(ApplicationSession session, DateTimeOffset instant) =>
        session.StartedAtUtc <= instant && (session.EndedAtUtc is null || session.EndedAtUtc > instant);

    private static bool Contains(HumanStateSession session, DateTimeOffset instant) =>
        session.StartedAtUtc <= instant && (session.EndedAtUtc is null || session.EndedAtUtc > instant);

    private static bool Contains(MachineStateSession session, DateTimeOffset instant) =>
        session.StartedAtUtc <= instant && (session.EndedAtUtc is null || session.EndedAtUtc > instant);

    private static bool CanMerge(ExcelTimelineRow left, ExcelTimelineRow right) =>
        left.EmployeeId == right.EmployeeId &&
        left.ComputerId == right.ComputerId &&
        left.EndedAtUtc == right.StartedAtUtc &&
        left.HumanState == right.HumanState &&
        left.MachineState == right.MachineState &&
        left.Application == right.Application &&
        left.WindowTitle == right.WindowTitle;
}
