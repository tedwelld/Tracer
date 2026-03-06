# Tracer

Tracer is a Windows-based local Wi-Fi and Bluetooth scanner built on .NET 8. It continuously scans nearby radios, stores discoveries in SQL Server, raises alerts for unknown devices, and exposes a local Razor Pages dashboard for operators.

## What the system does

- Scans nearby Wi-Fi access points.
- Scans nearby Bluetooth devices.
- Runs continuous background scans in a worker service.
- Stores devices, scan batches, observations, and alerts in SQL Server.
- Shows a local web dashboard for recent devices and pending alerts.
- Lets operators acknowledge alerts or mark devices as trusted.

## Tech stack

- Backend language: `C#`
- Runtime: `.NET 8`
- UI: `ASP.NET Core Razor Pages`
- Background processing: `.NET Worker Service`
- Wi-Fi scanning: `ManagedNativeWifi`
- Bluetooth scanning: `InTheHand.Net.Bluetooth` (`32feet.NET`)
- Database ORM: `Entity Framework Core 8`
- Database engine:
  - Development: `SQL Server LocalDB`
  - Production recommendation: `SQL Server 2022` or `SQL Server Express/Standard`
- OS target for radio scanning: `Windows`

## Solution architecture

The solution is split by responsibility:

- `Tracer.Core`
  - Shared contracts, enums, options, and entities.
- `Tracer.Infrastructure`
  - EF Core persistence.
  - SQL Server configuration.
  - scan orchestration.
  - database initialization and migrations.
- `Tracer.Radio.Windows`
  - Windows-only radio integrations for Wi-Fi and Bluetooth.
- `Tracer.Scanner.Worker`
  - Continuous background scanner process.
  - Best suited to run as a Windows Service in production.
- `Tracer.Web`
  - Local operator dashboard and HTTP endpoints.
  - Displays recent devices, latest scans, and pending alerts.

## Runtime flow

1. `Tracer.Scanner.Worker` starts.
2. The worker initializes the database and applies pending EF Core migrations.
3. The worker runs the scan loop on the configured interval.
4. Wi-Fi and Bluetooth snapshots are collected.
5. `ScanCoordinator` deduplicates and persists results.
6. New or returning unknown devices generate alerts.
7. `Tracer.Web` reads from the same SQL database and displays the current state.
8. The operator acknowledges or trusts devices from the web UI.

## Project structure

```text
Tracer
│
├── Tracer.Core
├── Tracer.Infrastructure
│   ├── Persistence
│   │   ├── TracerDbContext.cs
│   │   └── Migrations
│   └── Services
├── Tracer.Radio.Windows
│   └── Services
├── Tracer.Scanner.Worker
└── Tracer.Web
    ├── Pages
    └── wwwroot
```

## Database model

The EF Core model currently creates these main tables:

- `DiscoveredDevices`
- `DeviceObservations`
- `DeviceAlerts`
- `ScanBatches`
- `__EFMigrationsHistory`

## Direct NuGet packages installed

### `Tracer.Infrastructure`

- `Microsoft.EntityFrameworkCore` `8.0.12`
- `Microsoft.EntityFrameworkCore.Design` `8.0.12`
- `Microsoft.EntityFrameworkCore.SqlServer` `8.0.12`
- `Microsoft.Extensions.Options.ConfigurationExtensions` `8.0.0`

### `Tracer.Radio.Windows`

- `InTheHand.Net.Bluetooth` `4.2.2`
- `ManagedNativeWifi` `3.0.2`
- `Microsoft.Extensions.DependencyInjection.Abstractions` `8.0.2`
- `Microsoft.Extensions.Logging.Abstractions` `8.0.3`
- `Microsoft.Extensions.Options.ConfigurationExtensions` `8.0.0`

### `Tracer.Scanner.Worker`

- `Microsoft.Extensions.Hosting` `8.0.1`
- `Microsoft.Extensions.Hosting.WindowsServices` `8.0.1`

### `Tracer.Web`

