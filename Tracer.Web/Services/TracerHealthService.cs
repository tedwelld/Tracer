using Microsoft.EntityFrameworkCore;
using Tracer.Infrastructure.Persistence;
using Tracer.Infrastructure.Services;
using Tracer.Core.Interfaces;

namespace Tracer.Web.Services;

public sealed class TracerHealthService(
    IDbContextFactory<TracerDbContext> dbContextFactory,
    IRuntimeSettingsService runtimeSettingsService,
    OuiVendorLookupService ouiVendorLookupService)
{
    public async Task<TracerHealthSnapshot> GetSnapshotAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
            var latestBatch = await dbContext.ScanBatches
                .AsNoTracking()
                .OrderByDescending(x => x.CompletedUtc)
                .Select(x => new
                {
                    x.CompletedUtc,
                    x.ScannerNode,
                    x.AdapterStatusSummary,
                    x.ErrorCount
                })
                .FirstOrDefaultAsync(cancellationToken);

            var pendingAlerts = await dbContext.DeviceAlerts
                .AsNoTracking()
                .CountAsync(x => x.Status == Core.Enums.AlertStatus.Pending, cancellationToken);

            var settings = await runtimeSettingsService.GetCurrentAsync(cancellationToken);
            var ouiStatus = await ouiVendorLookupService.GetStatusAsync(cancellationToken);
            var now = DateTimeOffset.UtcNow;
            var maxScanAge = TimeSpan.FromSeconds(Math.Max(30, settings.ScanIntervalSeconds * 3));
            var scannerHealthy = latestBatch is not null && now - latestBatch.CompletedUtc <= maxScanAge;

            return new TracerHealthSnapshot(
                DatabaseHealthy: true,
                ScannerHealthy: scannerHealthy,
                LatestScanUtc: latestBatch?.CompletedUtc,
                ScannerNode: latestBatch?.ScannerNode,
                AdapterSummary: latestBatch?.AdapterStatusSummary,
                PendingAlertCount: pendingAlerts,
                OuiVendorCount: ouiStatus.Count,
                OuiCacheUpdatedUtc: ouiStatus.LastUpdatedUtc,
                Status: scannerHealthy ? "Healthy" : "Degraded");
        }
        catch (Exception ex)
        {
            return new TracerHealthSnapshot(
                DatabaseHealthy: false,
                ScannerHealthy: false,
                LatestScanUtc: null,
                ScannerNode: null,
                AdapterSummary: ex.Message,
                PendingAlertCount: 0,
                OuiVendorCount: 0,
                OuiCacheUpdatedUtc: null,
                Status: "Unhealthy");
        }
    }
}

public sealed record TracerHealthSnapshot(
    bool DatabaseHealthy,
    bool ScannerHealthy,
    DateTimeOffset? LatestScanUtc,
    string? ScannerNode,
    string? AdapterSummary,
    int PendingAlertCount,
    int OuiVendorCount,
    DateTimeOffset? OuiCacheUpdatedUtc,
    string Status);
