using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using Tracer.Core.Contracts;
using Tracer.Core.Enums;
using Tracer.Core.Interfaces;
using Tracer.Infrastructure;
using Tracer.Infrastructure.Persistence;
using Tracer.Radio.Windows;
using Tracer.Web.Services;

namespace Tracer.Web;

sealed record UpdateDeviceRequest(string? DisplayName, string? Password, bool IsKnown);
sealed record DeviceConnectRequest(string? Credential);
sealed record ScannerSettingsRequest(
    int ApproximateRangeMeters,
    bool EnableWifi,
    bool EnableBluetooth,
    int ScanIntervalSeconds,
    int WifiScanTimeoutSeconds,
    int MinimumWifiSignalQuality,
    bool CreateAlertsForUnknownDevices,
    int ReturnAlertThresholdMinutes,
    bool EnableRogueWifiDetection,
    bool EnableUnknownBluetoothConnectionAlerts,
    bool EnableAutomaticRecommendations,
    int RiskAlertThreshold,
    bool AutoLogDevices,
    bool EnablePacketMetadataCapture,
    bool EnableTrafficAnalysis);

class Program
{
    public static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        builder.Services
            .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(options =>
            {
                options.LoginPath = "/Account/Login";
                options.LogoutPath = "/Account/Logout";
                options.AccessDeniedPath = "/Account/Login";
                options.Cookie.Name = "Tracer.Admin";
                options.ExpireTimeSpan = TimeSpan.FromHours(8);
                options.SlidingExpiration = true;
            });

        builder.Services.AddAuthorization();

        builder.Services.AddRazorPages(options =>
        {
            options.Conventions.AuthorizeFolder("/");
            options.Conventions.AllowAnonymousToPage("/Account/Login");
            options.Conventions.AllowAnonymousToPage("/Error");
        });
        builder.Services.AddTracerInfrastructure(builder.Configuration);
        builder.Services.AddTracerWindowsRadioScanning();
        builder.Services.AddSingleton<WifiConnectionService>();
        builder.Services.AddSingleton<BluetoothConnectionService>();

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
        app.UseAuthentication();
        app.UseAuthorization();

        app.MapRazorPages();

        var api = app.MapGroup("/api")
            .RequireAuthorization();

        api.MapGet("/alerts/pending", async (IDbContextFactory<TracerDbContext> dbContextFactory, CancellationToken cancellationToken) =>
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

        api.MapPost("/alerts/{id:long}/acknowledge", async (long id, IDbContextFactory<TracerDbContext> dbContextFactory, CancellationToken cancellationToken) =>
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

        api.MapPost("/devices/{id:guid}/known", async (Guid id, IDbContextFactory<TracerDbContext> dbContextFactory, CancellationToken cancellationToken) =>
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

        api.MapPut("/devices/{id:guid}", async (Guid id, UpdateDeviceRequest request, IDbContextFactory<TracerDbContext> dbContextFactory, CancellationToken cancellationToken) =>
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

        api.MapDelete("/devices/{id:guid}", async (Guid id, IDbContextFactory<TracerDbContext> dbContextFactory, CancellationToken cancellationToken) =>
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

        api.MapDelete("/devices/{id:guid}/password", async (Guid id, IDbContextFactory<TracerDbContext> dbContextFactory, CancellationToken cancellationToken) =>
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

        api.MapPost("/wifi/{id:guid}/connect", async (
            Guid id,
            DeviceConnectRequest request,
            IDbContextFactory<TracerDbContext> dbContextFactory,
            WifiConnectionService wifiConnectionService,
            CancellationToken cancellationToken) =>
        {
            await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
            var device = await dbContext.DiscoveredDevices
                .AsNoTracking()
                .SingleOrDefaultAsync(x => x.Id == id && x.RadioKind == RadioKind.Wifi, cancellationToken);

            if (device is null)
            {
                return Results.NotFound();
            }

            var result = await wifiConnectionService.ConnectAsync(
                device.NetworkName ?? device.DisplayName ?? device.DeviceKey,
                string.IsNullOrWhiteSpace(request.Credential) ? device.Password : request.Credential,
                cancellationToken);

            return result.Success
                ? Results.Ok(result)
                : Results.BadRequest(result);
        });

        api.MapPost("/bluetooth/{id:guid}/connect", async (
            Guid id,
            DeviceConnectRequest request,
            IDbContextFactory<TracerDbContext> dbContextFactory,
            BluetoothConnectionService bluetoothConnectionService,
            CancellationToken cancellationToken) =>
        {
            await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
            var device = await dbContext.DiscoveredDevices
                .AsNoTracking()
                .SingleOrDefaultAsync(x => x.Id == id && x.RadioKind == RadioKind.Bluetooth, cancellationToken);

            if (device is null)
            {
                return Results.NotFound();
            }

            var result = await bluetoothConnectionService.ConnectAsync(
                device.HardwareAddress ?? string.Empty,
                request.Credential,
                cancellationToken);

            return result.Success
                ? Results.Ok(result)
                : Results.BadRequest(result);
        });

        api.MapPost("/settings", async (ScannerSettingsRequest request, IRuntimeSettingsService runtimeSettingsService, CancellationToken cancellationToken) =>
        {
            var updated = await runtimeSettingsService.UpdateAsync(
                new RuntimeSettingsSnapshot(
                    request.EnableWifi,
                    request.EnableBluetooth,
                    request.ScanIntervalSeconds,
                    request.ApproximateRangeMeters,
                    request.WifiScanTimeoutSeconds,
                    request.MinimumWifiSignalQuality,
                    request.CreateAlertsForUnknownDevices,
                    request.ReturnAlertThresholdMinutes,
                    request.EnableRogueWifiDetection,
                    request.EnableUnknownBluetoothConnectionAlerts,
                    request.EnableAutomaticRecommendations,
                    request.RiskAlertThreshold,
                    request.AutoLogDevices,
                    request.EnablePacketMetadataCapture,
                    request.EnableTrafficAnalysis),
                cancellationToken);

            return Results.Ok(updated);
        });

        api.MapPost("/scans/run", async (IScanCoordinator scanCoordinator, CancellationToken cancellationToken) =>
        {
            var summary = await scanCoordinator.ExecuteAsync(cancellationToken);
            return Results.Ok(summary);
        });

        await app.RunAsync();
    }
}
