using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Tracer.Core.Contracts;
using Tracer.Core.Interfaces;

namespace Tracer.Web.Pages;

public sealed class SettingsModel(IRuntimeSettingsService runtimeSettingsService) : PageModel
{
    public ScannerSettingsDto CurrentSettings { get; private set; } = ScannerSettingsDto.Empty;

    [BindProperty]
    public ScannerSettingsDto UpdatedSettings { get; set; } = ScannerSettingsDto.Empty;

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        var settings = await runtimeSettingsService.GetCurrentAsync(cancellationToken);
        CurrentSettings = ScannerSettingsDto.FromSnapshot(settings);
        UpdatedSettings = CurrentSettings;
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
        bool EnableTrafficAnalysis)
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
            false);

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
                snapshot.EnableTrafficAnalysis);
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
                EnableTrafficAnalysis);
        }
    }
}
