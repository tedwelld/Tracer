using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Tracer.Core.Enums;
using Tracer.Infrastructure.Persistence;
using Tracer.Web.Infrastructure;
using Tracer.Web.Services;

namespace Tracer.Web.Pages;

public sealed class BluetoothModel(
    IDbContextFactory<TracerDbContext> dbContextFactory,
    BluetoothConnectionService bluetoothConnectionService) : PageModel
{
    public IReadOnlyList<BluetoothDeviceDto> BluetoothDevices { get; private set; } = Array.Empty<BluetoothDeviceDto>();
    public IReadOnlyList<BluetoothLiveConnectionDetails> ConnectedDevices { get; private set; } = Array.Empty<BluetoothLiveConnectionDetails>();

    [BindProperty(SupportsGet = true)]
    public string? SearchTerm { get; set; }

    [BindProperty(SupportsGet = true)]
    public DateOnly? SearchDate { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? KnownFilter { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? PairingFilter { get; set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        BluetoothDevices = await LoadDevicesAsync(cancellationToken);
        ConnectedDevices = await bluetoothConnectionService.GetLiveConnectionsAsync(
            BluetoothDevices
                .Where(x => !string.IsNullOrWhiteSpace(x.HardwareAddress) && !string.Equals(x.HardwareAddress, "Unknown", StringComparison.OrdinalIgnoreCase))
                .Select(x => new BluetoothConnectionCandidate(x.DeviceName, x.HardwareAddress, x.SignalStrength))
                .ToList(),
            cancellationToken);
    }

    public async Task<FileContentResult> OnGetExportPdfAsync(CancellationToken cancellationToken)
    {
        var devices = await LoadDevicesAsync(cancellationToken);
        var blocks = new List<PdfBlock>
        {
            new PdfParagraph($"Filters: term={SearchTerm ?? "all"}, date={(SearchDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "all")}, known={KnownFilter ?? "all"}, paired={PairingFilter ?? "all"}"),
            new PdfTable(
                ["Device", "Address", "Signal", "Pairing", "Trust", "Risk", "State", "Last Seen"],
                devices.Select(device => new[]
                {
                    device.DeviceName,
                    device.HardwareAddress,
                    $"{device.SignalStrength}%",
                    device.IsPaired ? "Paired" : "Unpaired",
                    device.IsKnown ? "Known" : "Unknown",
                    device.RiskScore.ToString(CultureInfo.InvariantCulture),
                    device.ConnectionState,
                    device.LastSeenUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)
                }).ToList())
        };

        return File(
            PdfReportBuilder.Build("Tracer Bluetooth Device Report", blocks),
            "application/pdf",
            "tracer-bluetooth-report.pdf");
    }

    private async Task<IReadOnlyList<BluetoothDeviceDto>> LoadDevicesAsync(CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var query = dbContext.DiscoveredDevices
            .AsNoTracking()
            .Where(x => x.RadioKind == RadioKind.Bluetooth);

        if (!string.IsNullOrWhiteSpace(SearchTerm))
        {
            var term = SearchTerm.Trim();
            query = query.Where(x =>
                (x.DisplayName != null && x.DisplayName.Contains(term)) ||
                (x.HardwareAddress != null && x.HardwareAddress.Contains(term)) ||
                (x.VendorName != null && x.VendorName.Contains(term)) ||
                x.DeviceKey.Contains(term));
        }

        if (SearchDate.HasValue)
        {
            var startUtc = new DateTimeOffset(DateTime.SpecifyKind(SearchDate.Value.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc));
            var endUtc = startUtc.AddDays(1);
            query = query.Where(x => x.LastSeenUtc >= startUtc && x.LastSeenUtc < endUtc);
        }

        if (KnownFilter is "known")
        {
            query = query.Where(x => x.IsKnown);
        }
        else if (KnownFilter is "unknown")
        {
            query = query.Where(x => !x.IsKnown);
        }

        if (PairingFilter is "paired")
        {
            query = query.Where(x => x.IsPaired);
        }
        else if (PairingFilter is "unpaired")
        {
            query = query.Where(x => !x.IsPaired);
        }

        return await query
            .OrderByDescending(x => x.LastSeenUtc)
            .Select(x => new BluetoothDeviceDto(
                x.Id,
                x.DisplayName ?? x.DeviceKey,
                x.HardwareAddress ?? "Unknown",
                x.LastSignalStrength ?? 0,
                x.IsKnown,
                x.IsPaired,
                x.DisplayName,
                x.VendorName,
                x.DeviceType.ToString(),
                x.Reputation.ToString(),
                x.RiskScore,
                x.EstimatedDistanceMeters,
                x.MovementTrend.ToString(),
                x.ConnectionState.ToString(),
                x.FirstSeenUtc,
                x.LastSeenUtc,
                x.TotalObservations))
            .ToListAsync(cancellationToken);
    }

    public sealed record BluetoothDeviceDto(
        Guid Id,
        string DeviceName,
        string HardwareAddress,
        int SignalStrength,
        bool IsKnown,
        bool IsPaired,
        string? DisplayName,
        string? VendorName,
        string DeviceType,
        string Reputation,
        int RiskScore,
        decimal? EstimatedDistanceMeters,
        string MovementTrend,
        string ConnectionState,
        DateTimeOffset FirstSeenUtc,
        DateTimeOffset LastSeenUtc,
        int TotalObservations);
}
