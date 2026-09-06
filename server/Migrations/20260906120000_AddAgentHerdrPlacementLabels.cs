using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Antiphon.Server.Migrations
{
    /// <summary>
    /// CARD-0384: optional herdr workspace/tab label pins on Agents. Nullable text, 256-char
    /// application limit; no pane/tab/workspace ID columns and no data backfill.
    /// </summary>
    [DbContext(typeof(AppDbContext))]
    [Migration("20260906120000_AddAgentHerdrPlacementLabels")]
    public partial class AddAgentHerdrPlacementLabels : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "HerdrTabLabel",
                table: "Agents",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HerdrWorkspaceLabel",
                table: "Agents",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "HerdrTabLabel",
                table: "Agents");

            migrationBuilder.DropColumn(
                name: "HerdrWorkspaceLabel",
                table: "Agents");
        }
    }
}
