using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Antiphon.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddStandingAutomaticFreshRecovery : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "AutomaticFreshAlertedHoldAt",
                table: "AgentSupervisionStates",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "AutomaticFreshConsumed",
                table: "AgentSupervisionStates",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "AutomaticResumeFailures",
                table: "AgentSupervisionStates",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "LastAutomaticResumeSessionId",
                table: "AgentSupervisionStates",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastAutomaticResumeStartedAt",
                table: "AgentSupervisionStates",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AutomaticFreshAlertedHoldAt",
                table: "AgentSupervisionStates");

            migrationBuilder.DropColumn(
                name: "AutomaticFreshConsumed",
                table: "AgentSupervisionStates");

            migrationBuilder.DropColumn(
                name: "AutomaticResumeFailures",
                table: "AgentSupervisionStates");

            migrationBuilder.DropColumn(
                name: "LastAutomaticResumeSessionId",
                table: "AgentSupervisionStates");

            migrationBuilder.DropColumn(
                name: "LastAutomaticResumeStartedAt",
                table: "AgentSupervisionStates");
        }
    }
}
