# Tracer

Tracer is a Windows-based local Wi-Fi and Bluetooth monitoring system built on .NET 8. It continuously scans nearby radios, stores discoveries in SQL Server, classifies devices, assigns risk, raises alerts, and exposes a secured Razor Pages dashboard for operators.

## Current system capabilities

### Core scanning

- Detects nearby Wi-Fi access points.
- Detects nearby Bluetooth devices.
- Runs continuous background scans in `Tracer.Scanner.Worker`.
- Measures signal strength and stores RSSI-based observations.
- Persists discoveries, scan batches, alerts, logs, and runtime settings in SQL Server.

### Security and intelligence

- Raises alerts for unknown devices.
- Allows operators to mark devices as trusted.
- Detects returned devices after configurable absence.
- Detects potential rogue Wi-Fi / duplicate SSIDs.
- Flags unknown Bluetooth devices that appear connected.
- Computes device risk scores from heuristics.
- Tracks vendor prefix, vendor name, device type, reputation, connection state, movement trend, and estimated distance.
- Generates automatic security recommendations for suspicious conditions.

### UI and operations

- Secured admin login for the dashboard.
- Dashboard charts for Wi-Fi vs Bluetooth totals and connectivity status.
- Device management pages for Wi-Fi and Bluetooth.
- Password manager page for stored Wi-Fi credentials.
- Settings page backed by persisted runtime configuration.
- Monitoring page for scan diagnostics, event logs, adapter health, and recent observations.
- Reports page with daily summaries, weekly summaries, and CSV export.

## Default admin login

The database initializer seeds a default admin account if one does not already exist:

- Username: `admin`
- Password: `Admin@123`

Change this immediately for any environment beyond local development.

## Tech stack

- Backend language: `C#`
- Runtime: `.NET 8`
- UI: `ASP.NET Core Razor Pages`
- Background processing: `.NET Worker Service`
- ORM: `Entity Framework Core 8`
- Database:
  - Development: `SQL Server LocalDB`
  - Recommended production target: `SQL Server 2022`
- Wi-Fi scanning: `ManagedNativeWifi`
- Bluetooth scanning: `InTheHand.Net.Bluetooth` (`32feet.NET`)
- OS target for active scanning: `Windows`

## Solution architecture

- `Tracer.Core`
  - Shared entities, contracts, enums, and security helpers.
- `Tracer.Infrastructure`
  - EF Core persistence, migrations, database initialization, runtime settings, device intelligence, and scan orchestration.
- `Tracer.Radio.Windows`
  - Windows-specific Wi-Fi and Bluetooth scanner implementations.
- `Tracer.Scanner.Worker`
  - Background scan host intended for long-running service execution.
- `Tracer.Web`
  - Secured operator dashboard, settings UI, monitoring, reports, and API endpoints.

## Runtime flow

1. `Tracer.Scanner.Worker` starts.
2. The worker applies pending EF Core migrations.
3. Runtime settings are loaded from SQL Server.
4. Wi-Fi and Bluetooth snapshots are collected on the configured interval.
5. `ScanCoordinator` deduplicates, evaluates, scores, and persists results.
6. Alerts and scan event logs are created for unknown, suspicious, rogue, or connected devices.
7. `Tracer.Web` reads the same database and exposes dashboard, management, monitoring, and reports views.
8. Operators review devices, acknowledge alerts, and tune scanner behavior from the web UI.

## Project structure

```text
Tracer
|
+-- Tracer.Core
+-- Tracer.Infrastructure
|   +-- Persistence
|   |   +-- TracerDbContext.cs
|   |   +-- Migrations
|   +-- Services
+-- Tracer.Radio.Windows
|   +-- Services
+-- Tracer.Scanner.Worker
+-- Tracer.Web
    +-- Pages
    +-- wwwroot
```

## Database model

The current schema includes these primary tables:

- `AdminUsers`
- `RuntimeSettings`
- `DiscoveredDevices`
- `DeviceObservations`
- `DeviceAlerts`
- `ScanBatches`
- `ScanEventLogs`
- `__EFMigrationsHistory`

## Key stored device metadata

`DiscoveredDevices` and `DeviceObservations` now capture:

- MAC / hardware address
- network name or device name
- signal strength
- vendor prefix and vendor name
- device type
- device reputation
- risk score and risk reasons
- connection state
- movement trend
- estimated distance
- last recommendation

