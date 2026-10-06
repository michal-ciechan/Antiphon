using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Antiphon.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddHostOccupancySamples : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "HostOccupancySamples",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    HostId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    SampledAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    InventoryState = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    InventoryReason = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    InFlight = table.Column<int>(type: "integer", nullable: false),
                    DispatchedWorking = table.Column<int>(type: "integer", nullable: false),
                    Sessions = table.Column<int>(type: "integer", nullable: false),
                    PendingLaunch = table.Column<int>(type: "integer", nullable: false),
                    InFlightMirrors = table.Column<int>(type: "integer", nullable: false),
                    IdleSeats = table.Column<int>(type: "integer", nullable: false),
                    PooledWarmSeats = table.Column<int>(type: "integer", nullable: false),
                    OrphanSlots = table.Column<int>(type: "integer", nullable: false),
                    EffectiveLimit = table.Column<int>(type: "integer", nullable: true),
                    DeclaredCapacity = table.Column<int>(type: "integer", nullable: true),
                    OldestIdleSince = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HostOccupancySamples", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_HostOccupancySamples_HostId_SampledAt",
                table: "HostOccupancySamples",
                columns: new[] { "HostId", "SampledAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "HostOccupancySamples");
        }
    }
}
