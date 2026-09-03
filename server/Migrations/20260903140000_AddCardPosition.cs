using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Antiphon.Server.Migrations
{
    /// <summary>
    /// CARD-0098 S1: nullable Card.Position and CardRevision.Position. Hand-written
    /// (running daemons lock bin/); the [DbContext]/[Migration] attributes normally
    /// generated into the Designer live here. Snapshot is updated to match.
    /// No backfill: every card is unplaced on day one and today's order is unchanged.
    /// </summary>
    [DbContext(typeof(AppDbContext))]
    [Migration("20260903140000_AddCardPosition")]
    public partial class AddCardPosition : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Position",
                table: "Cards",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Position",
                table: "CardRevisions",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Position",
                table: "Cards");

            migrationBuilder.DropColumn(
                name: "Position",
                table: "CardRevisions");
        }
    }
}
