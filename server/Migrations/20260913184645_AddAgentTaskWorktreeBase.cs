using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Antiphon.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddAgentTaskWorktreeBase : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "RequestedWorktreeBaseTaskId",
                table: "AgentTasks",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WorktreeBaseBranch",
                table: "AgentTasks",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "WorktreeBaseMode",
                table: "AgentTasks",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "WorktreeBasePreviewJson",
                table: "AgentTasks",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "WorktreeBaseTaskId",
                table: "AgentTasks",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RequestedWorktreeBaseTaskId",
                table: "AgentTasks");

            migrationBuilder.DropColumn(
                name: "WorktreeBaseBranch",
                table: "AgentTasks");

            migrationBuilder.DropColumn(
                name: "WorktreeBaseMode",
                table: "AgentTasks");

            migrationBuilder.DropColumn(
                name: "WorktreeBasePreviewJson",
                table: "AgentTasks");

            migrationBuilder.DropColumn(
                name: "WorktreeBaseTaskId",
                table: "AgentTasks");
        }
    }
}
