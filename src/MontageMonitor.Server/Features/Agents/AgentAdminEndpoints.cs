using Microsoft.EntityFrameworkCore;
using MontageMonitor.Server.Domain;
using MontageMonitor.Server.Infrastructure.Persistence;
using MontageMonitor.Server.Infrastructure.Security;

namespace MontageMonitor.Server.Features.Agents;

public static class AgentAdminEndpoints
{
    public static IEndpointRouteBuilder MapAgentAdminEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/admin")
            .RequireAuthorization(SecurityPolicies.ManageEmployees)
            .WithTags("Администрирование агентов");

        group.MapGet("/computers", GetComputersAsync)
            .WithName("GetComputers")
            .WithSummary("Получить список компьютеров и статусы Agent");
        group.MapPost("/agents/{agentId:guid}/revoke", RevokeAgentAsync)
            .WithName("RevokeAgent")
            .WithSummary("Отозвать Agent и его credentials");

        return endpoints;
    }

    private static async Task<IResult> GetComputersAsync(
        MonitoringDbContext dbContext,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var heartbeatSetting = await dbContext.SystemSettings.AsNoTracking()
            .SingleAsync(item => item.Key == "agent.heartbeatIntervalSeconds", cancellationToken);
        var heartbeatInterval = int.TryParse(heartbeatSetting.JsonValue, out var configuredInterval)
            ? Math.Clamp(configuredInterval, 15, 300)
            : 30;
        var offlineAfterSeconds = Math.Max(90, heartbeatInterval * 3);
        var now = timeProvider.GetUtcNow();
        var offlineBefore = now.AddSeconds(-offlineAfterSeconds);
        var computers = await dbContext.Computers.AsNoTracking().ToListAsync(cancellationToken);
        var agents = await dbContext.Agents.AsNoTracking().ToListAsync(cancellationToken);
        var agentIds = agents.Select(item => item.Id).ToArray();
        var sessions = await dbContext.AgentOperatorSessions.AsNoTracking()
            .Where(item => agentIds.Contains(item.AgentId) && item.StartedAtUtc <= now &&
                           item.ExpiresAtUtc > now && item.EndedAtUtc == null)
            .ToListAsync(cancellationToken);
        var employeeIds = computers.Select(item => item.EmployeeId)
            .Concat(sessions.Select(item => item.EmployeeId))
            .Distinct()
            .ToArray();
        var employeeNames = await dbContext.Employees.AsNoTracking()
            .Where(item => employeeIds.Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, item => item.Name, cancellationToken);
        var agentByComputer = agents.ToDictionary(item => item.ComputerId);
        var sessionByAgent = sessions.ToDictionary(item => item.AgentId);
        var response = computers.Select(computer =>
            {
                var agent = agentByComputer.GetValueOrDefault(computer.Id);
                var operatorSession = agent is null ? null : sessionByAgent.GetValueOrDefault(agent.Id);
                var employeeId = operatorSession?.EmployeeId ?? computer.EmployeeId;
                return new ComputerResponse(
                    computer.Id,
                    employeeId,
                    employeeNames.GetValueOrDefault(employeeId) ?? "Неизвестный сотрудник",
                    computer.Name,
                    computer.OperatingSystem,
                    computer.AgentVersion,
                    computer.LastHeartbeatAtUtc,
                    computer.LastOnlineAtUtc,
                    computer.IsRevoked,
                    agent?.Id,
                    agent is null
                        ? null
                        : agent.Status == AgentStatus.Revoked
                            ? nameof(AgentStatus.Revoked)
                            : agent.LastSeenAtUtc < offlineBefore
                                ? nameof(AgentStatus.Offline)
                                : nameof(AgentStatus.Online),
                    agent?.LastSeenAtUtc);
            })
            .OrderBy(item => item.EmployeeName)
            .ThenBy(item => item.Name)
            .ToList();

        return Results.Ok(response);
    }

    private static async Task<IResult> RevokeAgentAsync(
        Guid agentId,
        HttpContext httpContext,
        MonitoringDbContext dbContext,
        AuditWriter auditWriter,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var agent = await dbContext.Agents.SingleOrDefaultAsync(item => item.Id == agentId, cancellationToken);
        if (agent is null)
        {
            return Results.NotFound();
        }

        var now = timeProvider.GetUtcNow();
        agent.Status = AgentStatus.Revoked;
        agent.RevokedAtUtc = now;
        agent.UpdatedAtUtc = now;
        var credentials = await dbContext.AgentCredentials
            .Where(item => item.AgentId == agentId && item.RevokedAtUtc == null)
            .ToListAsync(cancellationToken);
        foreach (var credential in credentials)
        {
            credential.RevokedAtUtc = now;
            credential.UpdatedAtUtc = now;
        }

        auditWriter.Add(
            httpContext,
            "agent.revoked",
            "Agent",
            agentId.ToString(),
            httpContext.User.GetRequiredUserId());
        await dbContext.SaveChangesAsync(cancellationToken);
        return Results.NoContent();
    }
}
