using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Antiphon.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddRunnerBoundRouting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RemoteWorktreePath",
                table: "AgentTasks",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RemoteWorktreeResidue",
                table: "AgentTasks",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RunnerId",
                table: "AgentTasks",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RunnerId",
                table: "Agents",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_AgentTasks_RunnerId",
                table: "AgentTasks",
                column: "RunnerId");

            migrationBuilder.CreateIndex(
                name: "IX_Agents_RunnerId",
                table: "Agents",
                column: "RunnerId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AgentTasks_RunnerId",
                table: "AgentTasks");

            migrationBuilder.DropIndex(
                name: "IX_Agents_RunnerId",
                table: "Agents");

            migrationBuilder.DropColumn(
                name: "RemoteWorktreePath",
                table: "AgentTasks");

            migrationBuilder.DropColumn(
                name: "RemoteWorktreeResidue",
                table: "AgentTasks");

            migrationBuilder.DropColumn(
                name: "RunnerId",
                table: "AgentTasks");

            migrationBuilder.DropColumn(
                name: "RunnerId",
                table: "Agents");
        }
    }
}
