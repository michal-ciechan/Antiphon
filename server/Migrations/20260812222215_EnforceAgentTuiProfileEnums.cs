using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Antiphon.Server.Migrations
{
    /// <inheritdoc />
    public partial class EnforceAgentTuiProfileEnums : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Guidance",
                table: "AgentTuiProfileRevisions",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AgentTuiProfiles_Kind_Valid",
                table: "AgentTuiProfiles",
                sql: "\"Kind\" IN (0, 1, 2, 3)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AgentTuiProfileRevisions_AuthenticationMode_Valid",
                table: "AgentTuiProfileRevisions",
                sql: "\"AuthenticationMode\" IN (0, 1)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_AgentTuiProfiles_Kind_Valid",
                table: "AgentTuiProfiles");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AgentTuiProfileRevisions_AuthenticationMode_Valid",
                table: "AgentTuiProfileRevisions");

            migrationBuilder.AlterColumn<string>(
                name: "Guidance",
                table: "AgentTuiProfileRevisions",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(4000)",
                oldMaxLength: 4000);
        }
    }
}
