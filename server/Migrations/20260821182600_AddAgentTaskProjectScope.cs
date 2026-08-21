using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Antiphon.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddAgentTaskProjectScope : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ProjectId",
                table: "AgentTasks",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_AgentTasks_ProjectId",
                table: "AgentTasks",
                column: "ProjectId");

            migrationBuilder.AddForeignKey(
                name: "FK_AgentTasks_Projects_ProjectId",
                table: "AgentTasks",
                column: "ProjectId",
                principalTable: "Projects",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AgentTasks_Projects_ProjectId",
                table: "AgentTasks");

            migrationBuilder.DropIndex(
                name: "IX_AgentTasks_ProjectId",
                table: "AgentTasks");

            migrationBuilder.DropColumn(
                name: "ProjectId",
                table: "AgentTasks");
        }
    }
}
