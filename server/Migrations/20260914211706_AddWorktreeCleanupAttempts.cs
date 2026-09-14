using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Antiphon.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddWorktreeCleanupAttempts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WorktreeCleanupAttempts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SchemaVersion = table.Column<int>(type: "integer", nullable: false),
                    RequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    OperationId = table.Column<Guid>(type: "uuid", nullable: false),
                    TaskId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConcurrencyToken = table.Column<Guid>(type: "uuid", nullable: false),
                    RepositoryPath = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    WorktreePath = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    CommonDirectory = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    GitDirectory = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    SourceFullRef = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    TargetFullRef = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    SourceSha = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TargetSha = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    InitialCommandId = table.Column<Guid>(type: "uuid", nullable: true),
                    InitialIntentAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    InitialCompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RetryCommandId = table.Column<Guid>(type: "uuid", nullable: true),
                    RetryIntentAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RetryCompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FirstGitFailureJson = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    LastGitOutcomeJson = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    CaptureState = table.Column<int>(type: "integer", nullable: false),
                    CaptureAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CaptureJson = table.Column<string>(type: "text", nullable: true),
                    Summary = table.Column<string>(type: "character varying(600)", maxLength: 600, nullable: true),
                    RetryReason = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    FinalizedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    TerminalEventId = table.Column<Guid>(type: "uuid", nullable: true),
                    DirectoryGone = table.Column<bool>(type: "boolean", nullable: false),
                    Unregistered = table.Column<bool>(type: "boolean", nullable: false),
                    BranchDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    Residue = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorktreeCleanupAttempts", x => x.Id);
                    table.CheckConstraint("CK_WorktreeCleanupAttempts_CaptureBytes", "\"CaptureJson\" IS NULL OR octet_length(\"CaptureJson\") <= 32768");
                    table.ForeignKey(
                        name: "FK_WorktreeCleanupAttempts_AgentTaskEvents_TerminalEventId",
                        column: x => x.TerminalEventId,
                        principalTable: "AgentTaskEvents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WorktreeCleanupAttempts_AgentTaskLandRequests_RequestId",
                        column: x => x.RequestId,
                        principalTable: "AgentTaskLandRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WorktreeCleanupAttempts_AgentTaskLandings_OperationId",
                        column: x => x.OperationId,
                        principalTable: "AgentTaskLandings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WorktreeCleanupAttempts_AgentTasks_TaskId",
                        column: x => x.TaskId,
                        principalTable: "AgentTasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WorktreeCleanupAttempts_OperationId_CreatedAt",
                table: "WorktreeCleanupAttempts",
                columns: new[] { "OperationId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_WorktreeCleanupAttempts_RequestId",
                table: "WorktreeCleanupAttempts",
                column: "RequestId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorktreeCleanupAttempts_TaskId",
                table: "WorktreeCleanupAttempts",
                column: "TaskId");

            migrationBuilder.CreateIndex(
                name: "IX_WorktreeCleanupAttempts_TerminalEventId",
                table: "WorktreeCleanupAttempts",
                column: "TerminalEventId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WorktreeCleanupAttempts");
        }
    }
}
