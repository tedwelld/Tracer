using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Tracer.Core.Contracts;
using Tracer.Core.Interfaces;

namespace Tracer.Web.Pages;

[Authorize(Policy = "ManageSettings")]
public sealed class SettingsModel(IRuntimeSettingsService runtimeSettingsService) : PageModel
{
    public ScannerSettingsDto CurrentSettings { get; private set; } = ScannerSettingsDto.Empty;

    [BindProperty]
    public ScannerSettingsDto UpdatedSettings { get; set; } = ScannerSettingsDto.Empty;

    [TempData]
    public string? StatusMessage { get; set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        var settings = await runtimeSettingsService.GetCurrentAsync(cancellationToken);
        CurrentSettings = ScannerSettingsDto.FromSnapshot(settings);
        UpdatedSettings = CurrentSettings;
    }

    public async Task<IActionResult> OnPostAsync(
        int rangeMeters,
        bool enableWifi,
        bool enableBluetooth,
        int scanIntervalSeconds,
        int wifiScanTimeoutSeconds,
        int minimumWifiSignalQuality,
        bool createAlertsForUnknownDevices,
        int returnAlertThresholdMinutes,
        bool enableRogueWifiDetection,
        bool enableUnknownBluetoothConnectionAlerts,
        bool enableAutomaticRecommendations,
        int riskAlertThreshold,
        bool autoLogDevices,
        bool enablePacketMetadataCapture,
        bool enableTrafficAnalysis,
        int observationRetentionDays,
        int alertRetentionDays,
        int eventLogRetentionDays,
        CancellationToken cancellationToken)
    {
        var snapshot = new RuntimeSettingsSnapshot(
            enableWifi,
            enableBluetooth,
            Math.Clamp(scanIntervalSeconds, 5, 300),
            Math.Clamp(rangeMeters, 1, 20),
            Math.Clamp(wifiScanTimeoutSeconds, 3, 30),
            Math.Clamp(minimumWifiSignalQuality, 0, 100),
            createAlertsForUnknownDevices,
            Math.Clamp(returnAlertThresholdMinutes, 15, 1440),
            enableRogueWifiDetection,
            enableUnknownBluetoothConnectionAlerts,
            enableAutomaticRecommendations,
            Math.Clamp(riskAlertThreshold, 10, 100),
            autoLogDevices,
            enablePacketMetadataCapture,
            enableTrafficAnalysis,
            Math.Max(1, observationRetentionDays),
            Math.Max(1, alertRetentionDays),
            Math.Max(1, eventLogRetentionDays));

        await runtimeSettingsService.UpdateAsync(snapshot, cancellationToken);
        StatusMessage = "Settings saved successfully.";
        return RedirectToPage();
    }

    public sealed record ScannerSettingsDto(
        int ApproximateRangeMeters,
        bool EnableWifi,
        bool EnableBluetooth,
        int ScanIntervalSeconds,
        int MaxRangeMeters,
        int WifiScanTimeoutSeconds,
        int MinimumWifiSignalQuality,
        bool CreateAlertsForUnknownDevices,
        int ReturnAlertThresholdMinutes,
        bool EnableRogueWifiDetection,
        bool EnableUnknownBluetoothConnectionAlerts,
        bool EnableAutomaticRecommendations,
        int RiskAlertThreshold,
        bool AutoLogDevices,
        bool EnablePacketMetadataCapture,
        bool EnableTrafficAnalysis,
        int ObservationRetentionDays,
        int AlertRetentionDays,
        int EventLogRetentionDays)
    {
        public static ScannerSettingsDto Empty { get; } = new(
            20,
            true,
            true,
            30,
            20,
            8,
            15,
            true,
            120,
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

        public static ScannerSettingsDto FromSnapshot(RuntimeSettingsSnapshot snapshot)
        {
            return new ScannerSettingsDto(
                snapshot.ApproximateRangeMeters,
                snapshot.EnableWifi,
                snapshot.EnableBluetooth,
                snapshot.ScanIntervalSeconds,
                20,
                snapshot.WifiScanTimeoutSeconds,
                snapshot.MinimumWifiSignalQuality,
                snapshot.CreateAlertsForUnknownDevices,
                snapshot.ReturnAlertThresholdMinutes,
                snapshot.EnableRogueWifiDetection,
                snapshot.EnableUnknownBluetoothConnectionAlerts,
                snapshot.EnableAutomaticRecommendations,
                snapshot.RiskAlertThreshold,
                snapshot.AutoLogDevices,
                snapshot.EnablePacketMetadataCapture,
                snapshot.EnableTrafficAnalysis,
                snapshot.ObservationRetentionDays,
                snapshot.AlertRetentionDays,
                snapshot.EventLogRetentionDays);
        }

        public RuntimeSettingsSnapshot ToSnapshot()
        {
            return new RuntimeSettingsSnapshot(
                EnableWifi,
                EnableBluetooth,
                ScanIntervalSeconds,
                ApproximateRangeMeters,
                WifiScanTimeoutSeconds,
                MinimumWifiSignalQuality,
                CreateAlertsForUnknownDevices,
                ReturnAlertThresholdMinutes,
                EnableRogueWifiDetection,
                EnableUnknownBluetoothConnectionAlerts,
                EnableAutomaticRecommendations,
                RiskAlertThreshold,
                AutoLogDevices,
                EnablePacketMetadataCapture,
                EnableTrafficAnalysis,
                ObservationRetentionDays,
                AlertRetentionDays,
                EventLogRetentionDays);
        }
    }
}
