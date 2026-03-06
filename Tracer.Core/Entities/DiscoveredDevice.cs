using Tracer.Core.Enums;

namespace Tracer.Core.Entities;

public sealed class DiscoveredDevice
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string DeviceKey { get; set; } = string.Empty;
    public RadioKind RadioKind { get; set; }
    public string? DisplayName { get; set; }
    public string? HardwareAddress { get; set; }
    public string? NetworkName { get; set; }
    public string? SecurityType { get; set; }
    public string? Password { get; set; }
    public string? VendorPrefix { get; set; }
    public string? VendorName { get; set; }
    public DeviceType DeviceType { get; set; } = DeviceType.Unknown;
    public DeviceReputation Reputation { get; set; } = DeviceReputation.Unknown;
    public int RiskScore { get; set; }
    public string? RiskReasons { get; set; }
    public string? LastRecommendation { get; set; }
    public ConnectionState ConnectionState { get; set; } = ConnectionState.Unknown;
    public decimal? EstimatedDistanceMeters { get; set; }
    public MovementTrend MovementTrend { get; set; } = MovementTrend.Stable;
    public string? LastInterfaceName { get; set; }
    public string? FrequencyBand { get; set; }
    public int? Channel { get; set; }
    public int? LastSignalStrength { get; set; }
    public bool IsPaired { get; set; }
    public bool IsKnown { get; set; }
    public int TotalObservations { get; set; }
    public DateTimeOffset FirstSeenUtc { get; set; }
    public DateTimeOffset LastSeenUtc { get; set; }
    public DateTimeOffset? LastConnectedUtc { get; set; }
    public ICollection<DeviceObservation> Observations { get; } = new List<DeviceObservation>();
    public ICollection<DeviceAlert> Alerts { get; } = new List<DeviceAlert>();
}
