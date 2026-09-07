using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace MontageMonitor.Server.Infrastructure.Persistence;

public sealed class PostgreSqlHealthCheck(IServiceScopeFactory scopeFactory) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<MonitoringDbContext>();
            var canConnect = await dbContext.Database.CanConnectAsync(cancellationToken);

            return canConnect
                ? HealthCheckResult.Healthy("PostgreSQL доступен.")
                : HealthCheckResult.Unhealthy("PostgreSQL недоступен.");
        }
        catch (Exception exception)
        {
            return HealthCheckResult.Unhealthy("Ошибка подключения к PostgreSQL.", exception);
        }
    }
}
