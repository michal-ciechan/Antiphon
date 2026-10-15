using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Antiphon.Server.Migrations
{
    /// <inheritdoc />
    public partial class SessionRuntimeActions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SessionRuntimeActions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AgentSessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    TranscriptEntryId = table.Column<Guid>(type: "uuid", nullable: true),
                    AcceptedGeneration = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Sequence = table.Column<long>(type: "bigint", nullable: false),
                    ProducerSequence = table.Column<long>(type: "bigint", nullable: false),
                    TriggerKind = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    TurnBoundary = table.Column<bool>(type: "boolean", nullable: false),
                    Phase = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DueAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    FailureReason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    UiProjectionVersion = table.Column<int>(type: "integer", nullable: false),
                    UiProjectionDelta = table.Column<byte[]>(type: "bytea", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SessionRuntimeActions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SessionRuntimeActions_AgentSessions_AgentSessionId",
                        column: x => x.AgentSessionId,
                        principalTable: "AgentSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SessionRuntimeActions_TranscriptEntries_TranscriptEntryId",
                        column: x => x.TranscriptEntryId,
                        principalTable: "TranscriptEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SessionRuntimeActions_AgentSessionId_Sequence_TriggerKind",
                table: "SessionRuntimeActions",
                columns: new[] { "AgentSessionId", "Sequence", "TriggerKind" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SessionRuntimeActions_DueAt_AgentSessionId",
                table: "SessionRuntimeActions",
                columns: new[] { "DueAt", "AgentSessionId" });

            migrationBuilder.CreateIndex(
                name: "IX_SessionRuntimeActions_TranscriptEntryId",
                table: "SessionRuntimeActions",
                column: "TranscriptEntryId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SessionRuntimeActions");
        }
    }
}
