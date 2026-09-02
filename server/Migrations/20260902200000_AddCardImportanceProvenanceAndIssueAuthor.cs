using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Antiphon.Server.Migrations
{
    /// <summary>
    /// CARD-0327: Card.ImportanceProvenance plus ExternalIssueRef.Author/AuthorIsOperator.
    /// Hand-written (running daemons lock bin/); the [DbContext]/[Migration] attributes
    /// normally generated into the Designer live here. Snapshot is updated to match.
    /// No provenance backfill: every import-origin card's current importance was written
    /// by the sync, so Auto is the truth. Author is backfilled from the stored GitHub payload.
    /// </summary>
    [DbContext(typeof(AppDbContext))]
    [Migration("20260902200000_AddCardImportanceProvenanceAndIssueAuthor")]
    public partial class AddCardImportanceProvenanceAndIssueAuthor : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ImportanceProvenance",
                table: "Cards",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "Author",
                table: "ExternalIssueRefs",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "AuthorIsOperator",
                table: "ExternalIssueRefs",
                type: "boolean",
                nullable: true);

            // TrackerKind.GitHubIssues = 2. AuthorIsOperator is left null; the first tick judges it.
            migrationBuilder.Sql(
                """
                UPDATE "ExternalIssueRefs"
                SET "Author" = "RawPayloadJson"->'user'->>'login'
                WHERE "TrackerKind" = 2 AND "Author" IS NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ImportanceProvenance",
                table: "Cards");

            migrationBuilder.DropColumn(
                name: "Author",
                table: "ExternalIssueRefs");

            migrationBuilder.DropColumn(
                name: "AuthorIsOperator",
                table: "ExternalIssueRefs");
        }
    }
}
