using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Antiphon.Server.Migrations
{
    /// <inheritdoc />
    public partial class LegacyCheckNotePublication : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LegacyCheckNotePublications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CheckedTaskId = table.Column<Guid>(type: "uuid", nullable: false),
                    CheckedTaskAttempt = table.Column<int>(type: "integer", nullable: false),
                    CheckedTaskDispatchedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CheckNumber = table.Column<int>(type: "integer", nullable: false),
                    RecoveryId = table.Column<Guid>(type: "uuid", nullable: false),
                    PhysicalAgentId = table.Column<Guid>(type: "uuid", nullable: false),
                    InterpreterSessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    InterpreterAcceptedStartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ParentSessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    CapturedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    FactsSnapshotJson = table.Column<string>(type: "text", nullable: false),
                    RenderContextJson = table.Column<string>(type: "text", nullable: false),
                    InterpretationTaskId = table.Column<Guid>(type: "uuid", nullable: true),
                    InterpretationDeadlineAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    InterpretationSnapshotJson = table.Column<string>(type: "text", nullable: true),
                    State = table.Column<int>(type: "integer", nullable: false),
                    SourceEventId = table.Column<Guid>(type: "uuid", nullable: false),
                    NotificationId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProducedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Body = table.Column<string>(type: "text", nullable: true),
                    ContentDigest = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    EventDetail = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    SuppressionReason = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    SuppressedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    NextAttemptAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastError = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    ConcurrencyToken = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LegacyCheckNotePublications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LegacyCheckNotePublications_CheckCompactionRecoveries_Recov~",
                        column: x => x.RecoveryId,
                        principalTable: "CheckCompactionRecoveries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LegacyCheckNotePublications_Check",
                table: "LegacyCheckNotePublications",
                columns: new[] { "CheckedTaskId", "CheckedTaskAttempt", "CheckedTaskDispatchedAt", "CheckNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LegacyCheckNotePublications_Event",
                table: "LegacyCheckNotePublications",
                column: "SourceEventId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LegacyCheckNotePublications_Notification",
                table: "LegacyCheckNotePublications",
                column: "NotificationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LegacyCheckNotePublications_RecoveryId",
                table: "LegacyCheckNotePublications",
                column: "RecoveryId");

            migrationBuilder.CreateIndex(
                name: "IX_LegacyCheckNotePublications_Run",
                table: "LegacyCheckNotePublications",
                column: "InterpretationTaskId",
                unique: true,
                filter: "\"InterpretationTaskId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LegacyCheckNotePublications");
        }
    }
}
