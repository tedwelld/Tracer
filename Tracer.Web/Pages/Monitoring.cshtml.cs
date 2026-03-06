using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Tracer.Core.Entities;
using Tracer.Core.Enums;
using Tracer.Infrastructure.Persistence;
using Tracer.Web.Infrastructure;

namespace Tracer.Web.Pages;

public sealed class MonitoringModel(IDbContextFactory<TracerDbContext> dbContextFactory) : PageModel
{
    private const int DefaultEventCount = 40;
    private const int DefaultObservationCount = 20;

    public MonitoringViewModel Monitoring { get; private set; } = MonitoringViewModel.Empty;

    [BindProperty(SupportsGet = true)]
    public string? SearchLevel { get; set; }

    [BindProperty(SupportsGet = true)]
    public DateOnly? SearchDate { get; set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Monitoring = await BuildViewModelAsync(cancellationToken);
    }

    public async Task<FileContentResult> OnGetExportPdfAsync(CancellationToken cancellationToken)
    {
        var monitoring = await BuildViewModelAsync(cancellationToken);
        return File(
            PdfReportBuilder.Build("Tracer Monitoring Report", BuildExportBlocks(monitoring)),
            "application/pdf",
            "tracer-monitoring-report.pdf");
    }

    private async Task<MonitoringViewModel> BuildViewModelAsync(CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var todayStart = new DateTimeOffset(now.UtcDateTime.Date, TimeSpan.Zero);
        var weekStart = todayStart.AddDays(-6);

        var latestBatch = await dbContext.ScanBatches
            .AsNoTracking()
            .OrderByDescending(x => x.CompletedUtc)
            .Select(x => new ScanBatchOverview(
                x.CompletedUtc,
                x.DurationMilliseconds,
                x.TotalDevices,
                x.WifiDevices,
                x.BluetoothDevices,
                x.ErrorCount,
                x.SuspiciousDevices,
                x.MemoryUsageMb,
                x.AdapterStatusSummary,
                x.ScannerNode))
            .FirstOrDefaultAsync(cancellationToken);

        var eventLogsQuery = dbContext.ScanEventLogs
            .AsNoTracking()
            .OrderByDescending(x => x.CreatedUtc)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(SearchLevel)
            && Enum.TryParse<ScanEventLevel>(SearchLevel, true, out var level))
        {
            eventLogsQuery = eventLogsQuery.Where(x => x.Level == level);
        }

        if (SearchDate.HasValue)
        {
            var startUtc = new DateTimeOffset(DateTime.SpecifyKind(SearchDate.Value.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc));
            var endUtc = startUtc.AddDays(1);
            eventLogsQuery = eventLogsQuery.Where(x => x.CreatedUtc >= startUtc && x.CreatedUtc < endUtc);
        }

        var recentEvents = await eventLogsQuery
            .Select(x => new ScanEventOverview(
                x.CreatedUtc,
                x.Level.ToString(),
                x.EventType.ToString(),
                x.RadioKind,
                x.Message,
                x.Details,
                x.Device != null
                    ? x.Device.DisplayName ?? x.Device.NetworkName ?? x.Device.HardwareAddress ?? x.Device.DeviceKey
                    : null))
            .Take(DefaultEventCount)
            .ToListAsync(cancellationToken);

        var observationsQuery = dbContext.DeviceObservations
            .AsNoTracking()
            .OrderByDescending(x => x.ObservedUtc)
            .AsQueryable();

        if (SearchDate.HasValue)
        {
            var startUtc = new DateTimeOffset(DateTime.SpecifyKind(SearchDate.Value.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc));
            var endUtc = startUtc.AddDays(1);
            observationsQuery = observationsQuery.Where(x => x.ObservedUtc >= startUtc && x.ObservedUtc < endUtc);
        }

        var recentObservations = await observationsQuery
            .Select(x => new ObservationOverview(
                x.ObservedUtc,
                x.Device!.RadioKind.ToString(),
                x.DisplayName ?? x.NetworkName ?? x.HardwareAddress ?? x.Device!.DeviceKey,
                x.SignalStrength,
                x.RiskScore,
                x.ConnectionState.ToString(),
                x.MovementTrend.ToString()))
            .Take(DefaultObservationCount)
            .ToListAsync(cancellationToken);

        var todayScans = await dbContext.ScanBatches
            .AsNoTracking()
            .Where(x => x.CompletedUtc >= todayStart)
            .ToListAsync(cancellationToken);

        var weekSuspicious = await dbContext.DeviceObservations
            .AsNoTracking()
            .Where(x => x.ObservedUtc >= weekStart && x.RiskScore >= 60)
            .CountAsync(cancellationToken);

        var todayErrors = recentEvents.Count(x => x.Level == ScanEventLevel.Error.ToString() && x.CreatedUtc >= todayStart);
        var averageDurationMs = todayScans.Count == 0 ? 0 : todayScans.Average(x => x.DurationMilliseconds);

