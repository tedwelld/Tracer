using System.Collections.Concurrent;
using System.Globalization;
using System.Runtime.InteropServices.WindowsRuntime;
using InTheHand.Net;
using InTheHand.Net.Bluetooth;
using InTheHand.Net.Sockets;
using Microsoft.Extensions.Logging;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Storage.Streams;

namespace Tracer.Web.Services;

public sealed class BluetoothConnectionService(ILogger<BluetoothConnectionService> logger) : IAsyncDisposable
{
    private static readonly Guid BatteryServiceUuid = Guid.Parse("0000180F-0000-1000-8000-00805F9B34FB");
    private static readonly Guid BatteryLevelCharacteristicUuid = Guid.Parse("00002A19-0000-1000-8000-00805F9B34FB");
    private readonly ConcurrentDictionary<string, BluetoothLEDevice> _sessions = new(StringComparer.OrdinalIgnoreCase);

    public async Task<ConnectionOperationResult> ConnectAsync(string hardwareAddress, string? passkey, CancellationToken cancellationToken)
    {
        if (!TryParseBluetoothAddress(hardwareAddress, out var classicAddress, out var bluetoothAddress))
        {
            return ConnectionOperationResult.FromFailure("The Bluetooth address is invalid.");
        }

        try
        {
            var deviceInfo = new BluetoothDeviceInfo(classicAddress);
            deviceInfo.Refresh();

            if (!deviceInfo.Authenticated && !string.IsNullOrWhiteSpace(passkey))
            {
                var paired = BluetoothSecurity.PairRequest(classicAddress, passkey, null);
                if (!paired)
                {
                    return ConnectionOperationResult.FromFailure("Bluetooth pairing failed. Verify the passkey and device mode, then try again.");
                }
            }

            var device = await BluetoothLEDevice.FromBluetoothAddressAsync(bluetoothAddress).AsTask(cancellationToken);
            if (device is not null)
            {
                _sessions.AddOrUpdate(
                    hardwareAddress,
                    device,
                    (_, existing) =>
                    {
                        existing.Dispose();
                        return device;
                    });

                return ConnectionOperationResult.FromSuccess($"Bluetooth session opened for {hardwareAddress}.");
            }

            deviceInfo.Refresh();
            return deviceInfo.Authenticated || deviceInfo.Connected
                ? ConnectionOperationResult.FromSuccess($"Bluetooth pairing is active for {hardwareAddress}.")
                : ConnectionOperationResult.FromFailure("Windows could not open a Bluetooth session for this device.");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Bluetooth connect attempt failed for {HardwareAddress}.", hardwareAddress);
            return ConnectionOperationResult.FromFailure("Windows rejected the Bluetooth connection attempt.");
        }
    }

    public async Task<IReadOnlyList<BluetoothLiveConnectionDetails>> GetLiveConnectionsAsync(
        IReadOnlyCollection<BluetoothConnectionCandidate> devices,
        CancellationToken cancellationToken)
    {
        var results = new List<BluetoothLiveConnectionDetails>();

        foreach (var device in devices)
        {
            if (!TryParseBluetoothAddress(device.HardwareAddress, out var classicAddress, out var bluetoothAddress))
            {
                continue;
            }

            try
            {
                var deviceInfo = new BluetoothDeviceInfo(classicAddress);
                deviceInfo.Refresh();

                var session = await GetOrCreateSessionAsync(device.HardwareAddress, bluetoothAddress, cancellationToken);
                var batteryPercent = await TryReadBatteryPercentAsync(session, cancellationToken);
                var isConnected = session?.ConnectionStatus == BluetoothConnectionStatus.Connected || deviceInfo.Connected;

                if (!isConnected && !deviceInfo.Authenticated && session is null)
                {
                    continue;
                }

                results.Add(new BluetoothLiveConnectionDetails(
                    device.DeviceName,
                    device.HardwareAddress,
                    isConnected,
                    deviceInfo.Authenticated,
                    batteryPercent,
                    null,
                    device.SignalStrength));
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Skipping Bluetooth live details for {HardwareAddress}.", device.HardwareAddress);
            }
        }

        return results
            .OrderByDescending(x => x.IsConnected)
            .ThenBy(x => x.DeviceName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var pair in _sessions)
        {
            pair.Value.Dispose();
        }

        _sessions.Clear();
        await Task.CompletedTask;
    }

    private async Task<BluetoothLEDevice?> GetOrCreateSessionAsync(string hardwareAddress, ulong bluetoothAddress, CancellationToken cancellationToken)
    {
        if (_sessions.TryGetValue(hardwareAddress, out var existing))
        {
            return existing;
        }

        var device = await BluetoothLEDevice.FromBluetoothAddressAsync(bluetoothAddress).AsTask(cancellationToken);
        if (device is not null)
        {
            _sessions.TryAdd(hardwareAddress, device);
        }

        return device;
    }

    private static async Task<int?> TryReadBatteryPercentAsync(BluetoothLEDevice? device, CancellationToken cancellationToken)
    {
        if (device is null)
        {
            return null;
        }

        try
        {
            var servicesResult = await device.GetGattServicesForUuidAsync(BatteryServiceUuid, BluetoothCacheMode.Uncached).AsTask(cancellationToken);
            if (servicesResult.Status != GattCommunicationStatus.Success)
            {
                return null;
            }

            var service = servicesResult.Services.FirstOrDefault();
            if (service is null)
            {
                return null;
            }

            using var _ = service;
            var characteristicsResult = await service.GetCharacteristicsForUuidAsync(BatteryLevelCharacteristicUuid, BluetoothCacheMode.Uncached).AsTask(cancellationToken);
            if (characteristicsResult.Status != GattCommunicationStatus.Success)
            {
                return null;
            }

            var characteristic = characteristicsResult.Characteristics.FirstOrDefault();
            if (characteristic is null)
            {
                return null;
            }

            var readResult = await characteristic.ReadValueAsync(BluetoothCacheMode.Uncached).AsTask(cancellationToken);
            if (readResult.Status != GattCommunicationStatus.Success || readResult.Value is null)
            {
                return null;
            }

            using var reader = DataReader.FromBuffer(readResult.Value);
            return reader.UnconsumedBufferLength > 0 ? reader.ReadByte() : null;
        }
        catch
        {
            return null;
        }
    }

    private static bool TryParseBluetoothAddress(string? hardwareAddress, out BluetoothAddress classicAddress, out ulong bluetoothAddress)
    {
        classicAddress = default!;
        bluetoothAddress = 0;

        if (string.IsNullOrWhiteSpace(hardwareAddress))
        {
            return false;
        }

        var normalized = hardwareAddress.Replace(":", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .Trim();

        if (!ulong.TryParse(normalized, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out bluetoothAddress))
        {
            return false;
        }

        classicAddress = BluetoothAddress.Parse(normalized);
        return true;
    }
}

public sealed record BluetoothConnectionCandidate(
    string DeviceName,
    string HardwareAddress,
    int? SignalStrength);

public sealed record BluetoothLiveConnectionDetails(
    string DeviceName,
    string HardwareAddress,
    bool IsConnected,
    bool IsPaired,
    int? BatteryPercent,
    string? IpAddress,
    int? SignalStrength);
