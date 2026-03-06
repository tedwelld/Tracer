namespace Tracer.Core.Entities;

public sealed class ScanBatch
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string ScannerNode { get; set; } = string.Empty;
    public DateTimeOffset StartedUtc { get; set; }
    public DateTimeOffset CompletedUtc { get; set; }
    public long DurationMilliseconds { get; set; }
    public int TotalDevices { get; set; }
    public int WifiDevices { get; set; }
    public int BluetoothDevices { get; set; }
    public int ErrorCount { get; set; }
    public int SuspiciousDevices { get; set; }
    public double MemoryUsageMb { get; set; }
    public string? AdapterStatusSummary { get; set; }
    public ICollection<DeviceObservation> Observations { get; } = new List<DeviceObservation>();
    public ICollection<ScanEventLog> EventLogs { get; } = new List<ScanEventLog>();
}
