using Microsoft.EntityFrameworkCore;
using MontageMonitor.Server.Domain;
using MontageMonitor.Server.Features.Employees;
using MontageMonitor.Server.Infrastructure.Persistence;
using MontageMonitor.Server.Infrastructure.Security;
using MontageMonitor.Shared.States;

namespace MontageMonitor.Server.Features.Reports;

public static class ReportEndpoints
{
    public static IEndpointRouteBuilder MapReportEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var reports = endpoints.MapGroup("/api/reports")
            .RequireAuthorization(SecurityPolicies.ViewEmployees)
            .WithTags("Отчёты");
        reports.MapGet("/summary", GetSummaryAsync)
            .WithName("GetSummaryReport").WithSummary("Получить сводку времени сотрудников");
        reports.MapGet("/applications", GetApplicationsAsync)
            .WithName("GetApplicationReport").WithSummary("Получить отчёт по foreground-приложениям");
        reports.MapGet("/renders", GetRendersAsync)
            .WithName("GetRenderReport").WithSummary("Получить отчёт по render, proxy и background");
        reports.MapGet("/export.xlsx", ExcelReportEndpoints.ExportAsync)
            .RequireAuthorization(SecurityPolicies.DownloadReports)
            .WithName("DownloadExcelReport")
            .WithSummary("Скачать сводный отчёт в формате Excel")
            .Produces(StatusCodes.Status200OK, contentType:
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");

        endpoints.MapGet("/api/screenshots", GetScreenshotsAsync)
            .RequireAuthorization(SecurityPolicies.ViewScreenshots)
            .WithTags("Скриншоты")
            .WithName("GetScreenshotGallery")
            .WithSummary("Получить страницу галереи скриншотов");
        return endpoints;
    }

