using Microsoft.EntityFrameworkCore;
using Tracer.Core.Entities;
using Tracer.Infrastructure.Persistence;

namespace Tracer.Infrastructure.Services;

public sealed class AdminAuditService(IDbContextFactory<TracerDbContext> dbContextFactory)
{
    public async Task WriteActionAsync(
        Guid? adminUserId,
        string userName,
        string? ipAddress,
        string? userAgent,
        string action,
        string entityType,
        string? entityId,
        string? details,
        CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        dbContext.AdminAuditLogs.Add(new AdminAuditLog
        {
            AdminUserId = adminUserId,
            UserName = userName,
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            IpAddress = ipAddress,
            UserAgent = userAgent,
            Details = details
        });

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task WriteLoginAttemptAsync(
        string userName,
        string? ipAddress,
        string? userAgent,
        bool succeeded,
        string? failureReason,
        Guid? adminUserId,
        CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        dbContext.LoginAuditLogs.Add(new LoginAuditLog
        {
            AdminUserId = adminUserId,
            UserName = userName,
            Succeeded = succeeded,
            FailureReason = failureReason,
            IpAddress = ipAddress,
            UserAgent = userAgent
        });

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
