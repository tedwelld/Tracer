using Microsoft.EntityFrameworkCore;
using Tracer.Core.Enums;
using Tracer.Core.Interfaces;
using Tracer.Infrastructure.Persistence;

namespace Tracer.Infrastructure.Services;

public sealed class DataRetentionService(
    IDbContextFactory<TracerDbContext> dbContextFactory,
    IRuntimeSettingsService runtimeSettingsService)
{
    public async Task<DataRetentionResult> ExecuteAsync(CancellationToken cancellationToken)
    {
        var settings = await runtimeSettingsService.GetCurrentAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var observationCutoff = now.AddDays(-Math.Max(1, settings.ObservationRetentionDays));
        var alertCutoff = now.AddDays(-Math.Max(1, settings.AlertRetentionDays));
        var eventCutoff = now.AddDays(-Math.Max(1, settings.EventLogRetentionDays));

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var deletedObservations = await dbContext.DeviceObservations
            .Where(x => x.ObservedUtc < observationCutoff)
            .ExecuteDeleteAsync(cancellationToken);

        var deletedAlerts = await dbContext.DeviceAlerts
            .Where(x => x.CreatedUtc < alertCutoff && x.Status != AlertStatus.Pending)
            .ExecuteDeleteAsync(cancellationToken);

        var deletedEventLogs = await dbContext.ScanEventLogs
            .Where(x => x.CreatedUtc < eventCutoff)
            .ExecuteDeleteAsync(cancellationToken);

        return new DataRetentionResult(
            deletedObservations,
            deletedAlerts,
            deletedEventLogs,
            now);
    }
}

public sealed record DataRetentionResult(
    int DeletedObservations,
    int DeletedAlerts,
    int DeletedEventLogs,
    DateTimeOffset ExecutedUtc);
