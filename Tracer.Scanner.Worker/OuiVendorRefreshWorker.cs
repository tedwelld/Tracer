using Tracer.Infrastructure.Services;

namespace Tracer.Scanner.Worker;

public sealed class OuiVendorRefreshWorker(
    OuiVendorLookupService ouiVendorLookupService,
    ILogger<OuiVendorRefreshWorker> logger) : BackgroundService
{
    private static readonly TimeSpan ExecutionInterval = TimeSpan.FromHours(24);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var result = await ouiVendorLookupService.RefreshAsync(stoppingToken);
                if (result.Refreshed)
                {
                    logger.LogInformation("OUI vendor cache refreshed with {ImportedCount} entries.", result.ImportedCount);
                }
                else if (!string.IsNullOrWhiteSpace(result.Message))
                {
                    logger.LogDebug("OUI vendor refresh skipped: {Message}", result.Message);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "OUI vendor refresh failed.");
            }

            await Task.Delay(ExecutionInterval, stoppingToken);
        }
    }
}
