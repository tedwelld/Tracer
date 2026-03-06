using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Tracer.Core.Enums;
using Tracer.Infrastructure.Persistence;

namespace Tracer.Web.Pages;

public sealed class IndexModel(IDbContextFactory<TracerDbContext> dbContextFactory) : PageModel
{
    private static readonly TimeSpan ActiveThreshold = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan QuietThreshold = TimeSpan.FromHours(1);
    private const int DefaultRecentDeviceCount = 5;

    public DashboardViewModel Dashboard { get; private set; } = DashboardViewModel.Empty;

    [BindProperty(SupportsGet = true)]
    public string? SearchName { get; set; }

    [BindProperty(SupportsGet = true)]
    public DateOnly? SearchDate { get; set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;

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

        var recentDevicesQuery = dbContext.DiscoveredDevices
            .AsNoTracking();

        if (!string.IsNullOrWhiteSpace(SearchName))
        {
            var searchTerm = SearchName.Trim();
            recentDevicesQuery = recentDevicesQuery.Where(x =>
                (x.DisplayName != null && x.DisplayName.Contains(searchTerm))
                || (x.NetworkName != null && x.NetworkName.Contains(searchTerm))
                || (x.HardwareAddress != null && x.HardwareAddress.Contains(searchTerm))
                || x.DeviceKey.Contains(searchTerm));
        }

        if (SearchDate.HasValue)
        {
            var startUtc = new DateTimeOffset(DateTime.SpecifyKind(SearchDate.Value.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc));
            var endUtc = startUtc.AddDays(1);
            recentDevicesQuery = recentDevicesQuery.Where(x => x.LastSeenUtc >= startUtc && x.LastSeenUtc < endUtc);
        }

        var recentDevices = await recentDevicesQuery
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
            .Take(DefaultRecentDeviceCount)
            .ToListAsync(cancellationToken);

        var radioCounts = await dbContext.DiscoveredDevices
            .AsNoTracking()
            .GroupBy(x => x.RadioKind)
            .Select(group => new
            {
                RadioKind = group.Key,
                Count = group.Count()
            })
            .ToListAsync(cancellationToken);

        var wifiCount = radioCounts.SingleOrDefault(x => x.RadioKind == RadioKind.Wifi)?.Count ?? 0;
        var bluetoothCount = radioCounts.SingleOrDefault(x => x.RadioKind == RadioKind.Bluetooth)?.Count ?? 0;

        var activeThreshold = now - ActiveThreshold;
        var quietThreshold = now - QuietThreshold;

        var activeCount = await dbContext.DiscoveredDevices.CountAsync(
            x => x.LastSeenUtc >= activeThreshold,
            cancellationToken);

        var quietCount = await dbContext.DiscoveredDevices.CountAsync(
            x => x.LastSeenUtc < activeThreshold && x.LastSeenUtc >= quietThreshold,
            cancellationToken);

        var offlineCount = await dbContext.DiscoveredDevices.CountAsync(
            x => x.LastSeenUtc < quietThreshold,
            cancellationToken);

        Dashboard = new DashboardViewModel(
            latestScan,
            pendingAlerts,
            recentDevices,
            wifiCount + bluetoothCount,
            await dbContext.DeviceAlerts.CountAsync(x => x.Status == AlertStatus.Pending, cancellationToken),
            await dbContext.DeviceObservations.CountAsync(cancellationToken),
            new RadioDistribution(wifiCount, bluetoothCount),
            new ConnectivityBreakdown(activeCount, quietCount, offlineCount));
    }

    public sealed record DashboardViewModel(
        ScanOverview? LatestScan,
        IReadOnlyList<AlertOverview> PendingAlerts,
        IReadOnlyList<DeviceOverview> RecentDevices,
        int TotalTrackedDevices,
        int PendingAlertCount,
        int TotalObservations,
        RadioDistribution RadioDistribution,
        ConnectivityBreakdown ConnectivityBreakdown)
    {
        public static DashboardViewModel Empty { get; } = new(
            null,
            Array.Empty<AlertOverview>(),
            Array.Empty<DeviceOverview>(),
            0,
            0,
            0,
            new RadioDistribution(0, 0),
            new ConnectivityBreakdown(0, 0, 0));
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

    public sealed record RadioDistribution(
        int WifiCount,
        int BluetoothCount)
    {
        public int MaxCount => Math.Max(Math.Max(WifiCount, BluetoothCount), 1);
        public int TotalCount => WifiCount + BluetoothCount;
    }

    public sealed record ConnectivityBreakdown(
        int ActiveCount,
        int QuietCount,
        int OfflineCount)
    {
        public int TotalCount => ActiveCount + QuietCount + OfflineCount;
    }

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
