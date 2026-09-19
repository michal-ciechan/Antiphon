using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Antiphon.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddCard0459WorktreeRetirement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "CleanupOnly",
                table: "AgentTaskLandRequests",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "Origin",
                table: "AgentTaskLandRequests",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "RequiredLandingOperationId",
                table: "AgentTaskLandRequests",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SweepRunId",
                table: "AgentTaskLandRequests",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "TaskWorktreeRetirements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SchemaVersion = table.Column<int>(type: "integer", nullable: false),
                    TaskId = table.Column<Guid>(type: "uuid", nullable: false),
                    TaskAttempt = table.Column<int>(type: "integer", nullable: false),
                    TerminalStatus = table.Column<int>(type: "integer", nullable: false),
                    TaskCompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ReportDigest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    MissingReportReviewed = table.Column<bool>(type: "boolean", nullable: false),
                    ReleasedTaskRevision = table.Column<Guid>(type: "uuid", nullable: false),
                    CallerSessionId = table.Column<Guid>(type: "uuid", nullable: true),
                    CallerTaskId = table.Column<Guid>(type: "uuid", nullable: true),
                    CallerIdentity = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ReleaseReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    ReleasedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    HandoffDispositionJson = table.Column<string>(type: "jsonb", nullable: false, defaultValue: "[]"),
                    RepositoryPath = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    CommonDirectory = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    WorktreePath = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    GitDirectory = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    SourceFullRef = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    SourceSha = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TargetFullRef = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    RemoteName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    DestinationFullRef = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    RemoteFingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ObservedTargetSha = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ResultPreservationPath = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    DeliverablePreservationPath = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    ClaimedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ClaimAttemptId = table.Column<Guid>(type: "uuid", nullable: true),
                    CommandStartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CommandIntentId = table.Column<Guid>(type: "uuid", nullable: true),
                    DirectoryRemovedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RegistrationRemovedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    BranchRemovedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RetirementCompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    State = table.Column<int>(type: "integer", nullable: false),
                    Active = table.Column<bool>(type: "boolean", nullable: false),
                    ConcurrencyToken = table.Column<Guid>(type: "uuid", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastReason = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaskWorktreeRetirements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TaskWorktreeRetirements_AgentTasks_TaskId",
                        column: x => x.TaskId,
                        principalTable: "AgentTasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WorkspaceUseReservations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Generation = table.Column<int>(type: "integer", nullable: false),
                    CanonicalPath = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    SourceFullRef = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    CommonDirectory = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    TaskId = table.Column<Guid>(type: "uuid", nullable: true),
                    SessionId = table.Column<Guid>(type: "uuid", nullable: true),
                    RetirementId = table.Column<Guid>(type: "uuid", nullable: true),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ReleasedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkspaceUseReservations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WorktreeResidueCandidateCursors",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CandidateKey = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    LastEvaluatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    NotBefore = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorktreeResidueCandidateCursors", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WorktreeResidueRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    FinishedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Execute = table.Column<bool>(type: "boolean", nullable: false),
                    Preview = table.Column<bool>(type: "boolean", nullable: false),
                    ActionBudget = table.Column<int>(type: "integer", nullable: false),
                    ActionsAccepted = table.Column<int>(type: "integer", nullable: false),
                    Candidates = table.Column<int>(type: "integer", nullable: false),
                    Held = table.Column<int>(type: "integer", nullable: false),
                    Deferred = table.Column<int>(type: "integer", nullable: false),
                    Queued = table.Column<int>(type: "integer", nullable: false),
                    Refused = table.Column<int>(type: "integer", nullable: false),
                    Partial = table.Column<int>(type: "integer", nullable: false),
                    Removed = table.Column<int>(type: "integer", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: true),
                    BoardId = table.Column<Guid>(type: "uuid", nullable: true),
                    Scope = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorktreeResidueRuns", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TaskWorktreeRetirementAttempts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RetirementId = table.Column<Guid>(type: "uuid", nullable: false),
                    SweepRunId = table.Column<Guid>(type: "uuid", nullable: true),
                    AttemptNumber = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FinishedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    NotBefore = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ReleasedTaskRevision = table.Column<Guid>(type: "uuid", nullable: false),
                    CommandIntentId = table.Column<Guid>(type: "uuid", nullable: true),
                    CommandIntentAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CommandResult = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    DirectoryRemoved = table.Column<bool>(type: "boolean", nullable: true),
                    RegistrationRemoved = table.Column<bool>(type: "boolean", nullable: true),
                    BranchRemoved = table.Column<bool>(type: "boolean", nullable: true),
                    Residue = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaskWorktreeRetirementAttempts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TaskWorktreeRetirementAttempts_TaskWorktreeRetirements_Reti~",
                        column: x => x.RetirementId,
                        principalTable: "TaskWorktreeRetirements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TaskWorktreeRetirementAttempts_WorktreeResidueRuns_SweepRun~",
                        column: x => x.SweepRunId,
                        principalTable: "WorktreeResidueRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "WorktreeResidueRunCandidates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RunId = table.Column<Guid>(type: "uuid", nullable: false),
                    Lane = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Outcome = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    ReasonCode = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    TaskId = table.Column<Guid>(type: "uuid", nullable: true),
                    RetirementId = table.Column<Guid>(type: "uuid", nullable: true),
                    LandingOperationId = table.Column<Guid>(type: "uuid", nullable: true),
                    LandRequestId = table.Column<Guid>(type: "uuid", nullable: true),
                    Path = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Branch = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    DirectoryRemoved = table.Column<bool>(type: "boolean", nullable: true),
                    RegistrationRemoved = table.Column<bool>(type: "boolean", nullable: true),
                    BranchRemoved = table.Column<bool>(type: "boolean", nullable: true),
                    EvaluatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorktreeResidueRunCandidates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorktreeResidueRunCandidates_WorktreeResidueRuns_RunId",
                        column: x => x.RunId,
                        principalTable: "WorktreeResidueRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TaskWorktreeRetirementAttempts_Retirement_Attempt",
                table: "TaskWorktreeRetirementAttempts",
                columns: new[] { "RetirementId", "AttemptNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TaskWorktreeRetirementAttempts_SweepRunId",
                table: "TaskWorktreeRetirementAttempts",
                column: "SweepRunId");

            migrationBuilder.CreateIndex(
                name: "IX_TaskWorktreeRetirements_TaskId_Attempt_Active",
                table: "TaskWorktreeRetirements",
                columns: new[] { "TaskId", "TaskAttempt" },
                unique: true,
                filter: "\"Active\" = TRUE");

            migrationBuilder.CreateIndex(
                name: "IX_WorkspaceUseReservations_Path_Ref_Kind_Active",
                table: "WorkspaceUseReservations",
                columns: new[] { "CanonicalPath", "SourceFullRef", "Kind" },
                filter: "\"Active\" = TRUE");

            migrationBuilder.CreateIndex(
                name: "IX_WorktreeResidueCandidateCursors_CandidateKey",
                table: "WorktreeResidueCandidateCursors",
                column: "CandidateKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorktreeResidueRunCandidates_RunId",
                table: "WorktreeResidueRunCandidates",
                column: "RunId");

            migrationBuilder.CreateIndex(
                name: "IX_WorktreeResidueRuns_StartedAt",
                table: "WorktreeResidueRuns",
                column: "StartedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TaskWorktreeRetirementAttempts");

            migrationBuilder.DropTable(
                name: "WorkspaceUseReservations");

            migrationBuilder.DropTable(
                name: "WorktreeResidueCandidateCursors");

            migrationBuilder.DropTable(
                name: "WorktreeResidueRunCandidates");

            migrationBuilder.DropTable(
                name: "TaskWorktreeRetirements");

            migrationBuilder.DropTable(
                name: "WorktreeResidueRuns");

            migrationBuilder.DropColumn(
                name: "CleanupOnly",
                table: "AgentTaskLandRequests");

            migrationBuilder.DropColumn(
                name: "Origin",
                table: "AgentTaskLandRequests");

            migrationBuilder.DropColumn(
                name: "RequiredLandingOperationId",
                table: "AgentTaskLandRequests");

            migrationBuilder.DropColumn(
                name: "SweepRunId",
                table: "AgentTaskLandRequests");
        }
    }
}
