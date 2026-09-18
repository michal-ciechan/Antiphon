using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Antiphon.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddRunnerBuildHold : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "RunnerBuildHeldAt",
                table: "AgentSupervisionStates",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RunnerBuildHeldIdentity",
                table: "AgentSupervisionStates",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RunnerBuildHoldEvidence",
                table: "AgentSupervisionStates",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RunnerBuildHeldAt",
                table: "AgentSupervisionStates");

            migrationBuilder.DropColumn(
                name: "RunnerBuildHeldIdentity",
                table: "AgentSupervisionStates");

            migrationBuilder.DropColumn(
                name: "RunnerBuildHoldEvidence",
                table: "AgentSupervisionStates");
        }
    }
}
