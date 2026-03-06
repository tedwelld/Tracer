using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Tracer.Core.Enums;
using Tracer.Infrastructure.Persistence;

namespace Tracer.Web.Pages;

public sealed class IndexModel(IDbContextFactory<TracerDbContext> dbContextFactory) : PageModel
{
    public DashboardViewModel Dashboard { get; private set; } = DashboardViewModel.Empty;

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var latestScan = await dbContext.ScanBatches
            .AsNoTracking()
            .OrderByDescending(x => x.CompletedUtc)
            .Select(x => new ScanOverview(
                x.CompletedUtc,
                x.TotalDevices,
                x.WifiDevices,
                x.BluetoothDevices,
                x.ScannerNode))
            .FirstOrDefaultAsync(cancellationToken);

        var pendingAlerts = await dbContext.DeviceAlerts
            .AsNoTracking()
            .Where(x => x.Status == AlertStatus.Pending)
            .OrderByDescending(x => x.CreatedUtc)
            .Select(x => new AlertOverview(
                x.Id,
                x.DeviceId,
                x.Title,
                x.Message,
                x.CreatedUtc,
                x.Device!.DisplayName ?? x.Device.NetworkName ?? x.Device.HardwareAddress ?? x.Device.DeviceKey,
                x.Device!.RadioKind.ToString()))
            .Take(6)
            .ToListAsync(cancellationToken);

        var recentDevices = await dbContext.DiscoveredDevices
            .AsNoTracking()
            .OrderByDescending(x => x.LastSeenUtc)
            .Select(x => new DeviceOverview(
                x.Id,
                x.RadioKind.ToString(),
                x.DisplayName ?? x.NetworkName ?? x.HardwareAddress ?? x.DeviceKey,
                x.HardwareAddress,
                x.SecurityType,
                x.LastSignalStrength,
                x.IsKnown,
                x.IsPaired,
                x.LastSeenUtc))
            .Take(20)
            .ToListAsync(cancellationToken);

        Dashboard = new DashboardViewModel(
            latestScan,
            pendingAlerts,
            recentDevices,
            await dbContext.DiscoveredDevices.CountAsync(cancellationToken),
            await dbContext.DeviceAlerts.CountAsync(x => x.Status == AlertStatus.Pending, cancellationToken),
            await dbContext.DeviceObservations.CountAsync(cancellationToken));
    }

    public sealed record DashboardViewModel(
        ScanOverview? LatestScan,
        IReadOnlyList<AlertOverview> PendingAlerts,
        IReadOnlyList<DeviceOverview> RecentDevices,
        int TotalTrackedDevices,
        int PendingAlertCount,
        int TotalObservations)
    {
        public static DashboardViewModel Empty { get; } = new(
            null,
            Array.Empty<AlertOverview>(),
            Array.Empty<DeviceOverview>(),
            0,
            0,
            0);
    }

    public sealed record ScanOverview(
        DateTimeOffset CompletedUtc,
        int TotalDevices,
        int WifiDevices,
        int BluetoothDevices,
        string ScannerNode);

    public sealed record AlertOverview(
        long Id,
        Guid DeviceId,
        string Title,
        string Message,
        DateTimeOffset CreatedUtc,
        string DeviceLabel,
        string RadioKind);

    public sealed record DeviceOverview(
        Guid Id,
        string RadioKind,
        string Label,
        string? HardwareAddress,
        string? SecurityType,
        int? SignalStrength,
        bool IsKnown,
        bool IsPaired,
        DateTimeOffset LastSeenUtc);
}
