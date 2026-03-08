using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Tracer.Core.Contracts;
using Tracer.Core.Enums;
using Tracer.Core.Entities;
using Tracer.Core.Interfaces;
using Tracer.Infrastructure;
using Tracer.Infrastructure.Persistence;
using Tracer.Infrastructure.Services;
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
    bool EnableTrafficAnalysis,
    int ObservationRetentionDays,
    int AlertRetentionDays,
    int EventLogRetentionDays);

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
                options.AccessDeniedPath = "/Account/AccessDenied";
                options.Cookie.Name = "Tracer.Admin";
                options.Cookie.HttpOnly = true;
                options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
                options.Cookie.SameSite = SameSiteMode.Lax;
                options.ExpireTimeSpan = TimeSpan.FromHours(8);
                options.SlidingExpiration = true;
                options.Events = new CookieAuthenticationEvents
                {
                    OnValidatePrincipal = async context =>
                    {
                        var idClaim = context.Principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                        if (!Guid.TryParse(idClaim, out var adminUserId))
                        {
                            context.RejectPrincipal();
                            await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                            return;
                        }

                        await using var scope = context.HttpContext.RequestServices.CreateAsyncScope();
                        var dbContextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<TracerDbContext>>();
                        await using var dbContext = await dbContextFactory.CreateDbContextAsync(context.HttpContext.RequestAborted);

                        var admin = await dbContext.AdminUsers
                            .AsNoTracking()
                            .SingleOrDefaultAsync(x => x.Id == adminUserId, context.HttpContext.RequestAborted);

                        if (admin is null || !admin.IsActive)
                        {
                            context.RejectPrincipal();
                            await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                            return;
                        }

                        var currentRole = context.Principal?.FindFirst(ClaimTypes.Role)?.Value;
                        var currentName = context.Principal?.Identity?.Name;
                        var expectedRole = admin.Role.ToString();

                        if (!string.Equals(currentRole, expectedRole, StringComparison.Ordinal)
                            || !string.Equals(currentName, admin.UserName, StringComparison.Ordinal))
                        {
                            context.ReplacePrincipal(BuildAdminPrincipal(admin));
                            context.ShouldRenew = true;
                        }
                    }
                };
            });

        builder.Services.AddAuthorization(options =>
        {
            options.AddPolicy("OperateSecurity", policy =>
                policy.RequireRole(AdminRole.SuperAdmin.ToString(), AdminRole.SecurityOperator.ToString()));

            options.AddPolicy("ManageSettings", policy =>
                policy.RequireRole(AdminRole.SuperAdmin.ToString()));

            options.AddPolicy("ViewAudit", policy =>
                policy.RequireRole(AdminRole.SuperAdmin.ToString(), AdminRole.Auditor.ToString()));
        });

        builder.Services.AddRazorPages(options =>
        {
            options.Conventions.AuthorizeFolder("/");
            options.Conventions.AuthorizePage("/Settings", "ManageSettings");
            options.Conventions.AllowAnonymousToPage("/Account/Login");
            options.Conventions.AllowAnonymousToPage("/Account/AccessDenied");
            options.Conventions.AllowAnonymousToPage("/Error");
        });

        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen(options =>
        {
            options.SwaggerDoc("v1", new()
            {
                Title = "Tracer API",
                Version = "v1",
                Description = "Testing surface for Tracer device, alert, observation, and operations endpoints."
            });
        });
        builder.Services.AddTracerInfrastructure(builder.Configuration);
        builder.Services.AddTracerWindowsRadioScanning();
        builder.Services.AddSingleton<WifiConnectionService>();
        builder.Services.AddSingleton<BluetoothConnectionService>();
        builder.Services.AddSingleton<TracerHealthService>();

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
        app.UseSwagger();
        app.UseSwaggerUI(options =>
        {
            options.SwaggerEndpoint("/swagger/v1/swagger.json", "Tracer API v1");
            options.RoutePrefix = "swagger";
            options.DocumentTitle = "Tracer API Test Console";
        });
        app.UseAuthentication();
        app.UseAuthorization();

        app.MapGet("/health", async (TracerHealthService healthService, CancellationToken cancellationToken) =>
        {
            var snapshot = await healthService.GetSnapshotAsync(cancellationToken);
            return snapshot.DatabaseHealthy && snapshot.ScannerHealthy
                ? Results.Ok(snapshot)
                : Results.Json(snapshot, statusCode: StatusCodes.Status503ServiceUnavailable);
        })
        .AllowAnonymous();

        app.MapRazorPages();

        var api = app.MapGroup("/api")
            .RequireAuthorization();

        MapReadEndpoints(api);
        MapOperationalEndpoints(api);

        await app.RunAsync();
    }

    private static void MapReadEndpoints(RouteGroupBuilder api)
    {
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
                    x.NotificationStatus,
                    x.NotificationAttempts,
                    DeviceLabel = x.Device!.DisplayName ?? x.Device.NetworkName ?? x.Device.HardwareAddress ?? x.Device.DeviceKey,
                    RadioKind = x.Device!.RadioKind.ToString()
                })
                .Take(10)
                .ToListAsync(cancellationToken);

            return Results.Ok(alerts);
        });

        api.MapGet("/alerts", async (string? status, int? take, IDbContextFactory<TracerDbContext> dbContextFactory, CancellationToken cancellationToken) =>
        {
            await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
            var query = dbContext.DeviceAlerts
                .AsNoTracking()
                .OrderByDescending(x => x.CreatedUtc)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(status)
                && Enum.TryParse<AlertStatus>(status, true, out var parsedStatus))
            {
                query = query.Where(x => x.Status == parsedStatus);
            }

            var alerts = await query
                .Take(Math.Clamp(take ?? 50, 1, 250))
                .Select(x => new
                {
                    x.Id,
                    x.AlertType,
                    x.Severity,
                    x.Status,
                    x.NotificationStatus,
                    x.NotificationAttempts,
                    x.CreatedUtc,
                    x.AcknowledgedUtc,
                    x.Title,
                    x.Message,
                    x.DeviceId,
                    DeviceLabel = x.Device!.DisplayName ?? x.Device.NetworkName ?? x.Device.HardwareAddress ?? x.Device.DeviceKey
                })
                .ToListAsync(cancellationToken);

            return Results.Ok(alerts);
        });

        api.MapGet("/devices", async (
            string? radioKind,
            bool? isKnown,
            string? search,
            int? take,
            IDbContextFactory<TracerDbContext> dbContextFactory,
            CancellationToken cancellationToken) =>
        {
            await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
            var query = dbContext.DiscoveredDevices
                .AsNoTracking()
                .OrderByDescending(x => x.LastSeenUtc)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(radioKind)
                && Enum.TryParse<RadioKind>(radioKind, true, out var parsedRadioKind))
            {
                query = query.Where(x => x.RadioKind == parsedRadioKind);
            }

            if (isKnown.HasValue)
            {
                query = query.Where(x => x.IsKnown == isKnown.Value);
            }

            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.Trim();
                query = query.Where(x =>
                    x.DeviceKey.Contains(term)
                    || (x.DisplayName != null && x.DisplayName.Contains(term))
                    || (x.NetworkName != null && x.NetworkName.Contains(term))
                    || (x.HardwareAddress != null && x.HardwareAddress.Contains(term))
                    || (x.VendorName != null && x.VendorName.Contains(term)));
            }

            var devices = await query
                .Take(Math.Clamp(take ?? 100, 1, 500))
                .Select(x => new
                {
                    x.Id,
                    x.DeviceKey,
                    x.RadioKind,
                    x.DisplayName,
                    x.NetworkName,
                    x.HardwareAddress,
                    x.SecurityType,
                    x.VendorPrefix,
                    x.VendorName,
                    x.DeviceType,
                    x.Reputation,
                    x.RiskScore,
                    x.ConnectionState,
                    x.EstimatedDistanceMeters,
                    x.MovementTrend,
                    x.Channel,
                    x.FrequencyBand,
                    x.IsKnown,
                    x.IsPaired,
                    x.TotalObservations,
                    x.FirstSeenUtc,
                    x.LastSeenUtc
                })
                .ToListAsync(cancellationToken);

            return Results.Ok(devices);
        });

        api.MapGet("/devices/{id:guid}", async (Guid id, IDbContextFactory<TracerDbContext> dbContextFactory, CancellationToken cancellationToken) =>
        {
            await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
            var device = await dbContext.DiscoveredDevices
                .AsNoTracking()
                .Where(x => x.Id == id)
                .Select(x => new
                {
                    x.Id,
                    x.DeviceKey,
                    x.RadioKind,
                    x.DisplayName,
                    x.NetworkName,
                    x.HardwareAddress,
                    x.SecurityType,
                    x.Password,
                    x.VendorPrefix,
                    x.VendorName,
                    x.DeviceType,
                    x.Reputation,
                    x.RiskScore,
                    x.RiskReasons,
                    x.LastRecommendation,
                    x.ConnectionState,
                    x.EstimatedDistanceMeters,
                    x.MovementTrend,
                    x.LastInterfaceName,
                    x.FrequencyBand,
                    x.Channel,
                    x.LastSignalStrength,
                    x.IsPaired,
                    x.IsKnown,
                    x.TotalObservations,
                    x.FirstSeenUtc,
                    x.LastSeenUtc,
                    x.LastConnectedUtc
                })
                .SingleOrDefaultAsync(cancellationToken);

            return device is null ? Results.NotFound() : Results.Ok(device);
        });

        api.MapGet("/observations", async (Guid? deviceId, int? take, IDbContextFactory<TracerDbContext> dbContextFactory, CancellationToken cancellationToken) =>
        {
            await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
            var query = dbContext.DeviceObservations
                .AsNoTracking()
                .OrderByDescending(x => x.ObservedUtc)
                .AsQueryable();

            if (deviceId.HasValue)
            {
                query = query.Where(x => x.DeviceId == deviceId.Value);
            }

            var observations = await query
                .Take(Math.Clamp(take ?? 100, 1, 500))
                .Select(x => new
                {
                    x.Id,
                    x.DeviceId,
                    DeviceLabel = x.Device!.DisplayName ?? x.Device.NetworkName ?? x.Device.HardwareAddress ?? x.Device.DeviceKey,
                    x.ObservedUtc,
                    x.DisplayName,
                    x.NetworkName,
                    x.HardwareAddress,
                    x.SecurityType,
                    x.Channel,
                    x.FrequencyBand,
                    x.SignalStrength,
                    x.DeviceType,
                    x.Reputation,
                    x.RiskScore,
                    x.ConnectionState,
                    x.MovementTrend
                })
                .ToListAsync(cancellationToken);

            return Results.Ok(observations);
        });

        api.MapGet("/vendors/status", async (OuiVendorLookupService ouiVendorLookupService, CancellationToken cancellationToken) =>
        {
            var status = await ouiVendorLookupService.GetStatusAsync(cancellationToken);
            return Results.Ok(status);
        });

        api.MapGet("/audit/actions", async (int? take, IDbContextFactory<TracerDbContext> dbContextFactory, CancellationToken cancellationToken) =>
        {
            await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
            var items = await dbContext.AdminAuditLogs
                .AsNoTracking()
                .OrderByDescending(x => x.CreatedUtc)
                .Take(Math.Clamp(take ?? 100, 1, 500))
                .ToListAsync(cancellationToken);

            return Results.Ok(items);
        })
        .RequireAuthorization("ViewAudit");

        api.MapGet("/audit/logins", async (int? take, IDbContextFactory<TracerDbContext> dbContextFactory, CancellationToken cancellationToken) =>
        {
            await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
            var items = await dbContext.LoginAuditLogs
                .AsNoTracking()
                .OrderByDescending(x => x.CreatedUtc)
                .Take(Math.Clamp(take ?? 100, 1, 500))
                .ToListAsync(cancellationToken);

            return Results.Ok(items);
        })
        .RequireAuthorization("ViewAudit");
    }

    private static void MapOperationalEndpoints(RouteGroupBuilder api)
    {
        api.MapPost("/alerts/{id:long}/acknowledge", async (
            long id,
            HttpContext httpContext,
            IDbContextFactory<TracerDbContext> dbContextFactory,
            AdminAuditService auditService,
            CancellationToken cancellationToken) =>
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
            await auditService.WriteActionAsync(
                ResolveAdminUserId(httpContext),
                httpContext.User.Identity?.Name ?? "unknown",
                httpContext.Connection.RemoteIpAddress?.ToString(),
                httpContext.Request.Headers["User-Agent"].ToString(),
                "AcknowledgeAlert",
                nameof(Tracer.Core.Entities.DeviceAlert),
                id.ToString(),
                alert.Title,
                cancellationToken);

            return Results.Ok();
        })
        .RequireAuthorization("OperateSecurity");

        api.MapPost("/devices/{id:guid}/known", async (
            Guid id,
            HttpContext httpContext,
            IDbContextFactory<TracerDbContext> dbContextFactory,
            AdminAuditService auditService,
            CancellationToken cancellationToken) =>
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
            await auditService.WriteActionAsync(
                ResolveAdminUserId(httpContext),
                httpContext.User.Identity?.Name ?? "unknown",
                httpContext.Connection.RemoteIpAddress?.ToString(),
                httpContext.Request.Headers["User-Agent"].ToString(),
                "MarkKnown",
                nameof(Tracer.Core.Entities.DiscoveredDevice),
                id.ToString(),
                device.DeviceKey,
                cancellationToken);
            return Results.Ok();
        })
        .RequireAuthorization("OperateSecurity");

        api.MapPut("/devices/{id:guid}", async (
            Guid id,
            UpdateDeviceRequest request,
            HttpContext httpContext,
            IDbContextFactory<TracerDbContext> dbContextFactory,
            AdminAuditService auditService,
            CancellationToken cancellationToken) =>
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
            await auditService.WriteActionAsync(
                ResolveAdminUserId(httpContext),
                httpContext.User.Identity?.Name ?? "unknown",
                httpContext.Connection.RemoteIpAddress?.ToString(),
                httpContext.Request.Headers["User-Agent"].ToString(),
                "UpdateDevice",
                nameof(Tracer.Core.Entities.DiscoveredDevice),
                id.ToString(),
                request.DisplayName,
                cancellationToken);
            return Results.Ok();
        })
        .RequireAuthorization("OperateSecurity");

        api.MapDelete("/devices/{id:guid}", async (
            Guid id,
            HttpContext httpContext,
            IDbContextFactory<TracerDbContext> dbContextFactory,
            AdminAuditService auditService,
            CancellationToken cancellationToken) =>
        {
            await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
            var device = await dbContext.DiscoveredDevices.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

            if (device is null)
            {
                return Results.NotFound();
            }

            dbContext.DiscoveredDevices.Remove(device);
            await dbContext.SaveChangesAsync(cancellationToken);
            await auditService.WriteActionAsync(
                ResolveAdminUserId(httpContext),
                httpContext.User.Identity?.Name ?? "unknown",
                httpContext.Connection.RemoteIpAddress?.ToString(),
                httpContext.Request.Headers["User-Agent"].ToString(),
                "DeleteDevice",
                nameof(Tracer.Core.Entities.DiscoveredDevice),
                id.ToString(),
                device.DeviceKey,
                cancellationToken);
            return Results.Ok();
        })
        .RequireAuthorization("OperateSecurity");

        api.MapDelete("/devices/{id:guid}/password", async (
            Guid id,
            HttpContext httpContext,
            IDbContextFactory<TracerDbContext> dbContextFactory,
            AdminAuditService auditService,
            CancellationToken cancellationToken) =>
        {
            await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
            var device = await dbContext.DiscoveredDevices.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

            if (device is null)
            {
                return Results.NotFound();
            }

            device.Password = null;
            await dbContext.SaveChangesAsync(cancellationToken);
            await auditService.WriteActionAsync(
                ResolveAdminUserId(httpContext),
                httpContext.User.Identity?.Name ?? "unknown",
                httpContext.Connection.RemoteIpAddress?.ToString(),
                httpContext.Request.Headers["User-Agent"].ToString(),
                "ClearDevicePassword",
                nameof(Tracer.Core.Entities.DiscoveredDevice),
                id.ToString(),
                device.DeviceKey,
                cancellationToken);
            return Results.Ok();
        })
        .RequireAuthorization("OperateSecurity");

        api.MapPost("/wifi/{id:guid}/connect", async (
            Guid id,
            DeviceConnectRequest request,
            HttpContext httpContext,
            IDbContextFactory<TracerDbContext> dbContextFactory,
            WifiConnectionService wifiConnectionService,
            AdminAuditService auditService,
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

            if (result.Success)
            {
                await auditService.WriteActionAsync(
                    ResolveAdminUserId(httpContext),
                    httpContext.User.Identity?.Name ?? "unknown",
                    httpContext.Connection.RemoteIpAddress?.ToString(),
                    httpContext.Request.Headers["User-Agent"].ToString(),
                    "ConnectWifi",
                    nameof(Tracer.Core.Entities.DiscoveredDevice),
                    id.ToString(),
                    device.DeviceKey,
                    cancellationToken);
                return Results.Ok(result);
            }

            return Results.BadRequest(result);
        })
        .RequireAuthorization("OperateSecurity");

        api.MapPost("/bluetooth/{id:guid}/connect", async (
            Guid id,
            DeviceConnectRequest request,
            HttpContext httpContext,
            IDbContextFactory<TracerDbContext> dbContextFactory,
            BluetoothConnectionService bluetoothConnectionService,
            AdminAuditService auditService,
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

            if (result.Success)
            {
                await auditService.WriteActionAsync(
                    ResolveAdminUserId(httpContext),
                    httpContext.User.Identity?.Name ?? "unknown",
                    httpContext.Connection.RemoteIpAddress?.ToString(),
                    httpContext.Request.Headers["User-Agent"].ToString(),
                    "ConnectBluetooth",
                    nameof(Tracer.Core.Entities.DiscoveredDevice),
                    id.ToString(),
                    device.DeviceKey,
                    cancellationToken);
                return Results.Ok(result);
            }

            return Results.BadRequest(result);
        })
        .RequireAuthorization("OperateSecurity");

        api.MapPost("/settings", async (
            ScannerSettingsRequest request,
            HttpContext httpContext,
            IRuntimeSettingsService runtimeSettingsService,
            AdminAuditService auditService,
            CancellationToken cancellationToken) =>
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
                    request.EnableTrafficAnalysis,
                    request.ObservationRetentionDays,
                    request.AlertRetentionDays,
                    request.EventLogRetentionDays),
                cancellationToken);

            await auditService.WriteActionAsync(
                ResolveAdminUserId(httpContext),
                httpContext.User.Identity?.Name ?? "unknown",
                httpContext.Connection.RemoteIpAddress?.ToString(),
                httpContext.Request.Headers["User-Agent"].ToString(),
                "UpdateSettings",
                nameof(Tracer.Core.Entities.RuntimeSettings),
                "1",
                $"ScanInterval={updated.ScanIntervalSeconds}",
                cancellationToken);
            return Results.Ok(updated);
        })
        .RequireAuthorization("ManageSettings");

        api.MapPost("/scans/run", async (
            HttpContext httpContext,
            IScanCoordinator scanCoordinator,
            AdminAuditService auditService,
            CancellationToken cancellationToken) =>
        {
            var summary = await scanCoordinator.ExecuteAsync(cancellationToken);
            await auditService.WriteActionAsync(
                ResolveAdminUserId(httpContext),
                httpContext.User.Identity?.Name ?? "unknown",
                httpContext.Connection.RemoteIpAddress?.ToString(),
                httpContext.Request.Headers["User-Agent"].ToString(),
                "RunScan",
                nameof(Tracer.Core.Entities.ScanBatch),
                null,
                $"Devices={summary.TotalDevices}",
                cancellationToken);
            return Results.Ok(summary);
        })
        .RequireAuthorization("OperateSecurity");

        api.MapPost("/vendors/sync", async (
            HttpContext httpContext,
            OuiVendorLookupService ouiVendorLookupService,
            AdminAuditService auditService,
            CancellationToken cancellationToken) =>
        {
            var result = await ouiVendorLookupService.RefreshAsync(cancellationToken);
            await auditService.WriteActionAsync(
                ResolveAdminUserId(httpContext),
                httpContext.User.Identity?.Name ?? "unknown",
                httpContext.Connection.RemoteIpAddress?.ToString(),
                httpContext.Request.Headers["User-Agent"].ToString(),
                "SyncOuiCache",
                nameof(Tracer.Core.Entities.OuiVendor),
                null,
                result.Message ?? $"Imported={result.ImportedCount}",
                cancellationToken);
            return Results.Ok(result);
        })
        .RequireAuthorization("ManageSettings");
    }

    private static Guid? ResolveAdminUserId(HttpContext httpContext)
    {
        return Guid.TryParse(
            httpContext.User.Claims.FirstOrDefault(x => x.Type == ClaimTypes.NameIdentifier)?.Value,
            out var adminUserId)
            ? adminUserId
            : null;
    }

    private static ClaimsPrincipal BuildAdminPrincipal(AdminUser admin)
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, admin.Id.ToString()),
            new Claim(ClaimTypes.Name, admin.UserName),
            new Claim(ClaimTypes.Role, admin.Role.ToString()),
            new Claim("tracer:role", admin.Role.ToString())
        };

        return new ClaimsPrincipal(
            new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme));
    }
}
