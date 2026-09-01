using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Antiphon.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddAgentTaskCardRoleCreatedAtIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_AgentTasks_CardId_Role_CreatedAt",
                table: "AgentTasks",
                columns: new[] { "CardId", "Role", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AgentTasks_CardId_Role_CreatedAt",
                table: "AgentTasks");
        }
    }
}
