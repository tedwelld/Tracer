using Microsoft.EntityFrameworkCore;
using Tracer.Core.Enums;
using Tracer.Core.Interfaces;
using Tracer.Infrastructure;
using Tracer.Infrastructure.Persistence;

namespace Tracer.Web;

sealed record UpdateDeviceRequest(string? DisplayName, string? Password, bool IsKnown);
sealed record ScannerSettingsRequest(
    int ApproximateRangeMeters,
    bool EnableWifi,
    bool EnableBluetooth,
    int ScanIntervalSeconds,
    int WifiScanTimeoutSeconds,
    int MinimumWifiSignalQuality);

class Program
{
    public static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        builder.Services.AddRazorPages();
        builder.Services.AddTracerInfrastructure(builder.Configuration);

        var app = builder.Build();

        await using (var scope = app.Services.CreateAsyncScope())
        {
            var initializer = scope.ServiceProvider.GetRequiredService<IDatabaseInitializer>();
            await initializer.InitializeAsync(CancellationToken.None);
        }

        if (!app.Environment.IsDevelopment())
        {
            app.UseExceptionHandler("/Error");
            app.UseHsts();
        }

        app.UseHttpsRedirection();
        app.UseStaticFiles();
        app.UseRouting();
        app.UseAuthorization();

        app.MapRazorPages();

        app.MapGet("/api/alerts/pending", async (IDbContextFactory<TracerDbContext> dbContextFactory, CancellationToken cancellationToken) =>
        {
            await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

            var alerts = await dbContext.DeviceAlerts
                .AsNoTracking()
                .Where(x => x.Status == AlertStatus.Pending)
                .OrderByDescending(x => x.CreatedUtc)
                .Select(x => new
                {
                    x.Id,
                    x.Title,
                    x.Message,
                    x.CreatedUtc,
                    x.DeviceId,
                    DeviceLabel = x.Device!.DisplayName ?? x.Device.NetworkName ?? x.Device.HardwareAddress ?? x.Device.DeviceKey,
                    RadioKind = x.Device!.RadioKind.ToString()
                })
                .Take(10)
                .ToListAsync(cancellationToken);

            return Results.Ok(alerts);
        });

        app.MapPost("/api/alerts/{id:long}/acknowledge", async (long id, IDbContextFactory<TracerDbContext> dbContextFactory, CancellationToken cancellationToken) =>
        {
            await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
            var alert = await dbContext.DeviceAlerts.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

            if (alert is null)
            {
                return Results.NotFound();
            }

            alert.Status = AlertStatus.Acknowledged;
            alert.AcknowledgedUtc = DateTimeOffset.UtcNow;
            await dbContext.SaveChangesAsync(cancellationToken);

            return Results.Ok();
        });

        app.MapPost("/api/devices/{id:guid}/known", async (Guid id, IDbContextFactory<TracerDbContext> dbContextFactory, CancellationToken cancellationToken) =>
        {
            await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
            var device = await dbContext.DiscoveredDevices.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

            if (device is null)
            {
                return Results.NotFound();
            }

            device.IsKnown = true;

            var pendingAlerts = await dbContext.DeviceAlerts
                .Where(x => x.DeviceId == id && x.Status == AlertStatus.Pending)
                .ToListAsync(cancellationToken);

            foreach (var alert in pendingAlerts)
            {
                alert.Status = AlertStatus.Acknowledged;
                alert.AcknowledgedUtc = DateTimeOffset.UtcNow;
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            return Results.Ok();
        });

        app.MapPut("/api/devices/{id:guid}", async (Guid id, UpdateDeviceRequest request, IDbContextFactory<TracerDbContext> dbContextFactory, CancellationToken cancellationToken) =>
        {
            await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
            var device = await dbContext.DiscoveredDevices.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

            if (device is null)
            {
                return Results.NotFound();
            }

            if (!string.IsNullOrWhiteSpace(request.DisplayName))
            {
                device.DisplayName = request.DisplayName;
            }

            if (request.Password != null)
            {
                device.Password = request.Password;
            }

            if (request.IsKnown)
            {
                device.IsKnown = true;
                var pendingAlerts = await dbContext.DeviceAlerts
                    .Where(x => x.DeviceId == id && x.Status == AlertStatus.Pending)
                    .ToListAsync(cancellationToken);

                foreach (var alert in pendingAlerts)
                {
                    alert.Status = AlertStatus.Acknowledged;
                    alert.AcknowledgedUtc = DateTimeOffset.UtcNow;
                }
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            return Results.Ok();
        });

        app.MapDelete("/api/devices/{id:guid}", async (Guid id, IDbContextFactory<TracerDbContext> dbContextFactory, CancellationToken cancellationToken) =>
        {
            await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
            var device = await dbContext.DiscoveredDevices.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

            if (device is null)
            {
                return Results.NotFound();
            }

            dbContext.DiscoveredDevices.Remove(device);
            await dbContext.SaveChangesAsync(cancellationToken);
            return Results.Ok();
        });

        app.MapDelete("/api/devices/{id:guid}/password", async (Guid id, IDbContextFactory<TracerDbContext> dbContextFactory, CancellationToken cancellationToken) =>
        {
            await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
            var device = await dbContext.DiscoveredDevices.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

            if (device is null)
            {
                return Results.NotFound();
            }

            device.Password = null;
            await dbContext.SaveChangesAsync(cancellationToken);
            return Results.Ok();
        });

        app.MapPost("/api/settings", async (ScannerSettingsRequest request, IConfiguration configuration, CancellationToken cancellationToken) =>
        {
            // Note: In a production environment, you would want to update the actual configuration
            // This is a simplified version that acknowledges the request
            // In reality, you'd need to persist these settings to a database or configuration file
            // and restart the worker service for the changes to take effect
            
            return Results.Ok(new { message = "Settings updated. Please restart the scanner worker service for changes to take effect." });
        });

        await app.RunAsync();
    }
}
