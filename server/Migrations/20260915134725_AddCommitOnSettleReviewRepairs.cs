using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Antiphon.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddCommitOnSettleReviewRepairs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CommitBaselineUpstreamSha",
                table: "AgentTasks",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CompletionNoteBody",
                table: "AgentTasks",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CompletionNoteHeader",
                table: "AgentTasks",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CommitBaselineUpstreamSha",
                table: "AgentTasks");

            migrationBuilder.DropColumn(
                name: "CompletionNoteBody",
                table: "AgentTasks");

            migrationBuilder.DropColumn(
                name: "CompletionNoteHeader",
                table: "AgentTasks");
        }
    }
}
