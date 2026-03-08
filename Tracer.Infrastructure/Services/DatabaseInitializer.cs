using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Tracer.Core.Entities;
using Tracer.Core.Interfaces;
using Tracer.Core.Security;
using Tracer.Infrastructure.Persistence;

namespace Tracer.Infrastructure.Services;

public sealed class DatabaseInitializer(
    IDbContextFactory<TracerDbContext> dbContextFactory,
    RuntimeSettingsService runtimeSettingsService,
    OuiVendorLookupService ouiVendorLookupService,
    ILogger<DatabaseInitializer> logger) : IDatabaseInitializer
{
    private const string SeededAdminUserName = "admin";
    private const string SeededAdminPassword = "Admin@123";

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        logger.LogInformation("Applying pending EF Core migrations for Tracer.");
        await dbContext.Database.MigrateAsync(cancellationToken);
        await SeedAdminAsync(dbContext, cancellationToken);
        await SeedRuntimeSettingsAsync(dbContext, cancellationToken);
        await SeedOuiVendorsAsync(dbContext, cancellationToken);
        await ouiVendorLookupService.WarmCacheAsync(cancellationToken);
    }

    private async Task SeedAdminAsync(TracerDbContext dbContext, CancellationToken cancellationToken)
    {
        var normalizedUserName = SeededAdminUserName.ToUpperInvariant();
        var existingAdmin = await dbContext.AdminUsers
            .SingleOrDefaultAsync(x => x.NormalizedUserName == normalizedUserName, cancellationToken);

        if (existingAdmin is not null)
        {
            return;
        }

        dbContext.AdminUsers.Add(new AdminUser
        {
            UserName = SeededAdminUserName,
            NormalizedUserName = normalizedUserName,
            PasswordHash = AdminPasswordHasher.HashPassword(SeededAdminPassword),
            Role = Tracer.Core.Enums.AdminRole.SuperAdmin,
            IsActive = true
        });

        await dbContext.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Seeded default admin account '{UserName}'.", SeededAdminUserName);
    }

    private async Task SeedRuntimeSettingsAsync(TracerDbContext dbContext, CancellationToken cancellationToken)
    {
        var existingSettings = await dbContext.RuntimeSettings.SingleOrDefaultAsync(x => x.Id == 1, cancellationToken);

        if (existingSettings is not null)
        {
            return;
        }

        var defaults = runtimeSettingsService.CreateDefaultSnapshot();
        var settings = new RuntimeSettings { Id = 1 };
        RuntimeSettingsService.Apply(defaults, settings);
        settings.LastUpdatedUtc = DateTimeOffset.UtcNow;
        dbContext.RuntimeSettings.Add(settings);
        await dbContext.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Seeded runtime scanner settings.");
    }

    private async Task SeedOuiVendorsAsync(TracerDbContext dbContext, CancellationToken cancellationToken)
    {
        if (await dbContext.OuiVendors.AnyAsync(cancellationToken))
        {
            return;
        }

        dbContext.OuiVendors.AddRange(OuiVendorLookupService.GetFallbackSeedVendors());
        await dbContext.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Seeded fallback OUI vendor cache.");
    }
}
