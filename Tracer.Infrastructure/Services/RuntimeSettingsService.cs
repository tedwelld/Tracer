using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Tracer.Core.Contracts;
using Tracer.Core.Entities;
using Tracer.Core.Interfaces;
using Tracer.Core.Options;
using Tracer.Infrastructure.Persistence;

namespace Tracer.Infrastructure.Services;

public sealed class RuntimeSettingsService(
    IDbContextFactory<TracerDbContext> dbContextFactory,
    IOptions<ScannerOptions> scannerOptions,
    IOptions<AlertOptions> alertOptions) : IRuntimeSettingsService
{
    private readonly ScannerOptions _scannerDefaults = scannerOptions.Value;
    private readonly AlertOptions _alertDefaults = alertOptions.Value;

    public async Task<RuntimeSettingsSnapshot> GetCurrentAsync(CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var settings = await dbContext.RuntimeSettings.AsNoTracking().SingleOrDefaultAsync(x => x.Id == 1, cancellationToken);

        return settings is null
            ? CreateDefaultSnapshot()
            : Map(settings);
    }

    public async Task<RuntimeSettingsSnapshot> UpdateAsync(RuntimeSettingsSnapshot snapshot, CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var settings = await dbContext.RuntimeSettings.SingleOrDefaultAsync(x => x.Id == 1, cancellationToken);

        if (settings is null)
        {
            settings = new RuntimeSettings { Id = 1 };
            dbContext.RuntimeSettings.Add(settings);
        }

        Apply(snapshot, settings);
        settings.LastUpdatedUtc = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);

        return Map(settings);
    }

    public RuntimeSettingsSnapshot CreateDefaultSnapshot()
    {
        return new RuntimeSettingsSnapshot(
            _scannerDefaults.EnableWifi,
            _scannerDefaults.EnableBluetooth,
            _scannerDefaults.ScanIntervalSeconds,
            _scannerDefaults.ApproximateRangeMeters,
            _scannerDefaults.WifiScanTimeoutSeconds,
            _scannerDefaults.MinimumWifiSignalQuality,
            _alertDefaults.CreateAlertsForUnknownDevices,
            _alertDefaults.ReturnAlertThresholdMinutes,
            true,
            true,
            true,
            60,
            true,
            true,
            false,
            90,
            365,
            180);
    }

    public static RuntimeSettingsSnapshot Map(RuntimeSettings settings)
    {
        return new RuntimeSettingsSnapshot(
            settings.EnableWifi,
            settings.EnableBluetooth,
            settings.ScanIntervalSeconds,
            settings.ApproximateRangeMeters,
            settings.WifiScanTimeoutSeconds,
            settings.MinimumWifiSignalQuality,
            settings.CreateAlertsForUnknownDevices,
            settings.ReturnAlertThresholdMinutes,
            settings.EnableRogueWifiDetection,
            settings.EnableUnknownBluetoothConnectionAlerts,
            settings.EnableAutomaticRecommendations,
            settings.RiskAlertThreshold,
            settings.AutoLogDevices,
            settings.EnablePacketMetadataCapture,
            settings.EnableTrafficAnalysis,
            settings.ObservationRetentionDays,
            settings.AlertRetentionDays,
            settings.EventLogRetentionDays);
    }

    public static void Apply(RuntimeSettingsSnapshot snapshot, RuntimeSettings settings)
    {
        settings.EnableWifi = snapshot.EnableWifi;
        settings.EnableBluetooth = snapshot.EnableBluetooth;
        settings.ScanIntervalSeconds = snapshot.ScanIntervalSeconds;
        settings.ApproximateRangeMeters = snapshot.ApproximateRangeMeters;
        settings.WifiScanTimeoutSeconds = snapshot.WifiScanTimeoutSeconds;
        settings.MinimumWifiSignalQuality = snapshot.MinimumWifiSignalQuality;
        settings.CreateAlertsForUnknownDevices = snapshot.CreateAlertsForUnknownDevices;
        settings.ReturnAlertThresholdMinutes = snapshot.ReturnAlertThresholdMinutes;
        settings.EnableRogueWifiDetection = snapshot.EnableRogueWifiDetection;
        settings.EnableUnknownBluetoothConnectionAlerts = snapshot.EnableUnknownBluetoothConnectionAlerts;
        settings.EnableAutomaticRecommendations = snapshot.EnableAutomaticRecommendations;
        settings.RiskAlertThreshold = snapshot.RiskAlertThreshold;
        settings.AutoLogDevices = snapshot.AutoLogDevices;
        settings.EnablePacketMetadataCapture = snapshot.EnablePacketMetadataCapture;
        settings.EnableTrafficAnalysis = snapshot.EnableTrafficAnalysis;
        settings.ObservationRetentionDays = snapshot.ObservationRetentionDays;
        settings.AlertRetentionDays = snapshot.AlertRetentionDays;
        settings.EventLogRetentionDays = snapshot.EventLogRetentionDays;
    }
}
