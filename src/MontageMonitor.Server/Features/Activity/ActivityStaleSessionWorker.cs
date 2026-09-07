namespace MontageMonitor.Server.Features.Activity;

public sealed class ActivityStaleSessionWorker(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    ILogger<ActivityStaleSessionWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(30), timeProvider, stoppingToken);
                await using var scope = scopeFactory.CreateAsyncScope();
                var aggregator = scope.ServiceProvider.GetRequiredService<ActivityAggregationService>();
                await aggregator.MarkStaleAgentsOfflineAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Не удалось закрыть устаревшие интервалы активности.");
            }
        }
    }
}
