using System;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Antiphon.Server.Migrations
{
    /// <summary>
    /// CARD-0305: per-card/stage routing pins with Human-vs-Auto provenance. Hand-written (running
    /// daemons lock bin/); the [DbContext]/[Migration] attributes normally generated into the
    /// Designer live here. Snapshot is updated to match.
    /// </summary>
    [DbContext(typeof(AppDbContext))]
    [Migration("20260901200000_AddRoutingPins")]
    public partial class AddRoutingPins : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RoutingPins",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CardId = table.Column<Guid>(type: "uuid", nullable: true),
                    Role = table.Column<int>(type: "integer", nullable: false),
                    Provenance = table.Column<int>(type: "integer", nullable: false),
                    Strength = table.Column<int>(type: "integer", nullable: false),
                    AgentKind = table.Column<int>(type: "integer", nullable: true),
                    ModelLevel = table.Column<int>(type: "integer", nullable: true),
                    AgentId = table.Column<Guid>(type: "uuid", nullable: true),
                    ForbiddenAliases = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    NotBefore = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    NotAfter = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Reason = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    SourceTaskId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ClearedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RoutingPins", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RoutingPins_Cards_CardId",
                        column: x => x.CardId,
                        principalTable: "Cards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RoutingPins_CardId_Role_Active",
                table: "RoutingPins",
                columns: new[] { "CardId", "Role" },
                unique: true,
                filter: "\"CardId\" IS NOT NULL AND \"ClearedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_RoutingPins_Role_Stage_Active",
                table: "RoutingPins",
                column: "Role",
                unique: true,
                filter: "\"CardId\" IS NULL AND \"ClearedAt\" IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RoutingPins");
        }
    }
}
