namespace Tracer.Core.Entities;

public sealed class RuntimeSettings
{
    public int Id { get; set; } = 1;
    public bool EnableWifi { get; set; } = true;
    public bool EnableBluetooth { get; set; } = true;
    public int ScanIntervalSeconds { get; set; } = 30;
    public int ApproximateRangeMeters { get; set; } = 20;
    public int WifiScanTimeoutSeconds { get; set; } = 8;
    public int MinimumWifiSignalQuality { get; set; } = 15;
    public bool CreateAlertsForUnknownDevices { get; set; } = true;
    public int ReturnAlertThresholdMinutes { get; set; } = 120;
    public bool EnableRogueWifiDetection { get; set; } = true;
    public bool EnableUnknownBluetoothConnectionAlerts { get; set; } = true;
    public bool EnableAutomaticRecommendations { get; set; } = true;
    public int RiskAlertThreshold { get; set; } = 60;
    public bool AutoLogDevices { get; set; } = true;
    public bool EnablePacketMetadataCapture { get; set; } = true;
    public bool EnableTrafficAnalysis { get; set; } = false;
    public int ObservationRetentionDays { get; set; } = 90;
    public int AlertRetentionDays { get; set; } = 365;
    public int EventLogRetentionDays { get; set; } = 180;
    public DateTimeOffset LastUpdatedUtc { get; set; } = DateTimeOffset.UtcNow;
}