- `Microsoft.EntityFrameworkCore.Design` `8.0.12`

## Development prerequisites

- Windows machine with Wi-Fi and Bluetooth hardware available.
- `.NET 8 SDK` installed.
- SQL Server LocalDB or SQL Server 2022 available.
- Windows location access enabled for Wi-Fi scanning on recent Windows builds.
- For reliable Bluetooth discovery, the host Bluetooth radio must be enabled and working.

## Connection strings

### Current default connection string

The solution is currently configured to use:

```text
Data Source=(localdb)\MSSQLLocalDB;Initial Catalog=TracerDb;MultipleActiveResultSets=true;TrustServerCertificate=true;
```

### Where to change connection strings

Update the `TracerDb` connection string in:

- `Tracer.Web/appsettings.json`
- `Tracer.Web/appsettings.Development.json`
- `Tracer.Scanner.Worker/appsettings.json`
- `Tracer.Scanner.Worker/appsettings.Development.json`

There is also a fallback default in:

- `Tracer.Infrastructure/ServiceCollectionExtensions.cs`

### Recommended rule

- For development, keep both `Tracer.Web` and `Tracer.Scanner.Worker` pointed at the same LocalDB or SQL Server database.
- For production, point both processes at the same SQL Server instance and database.

## Migrations

The application now uses EF Core migrations, not `EnsureCreated`.

### Existing migration

- `20260306071245_InitialCreate`

### Create a new migration

Run from the repository root:

```powershell
dotnet ef migrations add <MigrationName> --project .\Tracer.Infrastructure\Tracer.Infrastructure.csproj --startup-project .\Tracer.Web\Tracer.Web.csproj --output-dir Persistence\Migrations
```

Example:

```powershell
dotnet ef migrations add AddDeviceTags --project .\Tracer.Infrastructure\Tracer.Infrastructure.csproj --startup-project .\Tracer.Web\Tracer.Web.csproj --output-dir Persistence\Migrations
```

### Apply migrations manually

```powershell
dotnet ef database update --project .\Tracer.Infrastructure\Tracer.Infrastructure.csproj --startup-project .\Tracer.Web\Tracer.Web.csproj
```

### Roll back to a specific migration

```powershell
dotnet ef database update <MigrationName> --project .\Tracer.Infrastructure\Tracer.Infrastructure.csproj --startup-project .\Tracer.Web\Tracer.Web.csproj
```

### Runtime behavior

Both startup paths use `Database.MigrateAsync()`, so pending migrations are also applied automatically when the worker or web app starts.

## How to run the system locally

Open terminals in the repository root:

```powershell
cd C:\Users\tedwell_d\source\repos\Tracer
```

### Terminal 1: start the backend scanner worker

```powershell
dotnet run --project .\Tracer.Scanner.Worker\
```

This starts:

- database initialization
- automatic migration application
- continuous Wi-Fi and Bluetooth scan loop

### Terminal 2: start the frontend web dashboard

```powershell
dotnet run --project .\Tracer.Web\
```

This starts:

- the Razor Pages dashboard
- alert and device API endpoints
- the local UI for viewing discoveries and acknowledging alerts

### Recommended startup order

1. Start `Tracer.Scanner.Worker`
2. Start `Tracer.Web`
3. Open the URL shown in the terminal for `Tracer.Web`

## Build commands

Build the full solution:

```powershell
dotnet build .\Tracer.slnx
```

Clean the full solution:

```powershell
dotnet clean .\Tracer.slnx
```

## Configuration areas

### Scanner settings

Configured under the `Scanner` section:

- `EnableWifi`
- `EnableBluetooth`
- `ScanIntervalSeconds`
- `ApproximateRangeMeters`
- `WifiScanTimeoutSeconds`
- `MinimumWifiSignalQuality`

### Alert settings

Configured under the `Alerts` section:

- `CreateAlertsForUnknownDevices`
- `ReturnAlertThresholdMinutes`

## Creation pattern used in this system

