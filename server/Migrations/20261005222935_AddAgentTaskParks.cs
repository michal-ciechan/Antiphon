using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Antiphon.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddAgentTaskParks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AgentTaskParks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TaskId = table.Column<Guid>(type: "uuid", nullable: false),
                    Attempt = table.Column<int>(type: "integer", nullable: false),
                    BlockEventId = table.Column<Guid>(type: "uuid", nullable: false),
                    TaskConcurrencyToken = table.Column<Guid>(type: "uuid", nullable: false),
                    AgentId = table.Column<Guid>(type: "uuid", nullable: true),
                    SessionId = table.Column<Guid>(type: "uuid", nullable: true),
                    RunnerId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    RunnerStoreId = table.Column<Guid>(type: "uuid", nullable: true),
                    AcceptedStartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Workspace = table.Column<int>(type: "integer", nullable: false),
                    WorktreeId = table.Column<Guid>(type: "uuid", nullable: true),
                    WorktreePath = table.Column<string>(type: "text", nullable: true),
                    RemoteWorktreePath = table.Column<string>(type: "text", nullable: true),
                    FullRef = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    BaselineSha = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    RepositoryIdentity = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    EndpointFingerprint = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    SourceSha = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    VerifiedRemoteSha = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    PublicationReceiptId = table.Column<Guid>(type: "uuid", nullable: true),
                    PublicationReceiptDigest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    RunnerSeatReleaseId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReportReference = table.Column<string>(type: "text", nullable: true),
                    ReportDigest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    TranscriptSequence = table.Column<long>(type: "bigint", nullable: true),
                    BlockedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    State = table.Column<int>(type: "integer", nullable: false),
                    HeldFromState = table.Column<int>(type: "integer", nullable: true),
                    ReasonCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    PublishedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ReleasePendingAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ParkedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    NextAttemptAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SyncState = table.Column<int>(type: "integer", nullable: false),
                    SyncSourceSha = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    SyncReasonCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    SyncAttempts = table.Column<int>(type: "integer", nullable: false),
                    SyncNextAttemptAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SourceReadyAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ResumeInputEventId = table.Column<Guid>(type: "uuid", nullable: true),
                    ResumeAttempt = table.Column<int>(type: "integer", nullable: true),
                    ResumeSessionId = table.Column<Guid>(type: "uuid", nullable: true),
                    ResumePendingAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ResumedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AgentTaskParks", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AgentTaskParks_AgentId",
                table: "AgentTaskParks",
                column: "AgentId");

            migrationBuilder.CreateIndex(
                name: "IX_AgentTaskParks_PublicationReceiptId",
                table: "AgentTaskParks",
                column: "PublicationReceiptId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AgentTaskParks_RunnerSeatReleaseId",
                table: "AgentTaskParks",
                column: "RunnerSeatReleaseId");

            migrationBuilder.CreateIndex(
                name: "IX_AgentTaskParks_State_NextAttemptAt",
                table: "AgentTaskParks",
                columns: new[] { "State", "NextAttemptAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AgentTaskParks_SyncState_SyncNextAttemptAt",
                table: "AgentTaskParks",
                columns: new[] { "SyncState", "SyncNextAttemptAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AgentTaskParks_TaskId_Attempt_BlockEventId",
                table: "AgentTaskParks",
                columns: new[] { "TaskId", "Attempt", "BlockEventId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AgentTaskParks");
        }
    }
}
