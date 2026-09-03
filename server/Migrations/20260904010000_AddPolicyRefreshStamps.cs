using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Antiphon.Server.Migrations
{
    /// <summary>
    /// CARD-0334 S1: AgentSessions.InstructionFileStamp / PolicyNotifiedStamp and
    /// Agents.PolicyRefreshMode. Hand-written (running daemons lock bin/); the [DbContext]/
    /// [Migration] attributes normally generated into the Designer live here. Snapshot is
    /// updated to match. No backfill — null is no evidence / Auto.
    /// </summary>
    [DbContext(typeof(AppDbContext))]
    [Migration("20260904010000_AddPolicyRefreshStamps")]
    public partial class AddPolicyRefreshStamps : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "InstructionFileStamp",
                table: "AgentSessions",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PolicyNotifiedStamp",
                table: "AgentSessions",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PolicyRefreshMode",
                table: "Agents",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "InstructionFileStamp",
                table: "AgentSessions");

            migrationBuilder.DropColumn(
                name: "PolicyNotifiedStamp",
                table: "AgentSessions");

            migrationBuilder.DropColumn(
                name: "PolicyRefreshMode",
                table: "Agents");
        }
    }
}
