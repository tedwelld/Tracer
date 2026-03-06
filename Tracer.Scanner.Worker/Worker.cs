using Tracer.Core.Interfaces;

namespace Tracer.Scanner.Worker;

public sealed class Worker(
    IDatabaseInitializer databaseInitializer,
    IScanCoordinator scanCoordinator,
    IRuntimeSettingsService runtimeSettingsService,
    ILogger<Worker> logger) : BackgroundService
{
    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        await databaseInitializer.InitializeAsync(cancellationToken);
        await base.StartAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var summary = await scanCoordinator.ExecuteAsync(stoppingToken);
                logger.LogInformation(
                    "Scan completed at {CompletedUtc}. Total={TotalDevices}, Wi-Fi={WifiDevices}, Bluetooth={BluetoothDevices}, Alerts={CreatedAlerts}",
                    summary.CompletedUtc,
                    summary.TotalDevices,
                    summary.WifiDevices,
                    summary.BluetoothDevices,
                    summary.CreatedAlerts);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Unhandled failure during scanner cycle.");
            }

            var settings = await runtimeSettingsService.GetCurrentAsync(stoppingToken);
            var delay = TimeSpan.FromSeconds(Math.Max(5, settings.ScanIntervalSeconds));
            await Task.Delay(delay, stoppingToken);
        }
    }
}