## Runtime configuration

The settings page persists these runtime controls:

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

Both `Tracer.Web` and `Tracer.Scanner.Worker` use the same database-backed runtime settings.

## Monitoring and reporting

### Monitoring page

- latest worker batch details
- adapter status summary
- scan duration and memory usage
- recent observations
- diagnostic scan event log

### Reports page

- daily scan / observation summary
- weekly scan / observation summary
- high-risk device summary
- CSV export for daily, weekly, and risk-device reports

## Connection strings

### Default development connection string

```text
Data Source=(localdb)\MSSQLLocalDB;Initial Catalog=TracerDb;MultipleActiveResultSets=true;TrustServerCertificate=true;
```

### Where to change it

- `Tracer.Web/appsettings.json`
- `Tracer.Web/appsettings.Development.json`
- `Tracer.Scanner.Worker/appsettings.json`
- `Tracer.Scanner.Worker/appsettings.Development.json`
- fallback in `Tracer.Infrastructure/ServiceCollectionExtensions.cs`

### Rule

The web app and worker must point to the same database.

## Migrations

The applications use EF Core migrations and apply them on startup via `Database.MigrateAsync()`.

### Existing migrations

- `20260306071245_InitialCreate`
- `20260306102037_AddAdminUsers`
- `20260306113645_AddRuntimeSecurityMonitoring`
- `20260306124500_RepairRuntimeSecurityMonitoringSchema`

### Create a new migration

```powershell
dotnet ef migrations add <MigrationName> --project .\Tracer.Infrastructure\Tracer.Infrastructure.csproj --startup-project .\Tracer.Web\Tracer.Web.csproj --output-dir Persistence\Migrations
```

### Apply migrations manually

```powershell
dotnet ef database update --project .\Tracer.Infrastructure\Tracer.Infrastructure.csproj --startup-project .\Tracer.Web\Tracer.Web.csproj
```

## How to run locally

From the repository root:

```powershell
cd C:\Users\tedwell_d\source\repos\Tracer
```

### Start the worker

```powershell
dotnet run --project .\Tracer.Scanner.Worker\
```

### Start the web app

```powershell
dotnet run --project .\Tracer.Web\
```

### Recommended order

1. Start `Tracer.Scanner.Worker`
2. Start `Tracer.Web`
3. Open the URL printed by the web app
4. Log in with the seeded admin account if needed

## Build commands

```powershell
dotnet build .\Tracer.slnx
dotnet clean .\Tracer.slnx
```

## Production deployment notes

- Active radio scanning requires Windows hardware with working Wi-Fi and Bluetooth adapters.
- `LocalDB` is for development only; use SQL Server 2022 or another proper SQL Server edition in production.
- `Tracer.Scanner.Worker` is best hosted as a Windows Service.
- `Tracer.Web` is best hosted behind IIS or another supported ASP.NET Core reverse proxy setup.
- Both applications must share one SQL Server database.
- Replace the default admin password before production use.

## Key operational files

- `Tracer.Infrastructure/Persistence/TracerDbContext.cs`
- `Tracer.Infrastructure/Persistence/Migrations/`
- `Tracer.Infrastructure/Services/DatabaseInitializer.cs`
- `Tracer.Infrastructure/Services/RuntimeSettingsService.cs`
- `Tracer.Infrastructure/Services/DeviceIntelligenceService.cs`
- `Tracer.Infrastructure/Services/ScanCoordinator.cs`
- `Tracer.Radio.Windows/Services/WifiScanner.cs`
- `Tracer.Radio.Windows/Services/BluetoothScanner.cs`
- `Tracer.Scanner.Worker/Worker.cs`
- `Tracer.Scanner.Worker/Program.cs`
- `Tracer.Web/Program.cs`

## Current limitations

- Radio scanning is Windows-specific.
- Distance is estimated from signal strength and is inherently approximate.
- Vendor lookup is currently heuristic and based on a built-in prefix map, not a live OUI feed.
- Traffic analysis and packet metadata capture settings are present, but deep packet inspection is not yet implemented.
- PDF / Excel report export, heatmaps, proximity maps, automatic network blocking, AI behavior analysis, multi-location aggregation, and cloud sync are not implemented yet.
