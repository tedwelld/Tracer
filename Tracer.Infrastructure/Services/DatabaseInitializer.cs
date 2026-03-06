using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Tracer.Core.Interfaces;
using Tracer.Infrastructure.Persistence;

namespace Tracer.Infrastructure.Services;

public sealed class DatabaseInitializer(
    IDbContextFactory<TracerDbContext> dbContextFactory,
    ILogger<DatabaseInitializer> logger) : IDatabaseInitializer
{
    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        logger.LogInformation("Applying pending EF Core migrations for Tracer.");
        await dbContext.Database.MigrateAsync(cancellationToken);
    }
}
