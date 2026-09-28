using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Antiphon.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddCardWorktreeBaseContinuity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "RequestedWorktreeBaseMode",
                table: "AgentTasks",
                type: "integer",
                nullable: false,
                defaultValue: 0);

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

            migrationBuilder.AddColumn<string>(
                name: "WorktreeBasePreviewJson",
                table: "AgentTasks",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RequestedWorktreeBaseMode",
                table: "AgentTasks");

            migrationBuilder.DropColumn(
                name: "RequestedWorktreeBaseTaskId",
                table: "AgentTasks");

            migrationBuilder.DropColumn(
                name: "WorktreeBaseBranch",
                table: "AgentTasks");

            migrationBuilder.DropColumn(
                name: "WorktreeBasePreviewJson",
                table: "AgentTasks");
        }
    }
}
