namespace Tracer.Core.Contracts;

public sealed record RuntimeSettingsSnapshot(
    bool EnableWifi,
    bool EnableBluetooth,
    int ScanIntervalSeconds,
    int ApproximateRangeMeters,
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
    int EventLogRetentionDays);
