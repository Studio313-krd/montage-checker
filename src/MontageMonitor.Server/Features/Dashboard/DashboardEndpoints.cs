using Microsoft.EntityFrameworkCore;
using MontageMonitor.Server.Domain;
using MontageMonitor.Server.Features.Activity;
using MontageMonitor.Server.Features.Employees;
using MontageMonitor.Server.Infrastructure.Persistence;
using MontageMonitor.Server.Infrastructure.Security;
using MontageMonitor.Shared.States;

namespace MontageMonitor.Server.Features.Dashboard;

public static class DashboardEndpoints
{
    private const int RefreshAfterSeconds = 15;

    public static IEndpointRouteBuilder MapDashboardEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/dashboard", GetDashboardAsync)
            .RequireAuthorization(SecurityPolicies.ViewEmployees)
            .WithTags("Панель руководителя")
            .WithName("GetLiveDashboard")
            .WithSummary("Получить текущие состояния видимых сотрудников");
        return endpoints;
    }

    private static async Task<IResult> GetDashboardAsync(
        HttpContext httpContext,
        MonitoringDbContext dbContext,
        EmployeeAccessService employeeAccessService,
        ActivityAggregationService activityAggregationService,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var timing = await activityAggregationService.GetHeartbeatTimingAsync(cancellationToken);
        var employees = await employeeAccessService.ApplyVisibility(
                dbContext.Employees.AsNoTracking().Where(item => item.IsActive),
                httpContext.User)
            .OrderBy(item => item.Name)
            .ToListAsync(cancellationToken);
        if (employees.Count == 0)
        {
            return Results.Ok(new DashboardResponse(now, RefreshAfterSeconds, []));
        }

        var employeeIds = employees.Select(item => item.Id).ToArray();
        var computers = await dbContext.Computers.AsNoTracking()
            .Where(item => employeeIds.Contains(item.EmployeeId) && !item.IsRevoked)
            .ToListAsync(cancellationToken);
        var computerIds = computers.Select(item => item.Id).ToArray();
        if (computerIds.Length == 0)
        {
            return Results.Ok(new DashboardResponse(
                now,
                RefreshAfterSeconds,
                employees.Select(EmployeeWithoutComputer).ToList()));
        }

        var agents = await dbContext.Agents.AsNoTracking()
            .Where(item => computerIds.Contains(item.ComputerId) && item.RevokedAtUtc == null)
            .ToListAsync(cancellationToken);
        var latestHeartbeats = await dbContext.Heartbeats.AsNoTracking()
            .Where(item => computerIds.Contains(item.ComputerId))
            .GroupBy(item => item.ComputerId)
            .Select(group => group
                .OrderByDescending(item => item.TimestampUtc)
                .ThenByDescending(item => item.CreatedAtUtc)
                .First())
            .ToListAsync(cancellationToken);
        var openHumanStates = await dbContext.HumanStateSessions.AsNoTracking()
            .Where(item => computerIds.Contains(item.ComputerId) && item.EndedAtUtc == null)
            .ToListAsync(cancellationToken);
        var openMachineStates = await dbContext.MachineStateSessions.AsNoTracking()
            .Where(item => computerIds.Contains(item.ComputerId) && item.EndedAtUtc == null)
            .ToListAsync(cancellationToken);
        var applicationNames = await dbContext.ApplicationRules.AsNoTracking()
            .Where(item => item.IsEnabled)
            .ToDictionaryAsync(
                item => item.NormalizedProcessName,
                item => item.DisplayName,
                cancellationToken);

        var canViewScreenshots = SecurityPolicies.ManagementViewRoles.Any(httpContext.User.IsInRole);
        var latestScreenshots = canViewScreenshots
            ? await dbContext.Screenshots.AsNoTracking()
                .Where(item => employeeIds.Contains(item.EmployeeId) && item.FileDeletedAtUtc == null)
                .GroupBy(item => item.EmployeeId)
                .Select(group => group
                    .OrderByDescending(item => item.TimestampUtc)
                    .ThenByDescending(item => item.CreatedAtUtc)
                    .First())
                .ToListAsync(cancellationToken)
            : [];

        var agentByComputer = agents
            .GroupBy(item => item.ComputerId)
            .ToDictionary(group => group.Key, group => group.OrderByDescending(item => item.EnrolledAtUtc).First());
        var heartbeatByComputer = latestHeartbeats.ToDictionary(item => item.ComputerId);
        var humanByComputer = openHumanStates.ToDictionary(item => item.ComputerId);
        var machineByComputer = openMachineStates.ToDictionary(item => item.ComputerId);
        var screenshotByEmployee = latestScreenshots.ToDictionary(item => item.EmployeeId);

        var cards = new List<DashboardEmployeeResponse>(employees.Count);
        foreach (var employee in employees)
        {
            var selectedComputer = computers
                .Where(item => item.EmployeeId == employee.Id)
                .OrderByDescending(item => IsOnline(item.Id))
                .ThenByDescending(item => agentByComputer.GetValueOrDefault(item.Id)?.LastSeenAtUtc)
                .ThenByDescending(item => item.LastHeartbeatAtUtc)
                .FirstOrDefault();
            if (selectedComputer is null)
            {
                cards.Add(EmployeeWithoutComputer(employee));
                continue;
            }

            var agent = agentByComputer.GetValueOrDefault(selectedComputer.Id);
            var isOnline = DashboardStateResolver.IsOnline(
                agent?.Status,
                agent?.LastSeenAtUtc,
                now,
                timing.OfflineAfterSeconds);
            var heartbeat = heartbeatByComputer.GetValueOrDefault(selectedComputer.Id);
            var humanSession = humanByComputer.GetValueOrDefault(selectedComputer.Id);
            var machineSession = machineByComputer.GetValueOrDefault(selectedComputer.Id);
            var humanState = isOnline && heartbeat is not null ? heartbeat.HumanState : HumanState.Offline;
            var machineState = isOnline && heartbeat is not null ? heartbeat.MachineState : MachineState.Normal;
            var humanStartedAtUtc = humanSession?.State == humanState
                ? humanSession.StartedAtUtc
                : isOnline
                    ? heartbeat?.TimestampUtc
                    : selectedComputer.LastHeartbeatAtUtc;
            var machineStartedAtUtc = machineSession?.State == machineState
                ? machineSession.StartedAtUtc
                : isOnline
                    ? heartbeat?.TimestampUtc
                    : null;
            var processName = isOnline ? Normalize(heartbeat?.ForegroundProcess) : null;
            var displayName = processName is null
                ? null
                : applicationNames.GetValueOrDefault(processName.ToUpperInvariant()) ??
                  Path.GetFileNameWithoutExtension(processName);
            var screenshot = screenshotByEmployee.GetValueOrDefault(employee.Id);
            cards.Add(new DashboardEmployeeResponse(
                employee.Id,
                employee.Name,
                employee.Department,
                selectedComputer.Id,
                selectedComputer.Name,
                selectedComputer.AgentVersion,
                isOnline,
                displayName,
                processName,
                isOnline ? Normalize(heartbeat?.ForegroundWindowTitle) : null,
                humanState,
                humanStartedAtUtc,
                machineState,
                machineStartedAtUtc,
                DashboardStateResolver.PrimaryStateStartedAt(
                    isOnline,
                    machineState,
                    humanStartedAtUtc,
                    machineStartedAtUtc),
                selectedComputer.LastHeartbeatAtUtc,
                screenshot is null
                    ? null
                    : new DashboardScreenshotResponse(
                        screenshot.Id,
                        screenshot.TimestampUtc,
                        screenshot.Width,
                        screenshot.Height,
                        screenshot.ScreenIndex,
                        $"/api/screenshots/{screenshot.Id}/content")));
        }

        return Results.Ok(new DashboardResponse(now, RefreshAfterSeconds, cards));

        bool IsOnline(Guid computerId)
        {
            var agent = agentByComputer.GetValueOrDefault(computerId);
            return DashboardStateResolver.IsOnline(
                agent?.Status,
                agent?.LastSeenAtUtc,
                now,
                timing.OfflineAfterSeconds);
        }
    }

    private static DashboardEmployeeResponse EmployeeWithoutComputer(Employee employee) => new(
        employee.Id,
        employee.Name,
        employee.Department,
        null,
        null,
        null,
        false,
        null,
        null,
        null,
        HumanState.Offline,
        employee.CreatedAtUtc,
        MachineState.Normal,
        null,
        employee.CreatedAtUtc,
        null,
        null);

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
