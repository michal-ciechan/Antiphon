using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Antiphon.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddTaskPlatformPlacement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "RequiredPlatform",
                table: "Cards",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "RequiredPlatform",
                table: "CardRevisions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ObservedPlatform",
                table: "AgentTasks",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PlacementReason",
                table: "AgentTasks",
                type: "character varying(400)",
                maxLength: 400,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RequiredPlatform",
                table: "AgentTasks",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "RequirementSource",
                table: "AgentTasks",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<long>(
                name: "RunnerDefaultsRevision",
                table: "AgentTasks",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RunnerSelectionSource",
                table: "AgentTasks",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RequiredPlatform",
                table: "Cards");

            migrationBuilder.DropColumn(
                name: "RequiredPlatform",
                table: "CardRevisions");

            migrationBuilder.DropColumn(
                name: "ObservedPlatform",
                table: "AgentTasks");

            migrationBuilder.DropColumn(
                name: "PlacementReason",
                table: "AgentTasks");

            migrationBuilder.DropColumn(
                name: "RequiredPlatform",
                table: "AgentTasks");

            migrationBuilder.DropColumn(
                name: "RequirementSource",
                table: "AgentTasks");

            migrationBuilder.DropColumn(
                name: "RunnerDefaultsRevision",
                table: "AgentTasks");

            migrationBuilder.DropColumn(
                name: "RunnerSelectionSource",
                table: "AgentTasks");
        }
    }
}
