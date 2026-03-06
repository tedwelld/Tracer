using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tracer.Core.Contracts;
using Tracer.Core.Entities;
using Tracer.Core.Enums;
using Tracer.Core.Interfaces;
using Tracer.Core.Options;
using Tracer.Infrastructure.Persistence;

namespace Tracer.Infrastructure.Services;

public sealed class ScanCoordinator(
    IEnumerable<IRadioScanner> radioScanners,
    IDbContextFactory<TracerDbContext> dbContextFactory,
    IOptions<AlertOptions> alertOptions,
    ILogger<ScanCoordinator> logger) : IScanCoordinator
{
    private readonly AlertOptions _alertOptions = alertOptions.Value;

    public async Task<ScanCycleSummary> ExecuteAsync(CancellationToken cancellationToken)
    {
        var startedUtc = DateTimeOffset.UtcNow;
        var collectedSnapshots = new List<RadioDeviceSnapshot>();

        foreach (var scanner in radioScanners.OrderBy(x => x.RadioKind))
        {
            try
            {
                var snapshots = await scanner.ScanAsync(cancellationToken);
                collectedSnapshots.AddRange(snapshots);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Radio scan failed for {RadioKind}.", scanner.RadioKind);
            }
        }

        var snapshotsToPersist = collectedSnapshots
            .Where(x => !string.IsNullOrWhiteSpace(x.DeviceKey))
            .GroupBy(x => x.DeviceKey, StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderByDescending(x => x.SignalStrength ?? int.MinValue)
                .ThenByDescending(x => x.DisplayName is not null)
                .First())
            .ToList();

        var now = DateTimeOffset.UtcNow;
        var createdAlerts = 0;

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var scanBatch = new ScanBatch
        {
            StartedUtc = startedUtc,
            ScannerNode = Environment.MachineName
        };

        dbContext.ScanBatches.Add(scanBatch);

        foreach (var snapshot in snapshotsToPersist)
        {
            var device = await dbContext.DiscoveredDevices
                .SingleOrDefaultAsync(x => x.DeviceKey == snapshot.DeviceKey, cancellationToken);

            var isNewDevice = device is null;
            var previousLastSeenUtc = device?.LastSeenUtc;

            if (device is null)
            {
                device = new DiscoveredDevice
                {
                    DeviceKey = snapshot.DeviceKey,
                    RadioKind = snapshot.RadioKind,
                    FirstSeenUtc = now
                };

                dbContext.DiscoveredDevices.Add(device);
            }

            device.DisplayName = FirstNonEmpty(snapshot.DisplayName, device.DisplayName);
            device.HardwareAddress = FirstNonEmpty(snapshot.HardwareAddress, device.HardwareAddress);
            device.NetworkName = FirstNonEmpty(snapshot.NetworkName, device.NetworkName);
            device.SecurityType = FirstNonEmpty(snapshot.SecurityType, device.SecurityType);
            device.LastInterfaceName = FirstNonEmpty(snapshot.InterfaceName, device.LastInterfaceName);
            device.FrequencyBand = FirstNonEmpty(snapshot.FrequencyBand, device.FrequencyBand);
            device.Channel = snapshot.Channel ?? device.Channel;
            device.LastSignalStrength = snapshot.SignalStrength ?? device.LastSignalStrength;
            device.IsPaired = snapshot.IsPaired;
            device.LastSeenUtc = now;
            device.TotalObservations += 1;

            dbContext.DeviceObservations.Add(new DeviceObservation
            {
                Device = device,
                ScanBatch = scanBatch,
                ObservedUtc = now,
                DisplayName = snapshot.DisplayName,
                HardwareAddress = snapshot.HardwareAddress,
                NetworkName = snapshot.NetworkName,
                SecurityType = snapshot.SecurityType,
                InterfaceName = snapshot.InterfaceName,
                FrequencyBand = snapshot.FrequencyBand,
                Channel = snapshot.Channel,
                SignalStrength = snapshot.SignalStrength,
                IsPaired = snapshot.IsPaired,
                RawPayload = snapshot.RawPayload
            });

            var alert = await CreateAlertIfNeededAsync(
                dbContext,
                device,
                snapshot,
                isNewDevice,
                previousLastSeenUtc,
                now,
                cancellationToken);

            if (alert is not null)
            {
                dbContext.DeviceAlerts.Add(alert);
                createdAlerts += 1;
            }
        }

        scanBatch.CompletedUtc = DateTimeOffset.UtcNow;
        scanBatch.TotalDevices = snapshotsToPersist.Count;
        scanBatch.WifiDevices = snapshotsToPersist.Count(x => x.RadioKind == RadioKind.Wifi);
        scanBatch.BluetoothDevices = snapshotsToPersist.Count(x => x.RadioKind == RadioKind.Bluetooth);

        await dbContext.SaveChangesAsync(cancellationToken);

        return new ScanCycleSummary(
            startedUtc,
            scanBatch.CompletedUtc,
            scanBatch.TotalDevices,
            scanBatch.WifiDevices,
            scanBatch.BluetoothDevices,
            createdAlerts);
    }

    private async Task<DeviceAlert?> CreateAlertIfNeededAsync(
        TracerDbContext dbContext,
        DiscoveredDevice device,
        RadioDeviceSnapshot snapshot,
        bool isNewDevice,
        DateTimeOffset? previousLastSeenUtc,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (!_alertOptions.CreateAlertsForUnknownDevices || device.IsKnown)
        {
            return null;
        }

        var pendingAlertExists = await dbContext.DeviceAlerts
            .AnyAsync(x => x.DeviceId == device.Id && x.Status == AlertStatus.Pending, cancellationToken);

        if (pendingAlertExists)
        {
            return null;
        }

        var shouldRaiseReturnAlert = !isNewDevice
            && previousLastSeenUtc.HasValue
            && now - previousLastSeenUtc.Value >= TimeSpan.FromMinutes(_alertOptions.ReturnAlertThresholdMinutes);

        if (!isNewDevice && !shouldRaiseReturnAlert)
        {
            return null;
        }

        var alertType = isNewDevice ? AlertType.NewDeviceDetected : AlertType.DeviceReturned;
        var label = snapshot.DisplayName
            ?? snapshot.NetworkName
            ?? snapshot.HardwareAddress
            ?? snapshot.DeviceKey;

        return new DeviceAlert
        {
            Device = device,
            AlertType = alertType,
            Severity = snapshot.RadioKind == RadioKind.Bluetooth ? AlertSeverity.Warning : AlertSeverity.Info,
            CreatedUtc = now,
            Title = isNewDevice
                ? $"New {snapshot.RadioKind} device detected"
                : $"{snapshot.RadioKind} device returned",
            Message = $"{label} was observed by the {snapshot.RadioKind} scanner near the {Environment.MachineName} node."
        };
    }

    private static string? FirstNonEmpty(string? primary, string? fallback)
        => string.IsNullOrWhiteSpace(primary) ? fallback : primary.Trim();
}