    private static async Task<IResult> GetSummaryAsync(
        Guid? employeeId,
        DateTimeOffset? fromUtc,
        DateTimeOffset? toUtc,
        string? timeZone,
        HttpContext httpContext,
        MonitoringDbContext dbContext,
        EmployeeAccessService employeeAccessService,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var rangeResult = ReportRangeResolver.Resolve(fromUtc, toUtc, timeZone, timeProvider.GetUtcNow());
        if (rangeResult.Error is not null)
        {
            return Results.ValidationProblem(rangeResult.Error);
        }

        var range = rangeResult.Range!.Value;
        var employees = await VisibleEmployeesAsync(
            employeeId, httpContext, dbContext, employeeAccessService, cancellationToken);
        if (employees is null)
        {
            return Results.NotFound();
        }

        var ids = employees.Select(item => item.Id).ToArray();
        var human = ids.Length == 0
            ? []
            : await dbContext.HumanStateSessions.AsNoTracking()
                .Where(item => ids.Contains(item.EmployeeId) && item.StartedAtUtc < range.EndUtc &&
                               (item.EndedAtUtc == null || item.EndedAtUtc > range.StartUtc))
                .ToListAsync(cancellationToken);
        var machine = ids.Length == 0
            ? []
            : await dbContext.MachineStateSessions.AsNoTracking()
                .Where(item => ids.Contains(item.EmployeeId) && item.StartedAtUtc < range.EndUtc &&
                               (item.EndedAtUtc == null || item.EndedAtUtc > range.StartUtc))
                .ToListAsync(cancellationToken);

        var rows = employees.Select(employee =>
        {
            var employeeHuman = human.Where(item => item.EmployeeId == employee.Id).ToList();
            var employeeMachine = machine.Where(item => item.EmployeeId == employee.Id).ToList();
            var trackedIntervals = employeeHuman.Select(item =>
                    ReportTime.Clip(item.StartedAtUtc, item.EndedAtUtc, range.StartUtc, range.EffectiveEndUtc))
                .Where(item => item.HasValue).Select(item => item!.Value).ToList();
            var observedIntervals = trackedIntervals.Concat(employeeMachine.Select(item =>
                    ReportTime.Clip(item.StartedAtUtc, item.EndedAtUtc, range.StartUtc, range.EffectiveEndUtc))
                .Where(item => item.HasValue).Select(item => item!.Value)).ToList();
            var productive = employeeHuman.Where(item => item.State == HumanState.Active)
                .Select(item => ReportTime.Clip(item.StartedAtUtc, item.EndedAtUtc, range.StartUtc, range.EffectiveEndUtc))
                .Concat(employeeMachine.Where(item => item.State != MachineState.Normal)
                    .Select(item => ReportTime.Clip(item.StartedAtUtc, item.EndedAtUtc, range.StartUtc, range.EffectiveEndUtc)))
                .Where(item => item.HasValue).Select(item => item!.Value);
            var humanTime = ReportTime.SummarizeHumanStates(
                employeeHuman,
                range.StartUtc,
                range.EffectiveEndUtc);
            return new EmployeeSummaryResponse(
                employee.Id,
                employee.Name,
                observedIntervals.Count == 0 ? null : observedIntervals.Min(item => item.StartUtc),
                observedIntervals.Count == 0 ? null : observedIntervals.Max(item => item.EndUtc),
                humanTime.TotalSeconds,
                ReportTime.UnionSeconds(productive),
                humanTime.ActiveSeconds,
                StateSeconds(employeeMachine, MachineState.Render),
                StateSeconds(employeeMachine, MachineState.Proxy),
                StateSeconds(employeeMachine, MachineState.BackgroundProcessing),
                humanTime.IdleSeconds,
                humanTime.LockedSeconds,
                humanTime.OfflineSeconds);

            double StateSeconds<TSession, TState>(IEnumerable<TSession> sessions, TState state)
                where TState : struct, Enum => sessions.Sum(session => session switch
                {
                    HumanStateSession humanSession when EqualityComparer<TState>.Default.Equals(
                        (TState)(object)humanSession.State, state) => ReportTime.DurationSeconds(
                            humanSession.StartedAtUtc, humanSession.EndedAtUtc, range.StartUtc, range.EffectiveEndUtc),
                    MachineStateSession machineSession when EqualityComparer<TState>.Default.Equals(
                        (TState)(object)machineSession.State, state) => ReportTime.DurationSeconds(
                            machineSession.StartedAtUtc, machineSession.EndedAtUtc, range.StartUtc, range.EffectiveEndUtc),
                    _ => 0,
                });
        }).ToList();

        return Results.Ok(new SummaryReportResponse(
            range.StartUtc, range.EndUtc, range.TimeZone.Id, range.NowUtc, rows));
    }

    private static async Task<IResult> GetApplicationsAsync(
        Guid? employeeId,
        DateTimeOffset? fromUtc,
        DateTimeOffset? toUtc,
        string? timeZone,
        HttpContext httpContext,
        MonitoringDbContext dbContext,
        EmployeeAccessService employeeAccessService,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var rangeResult = ReportRangeResolver.Resolve(fromUtc, toUtc, timeZone, timeProvider.GetUtcNow());
        if (rangeResult.Error is not null)
        {
            return Results.ValidationProblem(rangeResult.Error);
        }

        var range = rangeResult.Range!.Value;
        var employees = await VisibleEmployeesAsync(
            employeeId, httpContext, dbContext, employeeAccessService, cancellationToken);
        if (employees is null)
        {
            return Results.NotFound();
        }

        var ids = employees.Select(item => item.Id).ToArray();
        var sessions = ids.Length == 0
            ? []
            : await dbContext.ApplicationSessions.AsNoTracking()
                .Where(item => ids.Contains(item.EmployeeId) && item.StartedAtUtc < range.EndUtc &&
                               (item.EndedAtUtc == null || item.EndedAtUtc > range.StartUtc))
                .ToListAsync(cancellationToken);
        var names = employees.ToDictionary(item => item.Id, item => item.Name);
        var rules = await dbContext.ApplicationRules.AsNoTracking().Where(item => item.IsEnabled)
            .ToDictionaryAsync(item => item.NormalizedProcessName, item => item.DisplayName, cancellationToken);

        var slices = sessions.SelectMany(session => ReportTime.SplitByLocalDate(
                session.StartedAtUtc,
                session.EndedAtUtc,
                range.StartUtc,
                range.EffectiveEndUtc,
                range.TimeZone)
            .Select(slice => new
            {
                session.EmployeeId,
                EmployeeName = names[session.EmployeeId],
                slice.Date,
                session.ProcessName,
                session.Classification,
                Application = rules.GetValueOrDefault(session.ProcessName.ToUpperInvariant()) ??
                              Path.GetFileNameWithoutExtension(session.ProcessName),
                slice.DurationSeconds,
            }));
        var rows = slices.GroupBy(item => new
        {
            item.EmployeeId,
            item.EmployeeName,
            item.Date,
            item.Application,
            item.ProcessName,
            item.Classification,
        })
            .Select(group => new ApplicationReportRow(
                group.Key.EmployeeId,
                group.Key.EmployeeName,
                group.Key.Date,
                group.Key.Application,
                group.Key.ProcessName,
                group.Key.Classification,
                group.Sum(item => item.DurationSeconds)))
            .OrderByDescending(item => item.DurationSeconds)
            .ThenBy(item => item.EmployeeName)
            .ToList();

        return Results.Ok(new ApplicationReportResponse(
            range.StartUtc, range.EndUtc, range.TimeZone.Id, range.NowUtc, rows));
    }

