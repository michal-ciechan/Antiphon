using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Antiphon.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddAgentReplyStyle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 0 is AgentReplyStyle.Normal, which composes to NOTHING at launch: every agent that
            // existed before this column keeps byte-identical launch arguments (CARD-0060).
            migrationBuilder.AddColumn<int>(
                name: "ReplyStyle",
                table: "Agents",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ReplyStyle",
                table: "Agents");
        }
    }
}
