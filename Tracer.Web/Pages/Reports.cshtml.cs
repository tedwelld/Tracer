using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Tracer.Infrastructure.Persistence;
using Tracer.Web.Infrastructure;

namespace Tracer.Web.Pages;

public sealed class ReportsModel(IDbContextFactory<TracerDbContext> dbContextFactory) : PageModel
{
    private const int DailyWindowDays = 3;
    private const int WeeklyWindowWeeks = 3;

    public ReportsViewModel Reports { get; private set; } = ReportsViewModel.Empty;

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Reports = await BuildViewModelAsync(cancellationToken);
    }

    public async Task<FileContentResult> OnGetExportPdfAsync(string scope, CancellationToken cancellationToken)
    {
        var reports = await BuildViewModelAsync(cancellationToken);
        var normalizedScope = scope?.ToLowerInvariant() ?? "daily";
        var title = normalizedScope switch
        {
            "weekly" => "Tracer Weekly Report",
            "devices" => "Tracer Risk Device Report",
            _ => "Tracer Daily Report"
        };

        var blocks = normalizedScope switch
        {
            "weekly" => BuildWeeklyReportBlocks(reports.WeeklyReports),
            "devices" => BuildDeviceReportBlocks(reports.TopRiskDevices),
            _ => BuildDailyReportBlocks(reports.DailyReports)
        };

        var fileName = normalizedScope switch
        {
            "weekly" => "tracer-weekly-report.pdf",
            "devices" => "tracer-risk-devices.pdf",
            _ => "tracer-daily-report.pdf"
        };

        var pdf = PdfReportBuilder.Build(title, blocks);
        return File(pdf, "application/pdf", fileName);
    }

    private async Task<ReportsViewModel> BuildViewModelAsync(CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var earliestDaily = new DateTimeOffset(now.UtcDateTime.Date.AddDays(-(DailyWindowDays - 1)), TimeSpan.Zero);
        var earliestWeekly = new DateTimeOffset(now.UtcDateTime.Date.AddDays(-7 * WeeklyWindowWeeks), TimeSpan.Zero);

        var recentBatches = await dbContext.ScanBatches
            .AsNoTracking()
            .Where(x => x.CompletedUtc >= earliestWeekly)
            .ToListAsync(cancellationToken);

        var recentObservations = await dbContext.DeviceObservations
            .AsNoTracking()
            .Where(x => x.ObservedUtc >= earliestWeekly)
            .Select(x => new ObservationSlice(x.ObservedUtc, x.DeviceId, x.RiskScore, x.ConnectionState.ToString()))
            .ToListAsync(cancellationToken);

        var recentAlerts = await dbContext.DeviceAlerts
            .AsNoTracking()
            .Where(x => x.CreatedUtc >= earliestWeekly)
            .Select(x => x.CreatedUtc)
            .ToListAsync(cancellationToken);

        var dailyReports = Enumerable.Range(0, DailyWindowDays)
            .Select(offset => earliestDaily.AddDays(offset))
            .Select(dayStart =>
            {
                var dayEnd = dayStart.AddDays(1);
                var dayBatches = recentBatches.Where(x => x.CompletedUtc >= dayStart && x.CompletedUtc < dayEnd).ToList();
                var dayObservations = recentObservations.Where(x => x.ObservedUtc >= dayStart && x.ObservedUtc < dayEnd).ToList();
                var dayAlerts = recentAlerts.Count(x => x >= dayStart && x < dayEnd);

                return new DailyReportRow(
                    DateOnly.FromDateTime(dayStart.UtcDateTime),
                    dayBatches.Count,
                    dayObservations.Count,
                    dayObservations.Select(x => x.DeviceId).Distinct().Count(),
                    dayBatches.Sum(x => x.TotalDevices),
                    dayBatches.Sum(x => x.SuspiciousDevices),
                    dayAlerts);
            })
            .OrderByDescending(x => x.Date)
            .ToList();

        var weeklyReports = Enumerable.Range(0, WeeklyWindowWeeks)
            .Select(offset =>
            {
                var weekStart = StartOfWeek(now.UtcDateTime.Date).AddDays(-7 * offset);
                var weekStartUtc = new DateTimeOffset(weekStart, TimeSpan.Zero);
                var weekEndUtc = weekStartUtc.AddDays(7);
                var weekBatches = recentBatches.Where(x => x.CompletedUtc >= weekStartUtc && x.CompletedUtc < weekEndUtc).ToList();
                var weekObservations = recentObservations.Where(x => x.ObservedUtc >= weekStartUtc && x.ObservedUtc < weekEndUtc).ToList();
                var weekAlerts = recentAlerts.Count(x => x >= weekStartUtc && x < weekEndUtc);

                return new WeeklyReportRow(
                    DateOnly.FromDateTime(weekStart),
                    DateOnly.FromDateTime(weekStart.AddDays(6)),
                    weekBatches.Count,
                    weekObservations.Count,
                    weekObservations.Select(x => x.DeviceId).Distinct().Count(),
                    weekBatches.Sum(x => x.SuspiciousDevices),
                    weekAlerts);
            })
            .OrderByDescending(x => x.WeekStart)
            .ToList();

        var topRiskDevices = await dbContext.DiscoveredDevices
            .AsNoTracking()
            .OrderByDescending(x => x.RiskScore)
            .ThenByDescending(x => x.LastSeenUtc)
            .Select(x => new RiskDeviceRow(
                x.RadioKind.ToString(),
                x.DisplayName ?? x.NetworkName ?? x.HardwareAddress ?? x.DeviceKey,
                x.VendorName,
                x.DeviceType.ToString(),
                x.Reputation.ToString(),
                x.RiskScore,
                x.LastRecommendation,
                x.LastSeenUtc))
            .Take(12)
            .ToListAsync(cancellationToken);

        return new ReportsViewModel(dailyReports, weeklyReports, topRiskDevices);
    }

    private static DateTime StartOfWeek(DateTime date)
    {
        var diff = (7 + (date.DayOfWeek - DayOfWeek.Monday)) % 7;
        return date.AddDays(-diff).Date;
    }

    private static IReadOnlyList<PdfBlock> BuildDailyReportBlocks(IEnumerable<DailyReportRow> rows)
        => new PdfBlock[]
        {
            new PdfTable(
                ["Date", "Scans", "Observations", "Unique", "Seen", "Suspicious", "Alerts"],
                rows.Select(row => new[]
                {
                    row.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    row.ScanBatches.ToString(CultureInfo.InvariantCulture),
                    row.ObservationCount.ToString(CultureInfo.InvariantCulture),
                    row.UniqueDevices.ToString(CultureInfo.InvariantCulture),
                    row.DevicesSeen.ToString(CultureInfo.InvariantCulture),
                    row.SuspiciousCount.ToString(CultureInfo.InvariantCulture),
                    row.AlertCount.ToString(CultureInfo.InvariantCulture)
                }).ToList())
        };

    private static IReadOnlyList<PdfBlock> BuildWeeklyReportBlocks(IEnumerable<WeeklyReportRow> rows)
        => new PdfBlock[]
        {
            new PdfTable(
                ["Week Start", "Week End", "Scans", "Observations", "Unique", "Suspicious", "Alerts"],
                rows.Select(row => new[]
                {
                    row.WeekStart.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    row.WeekEnd.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    row.ScanBatches.ToString(CultureInfo.InvariantCulture),
                    row.ObservationCount.ToString(CultureInfo.InvariantCulture),
                    row.UniqueDevices.ToString(CultureInfo.InvariantCulture),
                    row.SuspiciousCount.ToString(CultureInfo.InvariantCulture),
                    row.AlertCount.ToString(CultureInfo.InvariantCulture)
                }).ToList())
        };

    private static IReadOnlyList<PdfBlock> BuildDeviceReportBlocks(IEnumerable<RiskDeviceRow> rows)
        => new PdfBlock[]
        {
            new PdfTable(
                ["Radio", "Device", "Vendor", "Type", "Reputation", "Risk", "Recommendation", "Last Seen"],
                rows.Select(row => new[]
                {
                    row.RadioKind,
                    row.DeviceLabel,
                    row.VendorName ?? "Unknown vendor",
                    row.DeviceType,
                    row.Reputation,
                    row.RiskScore.ToString(CultureInfo.InvariantCulture),
                    row.Recommendation ?? "No recommendation",
                    row.LastSeenUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)
                }).ToList())
        };

    private sealed record ObservationSlice(
        DateTimeOffset ObservedUtc,
        Guid DeviceId,
        int RiskScore,
        string ConnectionState);

    public sealed record ReportsViewModel(
        IReadOnlyList<DailyReportRow> DailyReports,
        IReadOnlyList<WeeklyReportRow> WeeklyReports,
        IReadOnlyList<RiskDeviceRow> TopRiskDevices)
    {
        public static ReportsViewModel Empty { get; } = new(
            Array.Empty<DailyReportRow>(),
            Array.Empty<WeeklyReportRow>(),
            Array.Empty<RiskDeviceRow>());
    }

    public sealed record DailyReportRow(
        DateOnly Date,
        int ScanBatches,
        int ObservationCount,
        int UniqueDevices,
        int DevicesSeen,
        int SuspiciousCount,
        int AlertCount);

    public sealed record WeeklyReportRow(
        DateOnly WeekStart,
        DateOnly WeekEnd,
        int ScanBatches,
        int ObservationCount,
        int UniqueDevices,
        int SuspiciousCount,
        int AlertCount);

    public sealed record RiskDeviceRow(
        string RadioKind,
        string DeviceLabel,
        string? VendorName,
        string DeviceType,
        string Reputation,
        int RiskScore,
        string? Recommendation,
        DateTimeOffset LastSeenUtc);

}