    private static async Task<IResult> GetRendersAsync(
        Guid? employeeId,
        DateTimeOffset? fromUtc,
        DateTimeOffset? toUtc,
        HttpContext httpContext,
        MonitoringDbContext dbContext,
        EmployeeAccessService employeeAccessService,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var rangeResult = ReportRangeResolver.Resolve(fromUtc, toUtc, null, timeProvider.GetUtcNow());
        if (rangeResult.Error is not null)
        {
            return Results.ValidationProblem(rangeResult.Error);
        }

        var range = rangeResult.Range!.Value;
        var employees = await VisibleEmployeesAsync(
            employeeId, httpContext, dbContext, employeeAccessService, cancellationToken);
        if (employees is null)
        {
            return Results.NotFound();
        }

        var ids = employees.Select(item => item.Id).ToArray();
        var sessions = ids.Length == 0
            ? []
            : await dbContext.RenderSessions.AsNoTracking()
                .Where(item => ids.Contains(item.EmployeeId) && item.StartedAtUtc < range.EndUtc &&
                               (item.EndedAtUtc == null || item.EndedAtUtc > range.StartUtc))
                .OrderByDescending(item => item.StartedAtUtc)
                .ToListAsync(cancellationToken);
        var names = employees.ToDictionary(item => item.Id, item => item.Name);
        var computerIds = sessions.Select(item => item.ComputerId).Distinct().ToArray();
        var computers = await dbContext.Computers.AsNoTracking()
            .Where(item => computerIds.Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, item => item.Name, cancellationToken);
        var rows = sessions.Select(item => new RenderReportRow(
            item.Id,
            item.EmployeeId,
            names[item.EmployeeId],
            item.ComputerId,
            computers.GetValueOrDefault(item.ComputerId) ?? "Неизвестный компьютер",
            item.Type,
            item.Program,
            item.StartedAtUtc,
            item.EndedAtUtc,
            ReportTime.DurationSeconds(
                item.StartedAtUtc, item.EndedAtUtc, range.StartUtc, range.EffectiveEndUtc),
            item.OutputFolder,
            item.OutputFile,
            item.MaxCpuPercent,
            item.AverageCpuPercent,
            item.FileSizeBytes,
            item.DetectionConfidence,
            item.DetectionReason)).ToList();

        return Results.Ok(new RenderReportResponse(range.StartUtc, range.EndUtc, range.NowUtc, rows));
    }

