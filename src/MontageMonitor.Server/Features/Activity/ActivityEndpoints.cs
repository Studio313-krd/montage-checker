using Microsoft.EntityFrameworkCore;
using MontageMonitor.Server.Features.Employees;
using MontageMonitor.Server.Infrastructure.Persistence;
using MontageMonitor.Server.Infrastructure.Security;

namespace MontageMonitor.Server.Features.Activity;

public static class ActivityEndpoints
{
    public static IEndpointRouteBuilder MapActivityEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/employees/{employeeId:guid}/activity/sessions", GetSessionsAsync)
            .RequireAuthorization(SecurityPolicies.ViewEmployees)
            .WithTags("Активность")
            .WithName("GetEmployeeActivitySessions")
            .WithSummary("Получить интервалы приложений и состояний сотрудника");
        endpoints.MapGet("/api/employees/{employeeId:guid}/timeline", GetSessionsAsync)
            .RequireAuthorization(SecurityPolicies.ViewEmployees)
            .WithTags("Активность")
            .WithName("GetEmployeeTimeline")
            .WithSummary("Получить временную шкалу сотрудника");
        return endpoints;
    }

    private static async Task<IResult> GetSessionsAsync(
        Guid employeeId,
        DateTimeOffset? fromUtc,
        DateTimeOffset? toUtc,
        Guid? computerId,
        HttpContext httpContext,
        MonitoringDbContext dbContext,
        EmployeeAccessService employeeAccessService,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (!await employeeAccessService.CanViewAsync(employeeId, httpContext.User, cancellationToken) ||
            !await dbContext.Employees.AsNoTracking().AnyAsync(item => item.Id == employeeId, cancellationToken))
        {
            return Results.NotFound();
        }

        var now = timeProvider.GetUtcNow();
        var rangeEnd = (toUtc ?? now).ToUniversalTime();
        var rangeStart = (fromUtc ?? rangeEnd.AddHours(-24)).ToUniversalTime();
        var durationEnd = rangeEnd < now ? rangeEnd : now;
        if (rangeStart >= rangeEnd || rangeEnd - rangeStart > TimeSpan.FromDays(31))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["range"] = ["Диапазон должен быть положительным и не превышать 31 день."],
            });
        }

        if (computerId.HasValue && !await dbContext.Computers.AsNoTracking().AnyAsync(
                item => item.Id == computerId && item.EmployeeId == employeeId,
                cancellationToken))
        {
            return Results.NotFound();
        }

        var applicationsQuery = dbContext.ApplicationSessions.AsNoTracking()
            .Where(item => item.EmployeeId == employeeId && item.StartedAtUtc < rangeEnd &&
                           (item.EndedAtUtc == null || item.EndedAtUtc > rangeStart));
        var humanQuery = dbContext.HumanStateSessions.AsNoTracking()
            .Where(item => item.EmployeeId == employeeId && item.StartedAtUtc < rangeEnd &&
                           (item.EndedAtUtc == null || item.EndedAtUtc > rangeStart));
        var machineQuery = dbContext.MachineStateSessions.AsNoTracking()
            .Where(item => item.EmployeeId == employeeId && item.StartedAtUtc < rangeEnd &&
                           (item.EndedAtUtc == null || item.EndedAtUtc > rangeStart));
        var renderQuery = dbContext.RenderSessions.AsNoTracking()
            .Where(item => item.EmployeeId == employeeId && item.StartedAtUtc < rangeEnd &&
                           (item.EndedAtUtc == null || item.EndedAtUtc > rangeStart));
        if (computerId.HasValue)
        {
            applicationsQuery = applicationsQuery.Where(item => item.ComputerId == computerId);
            humanQuery = humanQuery.Where(item => item.ComputerId == computerId);
            machineQuery = machineQuery.Where(item => item.ComputerId == computerId);
            renderQuery = renderQuery.Where(item => item.ComputerId == computerId);
        }

        var applicationEntities = await applicationsQuery.OrderBy(item => item.StartedAtUtc)
            .ToListAsync(cancellationToken);
        var applications = applicationEntities
            .Select(item => new ApplicationSessionResponse(
                item.Id,
                item.ComputerId,
                item.StartedAtUtc,
                item.EndedAtUtc,
                DurationSeconds(item.StartedAtUtc, item.EndedAtUtc, rangeStart, durationEnd),
                item.ProcessName,
                item.ExecutablePath,
                item.WindowTitle,
                item.Classification))
            .ToList();
        var humanEntities = await humanQuery.OrderBy(item => item.StartedAtUtc)
            .ToListAsync(cancellationToken);
        var humanStates = humanEntities
            .Select(item => new HumanStateSessionResponse(
                item.Id,
                item.ComputerId,
                item.StartedAtUtc,
                item.EndedAtUtc,
                DurationSeconds(item.StartedAtUtc, item.EndedAtUtc, rangeStart, durationEnd),
                item.State))
            .ToList();
        var machineEntities = await machineQuery.OrderBy(item => item.StartedAtUtc)
            .ToListAsync(cancellationToken);
        var machineStates = machineEntities
            .Select(item => new MachineStateSessionResponse(
                item.Id,
                item.ComputerId,
                item.StartedAtUtc,
                item.EndedAtUtc,
                DurationSeconds(item.StartedAtUtc, item.EndedAtUtc, rangeStart, durationEnd),
                item.State,
                item.DetectionConfidence,
                item.DetectionReason))
            .ToList();
        var renderEntities = await renderQuery.OrderBy(item => item.StartedAtUtc)
            .ToListAsync(cancellationToken);
        var renders = renderEntities
            .Select(item => new RenderSessionResponse(
                item.Id,
                item.ComputerId,
                item.Type,
                item.Program,
                item.StartedAtUtc,
                item.EndedAtUtc,
                DurationSeconds(item.StartedAtUtc, item.EndedAtUtc, rangeStart, durationEnd),
                item.OutputFolder,
                item.OutputFile,
                item.AverageCpuPercent,
                item.MaxCpuPercent,
                item.FileSizeBytes,
                item.DetectionConfidence,
                item.DetectionReason))
            .ToList();

        return Results.Ok(new ActivityTimelineResponse(
            employeeId,
            rangeStart,
            rangeEnd,
            now,
            applications,
            humanStates,
            machineStates,
            renders));
    }

    private static double DurationSeconds(
        DateTimeOffset startedAtUtc,
        DateTimeOffset? endedAtUtc,
        DateTimeOffset rangeStart,
        DateTimeOffset rangeEnd)
    {
        var effectiveStart = startedAtUtc > rangeStart ? startedAtUtc : rangeStart;
        var effectiveEnd = endedAtUtc.HasValue && endedAtUtc.Value < rangeEnd
            ? endedAtUtc.Value
            : rangeEnd;
        return Math.Max(0, (effectiveEnd - effectiveStart).TotalSeconds);
    }
}