The implementation follows a layered, project-separated pattern:

- `Core` for shared domain types.
- `Infrastructure` for persistence and orchestration.
- `Radio.Windows` as the hardware adapter layer.
- `Scanner.Worker` as the background execution host.
- `Web` as the operator-facing presentation host.

This pattern makes the system easier to maintain because:

- hardware access stays isolated to Windows-specific code
- database concerns stay isolated to EF Core infrastructure
- the dashboard remains separate from scanning logic
- the worker and web app can be deployed independently while sharing the same database

## Recommended production deployment pattern

### Recommended topology

For a live environment, use this pattern:

1. A Windows machine with physical Wi-Fi and Bluetooth radios attached.
2. `Tracer.Scanner.Worker` deployed on that Windows machine.
3. `Tracer.Web` deployed on the same machine or another reachable Windows host.
4. A shared SQL Server database used by both applications.

### Important production note

`LocalDB` is suitable for local development, but it is not the recommended production database target.

For production, use one of:

- SQL Server 2022
- SQL Server Express
- SQL Server Standard

### Worker deployment pattern

Recommended:

- publish the worker
- install it as a Windows Service
- configure it to auto-start

Publish example:

```powershell
dotnet publish .\Tracer.Scanner.Worker\Tracer.Scanner.Worker.csproj -c Release -r win-x64 -o .\publish\worker
```

Typical live pattern:

- host OS: Windows
- service type: Windows Service
- restart policy: automatic
- account: service account with access to radios and database

### Web deployment pattern

Recommended:

- publish `Tracer.Web`
- host behind IIS on Windows, or run as an ASP.NET Core process behind a reverse proxy

Publish example:

```powershell
dotnet publish .\Tracer.Web\Tracer.Web.csproj -c Release -r win-x64 -o .\publish\web
```

Typical live pattern:

- IIS site or application
- ASP.NET Core Hosting Bundle installed on the server
- shared SQL Server connection string configured for production
- HTTPS enabled

## Deployment checklist for live server

- Use a Windows host with working Wi-Fi and Bluetooth radios.
- Enable Windows services and permissions needed for radio access.
- Confirm Windows location permissions for Wi-Fi scanning.
- Replace LocalDB with a production SQL Server connection string.
- Apply migrations before or during first startup.
- Publish worker and web artifacts in `Release`.
- Install `Tracer.Scanner.Worker` as a Windows Service.
- Host `Tracer.Web` under IIS or another supported reverse proxy setup.
- Ensure both applications use the same production database.
- Open firewall ports only as needed for the web dashboard.

## Suggested production connection string pattern

Example:

```text
Server=YOUR_SQL_SERVER;Database=TracerDb;User Id=YOUR_USER;Password=YOUR_PASSWORD;TrustServerCertificate=true;MultipleActiveResultSets=true;
```

Or with integrated security:

```text
Server=YOUR_SQL_SERVER;Database=TracerDb;Integrated Security=true;TrustServerCertificate=true;MultipleActiveResultSets=true;
```

## Key operational files

- `Tracer.Infrastructure/Persistence/TracerDbContext.cs`
- `Tracer.Infrastructure/Persistence/Migrations/`
- `Tracer.Infrastructure/Services/DatabaseInitializer.cs`
- `Tracer.Infrastructure/Services/ScanCoordinator.cs`
- `Tracer.Radio.Windows/Services/WifiScanner.cs`
- `Tracer.Radio.Windows/Services/BluetoothScanner.cs`
- `Tracer.Scanner.Worker/Worker.cs`
- `Tracer.Scanner.Worker/Program.cs`
- `Tracer.Web/Program.cs`

## Current limitations and deployment caveats

- Radio scanning is Windows-specific.
- Effective range is approximate and depends on hardware, signal strength, walls, and interference.
- Browser prompts in `Tracer.Web` require the dashboard to be open.
- `LocalDB` is development-oriented and should not be treated as a production database platform.
- A server without real radio hardware will not behave like a true scanner node.
