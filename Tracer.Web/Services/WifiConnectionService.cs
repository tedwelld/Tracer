using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security;
using System.Text;
using System.Text.RegularExpressions;
using ManagedNativeWifi;
using Microsoft.Extensions.Logging;

namespace Tracer.Web.Services;

public sealed class WifiConnectionService(ILogger<WifiConnectionService> logger)
{
    private static readonly Regex ArpLinePattern = new(
        @"^\s*(?<ip>\d+\.\d+\.\d+\.\d+)\s+(?<mac>([0-9a-f]{2}-){5}[0-9a-f]{2}|ff-ff-ff-ff-ff-ff)\s+(?<type>\w+)\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public async Task<ConnectionOperationResult> ConnectAsync(string ssid, string? password, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(ssid))
        {
            return ConnectionOperationResult.FromFailure("Wi-Fi network name is required.");
        }

        var network = NativeWifi.EnumerateAvailableNetworkGroups()
            .FirstOrDefault(x => string.Equals(x.Ssid.ToString(), ssid, StringComparison.Ordinal));

        if (network is null)
        {
            return ConnectionOperationResult.FromFailure("The selected Wi-Fi network is no longer visible.");
        }

        if (!network.IsConnectable)
        {
            return ConnectionOperationResult.FromFailure("Windows reports that this Wi-Fi network is not connectable right now.");
        }

        var profileName = BuildProfileName(ssid);
        var requiresCredential = RequiresCredential(network.AuthenticationAlgorithm);

        if (requiresCredential && string.IsNullOrWhiteSpace(password))
        {
            return ConnectionOperationResult.FromFailure("A password is required before the system can connect to this Wi-Fi network.");
        }

        var profileXml = BuildProfileXml(profileName, ssid, network.AuthenticationAlgorithm, network.CipherAlgorithm, password);
        var tempProfilePath = Path.Combine(Path.GetTempPath(), $"{profileName}.xml");

        await File.WriteAllTextAsync(tempProfilePath, profileXml, Encoding.UTF8, cancellationToken);

        try
        {
            var addProfile = await RunNetshAsync($"wlan add profile filename=\"{tempProfilePath}\" user=current", cancellationToken);
            if (addProfile.ExitCode != 0)
            {
                logger.LogWarning("Failed to add Wi-Fi profile for {Ssid}. Output: {Output}", ssid, addProfile.Output);
                return ConnectionOperationResult.FromFailure("Windows rejected the Wi-Fi profile for this network.");
            }

            var connect = await RunNetshAsync($"wlan connect name=\"{profileName}\" ssid=\"{ssid}\"", cancellationToken);
            if (connect.ExitCode != 0)
            {
                logger.LogWarning("Failed to connect to Wi-Fi {Ssid}. Output: {Output}", ssid, connect.Output);
                return ConnectionOperationResult.FromFailure("Windows could not connect to the selected Wi-Fi network.");
            }

            await Task.Delay(TimeSpan.FromSeconds(4), cancellationToken);

            var connectedSsid = NativeWifi.EnumerateConnectedNetworkSsids()
                .Select(x => x.ToString())
                .FirstOrDefault();

            return string.Equals(connectedSsid, ssid, StringComparison.Ordinal)
                ? ConnectionOperationResult.FromSuccess($"Connected to {ssid}.")
                : ConnectionOperationResult.FromFailure("Windows started the connection attempt, but the network is not active yet.");
        }
        finally
        {
            try
            {
                File.Delete(tempProfilePath);
            }
            catch (IOException)
            {
            }
        }
    }

