using System;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Antiphon.Server.Migrations
{
    /// <summary>
    /// CARD-0022: per-(kind, model-alias) availability holds. Hand-written (running daemons lock
    /// bin/); the [DbContext]/[Migration] attributes normally generated into the Designer live
    /// here. Snapshot is updated to match.
    /// </summary>
    [DbContext(typeof(AppDbContext))]
    [Migration("20260901180000_AddModelAvailabilityHolds")]
    public partial class AddModelAvailabilityHolds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ModelAvailabilityHolds",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    ModelAlias = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Source = table.Column<int>(type: "integer", nullable: false),
                    DisabledUntil = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    HitAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ClearedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RawText = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    SourceSessionId = table.Column<Guid>(type: "uuid", nullable: true),
                    SourceTaskId = table.Column<Guid>(type: "uuid", nullable: true),
                    Reason = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ModelAvailabilityHolds", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ModelAvailabilityHolds_Kind_ModelAlias_Active",
                table: "ModelAvailabilityHolds",
                columns: new[] { "Kind", "ModelAlias" },
                unique: true,
                filter: "\"ClearedAt\" IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ModelAvailabilityHolds");
        }
    }
}
