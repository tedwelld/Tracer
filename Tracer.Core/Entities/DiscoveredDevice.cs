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
    public string? LastInterfaceName { get; set; }
    public string? FrequencyBand { get; set; }
    public int? Channel { get; set; }
    public int? LastSignalStrength { get; set; }
    public bool IsPaired { get; set; }
    public bool IsKnown { get; set; }
    public int TotalObservations { get; set; }
    public DateTimeOffset FirstSeenUtc { get; set; }
    public DateTimeOffset LastSeenUtc { get; set; }
    public ICollection<DeviceObservation> Observations { get; } = new List<DeviceObservation>();
    public ICollection<DeviceAlert> Alerts { get; } = new List<DeviceAlert>();
}
