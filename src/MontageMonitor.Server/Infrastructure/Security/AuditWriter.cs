using System.Text.Json;
using MontageMonitor.Server.Domain;
using MontageMonitor.Server.Infrastructure.Persistence;

namespace MontageMonitor.Server.Infrastructure.Security;

public sealed class AuditWriter(MonitoringDbContext dbContext, TimeProvider timeProvider)
{
    public void Add(
        HttpContext? httpContext,
        string action,
        string entityName,
        string? entityId = null,
        Guid? userId = null,
        object? details = null)
    {
        dbContext.AuditLogs.Add(new AuditLog
        {
            UserId = userId,
            Action = action,
            EntityName = entityName,
            EntityId = entityId,
            TimestampUtc = timeProvider.GetUtcNow(),
            IpAddress = httpContext?.Connection.RemoteIpAddress?.ToString(),
            Details = details is null ? null : JsonSerializer.SerializeToDocument(details),
        });
    }
}
