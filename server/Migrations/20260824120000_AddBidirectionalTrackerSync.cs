using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Antiphon.Server.Migrations
{
    /// <summary>
    /// CARD-0166 S1: all bidirectional-sync schema in one migration — Board activation/cursor
    /// columns, ExternalIssueRef origin+cursors, and the new CardComments discussion table.
    /// Hand-written (running daemons lock bin/); snapshot is updated to match.
    /// </summary>
    [DbContext(typeof(AppDbContext))]
    [Migration("20260824120000_AddBidirectionalTrackerSync")]
    public partial class AddBidirectionalTrackerSync : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "TrackerActivatedAt",
                table: "Boards",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "TrackerCommentsPulledAt",
                table: "Boards",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Origin",
                table: "ExternalIssueRefs",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "LastKnownExternalState",
                table: "ExternalIssueRefs",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LastRevisionSynced",
                table: "ExternalIssueRefs",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastOutboundSyncedAt",
                table: "ExternalIssueRefs",
                type: "timestamp with time zone",
                nullable: true);

            // A legacy linked card must not comment-echo its entire edit history on first sync.
            // Vacuously none in this deployment; correct on any deployment that already has refs.
            migrationBuilder.Sql(
                """
                UPDATE "ExternalIssueRefs" AS r
                SET "LastRevisionSynced" = c."RevisionCount"
                FROM "Cards" AS c
                WHERE c."Id" = r."CardId";
                """);

            migrationBuilder.CreateTable(
                name: "CardComments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CardId = table.Column<Guid>(type: "uuid", nullable: false),
                    Body = table.Column<string>(type: "text", nullable: false),
                    Author = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Origin = table.Column<int>(type: "integer", nullable: false),
                    ExternalCommentId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ExternalUrl = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SyncedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CardComments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CardComments_Cards_CardId",
                        column: x => x.CardId,
                        principalTable: "Cards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CardComments_CardId_CreatedAt",
                table: "CardComments",
                columns: new[] { "CardId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_CardComments_ExternalCommentId",
                table: "CardComments",
                column: "ExternalCommentId",
                unique: true,
                filter: "\"ExternalCommentId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CardComments");

            migrationBuilder.DropColumn(
                name: "LastOutboundSyncedAt",
                table: "ExternalIssueRefs");

            migrationBuilder.DropColumn(
                name: "LastRevisionSynced",
                table: "ExternalIssueRefs");

            migrationBuilder.DropColumn(
                name: "LastKnownExternalState",
                table: "ExternalIssueRefs");

            migrationBuilder.DropColumn(
                name: "Origin",
                table: "ExternalIssueRefs");

            migrationBuilder.DropColumn(
                name: "TrackerCommentsPulledAt",
                table: "Boards");

            migrationBuilder.DropColumn(
                name: "TrackerActivatedAt",
                table: "Boards");
        }
    }
}
