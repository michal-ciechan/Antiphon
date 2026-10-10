using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Antiphon.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddOrchestratorInstructions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "OrchestratorInstructionsVersion",
                table: "AgentSessions",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "OrchestratorInstructionsStates",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    Version = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Body = table.Column<string>(type: "text", nullable: false),
                    SnapshotJson = table.Column<string>(type: "text", nullable: false),
                    PreviousSnapshotJson = table.Column<string>(type: "text", nullable: true),
                    WrittenAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    WrittenPath = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    LastReason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    LastWriteError = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrchestratorInstructionsStates", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OrchestratorInstructionsStates");

            migrationBuilder.DropColumn(
                name: "OrchestratorInstructionsVersion",
                table: "AgentSessions");
        }
    }
}
