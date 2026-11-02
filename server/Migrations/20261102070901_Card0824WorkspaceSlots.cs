using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Antiphon.Server.Migrations
{
    /// <inheritdoc />
    public partial class Card0824WorkspaceSlots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "RemoteWorkspaceSlotGeneration",
                table: "AgentTasks",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RemoteWorkspaceSlotId",
                table: "AgentTasks",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "RemoteWorkspaceSlotReleasedAt",
                table: "AgentTasks",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "WorkspaceColdReset",
                table: "AgentTasks",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RemoteWorkspaceSlotGeneration",
                table: "AgentTasks");

            migrationBuilder.DropColumn(
                name: "RemoteWorkspaceSlotId",
                table: "AgentTasks");

            migrationBuilder.DropColumn(
                name: "RemoteWorkspaceSlotReleasedAt",
                table: "AgentTasks");

            migrationBuilder.DropColumn(
                name: "WorkspaceColdReset",
                table: "AgentTasks");
        }
    }
}
