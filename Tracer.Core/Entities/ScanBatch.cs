namespace Tracer.Core.Entities;

public sealed class ScanBatch
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string ScannerNode { get; set; } = string.Empty;
    public DateTimeOffset StartedUtc { get; set; }
    public DateTimeOffset CompletedUtc { get; set; }
    public int TotalDevices { get; set; }
    public int WifiDevices { get; set; }
    public int BluetoothDevices { get; set; }
    public ICollection<DeviceObservation> Observations { get; } = new List<DeviceObservation>();
}
