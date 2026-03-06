namespace Tracer.Core.Options;

public sealed class ScannerOptions
{
    public const string SectionName = "Scanner";

    public bool EnableWifi { get; set; } = true;
    public bool EnableBluetooth { get; set; } = true;
    public int ScanIntervalSeconds { get; set; } = 30;
    public int ApproximateRangeMeters { get; set; } = 20;
    public int WifiScanTimeoutSeconds { get; set; } = 8;
    public int MinimumWifiSignalQuality { get; set; } = 15;
}