        return new MonitoringViewModel(
            latestBatch,
            new HealthOverview(
                todayScans.Count,
                todayErrors,
                weekSuspicious,
                Math.Round(averageDurationMs / 1000d, 2, MidpointRounding.AwayFromZero)),
            recentEvents,
            recentObservations);
    }

    private IReadOnlyList<PdfBlock> BuildExportBlocks(MonitoringViewModel monitoring)
    {
        var blocks = new List<PdfBlock>
        {
            new PdfParagraph($"Filters: level={SearchLevel ?? "all"}, date={(SearchDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "all")}"),
            new PdfParagraph("Health summary"),
            new PdfTable(
                ["Scans Today", "Errors Today", "Suspicious This Week", "Average Duration (s)"],
                new List<string[]>
                {
                    new[]
                    {
                        monitoring.Health.TodayScanCount.ToString(CultureInfo.InvariantCulture),
                        monitoring.Health.TodayErrorCount.ToString(CultureInfo.InvariantCulture),
                        monitoring.Health.WeekSuspiciousObservations.ToString(CultureInfo.InvariantCulture),
                        monitoring.Health.AverageDurationSeconds.ToString("0.##", CultureInfo.InvariantCulture)
                    }
                })
        };

        if (monitoring.LatestBatch is not null)
        {
            blocks.Add(new PdfParagraph("Latest worker batch"));
            blocks.Add(new PdfTable(
                ["Completed", "Node", "Duration (s)", "Devices", "Wi-Fi", "Bluetooth", "Errors", "Suspicious"],
                new List<string[]>
                {
                    new[]
                    {
                        monitoring.LatestBatch.CompletedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
                        monitoring.LatestBatch.ScannerNode,
                        (monitoring.LatestBatch.DurationMilliseconds / 1000d).ToString("0.##", CultureInfo.InvariantCulture),
                        monitoring.LatestBatch.TotalDevices.ToString(CultureInfo.InvariantCulture),
                        monitoring.LatestBatch.WifiDevices.ToString(CultureInfo.InvariantCulture),
                        monitoring.LatestBatch.BluetoothDevices.ToString(CultureInfo.InvariantCulture),
                        monitoring.LatestBatch.ErrorCount.ToString(CultureInfo.InvariantCulture),
                        monitoring.LatestBatch.SuspiciousDevices.ToString(CultureInfo.InvariantCulture)
                    }
                }));
            blocks.Add(new PdfParagraph($"Adapter status: {monitoring.LatestBatch.AdapterStatusSummary ?? "No adapter summary was captured."}"));
        }

        blocks.Add(new PdfParagraph("Recent event log"));
        blocks.Add(new PdfTable(
            ["Time", "Level", "Event", "Radio", "Device", "Message"],
            monitoring.RecentEvents.Select(entry => new[]
            {
                entry.CreatedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
                entry.Level,
                entry.EventType,
                entry.RadioKind ?? "System",
                entry.DeviceLabel ?? "-",
                entry.Message
            }).ToList()));
        blocks.Add(new PdfParagraph("Recent observations"));
        blocks.Add(new PdfTable(
            ["Time", "Radio", "Device", "Signal", "Risk", "State", "Trend"],
            monitoring.RecentObservations.Select(observation => new[]
            {
                observation.ObservedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
                observation.RadioKind,
                observation.DeviceLabel,
                observation.SignalStrength?.ToString(CultureInfo.InvariantCulture) ?? "-",
                observation.RiskScore.ToString(CultureInfo.InvariantCulture),
                observation.ConnectionState,
                observation.MovementTrend
            }).ToList()));

        return blocks;
    }

    public sealed record MonitoringViewModel(
        ScanBatchOverview? LatestBatch,
        HealthOverview Health,
        IReadOnlyList<ScanEventOverview> RecentEvents,
        IReadOnlyList<ObservationOverview> RecentObservations)
    {
        public static MonitoringViewModel Empty { get; } = new(
            null,
            new HealthOverview(0, 0, 0, 0),
            Array.Empty<ScanEventOverview>(),
            Array.Empty<ObservationOverview>());
    }

    public sealed record ScanBatchOverview(
        DateTimeOffset CompletedUtc,
        long DurationMilliseconds,
        int TotalDevices,
        int WifiDevices,
        int BluetoothDevices,
        int ErrorCount,
        int SuspiciousDevices,
        double MemoryUsageMb,
        string? AdapterStatusSummary,
        string ScannerNode);

    public sealed record HealthOverview(
        int TodayScanCount,
        int TodayErrorCount,
        int WeekSuspiciousObservations,
        double AverageDurationSeconds);

    public sealed record ScanEventOverview(
        DateTimeOffset CreatedUtc,
        string Level,
        string EventType,
        string? RadioKind,
        string Message,
        string? Details,
        string? DeviceLabel);

    public sealed record ObservationOverview(
        DateTimeOffset ObservedUtc,
        string RadioKind,
        string DeviceLabel,
        int? SignalStrength,
        int RiskScore,
        string ConnectionState,
        string MovementTrend);
}
