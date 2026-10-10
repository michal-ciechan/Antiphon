using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Antiphon.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddDispatchConcurrencySettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DispatchConcurrencySettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ScopeKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: true),
                    SchemaVersion = table.Column<int>(type: "integer", nullable: false),
                    OverridesJson = table.Column<string>(type: "jsonb", nullable: false),
                    SeedJson = table.Column<string>(type: "jsonb", nullable: true),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastReason = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    LastProvenance = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    LastCallerTaskId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DispatchConcurrencySettings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DispatchConcurrencyRevisions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SettingsId = table.Column<Guid>(type: "uuid", nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    PreviousRevision = table.Column<long>(type: "bigint", nullable: true),
                    SnapshotJson = table.Column<string>(type: "jsonb", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Reason = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    Provenance = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CallerTaskId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DispatchConcurrencyRevisions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DispatchConcurrencyRevisions_DispatchConcurrencySettings_Se~",
                        column: x => x.SettingsId,
                        principalTable: "DispatchConcurrencySettings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DispatchConcurrencyRevisions_SettingsId_Revision",
                table: "DispatchConcurrencyRevisions",
                columns: new[] { "SettingsId", "Revision" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DispatchConcurrencySettings_ScopeKey",
                table: "DispatchConcurrencySettings",
                column: "ScopeKey",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DispatchConcurrencyRevisions");

            migrationBuilder.DropTable(
                name: "DispatchConcurrencySettings");
        }
    }
}
