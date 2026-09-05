using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Antiphon.Server.Migrations
{
    /// <summary>
    /// CARD-0350 S2: nullable Card.Alias and CardRevision.Alias. Hand-written
    /// (running daemons lock bin/); the [DbContext]/[Migration] attributes normally
    /// generated into the Designer live here. Snapshot is updated to match.
    /// No backfill: existing cards have no alias.
    /// </summary>
    [DbContext(typeof(AppDbContext))]
    [Migration("20260905010000_AddCardAlias")]
    public partial class AddCardAlias : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Alias",
                table: "Cards",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Alias",
                table: "CardRevisions",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Alias",
                table: "Cards");

            migrationBuilder.DropColumn(
                name: "Alias",
                table: "CardRevisions");
        }
    }
}
