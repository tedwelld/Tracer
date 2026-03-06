using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;
using Tracer.Core.Options;

namespace Tracer.Web.Pages;

public sealed class SettingsModel(IOptions<ScannerOptions> scannerOptions) : PageModel
{
    public ScannerSettingsDto CurrentSettings { get; private set; } = new(0, true, true, 30, 20, 8, 15);

    [BindProperty]
    public ScannerSettingsDto UpdatedSettings { get; set; } = new(0, true, true, 30, 20, 8, 15);

    public void OnGet()
    {
        var options = scannerOptions.Value;
        CurrentSettings = new ScannerSettingsDto(
            options.ApproximateRangeMeters,
            options.EnableWifi,
            options.EnableBluetooth,
            options.ScanIntervalSeconds,
            20,  // Max range meters
            options.WifiScanTimeoutSeconds,
            options.MinimumWifiSignalQuality);

        UpdatedSettings = CurrentSettings;
    }

    public sealed record ScannerSettingsDto(
        int ApproximateRangeMeters,
        bool EnableWifi,
        bool EnableBluetooth,
        int ScanIntervalSeconds,
        int MaxRangeMeters,
        int WifiScanTimeoutSeconds,
        int MinimumWifiSignalQuality);
}
