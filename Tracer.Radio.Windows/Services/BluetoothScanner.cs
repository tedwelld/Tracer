using InTheHand.Net;
using InTheHand.Net.Sockets;
using Microsoft.Extensions.Logging;
using Tracer.Core.Contracts;
using Tracer.Core.Enums;
using Tracer.Core.Interfaces;

namespace Tracer.Radio.Windows.Services;

public sealed class BluetoothScanner(
    IRuntimeSettingsService runtimeSettingsService,
    ILogger<BluetoothScanner> logger) : IRadioScanner
{
    public RadioKind RadioKind => RadioKind.Bluetooth;

    public async Task<IReadOnlyCollection<RadioDeviceSnapshot>> ScanAsync(CancellationToken cancellationToken)
    {
        var settings = await runtimeSettingsService.GetCurrentAsync(cancellationToken);

        if (!settings.EnableBluetooth)
        {
            return Array.Empty<RadioDeviceSnapshot>();
        }

        cancellationToken.ThrowIfCancellationRequested();

        using var client = new BluetoothClient();

        var discoveredDevices = client.DiscoverDevices(255)
            .Concat(client.PairedDevices)
            .GroupBy(x => FormatAddress(x.DeviceAddress), StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();

        var snapshots = discoveredDevices
            .Select(device => new RadioDeviceSnapshot(
                RadioKind.Bluetooth,
                $"bt:{FormatAddress(device.DeviceAddress)}",
                string.IsNullOrWhiteSpace(device.DeviceName) ? "Bluetooth device" : device.DeviceName,
                FormatAddress(device.DeviceAddress),
                null,
                null,
                device.Authenticated ? "Paired" : "Unpaired",
                device.Authenticated,
                "Bluetooth Radio",
                null,
                "2.4 GHz",
                $"Connected={device.Connected};Class={device.ClassOfDevice}"))
            .Cast<RadioDeviceSnapshot>()
            .ToList();

        logger.LogInformation("Bluetooth scanner observed {Count} nearby devices.", snapshots.Count);
        return snapshots;
    }

    private static string FormatAddress(BluetoothAddress address)
    {
        var bytes = address.ToByteArray();

        return bytes.Length == 0
            ? address.ToString()
            : string.Join(":", bytes.Reverse().Select(x => x.ToString("X2")));
    }
}
