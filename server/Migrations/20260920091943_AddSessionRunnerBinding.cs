using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Antiphon.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddSessionRunnerBinding : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RunnerCwd",
                table: "AgentSessions",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RunnerId",
                table: "AgentSessions",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RunnerStoreId",
                table: "AgentSessions",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_AgentSessions_RunnerId",
                table: "AgentSessions",
                column: "RunnerId");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AgentSessions_RunnerBinding_AllOrNone",
                table: "AgentSessions",
                sql: "(\"RunnerId\" IS NULL AND \"RunnerStoreId\" IS NULL AND \"RunnerCwd\" IS NULL) OR (\"RunnerId\" IS NOT NULL AND \"RunnerStoreId\" IS NOT NULL AND \"RunnerCwd\" IS NOT NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AgentSessions_RunnerId",
                table: "AgentSessions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AgentSessions_RunnerBinding_AllOrNone",
                table: "AgentSessions");

            migrationBuilder.DropColumn(
                name: "RunnerCwd",
                table: "AgentSessions");

            migrationBuilder.DropColumn(
                name: "RunnerId",
                table: "AgentSessions");

            migrationBuilder.DropColumn(
                name: "RunnerStoreId",
                table: "AgentSessions");
        }
    }
}
