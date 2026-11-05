using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Antiphon.Server.Migrations
{
    /// <inheritdoc />
    public partial class Card0824RefOnlyDesktopCleanup : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DesktopCleanupDisposition",
                table: "CardWorktreeCleanupTargets",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "DesktopWorkspaceKind",
                table: "CardWorktreeCleanupTargets",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "TaskRefWorkspaceBindingId",
                table: "CardWorktreeCleanupTargets",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DesktopCleanupDisposition",
                table: "AgentTaskLandings",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DesktopCleanupDisposition",
                table: "CardWorktreeCleanupTargets");

            migrationBuilder.DropColumn(
                name: "DesktopWorkspaceKind",
                table: "CardWorktreeCleanupTargets");

            migrationBuilder.DropColumn(
                name: "TaskRefWorkspaceBindingId",
                table: "CardWorktreeCleanupTargets");

            migrationBuilder.DropColumn(
                name: "DesktopCleanupDisposition",
                table: "AgentTaskLandings");
        }
    }
}
