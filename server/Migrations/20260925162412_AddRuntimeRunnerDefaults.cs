using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Antiphon.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddRuntimeRunnerDefaults : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RunnerRoutingSettings",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    GlobalRunnerId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastReason = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    LastProvenance = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    LastCallerTaskId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RunnerRoutingSettings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RunnerKindDefaults",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SettingsId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    AgentKind = table.Column<int>(type: "integer", nullable: false),
                    RunnerId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RunnerKindDefaults", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RunnerKindDefaults_RunnerRoutingSettings_SettingsId",
                        column: x => x.SettingsId,
                        principalTable: "RunnerRoutingSettings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RunnerRoutingRevisions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SettingsId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
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
                    table.PrimaryKey("PK_RunnerRoutingRevisions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RunnerRoutingRevisions_RunnerRoutingSettings_SettingsId",
                        column: x => x.SettingsId,
                        principalTable: "RunnerRoutingSettings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RunnerKindDefaults_SettingsId_AgentKind",
                table: "RunnerKindDefaults",
                columns: new[] { "SettingsId", "AgentKind" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RunnerRoutingRevisions_SettingsId_Revision",
                table: "RunnerRoutingRevisions",
                columns: new[] { "SettingsId", "Revision" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RunnerKindDefaults");

            migrationBuilder.DropTable(
                name: "RunnerRoutingRevisions");

            migrationBuilder.DropTable(
                name: "RunnerRoutingSettings");
        }
    }
}
