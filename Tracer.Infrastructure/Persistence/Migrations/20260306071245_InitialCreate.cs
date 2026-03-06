using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tracer.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DiscoveredDevices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DeviceKey = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    RadioKind = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    HardwareAddress = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    NetworkName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    SecurityType = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    LastInterfaceName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    FrequencyBand = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    Channel = table.Column<int>(type: "int", nullable: true),
                    LastSignalStrength = table.Column<int>(type: "int", nullable: true),
                    IsPaired = table.Column<bool>(type: "bit", nullable: false),
                    IsKnown = table.Column<bool>(type: "bit", nullable: false),
                    TotalObservations = table.Column<int>(type: "int", nullable: false),
                    FirstSeenUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    LastSeenUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DiscoveredDevices", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ScanBatches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ScannerNode = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    StartedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CompletedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    TotalDevices = table.Column<int>(type: "int", nullable: false),
                    WifiDevices = table.Column<int>(type: "int", nullable: false),
                    BluetoothDevices = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScanBatches", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DeviceAlerts",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DeviceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AlertType = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Severity = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    AcknowledgedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Title = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Message = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeviceAlerts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DeviceAlerts_DiscoveredDevices_DeviceId",
                        column: x => x.DeviceId,
                        principalTable: "DiscoveredDevices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DeviceObservations",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DeviceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ScanBatchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ObservedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    HardwareAddress = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    NetworkName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    SecurityType = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    InterfaceName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    FrequencyBand = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    Channel = table.Column<int>(type: "int", nullable: true),
                    SignalStrength = table.Column<int>(type: "int", nullable: true),
                    IsPaired = table.Column<bool>(type: "bit", nullable: false),
                    RawPayload = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeviceObservations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DeviceObservations_DiscoveredDevices_DeviceId",
                        column: x => x.DeviceId,
                        principalTable: "DiscoveredDevices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DeviceObservations_ScanBatches_ScanBatchId",
                        column: x => x.ScanBatchId,
                        principalTable: "ScanBatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DeviceAlerts_DeviceId",
                table: "DeviceAlerts",
                column: "DeviceId");

            migrationBuilder.CreateIndex(
                name: "IX_DeviceAlerts_Status_CreatedUtc",
                table: "DeviceAlerts",
                columns: new[] { "Status", "CreatedUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_DeviceObservations_DeviceId",
                table: "DeviceObservations",
                column: "DeviceId");

            migrationBuilder.CreateIndex(
                name: "IX_DeviceObservations_ObservedUtc",
                table: "DeviceObservations",
                column: "ObservedUtc");

            migrationBuilder.CreateIndex(
                name: "IX_DeviceObservations_ScanBatchId",
                table: "DeviceObservations",
                column: "ScanBatchId");

            migrationBuilder.CreateIndex(
                name: "IX_DiscoveredDevices_DeviceKey",
                table: "DiscoveredDevices",
                column: "DeviceKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DiscoveredDevices_LastSeenUtc",
                table: "DiscoveredDevices",
                column: "LastSeenUtc");

            migrationBuilder.CreateIndex(
                name: "IX_ScanBatches_CompletedUtc",
                table: "ScanBatches",
                column: "CompletedUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DeviceAlerts");

            migrationBuilder.DropTable(
                name: "DeviceObservations");

            migrationBuilder.DropTable(
                name: "DiscoveredDevices");

            migrationBuilder.DropTable(
                name: "ScanBatches");
        }
    }
}
