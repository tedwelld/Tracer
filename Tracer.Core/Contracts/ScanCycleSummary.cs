namespace Tracer.Core.Contracts;

public sealed record ScanCycleSummary(
    DateTimeOffset StartedUtc,
    DateTimeOffset CompletedUtc,
    int TotalDevices,
    int WifiDevices,
    int BluetoothDevices,
    int CreatedAlerts);
