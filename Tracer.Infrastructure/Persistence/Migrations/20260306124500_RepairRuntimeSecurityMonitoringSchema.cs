using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tracer.Infrastructure.Persistence.Migrations
{
    [DbContext(typeof(TracerDbContext))]
    [Migration("20260306124500_RepairRuntimeSecurityMonitoringSchema")]
    public partial class RepairRuntimeSecurityMonitoringSchema : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ConnectionState",
                table: "DiscoveredDevices",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "Unknown");

            migrationBuilder.AddColumn<string>(
                name: "DeviceType",
                table: "DiscoveredDevices",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "Unknown");

            migrationBuilder.AddColumn<decimal>(
                name: "EstimatedDistanceMeters",
                table: "DiscoveredDevices",
                type: "decimal(8,2)",
                precision: 8,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastConnectedUtc",
                table: "DiscoveredDevices",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastRecommendation",
                table: "DiscoveredDevices",
                type: "nvarchar(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MovementTrend",
                table: "DiscoveredDevices",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "Stable");

            migrationBuilder.AddColumn<string>(
                name: "Reputation",
                table: "DiscoveredDevices",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "Unknown");

            migrationBuilder.AddColumn<string>(
                name: "RiskReasons",
                table: "DiscoveredDevices",
                type: "nvarchar(1024)",
                maxLength: 1024,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RiskScore",
                table: "DiscoveredDevices",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "VendorName",
                table: "DiscoveredDevices",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VendorPrefix",
                table: "DiscoveredDevices",
                type: "nvarchar(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ConnectionState",
                table: "DeviceObservations",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "Unknown");

            migrationBuilder.AddColumn<string>(
                name: "DeviceType",
                table: "DeviceObservations",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "Unknown");

            migrationBuilder.AddColumn<decimal>(
                name: "EstimatedDistanceMeters",
                table: "DeviceObservations",
                type: "decimal(8,2)",
                precision: 8,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MovementTrend",
                table: "DeviceObservations",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "Stable");

            migrationBuilder.AddColumn<string>(
                name: "Reputation",
                table: "DeviceObservations",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "Unknown");

            migrationBuilder.AddColumn<int>(
                name: "RiskScore",
                table: "DeviceObservations",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "AdapterStatusSummary",
                table: "ScanBatches",
                type: "nvarchar(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "DurationMilliseconds",
                table: "ScanBatches",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<int>(
                name: "ErrorCount",
                table: "ScanBatches",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<double>(
                name: "MemoryUsageMb",
                table: "ScanBatches",
                type: "float",
                nullable: false,
                defaultValue: 0d);

            migrationBuilder.AddColumn<int>(
                name: "SuspiciousDevices",
                table: "ScanBatches",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "RuntimeSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    EnableWifi = table.Column<bool>(type: "bit", nullable: false),
                    EnableBluetooth = table.Column<bool>(type: "bit", nullable: false),
                    ScanIntervalSeconds = table.Column<int>(type: "int", nullable: false),
                    ApproximateRangeMeters = table.Column<int>(type: "int", nullable: false),
                    WifiScanTimeoutSeconds = table.Column<int>(type: "int", nullable: false),
                    MinimumWifiSignalQuality = table.Column<int>(type: "int", nullable: false),
                    CreateAlertsForUnknownDevices = table.Column<bool>(type: "bit", nullable: false),
                    ReturnAlertThresholdMinutes = table.Column<int>(type: "int", nullable: false),
                    EnableRogueWifiDetection = table.Column<bool>(type: "bit", nullable: false),
                    EnableUnknownBluetoothConnectionAlerts = table.Column<bool>(type: "bit", nullable: false),
                    EnableAutomaticRecommendations = table.Column<bool>(type: "bit", nullable: false),
                    RiskAlertThreshold = table.Column<int>(type: "int", nullable: false),
                    AutoLogDevices = table.Column<bool>(type: "bit", nullable: false),
                    EnablePacketMetadataCapture = table.Column<bool>(type: "bit", nullable: false),
                    EnableTrafficAnalysis = table.Column<bool>(type: "bit", nullable: false),
                    LastUpdatedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RuntimeSettings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ScanEventLogs",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ScanBatchId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DeviceId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ScannerNode = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    RadioKind = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    Level = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    EventType = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Message = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    Details = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScanEventLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ScanEventLogs_DiscoveredDevices_DeviceId",
                        column: x => x.DeviceId,
                        principalTable: "DiscoveredDevices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_ScanEventLogs_ScanBatches_ScanBatchId",
                        column: x => x.ScanBatchId,
                        principalTable: "ScanBatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ScanEventLogs_CreatedUtc",
                table: "ScanEventLogs",
                column: "CreatedUtc");

            migrationBuilder.CreateIndex(
                name: "IX_ScanEventLogs_DeviceId",
                table: "ScanEventLogs",
                column: "DeviceId");

            migrationBuilder.CreateIndex(
                name: "IX_ScanEventLogs_ScanBatchId",
                table: "ScanEventLogs",
                column: "ScanBatchId");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RuntimeSettings");

            migrationBuilder.DropTable(
                name: "ScanEventLogs");

            migrationBuilder.DropColumn(
                name: "ConnectionState",
                table: "DiscoveredDevices");

            migrationBuilder.DropColumn(
                name: "DeviceType",
                table: "DiscoveredDevices");

            migrationBuilder.DropColumn(
                name: "EstimatedDistanceMeters",
                table: "DiscoveredDevices");

            migrationBuilder.DropColumn(
                name: "LastConnectedUtc",
                table: "DiscoveredDevices");

            migrationBuilder.DropColumn(
                name: "LastRecommendation",
                table: "DiscoveredDevices");

            migrationBuilder.DropColumn(
                name: "MovementTrend",
                table: "DiscoveredDevices");

            migrationBuilder.DropColumn(
                name: "Reputation",
                table: "DiscoveredDevices");

            migrationBuilder.DropColumn(
                name: "RiskReasons",
                table: "DiscoveredDevices");

            migrationBuilder.DropColumn(
                name: "RiskScore",
                table: "DiscoveredDevices");

            migrationBuilder.DropColumn(
                name: "VendorName",
                table: "DiscoveredDevices");

            migrationBuilder.DropColumn(
                name: "VendorPrefix",
                table: "DiscoveredDevices");

            migrationBuilder.DropColumn(
                name: "ConnectionState",
                table: "DeviceObservations");

            migrationBuilder.DropColumn(
                name: "DeviceType",
                table: "DeviceObservations");

            migrationBuilder.DropColumn(
                name: "EstimatedDistanceMeters",
                table: "DeviceObservations");

            migrationBuilder.DropColumn(
                name: "MovementTrend",
                table: "DeviceObservations");

            migrationBuilder.DropColumn(
                name: "Reputation",
                table: "DeviceObservations");

            migrationBuilder.DropColumn(
                name: "RiskScore",
                table: "DeviceObservations");

            migrationBuilder.DropColumn(
                name: "AdapterStatusSummary",
                table: "ScanBatches");

            migrationBuilder.DropColumn(
                name: "DurationMilliseconds",
                table: "ScanBatches");

            migrationBuilder.DropColumn(
                name: "ErrorCount",
                table: "ScanBatches");

            migrationBuilder.DropColumn(
                name: "MemoryUsageMb",
                table: "ScanBatches");

            migrationBuilder.DropColumn(
                name: "SuspiciousDevices",
                table: "ScanBatches");
        }
    }
}
