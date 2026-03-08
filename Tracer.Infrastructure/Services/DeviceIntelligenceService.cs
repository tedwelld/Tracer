using System.Globalization;
using Tracer.Core.Contracts;
using Tracer.Core.Entities;
using Tracer.Core.Enums;

namespace Tracer.Infrastructure.Services;

public sealed class DeviceIntelligenceService
{
    private readonly OuiVendorLookupService _ouiVendorLookupService;

    public DeviceIntelligenceService(OuiVendorLookupService ouiVendorLookupService)
    {
        _ouiVendorLookupService = ouiVendorLookupService;
    }

    public DeviceAssessment Assess(
        RadioDeviceSnapshot snapshot,
        DiscoveredDevice? existingDevice,
        RuntimeSettingsSnapshot settings,
        bool isRogueWifiCandidate)
    {
        var vendorPrefix = ExtractVendorPrefix(snapshot.HardwareAddress);
        var vendorName = _ouiVendorLookupService.ResolveVendorName(vendorPrefix);
        var deviceType = ClassifyDevice(snapshot, vendorName);
        var connectionState = DetermineConnectionState(snapshot);
        var estimatedDistanceMeters = EstimateDistance(snapshot.SignalStrength, settings.ApproximateRangeMeters);
        var movementTrend = DetermineMovementTrend(existingDevice?.LastSignalStrength, snapshot.SignalStrength);
        var riskReasons = new List<string>();
        var riskScore = 0;

        if (existingDevice?.IsKnown != true)
        {
            riskScore += 20;
            riskReasons.Add("Device is not in the trusted list.");
        }

        if (deviceType == DeviceType.Tracker)
        {
            riskScore += 30;
            riskReasons.Add("Device resembles a Bluetooth or proximity tracker.");
        }

        if (snapshot.RadioKind == RadioKind.Bluetooth
            && connectionState == ConnectionState.Connected
            && existingDevice?.IsKnown != true)
        {
            riskScore += 25;
            riskReasons.Add("Unknown Bluetooth device appears connected to the system.");
        }

        if (isRogueWifiCandidate)
        {
            riskScore += 25;
            riskReasons.Add("Wi-Fi network shares an SSID with another access point.");
        }

        if (string.IsNullOrWhiteSpace(vendorName))
        {
            riskScore += 10;
            riskReasons.Add("Vendor could not be identified from MAC OUI.");
        }

        if (IsLocallyAdministered(snapshot.HardwareAddress))
        {
            riskScore += 10;
            riskReasons.Add("MAC address appears locally administered or randomized.");
        }

        if (snapshot.SignalStrength is >= 80 && existingDevice?.IsKnown != true)
        {
            riskScore += 5;
            riskReasons.Add("Unknown device is very close to the scanner.");
        }

        riskScore = Math.Clamp(riskScore, 0, 100);

        var reputation = riskScore >= settings.RiskAlertThreshold
            ? DeviceReputation.Suspicious
            : existingDevice?.IsKnown == true
                ? DeviceReputation.Safe
                : DeviceReputation.Unknown;

        var recommendation = settings.EnableAutomaticRecommendations
            ? BuildRecommendation(snapshot, deviceType, connectionState, isRogueWifiCandidate, riskScore)
            : null;

        return new DeviceAssessment(
            vendorPrefix,
            vendorName,
            deviceType,
            reputation,
            riskScore,
            riskReasons.ToArray(),
            recommendation,
            connectionState,
            estimatedDistanceMeters,
            movementTrend,
            isRogueWifiCandidate);
    }

    private static string? ExtractVendorPrefix(string? hardwareAddress)
        => OuiVendorLookupService.NormalizePrefix(hardwareAddress);

