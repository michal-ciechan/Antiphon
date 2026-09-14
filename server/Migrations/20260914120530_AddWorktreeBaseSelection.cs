using System;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Antiphon.Server.Migrations
{
    /// <summary>
    /// CARD-0508 S1: recorded worktree-base columns. Null/Unset on every pre-existing row — no backfill.
    /// </summary>
    [DbContext(typeof(AppDbContext))]
    [Migration("20260914120530_AddWorktreeBaseSelection")]
    public partial class AddWorktreeBaseSelection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "WorktreeBaseRef",
                table: "AgentTasks",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WorktreeBaseRequestedRef",
                table: "AgentTasks",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "WorktreeBaseSource",
                table: "AgentTasks",
                type: "integer",
                nullable: false,
                defaultValue: 0);

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
                name: "WorktreeBaseRef",
                table: "AgentTasks");

            migrationBuilder.DropColumn(
                name: "WorktreeBaseRequestedRef",
                table: "AgentTasks");

            migrationBuilder.DropColumn(
                name: "WorktreeBaseSource",
                table: "AgentTasks");

            migrationBuilder.DropColumn(
                name: "WorktreeBaseTaskId",
                table: "AgentTasks");
        }
    }
}
