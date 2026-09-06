using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Antiphon.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddHerdrSupervisionHold : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "HerdrConsecutiveFailures",
                table: "AgentSupervisionStates",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "HerdrFailureHeldAt",
                table: "AgentSupervisionStates",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "HerdrHealthySince",
                table: "AgentSupervisionStates",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LastHerdrFailureKind",
                table: "AgentSupervisionStates",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "LastHerdrObservedSessionId",
                table: "AgentSupervisionStates",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastHerdrObservedStartedAt",
                table: "AgentSupervisionStates",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "HerdrSupervisionFailureKind",
                table: "AgentSessions",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "HerdrConsecutiveFailures",
                table: "AgentSupervisionStates");

            migrationBuilder.DropColumn(
                name: "HerdrFailureHeldAt",
                table: "AgentSupervisionStates");

            migrationBuilder.DropColumn(
                name: "HerdrHealthySince",
                table: "AgentSupervisionStates");

            migrationBuilder.DropColumn(
                name: "LastHerdrFailureKind",
                table: "AgentSupervisionStates");

            migrationBuilder.DropColumn(
                name: "LastHerdrObservedSessionId",
                table: "AgentSupervisionStates");

            migrationBuilder.DropColumn(
                name: "LastHerdrObservedStartedAt",
                table: "AgentSupervisionStates");

            migrationBuilder.DropColumn(
                name: "HerdrSupervisionFailureKind",
                table: "AgentSessions");
        }
    }
}