    public async Task<WifiConnectionDetails?> GetCurrentConnectionAsync(CancellationToken cancellationToken)
    {
        var networkInterface = NetworkInterface.GetAllNetworkInterfaces()
            .FirstOrDefault(x =>
                x.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 &&
                x.OperationalStatus == OperationalStatus.Up);

        var networkName = NativeWifi.EnumerateConnectedNetworkSsids()
            .Select(x => x.ToString())
            .FirstOrDefault();

        if (networkInterface is null || string.IsNullOrWhiteSpace(networkName))
        {
            return null;
        }

        var currentGroup = NativeWifi.EnumerateAvailableNetworkGroups()
            .Where(x => string.Equals(x.Ssid.ToString(), networkName, StringComparison.Ordinal))
            .OrderByDescending(x => x.SignalQuality)
            .FirstOrDefault();

        var ipv4 = networkInterface.GetIPProperties().UnicastAddresses
            .FirstOrDefault(x => x.Address.AddressFamily == AddressFamily.InterNetwork);

        var gateway = networkInterface.GetIPProperties().GatewayAddresses
            .FirstOrDefault(x => x.Address.AddressFamily == AddressFamily.InterNetwork)
            ?.Address
            .ToString();

        var peers = ipv4 is null
            ? Array.Empty<WifiPeerDevice>()
            : await GetLanPeersAsync(ipv4.Address, ipv4.IPv4Mask, gateway, cancellationToken);

        return new WifiConnectionDetails(
            networkName,
            currentGroup?.SignalQuality ?? 0,
            ipv4?.Address.ToString(),
            gateway,
            peers);
    }

    private static bool RequiresCredential(AuthenticationAlgorithm algorithm)
        => algorithm is not AuthenticationAlgorithm.Open
            and not AuthenticationAlgorithm.OWE;