    private static async Task<IResult> GetScreenshotsAsync(
        Guid? employeeId,
        DateTimeOffset? fromUtc,
        DateTimeOffset? toUtc,
        string? application,
        int? page,
        int? pageSize,
        HttpContext httpContext,
        MonitoringDbContext dbContext,
        EmployeeAccessService employeeAccessService,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var rangeResult = ReportRangeResolver.Resolve(fromUtc, toUtc, null, timeProvider.GetUtcNow());
        if (rangeResult.Error is not null)
        {
            return Results.ValidationProblem(rangeResult.Error);
        }

        var currentPage = page ?? 1;
        var currentPageSize = pageSize ?? 24;
        if (currentPage is < 1 or > 1_000_000 || currentPageSize is < 1 or > 100 || application?.Length > 255)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["pagination"] = ["Страница должна быть положительной, размер страницы — от 1 до 100."],
            });
        }

        var range = rangeResult.Range!.Value;
        var employees = await VisibleEmployeesAsync(
            employeeId, httpContext, dbContext, employeeAccessService, cancellationToken);
        if (employees is null)
        {
            return Results.NotFound();
        }

        var ids = employees.Select(item => item.Id).ToArray();
        var ruleEntities = await dbContext.ApplicationRules.AsNoTracking().Where(item => item.IsEnabled)
            .ToListAsync(cancellationToken);
        var query = dbContext.Screenshots.AsNoTracking()
            .Where(item => ids.Contains(item.EmployeeId) && item.FileDeletedAtUtc == null &&
                           item.TimestampUtc >= range.StartUtc && item.TimestampUtc < range.EndUtc);
        if (!string.IsNullOrWhiteSpace(application))
        {
            var search = application.Trim().ToUpperInvariant();
            var matchingProcesses = ruleEntities
                .Where(item => item.DisplayName.Contains(application.Trim(), StringComparison.OrdinalIgnoreCase) ||
                               item.ProcessName.Contains(application.Trim(), StringComparison.OrdinalIgnoreCase))
                .Select(item => item.NormalizedProcessName)
                .ToArray();
            query = query.Where(item => item.ForegroundProcess != null &&
                                       (item.ForegroundProcess.ToUpper().Contains(search) ||
                                        matchingProcesses.Contains(item.ForegroundProcess.ToUpper())));
        }

        var total = await query.CountAsync(cancellationToken);
        var screenshots = await query.OrderByDescending(item => item.TimestampUtc)
            .Skip((currentPage - 1) * currentPageSize)
            .Take(currentPageSize)
            .ToListAsync(cancellationToken);
        var names = employees.ToDictionary(item => item.Id, item => item.Name);
        var computerIds = screenshots.Select(item => item.ComputerId).Distinct().ToArray();
        var computers = await dbContext.Computers.AsNoTracking()
            .Where(item => computerIds.Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, item => item.Name, cancellationToken);
        var rules = ruleEntities.ToDictionary(item => item.NormalizedProcessName, item => item.DisplayName);
        var items = screenshots.Select(item => new ScreenshotGalleryItem(
            item.Id,
            item.EmployeeId,
            names[item.EmployeeId],
            item.ComputerId,
            computers.GetValueOrDefault(item.ComputerId) ?? "Неизвестный компьютер",
            item.TimestampUtc,
            item.Width,
            item.Height,
            item.ScreenIndex,
            item.ForegroundProcess is null
                ? null
                : rules.GetValueOrDefault(item.ForegroundProcess.ToUpperInvariant()) ??
                  Path.GetFileNameWithoutExtension(item.ForegroundProcess),
            item.ForegroundWindowTitle,
            item.HumanState,
            item.MachineState,
            $"/api/screenshots/{item.Id}/content")).ToList();

        return Results.Ok(new ScreenshotGalleryResponse(
            range.StartUtc,
            range.EndUtc,
            range.NowUtc,
            currentPage,
            currentPageSize,
            total,
            items));
    }

    private static async Task<List<Employee>?> VisibleEmployeesAsync(
        Guid? employeeId,
        HttpContext httpContext,
        MonitoringDbContext dbContext,
        EmployeeAccessService employeeAccessService,
        CancellationToken cancellationToken)
    {
        var query = employeeAccessService.ApplyVisibility(dbContext.Employees.AsNoTracking(), httpContext.User);
        if (employeeId.HasValue)
        {
            query = query.Where(item => item.Id == employeeId.Value);
        }

        var employees = await query.OrderBy(item => item.Name).ToListAsync(cancellationToken);
        return employeeId.HasValue && employees.Count == 0 ? null : employees;
    }

}
