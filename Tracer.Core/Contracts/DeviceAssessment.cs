using Tracer.Core.Enums;

namespace Tracer.Core.Contracts;

public sealed record DeviceAssessment(
    string? VendorPrefix,
    string? VendorName,
    DeviceType DeviceType,
    DeviceReputation Reputation,
    int RiskScore,
    string[] RiskReasons,
    string? Recommendation,
    ConnectionState ConnectionState,
    decimal? EstimatedDistanceMeters,
    MovementTrend MovementTrend,
    bool IsRogueWifiCandidate);
