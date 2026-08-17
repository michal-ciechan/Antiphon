using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Antiphon.Server.Migrations
{
    /// <inheritdoc />
    public partial class AllowGrokAgentKind : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_AgentTuiProfiles_Kind_Valid",
                table: "AgentTuiProfiles");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AgentTuiProfiles_Kind_Valid",
                table: "AgentTuiProfiles",
                sql: "\"Kind\" IN (0, 1, 2, 3, 4)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_AgentTuiProfiles_Kind_Valid",
                table: "AgentTuiProfiles");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AgentTuiProfiles_Kind_Valid",
                table: "AgentTuiProfiles",
                sql: "\"Kind\" IN (0, 1, 2, 3)");
        }
    }
}
