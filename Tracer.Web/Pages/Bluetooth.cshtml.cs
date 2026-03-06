using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Tracer.Core.Enums;
using Tracer.Infrastructure.Persistence;

namespace Tracer.Web.Pages;

public sealed class BluetoothModel(IDbContextFactory<TracerDbContext> dbContextFactory) : PageModel
{
    public IReadOnlyList<BluetoothDeviceDto> BluetoothDevices { get; private set; } = Array.Empty<BluetoothDeviceDto>();

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        BluetoothDevices = await dbContext.DiscoveredDevices
            .AsNoTracking()
            .Where(x => x.RadioKind == RadioKind.Bluetooth)
            .OrderByDescending(x => x.LastSeenUtc)
            .Select(x => new BluetoothDeviceDto(
                x.Id,
                x.DisplayName ?? x.DeviceKey,
                x.HardwareAddress ?? "Unknown",
                x.LastSignalStrength ?? 0,
                x.IsKnown,
                x.IsPaired,
                x.DisplayName,
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
        DateTimeOffset FirstSeenUtc,
        DateTimeOffset LastSeenUtc,
        int TotalObservations);
}
