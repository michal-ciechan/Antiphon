using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Antiphon.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddApiErrorRecovery : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ApiErrorRecoveries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AgentSessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    StubSequence = table.Column<long>(type: "bigint", nullable: false),
                    StubUuid = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Classification = table.Column<int>(type: "integer", nullable: false),
                    ApiErrorClass = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    ApiErrorStatus = table.Column<int>(type: "integer", nullable: true),
                    DetectedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    NextAttemptAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ResolvedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ResolvedReason = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    LastEnqueuedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApiErrorRecoveries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ApiErrorRecoveries_AgentSessions_AgentSessionId",
                        column: x => x.AgentSessionId,
                        principalTable: "AgentSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TranscriptEntries_IsApiError",
                table: "TranscriptEntries",
                column: "IsApiError",
                filter: "\"IsApiError\" = true");

            migrationBuilder.CreateIndex(
                name: "IX_ApiErrorRecoveries_AgentSessionId_StubSequence",
                table: "ApiErrorRecoveries",
                columns: new[] { "AgentSessionId", "StubSequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ApiErrorRecoveries_NextAttemptAt",
                table: "ApiErrorRecoveries",
                column: "NextAttemptAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ApiErrorRecoveries");

            migrationBuilder.DropIndex(
                name: "IX_TranscriptEntries_IsApiError",
                table: "TranscriptEntries");
        }
    }
}
