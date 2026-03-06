using System.Globalization;
using ManagedNativeWifi;
using Microsoft.Extensions.Logging;
using Tracer.Core.Contracts;
using Tracer.Core.Enums;
using Tracer.Core.Interfaces;

namespace Tracer.Radio.Windows.Services;

public sealed class WifiScanner(
    IRuntimeSettingsService runtimeSettingsService,
    ILogger<WifiScanner> logger) : IRadioScanner
{
    public RadioKind RadioKind => RadioKind.Wifi;

    public async Task<IReadOnlyCollection<RadioDeviceSnapshot>> ScanAsync(CancellationToken cancellationToken)
    {
        var settings = await runtimeSettingsService.GetCurrentAsync(cancellationToken);

        if (!settings.EnableWifi)
        {
            return Array.Empty<RadioDeviceSnapshot>();
        }

        try
        {
            await NativeWifi.ScanNetworksAsync(
                timeout: TimeSpan.FromSeconds(Math.Max(3, settings.WifiScanTimeoutSeconds)),
                cancellationToken: cancellationToken);
        }
        catch (UnauthorizedAccessException ex)
        {
            logger.LogWarning(ex, "Wi-Fi scan blocked by Windows location privacy settings.");
            return Array.Empty<RadioDeviceSnapshot>();
        }

        var snapshots = new List<RadioDeviceSnapshot>();

        foreach (var group in NativeWifi.EnumerateAvailableNetworkGroups())
        {
            foreach (var bssNetwork in group.BssNetworks.DefaultIfEmpty())
            {
                var signalQuality = bssNetwork?.LinkQuality ?? group.SignalQuality;

                if (signalQuality < settings.MinimumWifiSignalQuality)
                {
                    continue;
                }

                var hardwareAddress = bssNetwork is null
                    ? null
                    : FormatIdentifier(bssNetwork.Bssid);

                var networkName = group.Ssid.ToString();
                var deviceKey = $"wifi:{hardwareAddress ?? networkName}";

                snapshots.Add(new RadioDeviceSnapshot(
                    RadioKind.Wifi,
                    deviceKey,
                    networkName,
                    hardwareAddress,
                    networkName,
                    signalQuality,
                    $"{group.AuthenticationAlgorithm}/{group.CipherAlgorithm}",
                    false,
                    ExtractInterfaceName(group.InterfaceInfo),
                    bssNetwork?.Channel ?? group.Channel,
                    bssNetwork is null ? null : $"{bssNetwork.Band.ToString("0.0", CultureInfo.InvariantCulture)} GHz",
                    $"SSID={networkName};Connectable={group.IsConnectable};RangeHintMeters={settings.ApproximateRangeMeters}"));
            }
        }

        logger.LogInformation("Wi-Fi scanner observed {Count} access points above the configured threshold.", snapshots.Count);
        return snapshots;
    }

    private static string? ExtractInterfaceName(object? interfaceInfo)
        => interfaceInfo is null
            ? null
            : ReadProperty(interfaceInfo, "Description")
                ?? ReadProperty(interfaceInfo, "Name")
                ?? ReadProperty(interfaceInfo, "Id");

    private static string? ReadProperty(object instance, string propertyName)
        => instance.GetType().GetProperty(propertyName)?.GetValue(instance)?.ToString();

    private static string? FormatIdentifier(object? identifier)
    {
        if (identifier is null)
        {
            return null;
        }

        var toBytesMethod = identifier.GetType().GetMethod("ToBytes", Type.EmptyTypes);

        if (toBytesMethod?.Invoke(identifier, null) is byte[] bytes && bytes.Length > 0)
        {
            return string.Join(":", bytes.Select(x => x.ToString("X2", CultureInfo.InvariantCulture)));
        }

        return identifier.ToString();
    }
}
