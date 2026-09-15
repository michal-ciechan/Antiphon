using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Antiphon.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddCommitOnSettlePolicy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "CommitOnSettle",
                table: "Projects",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CommitBaselineSha",
                table: "AgentTasks",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CommitOnSettle",
                table: "AgentTasks",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CommitOnSettle",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "CommitBaselineSha",
                table: "AgentTasks");

            migrationBuilder.DropColumn(
                name: "CommitOnSettle",
                table: "AgentTasks");
        }
    }
}
