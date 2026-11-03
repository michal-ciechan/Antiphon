using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Antiphon.Server.Migrations
{
    /// <inheritdoc />
    public partial class Card0824BranchDeletion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "BranchDeletionAt",
                table: "AgentTaskLandings",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BranchDeletionDetail",
                table: "AgentTaskLandings",
                type: "character varying(400)",
                maxLength: 400,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BranchDeletionReason",
                table: "AgentTaskLandings",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "BranchDeletionState",
                table: "AgentTaskLandings",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "RunnerRefsDeletedAt",
                table: "AgentTaskLandings",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BranchDeletionAt",
                table: "AgentTaskLandings");

            migrationBuilder.DropColumn(
                name: "BranchDeletionDetail",
                table: "AgentTaskLandings");

            migrationBuilder.DropColumn(
                name: "BranchDeletionReason",
                table: "AgentTaskLandings");

            migrationBuilder.DropColumn(
                name: "BranchDeletionState",
                table: "AgentTaskLandings");

            migrationBuilder.DropColumn(
                name: "RunnerRefsDeletedAt",
                table: "AgentTaskLandings");
        }
    }
}
