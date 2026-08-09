using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Antiphon.Server.Migrations
{
    /// <inheritdoc />
    public partial class SessionDelegationTokenAndAgentModelLevelFix : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DelegationTokenHash",
                table: "AgentSessions",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "ModelLevel",
                table: "Agents",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldDefaultValue: 1);

            migrationBuilder.CreateIndex(
                name: "IX_AgentSessions_DelegationTokenHash",
                table: "AgentSessions",
                column: "DelegationTokenHash");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AgentSessions_DelegationTokenHash",
                table: "AgentSessions");

            migrationBuilder.DropColumn(
                name: "DelegationTokenHash",
                table: "AgentSessions");

            migrationBuilder.AlterColumn<int>(
                name: "ModelLevel",
                table: "Agents",
                type: "integer",
                nullable: false,
                defaultValue: 1,
                oldClrType: typeof(int),
                oldType: "integer");
        }
    }
}
