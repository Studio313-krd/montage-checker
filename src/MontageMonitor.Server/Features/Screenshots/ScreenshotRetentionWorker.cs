using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MontageMonitor.Server.Infrastructure.Persistence;

namespace MontageMonitor.Server.Features.Screenshots;

public sealed class ScreenshotRetentionWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<ScreenshotStorageOptions> storageOptions,
    TimeProvider timeProvider,
    ILogger<ScreenshotRetentionWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await DeleteExpiredAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Не удалось выполнить retention скриншотов.");
            }

            await Task.Delay(TimeSpan.FromDays(1), timeProvider, stoppingToken);
        }
    }

    private async Task DeleteExpiredAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<MonitoringDbContext>();
        var configured = await dbContext.SystemSettings.AsNoTracking()
            .Where(item => item.Key == "screenshots.retentionDays")
            .Select(item => item.JsonValue)
            .SingleAsync(cancellationToken);
        var retentionDays = int.TryParse(configured, out var parsed) ? Math.Clamp(parsed, 1, 3_650) : 30;
        var now = timeProvider.GetUtcNow();
        var cutoff = now.AddDays(-retentionDays);
        var root = Path.GetFullPath(storageOptions.Value.RootPath);
        var failedIds = new HashSet<Guid>();
        while (!cancellationToken.IsCancellationRequested)
        {
            var query = dbContext.Screenshots
                .Where(item => item.TimestampUtc < cutoff && item.FileDeletedAtUtc == null);
            if (failedIds.Count > 0)
            {
                query = query.Where(item => !failedIds.Contains(item.Id));
            }

            var expired = await query
                .OrderBy(item => item.TimestampUtc)
                .Take(500)
                .ToListAsync(cancellationToken);
            if (expired.Count == 0)
            {
                break;
            }

            foreach (var screenshot in expired)
            {
                string path;
                try
                {
                    path = Path.GetFullPath(Path.Combine(
                        root,
                        screenshot.StoragePath.Replace('/', Path.DirectorySeparatorChar)));
                }
                catch (Exception exception) when (
                    exception is IOException or ArgumentException or NotSupportedException)
                {
                    failedIds.Add(screenshot.Id);
                    logger.LogWarning(exception, "Некорректный storage path скриншота {ScreenshotId}.", screenshot.Id);
                    continue;
                }

                if (!IsInsideRoot(root, path))
                {
                    failedIds.Add(screenshot.Id);
                    logger.LogWarning("Пропущен небезопасный storage path скриншота {ScreenshotId}.", screenshot.Id);
                    continue;
                }

                try
                {
                    File.Delete(path);
                    screenshot.FileDeletedAtUtc = now;
                    screenshot.UpdatedAtUtc = now;
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    failedIds.Add(screenshot.Id);
                    logger.LogWarning(exception, "Не удалось удалить файл скриншота {ScreenshotId}.", screenshot.Id);
                }
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            dbContext.ChangeTracker.Clear();
            if (failedIds.Count >= 5_000)
            {
                logger.LogError("Retention остановлен: обнаружено {Count} неудаляемых файлов.", failedIds.Count);
                break;
            }
        }
    }

    private static bool IsInsideRoot(string root, string path)
    {
        var prefix = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
                     Path.DirectorySeparatorChar;
        return path.StartsWith(prefix, StringComparison.Ordinal);
    }
}
