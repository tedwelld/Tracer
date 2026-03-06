using InTheHand.Net;
using InTheHand.Net.Sockets;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tracer.Core.Contracts;
using Tracer.Core.Enums;
using Tracer.Core.Interfaces;
using Tracer.Core.Options;

namespace Tracer.Radio.Windows.Services;

public sealed class BluetoothScanner(
    IOptions<ScannerOptions> options,
    ILogger<BluetoothScanner> logger) : IRadioScanner
{
    private readonly ScannerOptions _options = options.Value;

    public RadioKind RadioKind => RadioKind.Bluetooth;

    public Task<IReadOnlyCollection<RadioDeviceSnapshot>> ScanAsync(CancellationToken cancellationToken)
    {
        if (!_options.EnableBluetooth)
        {
            return Task.FromResult<IReadOnlyCollection<RadioDeviceSnapshot>>(Array.Empty<RadioDeviceSnapshot>());
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
        return Task.FromResult<IReadOnlyCollection<RadioDeviceSnapshot>>(snapshots);
    }

    private static string FormatAddress(BluetoothAddress address)
    {
        var bytes = address.ToByteArray();

        return bytes.Length == 0
            ? address.ToString()
            : string.Join(":", bytes.Reverse().Select(x => x.ToString("X2")));
    }
}
