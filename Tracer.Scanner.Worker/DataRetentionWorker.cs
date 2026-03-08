using Tracer.Infrastructure.Services;

namespace Tracer.Scanner.Worker;

public sealed class DataRetentionWorker(
    DataRetentionService dataRetentionService,
    ILogger<DataRetentionWorker> logger) : BackgroundService
{
    private static readonly TimeSpan ExecutionInterval = TimeSpan.FromHours(6);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var result = await dataRetentionService.ExecuteAsync(stoppingToken);
                logger.LogInformation(
                    "Retention cleanup completed at {ExecutedUtc}. Observations={DeletedObservations}, Alerts={DeletedAlerts}, EventLogs={DeletedEventLogs}",
                    result.ExecutedUtc,
                    result.DeletedObservations,
                    result.DeletedAlerts,
                    result.DeletedEventLogs);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Retention cleanup failed.");
            }

            await Task.Delay(ExecutionInterval, stoppingToken);
        }
    }
}
