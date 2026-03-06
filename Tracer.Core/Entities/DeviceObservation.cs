using Tracer.Core.Enums;

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
    public DeviceType DeviceType { get; set; } = DeviceType.Unknown;
    public DeviceReputation Reputation { get; set; } = DeviceReputation.Unknown;
    public int RiskScore { get; set; }
    public decimal? EstimatedDistanceMeters { get; set; }
    public MovementTrend MovementTrend { get; set; } = MovementTrend.Stable;
    public ConnectionState ConnectionState { get; set; } = ConnectionState.Unknown;
    public string? RawPayload { get; set; }
}
