using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Tracer.Core.Contracts;
using Tracer.Core.Entities;
using Tracer.Core.Enums;
using Tracer.Core.Interfaces;
using Tracer.Infrastructure.Persistence;

namespace Tracer.Infrastructure.Services;

public sealed class ScanCoordinator(
    IEnumerable<IRadioScanner> radioScanners,
    IDbContextFactory<TracerDbContext> dbContextFactory,
    IRuntimeSettingsService runtimeSettingsService,
    DeviceIntelligenceService deviceIntelligenceService,
    ILogger<ScanCoordinator> logger) : IScanCoordinator
{
    public async Task<ScanCycleSummary> ExecuteAsync(CancellationToken cancellationToken)
    {
        var settings = await runtimeSettingsService.GetCurrentAsync(cancellationToken);
        var startedUtc = DateTimeOffset.UtcNow;
        var collectedSnapshots = new List<RadioDeviceSnapshot>();
        var scannerOutcomes = new List<ScannerOutcome>();

        foreach (var scanner in radioScanners.OrderBy(x => x.RadioKind))
        {
            try
            {
                var snapshots = await scanner.ScanAsync(cancellationToken);
                collectedSnapshots.AddRange(snapshots);
                scannerOutcomes.Add(new ScannerOutcome(scanner.RadioKind, true, snapshots.Count, null));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Radio scan failed for {RadioKind}.", scanner.RadioKind);
                scannerOutcomes.Add(new ScannerOutcome(scanner.RadioKind, false, 0, ex.Message));
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

        var rogueWifiDeviceKeys = settings.EnableRogueWifiDetection
            ? snapshotsToPersist
                .Where(x => x.RadioKind == RadioKind.Wifi && !string.IsNullOrWhiteSpace(x.NetworkName))
                .GroupBy(x => x.NetworkName!, StringComparer.OrdinalIgnoreCase)
                .Where(group => group.Select(x => x.HardwareAddress).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1)
                .SelectMany(group => group.Select(x => x.DeviceKey))
                .ToHashSet(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var now = DateTimeOffset.UtcNow;
        var createdAlerts = 0;

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var scanBatch = new ScanBatch
        {
            StartedUtc = startedUtc,
            ScannerNode = Environment.MachineName
        };

        dbContext.ScanBatches.Add(scanBatch);
        dbContext.ScanEventLogs.Add(new ScanEventLog
        {
            ScanBatch = scanBatch,
            CreatedUtc = startedUtc,
            ScannerNode = scanBatch.ScannerNode,
            Level = ScanEventLevel.Info,
            EventType = ScanEventType.ScanStarted,
            Message = $"Scan cycle started with {scannerOutcomes.Count} scanner(s)."
        });

        foreach (var snapshot in snapshotsToPersist)
        {
            var device = await dbContext.DiscoveredDevices
                .SingleOrDefaultAsync(x => x.DeviceKey == snapshot.DeviceKey, cancellationToken);

            var isNewDevice = device is null;
            var previousLastSeenUtc = device?.LastSeenUtc;
            var previousSignalStrength = device?.LastSignalStrength;

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

            var assessment = deviceIntelligenceService.Assess(
                snapshot,
                device,
                settings,
                rogueWifiDeviceKeys.Contains(snapshot.DeviceKey));

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
            device.VendorPrefix = assessment.VendorPrefix;
            device.VendorName = assessment.VendorName;
            device.DeviceType = assessment.DeviceType;
            device.Reputation = assessment.Reputation;
            device.RiskScore = assessment.RiskScore;
            device.RiskReasons = assessment.RiskReasons.Length == 0 ? null : string.Join(" | ", assessment.RiskReasons);
            device.LastRecommendation = assessment.Recommendation;
            device.ConnectionState = assessment.ConnectionState;
            device.EstimatedDistanceMeters = assessment.EstimatedDistanceMeters;
            device.MovementTrend = assessment.MovementTrend;

            if (assessment.ConnectionState == ConnectionState.Connected)
            {
                device.LastConnectedUtc = now;
            }

            if (settings.AutoLogDevices)
            {
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
                    DeviceType = assessment.DeviceType,
                    Reputation = assessment.Reputation,
                    RiskScore = assessment.RiskScore,
                    EstimatedDistanceMeters = assessment.EstimatedDistanceMeters,
                    MovementTrend = DetermineObservationMovement(previousSignalStrength, snapshot.SignalStrength, assessment.MovementTrend),
                    ConnectionState = assessment.ConnectionState,
                    RawPayload = settings.EnablePacketMetadataCapture ? snapshot.RawPayload : null
                });
            }

            dbContext.ScanEventLogs.Add(new ScanEventLog
            {
                ScanBatch = scanBatch,
                Device = device,
                CreatedUtc = now,
                ScannerNode = scanBatch.ScannerNode,
                RadioKind = snapshot.RadioKind.ToString(),
                Level = assessment.RiskScore >= settings.RiskAlertThreshold ? ScanEventLevel.Warning : ScanEventLevel.Info,
                EventType = ScanEventType.DeviceDetected,
                Message = $"{device.RadioKind} device observed: {device.DisplayName ?? device.NetworkName ?? device.DeviceKey}",
                Details = BuildDeviceEventDetails(device, assessment)
            });

            var alerts = await CreateAlertsIfNeededAsync(
                dbContext,
                device,
                snapshot,
                assessment,
                isNewDevice,
                previousLastSeenUtc,
                now,
                settings,
                cancellationToken);

            foreach (var alert in alerts)
            {
                dbContext.DeviceAlerts.Add(alert);
                createdAlerts += 1;
            }

            if (!string.IsNullOrWhiteSpace(assessment.Recommendation))
            {
                dbContext.ScanEventLogs.Add(new ScanEventLog
                {
                    ScanBatch = scanBatch,
                    Device = device,
                    CreatedUtc = now,
                    ScannerNode = scanBatch.ScannerNode,
                    RadioKind = snapshot.RadioKind.ToString(),
                    Level = ScanEventLevel.Info,
                    EventType = ScanEventType.SecurityRecommendation,
                    Message = $"Recommendation generated for {device.DisplayName ?? device.DeviceKey}",
                    Details = assessment.Recommendation
                });
            }
        }

        foreach (var outcome in scannerOutcomes.Where(x => !x.Success))
        {
            dbContext.ScanEventLogs.Add(new ScanEventLog
            {
                ScanBatch = scanBatch,
                CreatedUtc = now,
                ScannerNode = scanBatch.ScannerNode,
                RadioKind = outcome.RadioKind.ToString(),
                Level = ScanEventLevel.Error,
                EventType = ScanEventType.ScannerError,
                Message = $"{outcome.RadioKind} scanner failed.",
                Details = outcome.ErrorMessage
            });
        }

        var completedUtc = DateTimeOffset.UtcNow;
        var currentProcess = Process.GetCurrentProcess();

        scanBatch.CompletedUtc = completedUtc;
        scanBatch.DurationMilliseconds = Math.Max(0L, (long)(completedUtc - startedUtc).TotalMilliseconds);
        scanBatch.TotalDevices = snapshotsToPersist.Count;
        scanBatch.WifiDevices = snapshotsToPersist.Count(x => x.RadioKind == RadioKind.Wifi);
        scanBatch.BluetoothDevices = snapshotsToPersist.Count(x => x.RadioKind == RadioKind.Bluetooth);
        scanBatch.ErrorCount = scannerOutcomes.Count(x => !x.Success);
        scanBatch.SuspiciousDevices = snapshotsToPersist.Count(x => rogueWifiDeviceKeys.Contains(x.DeviceKey));
        scanBatch.MemoryUsageMb = Math.Round(currentProcess.WorkingSet64 / 1024d / 1024d, 2, MidpointRounding.AwayFromZero);
        scanBatch.AdapterStatusSummary = string.Join("; ", scannerOutcomes.Select(x => $"{x.RadioKind}:{(x.Success ? $"OK({x.DeviceCount})" : "Error")}"));

        dbContext.ScanEventLogs.Add(new ScanEventLog
        {
            ScanBatch = scanBatch,
            CreatedUtc = completedUtc,
            ScannerNode = scanBatch.ScannerNode,
            Level = scanBatch.ErrorCount == 0 ? ScanEventLevel.Info : ScanEventLevel.Warning,
            EventType = ScanEventType.ScanCompleted,
            Message = $"Scan completed. Devices={scanBatch.TotalDevices}, Alerts={createdAlerts}, Errors={scanBatch.ErrorCount}",
            Details = $"Memory={scanBatch.MemoryUsageMb}MB; Status={scanBatch.AdapterStatusSummary}"
        });

        await dbContext.SaveChangesAsync(cancellationToken);

        return new ScanCycleSummary(
            startedUtc,
            scanBatch.CompletedUtc,
            scanBatch.TotalDevices,
            scanBatch.WifiDevices,
            scanBatch.BluetoothDevices,
            createdAlerts);
    }

    private static MovementTrend DetermineObservationMovement(int? previousSignalStrength, int? currentSignalStrength, MovementTrend fallback)
    {
        if (previousSignalStrength is null || currentSignalStrength is null)
        {
            return fallback;
        }

        var delta = currentSignalStrength.Value - previousSignalStrength.Value;

        if (delta >= 10)
        {
            return MovementTrend.Approaching;
        }

        if (delta <= -10)
        {
            return MovementTrend.Leaving;
        }

        return MovementTrend.Stable;
    }

    private async Task<List<DeviceAlert>> CreateAlertsIfNeededAsync(
        TracerDbContext dbContext,
        DiscoveredDevice device,
        RadioDeviceSnapshot snapshot,
        DeviceAssessment assessment,
        bool isNewDevice,
        DateTimeOffset? previousLastSeenUtc,
        DateTimeOffset now,
        RuntimeSettingsSnapshot settings,
        CancellationToken cancellationToken)
    {
        var alerts = new List<DeviceAlert>();

        if (settings.CreateAlertsForUnknownDevices && !device.IsKnown)
        {
            var shouldRaiseReturnAlert = !isNewDevice
                && previousLastSeenUtc.HasValue
                && now - previousLastSeenUtc.Value >= TimeSpan.FromMinutes(settings.ReturnAlertThresholdMinutes);

            if (isNewDevice)
            {
                var alert = await CreateAlertIfMissingAsync(
                    dbContext,
                    device,
                    AlertType.NewDeviceDetected,
                    snapshot.RadioKind == RadioKind.Bluetooth ? AlertSeverity.Warning : AlertSeverity.Info,
                    $"New {snapshot.RadioKind} device detected",
                    $"{DeviceLabel(snapshot)} was observed by the {snapshot.RadioKind} scanner near the {Environment.MachineName} node.",
                    now,
                    cancellationToken);

                if (alert is not null)
                {
                    alerts.Add(alert);
                }
            }
            else if (shouldRaiseReturnAlert)
            {
                var alert = await CreateAlertIfMissingAsync(
                    dbContext,
                    device,
                    AlertType.DeviceReturned,
                    AlertSeverity.Info,
                    $"{snapshot.RadioKind} device returned",
                    $"{DeviceLabel(snapshot)} returned after being absent from recent scans.",
                    now,
                    cancellationToken);

                if (alert is not null)
                {
                    alerts.Add(alert);
                }
            }
        }

        if (assessment.IsRogueWifiCandidate && settings.EnableRogueWifiDetection)
        {
            var alert = await CreateAlertIfMissingAsync(
                dbContext,
                device,
                AlertType.RogueWifiDetected,
                AlertSeverity.Critical,
                "Potential rogue Wi-Fi network detected",
                $"{DeviceLabel(snapshot)} shares its SSID with another access point and should be verified before connecting.",
                now,
                cancellationToken);

            if (alert is not null)
            {
                alerts.Add(alert);
            }
        }

        if (assessment.RiskScore >= settings.RiskAlertThreshold)
        {
            var alert = await CreateAlertIfMissingAsync(
                dbContext,
                device,
                AlertType.SuspiciousDeviceDetected,
                AlertSeverity.Warning,
                "Suspicious device activity detected",
                $"{DeviceLabel(snapshot)} reached risk score {assessment.RiskScore}. {device.RiskReasons}",
                now,
                cancellationToken);

            if (alert is not null)
            {
                alerts.Add(alert);
            }
        }

        if (snapshot.RadioKind == RadioKind.Bluetooth
            && settings.EnableUnknownBluetoothConnectionAlerts
            && assessment.ConnectionState == ConnectionState.Connected
            && !device.IsKnown)
        {
            var alert = await CreateAlertIfMissingAsync(
                dbContext,
                device,
                AlertType.UnknownBluetoothConnection,
                AlertSeverity.Critical,
                "Unknown Bluetooth connection detected",
                $"{DeviceLabel(snapshot)} appears connected to the system. Review and disconnect if this device is not expected.",
                now,
                cancellationToken);

            if (alert is not null)
            {
                alerts.Add(alert);
            }
        }

        if (settings.EnableAutomaticRecommendations && !string.IsNullOrWhiteSpace(assessment.Recommendation))
        {
            var alert = await CreateAlertIfMissingAsync(
                dbContext,
                device,
                AlertType.SecurityRecommendation,
                AlertSeverity.Info,
                "Recommended security action",
                assessment.Recommendation!,
                now,
                cancellationToken);

            if (alert is not null)
            {
                alerts.Add(alert);
            }
        }

        return alerts;
    }

    private static async Task<DeviceAlert?> CreateAlertIfMissingAsync(
        TracerDbContext dbContext,
        DiscoveredDevice device,
        AlertType alertType,
        AlertSeverity severity,
        string title,
        string message,
        DateTimeOffset createdUtc,
        CancellationToken cancellationToken)
    {
        var exists = await dbContext.DeviceAlerts.AnyAsync(
            x => x.DeviceId == device.Id && x.AlertType == alertType && x.Status == AlertStatus.Pending,
            cancellationToken);

        if (exists)
        {
            return null;
        }

        return new DeviceAlert
        {
            Device = device,
            AlertType = alertType,
            Severity = severity,
            CreatedUtc = createdUtc,
            Title = title,
            Message = message
        };
    }

    private static string DeviceLabel(RadioDeviceSnapshot snapshot)
        => snapshot.DisplayName
            ?? snapshot.NetworkName
            ?? snapshot.HardwareAddress
            ?? snapshot.DeviceKey;

    private static string BuildDeviceEventDetails(DiscoveredDevice device, DeviceAssessment assessment)
    {
        return $"Risk={assessment.RiskScore}; Vendor={assessment.VendorName ?? "Unknown"}; Type={assessment.DeviceType}; Distance={assessment.EstimatedDistanceMeters?.ToString("0.##") ?? "n/a"}m; Movement={assessment.MovementTrend}; State={assessment.ConnectionState}";
    }

    private static string? FirstNonEmpty(string? primary, string? fallback)
        => string.IsNullOrWhiteSpace(primary) ? fallback : primary.Trim();

    private sealed record ScannerOutcome(RadioKind RadioKind, bool Success, int DeviceCount, string? ErrorMessage);
}
