using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Tracer.Core.Enums;
using Tracer.Infrastructure.Persistence;
using Tracer.Web.Infrastructure;
using Tracer.Web.Services;

namespace Tracer.Web.Pages;

public sealed class WifiModel(
    IDbContextFactory<TracerDbContext> dbContextFactory,
    WifiConnectionService wifiConnectionService) : PageModel
{
    public IReadOnlyList<WifiDeviceDto> WifiDevices { get; private set; } = Array.Empty<WifiDeviceDto>();
    public WifiConnectionDetails? ConnectedNetwork { get; private set; }

    [BindProperty(SupportsGet = true)]
    public string? SearchTerm { get; set; }

    [BindProperty(SupportsGet = true)]
    public DateOnly? SearchDate { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? KnownFilter { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? SecurityFilter { get; set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        WifiDevices = await LoadDevicesAsync(cancellationToken);
        ConnectedNetwork = await wifiConnectionService.GetCurrentConnectionAsync(cancellationToken);
    }

    public async Task<FileContentResult> OnGetExportPdfAsync(CancellationToken cancellationToken)
    {
        var devices = await LoadDevicesAsync(cancellationToken);
        var blocks = new List<PdfBlock>
        {
            new PdfParagraph($"Filters: term={SearchTerm ?? "all"}, date={(SearchDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "all")}, known={KnownFilter ?? "all"}, security={SecurityFilter ?? "all"}"),
            new PdfTable(
                ["Network", "Address", "Security", "Signal", "Trust", "Risk", "State", "Last Seen"],
                devices.Select(device => new[]
                {
                    device.NetworkName,
                    device.HardwareAddress,
                    device.SecurityType,
                    $"{device.SignalStrength}%",
                    device.IsKnown ? "Known" : "Unknown",
                    device.RiskScore.ToString(CultureInfo.InvariantCulture),
                    device.ConnectionState,
                    device.LastSeenUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)
                }).ToList())
        };

        return File(
            PdfReportBuilder.Build("Tracer Wi-Fi Device Report", blocks),
            "application/pdf",
            "tracer-wifi-report.pdf");
    }

    private async Task<IReadOnlyList<WifiDeviceDto>> LoadDevicesAsync(CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var query = dbContext.DiscoveredDevices
            .AsNoTracking()
            .Where(x => x.RadioKind == RadioKind.Wifi);

        if (!string.IsNullOrWhiteSpace(SearchTerm))
        {
            var term = SearchTerm.Trim();
            query = query.Where(x =>
                (x.NetworkName != null && x.NetworkName.Contains(term)) ||
                (x.DisplayName != null && x.DisplayName.Contains(term)) ||
                (x.HardwareAddress != null && x.HardwareAddress.Contains(term)) ||
                (x.VendorName != null && x.VendorName.Contains(term)));
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

        if (!string.IsNullOrWhiteSpace(SecurityFilter))
        {
            var security = SecurityFilter.Trim();
            query = query.Where(x => x.SecurityType != null && x.SecurityType.Contains(security));
        }

        return await query
            .OrderByDescending(x => x.LastSeenUtc)
            .Select(x => new WifiDeviceDto(
                x.Id,
                x.NetworkName ?? x.DeviceKey,
                x.HardwareAddress ?? "Unknown",
                x.SecurityType ?? "Open",
                x.LastSignalStrength ?? 0,
                x.IsKnown,
                x.Password,
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
                x.TotalObservations,
                x.Channel,
                x.FrequencyBand))
            .ToListAsync(cancellationToken);
    }

    public sealed record WifiDeviceDto(
        Guid Id,
        string NetworkName,
        string HardwareAddress,
        string SecurityType,
        int SignalStrength,
        bool IsKnown,
        string? Password,
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
        int TotalObservations,
        int? Channel,
        string? FrequencyBand);
}