    private static DeviceType ClassifyDevice(RadioDeviceSnapshot snapshot, string? vendorName)
    {
        var name = $"{snapshot.DisplayName} {snapshot.NetworkName} {snapshot.RawPayload}".ToLowerInvariant();

        if (name.Contains("airtag") || name.Contains("tile") || name.Contains("tracker"))
        {
            return DeviceType.Tracker;
        }

        if (name.Contains("headset") || name.Contains("airpods") || name.Contains("buds") || name.Contains("speaker"))
        {
            return DeviceType.Headphones;
        }

        if (name.Contains("watch") || name.Contains("band"))
        {
            return DeviceType.Wearable;
        }

        if (snapshot.RadioKind == RadioKind.Wifi)
        {
            if (!string.IsNullOrWhiteSpace(snapshot.NetworkName))
            {
                return DeviceType.AccessPoint;
            }

            return DeviceType.Router;
        }

        if (name.Contains("iphone") || name.Contains("android") || name.Contains("galaxy") || vendorName is "Apple" or "Samsung" or "Google")
        {
            return DeviceType.Phone;
        }

        if (name.Contains("laptop") || name.Contains("pc") || vendorName is "Intel" or "Microsoft")
        {
            return DeviceType.Laptop;
        }

        if (name.Contains("keyboard") || name.Contains("mouse"))
        {
            return DeviceType.Peripheral;
        }

        return DeviceType.Unknown;
    }

    private static ConnectionState DetermineConnectionState(RadioDeviceSnapshot snapshot)
    {
        if (snapshot.RawPayload?.Contains("Connected=True", StringComparison.OrdinalIgnoreCase) == true)
        {
            return ConnectionState.Connected;
        }

        return snapshot.RadioKind == RadioKind.Bluetooth && snapshot.IsPaired
            ? ConnectionState.Connected
            : ConnectionState.Nearby;
    }

    private static decimal? EstimateDistance(int? signalStrength, int approximateRangeMeters)
    {
        if (signalStrength is null)
        {
            return null;
        }

        var normalized = Math.Clamp(signalStrength.Value / 100d, 0d, 1d);
        var distance = Math.Max(0.5d, approximateRangeMeters * (1.05d - normalized));
        return decimal.Round((decimal)distance, 2, MidpointRounding.AwayFromZero);
    }

    private static MovementTrend DetermineMovementTrend(int? previousSignalStrength, int? currentSignalStrength)
    {
        if (previousSignalStrength is null || currentSignalStrength is null)
        {
            return MovementTrend.Stable;
        }

        var delta = currentSignalStrength.Value - previousSignalStrength.Value;

        if (delta >= 10)
        {
            return MovementTrend.Approaching;
        }

        if (delta <= -10)
        {
            return MovementTrend.Leaving;
        }

        return MovementTrend.Stable;
    }

    private static bool IsLocallyAdministered(string? hardwareAddress)
    {
        if (string.IsNullOrWhiteSpace(hardwareAddress))
        {
            return false;
        }

        var cleaned = hardwareAddress.Replace(":", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal);

        if (!byte.TryParse(cleaned.AsSpan(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var firstByte))
        {
            return false;
        }

        return (firstByte & 0b0000_0010) != 0;
    }

    private static string? BuildRecommendation(
        RadioDeviceSnapshot snapshot,
        DeviceType deviceType,
        ConnectionState connectionState,
        bool isRogueWifiCandidate,
        int riskScore)
    {
        if (isRogueWifiCandidate)
        {
            return "Verify the SSID and disconnect from duplicate access points if this network is unexpected.";
        }

        if (snapshot.RadioKind == RadioKind.Bluetooth && connectionState == ConnectionState.Connected)
        {
            return "Review current Bluetooth connections and disable or remove unknown paired devices.";
        }

        if (deviceType == DeviceType.Tracker)
        {
            return "Inspect for nearby tracking tags and disable Bluetooth if the device is unfamiliar.";
        }

        if (riskScore >= 60)
        {
            return "Review the device details, mark it trusted only if recognized, or disconnect suspicious wireless connections.";
        }

        return null;
    }
}
