using Tracer.Core.Enums;

namespace Tracer.Core.Contracts;

public sealed record RadioDeviceSnapshot(
    RadioKind RadioKind,
    string DeviceKey,
    string? DisplayName,
    string? HardwareAddress,
    string? NetworkName,
    int? SignalStrength,
    string? SecurityType,
    bool IsPaired,
    string? InterfaceName,
    int? Channel,
    string? FrequencyBand,
    string? RawPayload);
