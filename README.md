# Tracer

Tracer is a Windows-focused Wi-Fi and Bluetooth monitoring platform built on .NET 8. It scans nearby radios, stores observations in SQL Server, scores device risk, raises alerts, exposes a secured Razor Pages dashboard, and now includes a Swagger UI surface for API testing.

## Current capabilities

### Radio monitoring

- Continuous Wi-Fi and Bluetooth scanning through `Tracer.Scanner.Worker`
- Scan batch tracking with duration, adapter summary, memory usage, and error counts
- Device observation history with RSSI, channel, band, connection state, movement trend, and estimated distance
- Shared SQL Server persistence across the worker and web app

### Security and intelligence

- Unknown-device alerts
- Returned-device alerts after configurable absence
- Rogue Wi-Fi / duplicate SSID detection
- Unknown Bluetooth connection alerts
- Risk scoring and recommendations
- OUI vendor cache with seeded data and optional IEEE CSV refresh
- Vendor, reputation, device type, connection state, and movement metadata stored on devices and observations

### Enterprise and operations features

- Role-aware admin authentication
- Login auditing and admin action auditing
- Login lockout after repeated failures
- Health endpoint at `/health`
- Retention settings for observations, alerts, and event logs
- Background retention cleanup worker
- Background OUI refresh worker
- Email and webhook alert notification configuration
- Swagger/OpenAPI UI for API testing

### UI and API

- Dashboard, Wi-Fi, Bluetooth, Passwords, Settings, Monitoring, and Reports pages
- Persisted settings page with direct form post fallback and API-backed save path
- Expanded authenticated API for devices, alerts, observations, audit logs, vendor cache status, scan execution, and settings updates
- Swagger UI at `/swagger`

## Default admin login

The database initializer seeds a default admin account if one does not exist:

- Username: `admin`
- Password: `Admin@123`
- Seeded role: `SuperAdmin`

Change this immediately outside local development.

## Tech stack

- Language: `C#`
- Runtime: `.NET 8`
- UI: `ASP.NET Core Razor Pages`
- API docs/test UI: `Swagger / Swashbuckle`
- Background host: `.NET Worker Service`
- ORM: `Entity Framework Core 8`
- Database: `SQL Server / LocalDB for development`
- Wi-Fi scanning: `ManagedNativeWifi`
- Bluetooth scanning: `InTheHand.Net.Bluetooth`
- Active scanning target OS: `Windows`

## Solution layout

- `Tracer.Core`
  Shared entities, contracts, enums, options, and security helpers
- `Tracer.Infrastructure`
  EF Core, migrations, initialization, runtime settings, device intelligence, retention, notifications, OUI lookup, and scan orchestration
- `Tracer.Radio.Windows`
  Windows-specific Wi-Fi and Bluetooth scanner implementations
- `Tracer.Scanner.Worker`
  Long-running scan host plus OUI refresh and retention cleanup workers
- `Tracer.Web`
  Razor Pages UI, authenticated API, health endpoint, and Swagger UI

## Runtime flow

1. `Tracer.Scanner.Worker` starts and applies pending migrations.
2. Runtime settings load from SQL Server.
3. Wi-Fi and Bluetooth scans run on the configured interval.
4. `ScanCoordinator` deduplicates, assesses, scores, and persists device state.
5. Alerts and scan event logs are created.
6. Notification dispatch attempts webhook and/or email delivery for new alerts.
7. Retention and OUI refresh workers run in the background.
8. `Tracer.Web` reads the shared database and exposes the dashboard, pages, and API surface.

## Database model

Primary tables now include:

- `AdminUsers`
- `AdminAuditLogs`
- `LoginAuditLogs`
- `RuntimeSettings`
- `OuiVendors`
- `DiscoveredDevices`
- `DeviceObservations`
- `DeviceAlerts`
- `ScanBatches`
- `ScanEventLogs`
- `__EFMigrationsHistory`

## Runtime settings

The settings page and settings API persist these controls:

- `EnableWifi`
- `EnableBluetooth`
- `ScanIntervalSeconds`
- `ApproximateRangeMeters`
- `WifiScanTimeoutSeconds`
- `MinimumWifiSignalQuality`
- `CreateAlertsForUnknownDevices`
- `ReturnAlertThresholdMinutes`
- `EnableRogueWifiDetection`
- `EnableUnknownBluetoothConnectionAlerts`
- `EnableAutomaticRecommendations`
- `RiskAlertThreshold`
- `AutoLogDevices`
- `EnablePacketMetadataCapture`
- `EnableTrafficAnalysis`
- `ObservationRetentionDays`
- `AlertRetentionDays`
- `EventLogRetentionDays`

## API surface

Authenticated endpoints include:

- `GET /api/devices`
- `GET /api/devices/{id}`
- `GET /api/alerts`
- `GET /api/alerts/pending`
- `GET /api/observations`
- `GET /api/vendors/status`
- `POST /api/vendors/sync`
- `POST /api/scans/run`
- `POST /api/settings`
- `GET /api/audit/actions`
- `GET /api/audit/logins`

Swagger UI:

- `https://localhost:7124/swagger`
- `http://localhost:5200/swagger`

Health endpoint:

- `GET /health`

## Configuration files

Shared development configuration lives in:

- [Tracer.Web/appsettings.json](c:\Users\tedwell_d\source\repos\Tracer\Tracer.Web\appsettings.json)
- [Tracer.Scanner.Worker/appsettings.json](c:\Users\tedwell_d\source\repos\Tracer\Tracer.Scanner.Worker\appsettings.json)

Key config sections:

