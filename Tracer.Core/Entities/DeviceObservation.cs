namespace Tracer.Core.Entities;

public sealed class DeviceObservation
{
    public long Id { get; set; }
    public Guid DeviceId { get; set; }
    public DiscoveredDevice? Device { get; set; }
    public Guid ScanBatchId { get; set; }
    public ScanBatch? ScanBatch { get; set; }
    public DateTimeOffset ObservedUtc { get; set; }
    public string? DisplayName { get; set; }
    public string? HardwareAddress { get; set; }
    public string? NetworkName { get; set; }
    public string? SecurityType { get; set; }
    public string? InterfaceName { get; set; }
    public string? FrequencyBand { get; set; }
    public int? Channel { get; set; }
    public int? SignalStrength { get; set; }
    public bool IsPaired { get; set; }
    public string? RawPayload { get; set; }
}
