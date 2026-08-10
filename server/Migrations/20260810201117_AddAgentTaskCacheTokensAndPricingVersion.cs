using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Antiphon.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddAgentTaskCacheTokensAndPricingVersion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "CacheCreationTokens",
                table: "AgentTasks",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "CacheReadTokens",
                table: "AgentTasks",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<int>(
                name: "CostPricingVersion",
                table: "AgentTasks",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CacheCreationTokens",
                table: "AgentTasks");

            migrationBuilder.DropColumn(
                name: "CacheReadTokens",
                table: "AgentTasks");

            migrationBuilder.DropColumn(
                name: "CostPricingVersion",
                table: "AgentTasks");
        }
    }
}