- `ConnectionStrings`
- `Scanner`
- `Alerts`
- `OuiVendor`
- `Notifications`

The worker and web app must point to the same database.

## Local launch URLs

The web launch profiles now default to:

- HTTPS: `https://localhost:7124`
- HTTP: `http://localhost:5200`
- Startup page: `/swagger`

Configured in [launchSettings.json](c:\Users\tedwell_d\source\repos\Tracer\Tracer.Web\Properties\launchSettings.json).

## Migrations

The applications apply EF Core migrations automatically on startup through `Database.MigrateAsync()`.

Current migration set includes:

- `20260306071245_InitialCreate`
- `20260306102037_AddAdminUsers`
- `20260306113645_AddRuntimeSecurityMonitoring`
- `20260306124500_RepairRuntimeSecurityMonitoringSchema`
- `20260308082849_ProductionHardening`

Create a new migration:

```powershell
dotnet ef migrations add <MigrationName> --project .\Tracer.Infrastructure\Tracer.Infrastructure.csproj --startup-project .\Tracer.Web\Tracer.Web.csproj --output-dir Persistence\Migrations
```

Apply manually:

```powershell
dotnet ef database update --project .\Tracer.Infrastructure\Tracer.Infrastructure.csproj --startup-project .\Tracer.Web\Tracer.Web.csproj
```

## How to run locally

From the repository root:

```powershell
cd C:\Users\tedwell_d\source\repos\Tracer
```

Start the worker:

```powershell
dotnet run --project .\Tracer.Scanner.Worker\
```

Start the web app:

```powershell
dotnet run --project .\Tracer.Web\
```

Recommended order:

1. Start `Tracer.Scanner.Worker`
2. Start `Tracer.Web`
3. Open `https://localhost:7124/swagger` or `http://localhost:5200/swagger`
4. Sign in with the admin account
5. Open the dashboard or settings pages after authentication

## Build commands

```powershell
dotnet build .\Tracer.slnx
dotnet clean .\Tracer.slnx
```

## Production notes

- Active radio scanning requires Windows hardware with working Wi-Fi and Bluetooth adapters.
- `LocalDB` is development-only; use a proper SQL Server instance in production.
- `Tracer.Scanner.Worker` is intended to run as a Windows Service.
- `Tracer.Web` should be hosted behind IIS or another supported reverse proxy.
- Replace the default admin password immediately.
- Configure `Notifications:Email` and/or `Notifications:Webhook` before expecting alert delivery.
- Existing signed-in sessions may need a fresh login after auth/role changes so cookie claims are refreshed.

## Key operational files

- [Tracer.Infrastructure/Persistence/TracerDbContext.cs](c:\Users\tedwell_d\source\repos\Tracer\Tracer.Infrastructure\Persistence\TracerDbContext.cs)
- [Tracer.Infrastructure/Persistence/Migrations/](c:\Users\tedwell_d\source\repos\Tracer\Tracer.Infrastructure\Persistence\Migrations)
- [Tracer.Infrastructure/Services/DatabaseInitializer.cs](c:\Users\tedwell_d\source\repos\Tracer\Tracer.Infrastructure\Services\DatabaseInitializer.cs)
- [Tracer.Infrastructure/Services/RuntimeSettingsService.cs](c:\Users\tedwell_d\source\repos\Tracer\Tracer.Infrastructure\Services\RuntimeSettingsService.cs)
- [Tracer.Infrastructure/Services/DeviceIntelligenceService.cs](c:\Users\tedwell_d\source\repos\Tracer\Tracer.Infrastructure\Services\DeviceIntelligenceService.cs)
- [Tracer.Infrastructure/Services/OuiVendorLookupService.cs](c:\Users\tedwell_d\source\repos\Tracer\Tracer.Infrastructure\Services\OuiVendorLookupService.cs)
- [Tracer.Infrastructure/Services/AlertNotificationService.cs](c:\Users\tedwell_d\source\repos\Tracer\Tracer.Infrastructure\Services\AlertNotificationService.cs)
- [Tracer.Infrastructure/Services/DataRetentionService.cs](c:\Users\tedwell_d\source\repos\Tracer\Tracer.Infrastructure\Services\DataRetentionService.cs)
- [Tracer.Scanner.Worker/Program.cs](c:\Users\tedwell_d\source\repos\Tracer\Tracer.Scanner.Worker\Program.cs)
- [Tracer.Scanner.Worker/Worker.cs](c:\Users\tedwell_d\source\repos\Tracer\Tracer.Scanner.Worker\Worker.cs)
- [Tracer.Scanner.Worker/OuiVendorRefreshWorker.cs](c:\Users\tedwell_d\source\repos\Tracer\Tracer.Scanner.Worker\OuiVendorRefreshWorker.cs)
- [Tracer.Scanner.Worker/DataRetentionWorker.cs](c:\Users\tedwell_d\source\repos\Tracer\Tracer.Scanner.Worker\DataRetentionWorker.cs)
- [Tracer.Web/Program.cs](c:\Users\tedwell_d\source\repos\Tracer\Tracer.Web\Program.cs)
- [Tracer.Web/Pages/Settings.cshtml](c:\Users\tedwell_d\source\repos\Tracer\Tracer.Web\Pages\Settings.cshtml)

## Known limitations

- Radio scanning remains Windows-specific.
- Distance estimation is still RSSI-based and approximate.
- Deep packet inspection is not implemented.
- Real MFA, external identity providers, distributed scanner nodes, SIEM forwarding, and advanced ML analysis are not implemented yet.
- Swagger is intended for local/testing use; lock it down appropriately before broader exposure.
