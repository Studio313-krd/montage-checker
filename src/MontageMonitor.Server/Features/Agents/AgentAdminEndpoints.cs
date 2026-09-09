using System.Security.Cryptography;
using Microsoft.AspNetCore.WebUtilities;
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

        group.MapPost("/employees/{employeeId:guid}/agent-enrollment", CreateEnrollmentTokenAsync)
            .WithName("CreateAgentEnrollmentToken")
            .WithSummary("Создать одноразовый код регистрации Agent");
        group.MapGet("/computers", GetComputersAsync)
            .WithName("GetComputers")
            .WithSummary("Получить список компьютеров и статусы Agent");
        group.MapPost("/agents/{agentId:guid}/revoke", RevokeAgentAsync)
            .WithName("RevokeAgent")
            .WithSummary("Отозвать Agent и его credentials");

        return endpoints;
    }

    private static async Task<IResult> CreateEnrollmentTokenAsync(
        Guid employeeId,
        CreateEnrollmentTokenRequest request,
        HttpContext httpContext,
        MonitoringDbContext dbContext,
        AuditWriter auditWriter,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (request.ExpiresInHours is < 1 or > 168)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(request.ExpiresInHours)] = ["Срок действия должен быть от 1 до 168 часов."],
            });
        }

        var employee = await dbContext.Employees.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == employeeId, cancellationToken);
        if (employee is null)
        {
            return Results.NotFound();
        }

        if (!employee.IsActive)
        {
            return Results.Conflict(new { message = "Нельзя зарегистрировать Agent для неактивного сотрудника." });
        }

        var rawToken = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        var now = timeProvider.GetUtcNow();
        var entity = new AgentEnrollmentToken
        {
            EmployeeId = employeeId,
            TokenHash = AgentCredentialService.Hash(rawToken),
            ExpiresAtUtc = now.AddHours(request.ExpiresInHours),
            CreatedByUserId = httpContext.User.GetRequiredUserId(),
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };
        dbContext.AgentEnrollmentTokens.Add(entity);
        auditWriter.Add(
            httpContext,
            "agent.enrollment_token.created",
            "Employee",
            employeeId.ToString(),
            entity.CreatedByUserId,
            new { tokenId = entity.Id, entity.ExpiresAtUtc });
        await dbContext.SaveChangesAsync(cancellationToken);

        return Results.Ok(new EnrollmentTokenResponse(
            entity.Id,
            employeeId,
            rawToken,
            entity.ExpiresAtUtc));
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