    private static string BuildProfileName(string ssid)
    {
        var sanitized = new string(ssid.Where(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_').ToArray());
        sanitized = string.IsNullOrWhiteSpace(sanitized) ? "TracerNetwork" : sanitized;
        return $"Tracer-{sanitized}";
    }

    private static string BuildProfileXml(
        string profileName,
        string ssid,
        AuthenticationAlgorithm authentication,
        CipherAlgorithm cipher,
        string? password)
    {
        var authValue = authentication switch
        {
            AuthenticationAlgorithm.Open => "open",
            AuthenticationAlgorithm.Shared => "shared",
            AuthenticationAlgorithm.WPA => "WPA",
            AuthenticationAlgorithm.WPA_PSK => "WPAPSK",
            AuthenticationAlgorithm.RSNA => "WPA2",
            AuthenticationAlgorithm.RSNA_PSK => "WPA2PSK",
            AuthenticationAlgorithm.WPA3_ENT_192 => "WPA3ENT192",
            AuthenticationAlgorithm.WPA3_ENT => "WPA3ENT",
            AuthenticationAlgorithm.WPA3_SAE => "WPA3SAE",
            AuthenticationAlgorithm.OWE => "OWE",
            _ => "WPA2PSK"
        };

        var cipherValue = cipher switch
        {
            CipherAlgorithm.None => "none",
            CipherAlgorithm.WEP or CipherAlgorithm.WEP_40 or CipherAlgorithm.WEP_104 => "WEP",
            CipherAlgorithm.TKIP => "TKIP",
            _ => "AES"
        };

        var keySection = string.IsNullOrWhiteSpace(password)
            ? string.Empty
            : $"""
                    <sharedKey>
                        <keyType>passPhrase</keyType>
                        <protected>false</protected>
                        <keyMaterial>{SecurityElement.Escape(password)}</keyMaterial>
                    </sharedKey>
                """;

        return $"""
            <?xml version="1.0"?>
            <WLANProfile xmlns="http://www.microsoft.com/networking/WLAN/profile/v1">
                <name>{SecurityElement.Escape(profileName)}</name>
                <SSIDConfig>
                    <SSID>
                        <name>{SecurityElement.Escape(ssid)}</name>
                    </SSID>
                </SSIDConfig>
                <connectionType>ESS</connectionType>
                <connectionMode>manual</connectionMode>
                <MSM>
                    <security>
                        <authEncryption>
                            <authentication>{authValue}</authentication>
                            <encryption>{cipherValue}</encryption>
                            <useOneX>false</useOneX>
                        </authEncryption>
            {keySection}
                    </security>
                </MSM>
            </WLANProfile>
            """;
    }

    private async Task<IReadOnlyList<WifiPeerDevice>> GetLanPeersAsync(
        IPAddress localAddress,
        IPAddress? subnetMask,
        string? gatewayAddress,
        CancellationToken cancellationToken)
    {
        if (subnetMask is null)
        {
            return Array.Empty<WifiPeerDevice>();
        }

        var arp = await RunProcessAsync("arp", "-a", cancellationToken);
        if (arp.ExitCode != 0 || string.IsNullOrWhiteSpace(arp.Output))
        {
            return Array.Empty<WifiPeerDevice>();
        }

        var peers = new List<WifiPeerDevice>();
        var gateway = IPAddress.TryParse(gatewayAddress, out var parsedGateway) ? parsedGateway : null;

        foreach (var line in arp.Output.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries))
        {
            var match = ArpLinePattern.Match(line);
            if (!match.Success || !IPAddress.TryParse(match.Groups["ip"].Value, out var peerIp))
            {
                continue;
            }

            if (!IsSameSubnet(localAddress, peerIp, subnetMask))
            {
                continue;
            }

            if (peerIp.Equals(localAddress) || (gateway is not null && peerIp.Equals(gateway)))
            {
                continue;
            }

            var deviceName = await TryResolveHostNameAsync(peerIp, cancellationToken);
            peers.Add(new WifiPeerDevice(peerIp.ToString(), deviceName));
        }

        return peers
            .DistinctBy(x => x.IpAddress)
            .OrderBy(x => x.IpAddress, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool IsSameSubnet(IPAddress left, IPAddress right, IPAddress subnetMask)
    {
        var leftBytes = left.GetAddressBytes();
        var rightBytes = right.GetAddressBytes();
        var maskBytes = subnetMask.GetAddressBytes();

        if (leftBytes.Length != rightBytes.Length || leftBytes.Length != maskBytes.Length)
        {
            return false;
        }

        for (var i = 0; i < leftBytes.Length; i++)
        {
            if ((leftBytes[i] & maskBytes[i]) != (rightBytes[i] & maskBytes[i]))
            {
                return false;
            }
        }

        return true;
    }

    private static async Task<string?> TryResolveHostNameAsync(IPAddress address, CancellationToken cancellationToken)
    {
        try
        {
            using var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            linkedSource.CancelAfter(TimeSpan.FromMilliseconds(450));
            var lookup = Dns.GetHostEntryAsync(address);
            var completed = await Task.WhenAny(lookup, Task.Delay(Timeout.InfiniteTimeSpan, linkedSource.Token));
            if (completed == lookup)
            {
                var host = await lookup;
                return string.Equals(host.HostName, address.ToString(), StringComparison.OrdinalIgnoreCase)
                    ? null
                    : host.HostName;
            }
        }
        catch
        {
        }

        return null;
    }

    private static Task<ProcessResult> RunNetshAsync(string arguments, CancellationToken cancellationToken)
        => RunProcessAsync("netsh", arguments, cancellationToken);

    private static async Task<ProcessResult> RunProcessAsync(string fileName, string arguments, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo(fileName, arguments)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Unable to start process '{fileName}'.");

        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        var output = await outputTask;
        var error = await errorTask;
        return new ProcessResult(process.ExitCode, string.Join(Environment.NewLine, new[] { output, error }.Where(x => !string.IsNullOrWhiteSpace(x))));
    }

    private sealed record ProcessResult(int ExitCode, string Output);
}

public sealed record WifiConnectionDetails(
    string NetworkName,
    int SignalQuality,
    string? LocalIpAddress,
    string? GatewayAddress,
    IReadOnlyList<WifiPeerDevice> PeerDevices)
{
    public int PeerCount => PeerDevices.Count;
}

public sealed record WifiPeerDevice(string IpAddress, string? DeviceName);

public sealed record ConnectionOperationResult(bool Success, string Message)
{
    public static ConnectionOperationResult FromFailure(string message) => new(false, message);
    public static ConnectionOperationResult FromSuccess(string message) => new(true, message);
}
