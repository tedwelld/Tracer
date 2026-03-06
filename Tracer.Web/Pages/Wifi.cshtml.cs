using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Tracer.Core.Enums;
using Tracer.Infrastructure.Persistence;

namespace Tracer.Web.Pages;

public sealed class WifiModel(IDbContextFactory<TracerDbContext> dbContextFactory) : PageModel
{
    public IReadOnlyList<WifiDeviceDto> WifiDevices { get; private set; } = Array.Empty<WifiDeviceDto>();

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        WifiDevices = await dbContext.DiscoveredDevices
            .AsNoTracking()
            .Where(x => x.RadioKind == RadioKind.Wifi)
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
        DateTimeOffset FirstSeenUtc,
        DateTimeOffset LastSeenUtc,
        int TotalObservations,
        int? Channel,
        string? FrequencyBand);
}
