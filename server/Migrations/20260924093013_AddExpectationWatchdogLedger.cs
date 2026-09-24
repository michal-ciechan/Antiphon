using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Antiphon.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddExpectationWatchdogLedger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ExpectationEpisodes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DirectiveId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    SubjectKey = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    FirstObservedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastObservedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ResolvedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Evidence = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    ConfigDigest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ConcurrencyToken = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExpectationEpisodes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ExpectationNudges",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DirectiveId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Ordinal = table.Column<int>(type: "integer", nullable: false),
                    EpisodeIdsJson = table.Column<string>(type: "text", nullable: false),
                    EvidenceSnapshot = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    Body = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: false),
                    BodyDigest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    DestinationSessionId = table.Column<Guid>(type: "uuid", nullable: true),
                    DestinationGeneration = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    BaselineSequence = table.Column<long>(type: "bigint", nullable: true),
                    AttemptState = table.Column<int>(type: "integer", nullable: false),
                    ReceiptAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    AnsweredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    OperatorOutboxState = table.Column<int>(type: "integer", nullable: false),
                    OperatorChannelId = table.Column<Guid>(type: "uuid", nullable: true),
                    OperatorPublishedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    OperatorAttemptCount = table.Column<int>(type: "integer", nullable: false),
                    OperatorNextAttemptAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    OperatorLastError = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    AuditCommentId = table.Column<Guid>(type: "uuid", nullable: false),
                    CheckEventIdsJson = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ConcurrencyToken = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExpectationNudges", x => x.Id);
                    table.CheckConstraint("CK_ExpectationNudges_Body", "length(btrim(\"Body\")) > 0 AND length(\"BodyDigest\") = 64");
                    table.CheckConstraint("CK_ExpectationNudges_Ordinal", "\"Ordinal\" >= 1");
                    table.ForeignKey(
                        name: "FK_ExpectationNudges_CardComments_AuditCommentId",
                        column: x => x.AuditCommentId,
                        principalTable: "CardComments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ExpectationWatchStates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DirectiveId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ConfigDigest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    LastSuccessfulScanAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    NextNudgeAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastObservationError = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ConcurrencyToken = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExpectationWatchStates", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ExpectationEpisodes_Directive_Resolved",
                table: "ExpectationEpisodes",
                columns: new[] { "DirectiveId", "ResolvedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ExpectationEpisodes_Open",
                table: "ExpectationEpisodes",
                columns: new[] { "DirectiveId", "Kind", "SubjectKey" },
                unique: true,
                filter: "\"ResolvedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ExpectationNudges_AuditCommentId",
                table: "ExpectationNudges",
                column: "AuditCommentId");

            migrationBuilder.CreateIndex(
                name: "IX_ExpectationNudges_Directive_Ordinal",
                table: "ExpectationNudges",
                columns: new[] { "DirectiveId", "Ordinal" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExpectationNudges_PendingDelivery",
                table: "ExpectationNudges",
                columns: new[] { "OperatorOutboxState", "OperatorNextAttemptAt" },
                filter: "\"OperatorOutboxState\" = 1");

            migrationBuilder.CreateIndex(
                name: "IX_ExpectationWatchStates_DirectiveId",
                table: "ExpectationWatchStates",
                column: "DirectiveId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ExpectationEpisodes");

            migrationBuilder.DropTable(
                name: "ExpectationNudges");

            migrationBuilder.DropTable(
                name: "ExpectationWatchStates");
        }
    }
}
