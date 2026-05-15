using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Antiphon.Server.Migrations
{
    /// <inheritdoc />
    public partial class KanbanInitial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Boards",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    TrackerKind = table.Column<int>(type: "integer", nullable: false),
                    MaxConcurrentSessions = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Boards", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Boards_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "BoardColumns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BoardId = table.Column<Guid>(type: "uuid", nullable: false),
                    StateKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ColumnOrder = table.Column<int>(type: "integer", nullable: false),
                    CardStatus = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    IsTerminal = table.Column<bool>(type: "boolean", nullable: false),
                    MaxConcurrentSessions = table.Column<int>(type: "integer", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BoardColumns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BoardColumns_Boards_BoardId",
                        column: x => x.BoardId,
                        principalTable: "Boards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "BoardWorkflowDefinitions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BoardId = table.Column<Guid>(type: "uuid", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Content = table.Column<string>(type: "text", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BoardWorkflowDefinitions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BoardWorkflowDefinitions_Boards_BoardId",
                        column: x => x.BoardId,
                        principalTable: "Boards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AgentSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CardId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorktreeId = table.Column<Guid>(type: "uuid", nullable: true),
                    DefinitionName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    AgentKind = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Cwd = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    Cols = table.Column<int>(type: "integer", nullable: false),
                    Rows = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastSeenAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EndedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ExitCode = table.Column<int>(type: "integer", nullable: true),
                    FailureReason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AgentSessions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Cards",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BoardId = table.Column<Guid>(type: "uuid", nullable: false),
                    BoardColumnId = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerSessionId = table.Column<Guid>(type: "uuid", nullable: true),
                    CurrentWorktreeId = table.Column<Guid>(type: "uuid", nullable: true),
                    Identifier = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Description = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    Priority = table.Column<int>(type: "integer", nullable: false),
                    LabelsJson = table.Column<string>(type: "jsonb", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ConcurrencyToken = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    TerminalReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Cards", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Cards_AgentSessions_OwnerSessionId",
                        column: x => x.OwnerSessionId,
                        principalTable: "AgentSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Cards_BoardColumns_BoardColumnId",
                        column: x => x.BoardColumnId,
                        principalTable: "BoardColumns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Cards_Boards_BoardId",
                        column: x => x.BoardId,
                        principalTable: "Boards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ExternalIssueRefs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CardId = table.Column<Guid>(type: "uuid", nullable: false),
                    TrackerKind = table.Column<int>(type: "integer", nullable: false),
                    ExternalId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ExternalKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Url = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    RawPayloadJson = table.Column<string>(type: "jsonb", nullable: false),
                    LastSyncedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExternalIssueRefs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExternalIssueRefs_Cards_CardId",
                        column: x => x.CardId,
                        principalTable: "Cards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RetrySchedules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CardId = table.Column<Guid>(type: "uuid", nullable: false),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    MaxAttempts = table.Column<int>(type: "integer", nullable: false),
                    NextRetryAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastAttemptAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastError = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RetrySchedules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RetrySchedules_Cards_CardId",
                        column: x => x.CardId,
                        principalTable: "Cards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Worktrees",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CardId = table.Column<Guid>(type: "uuid", nullable: false),
                    RepoPath = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    Path = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    Branch = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    BaseRef = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastTouchedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RemovedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Worktrees", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Worktrees_Cards_CardId",
                        column: x => x.CardId,
                        principalTable: "Cards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RunAttempts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CardId = table.Column<Guid>(type: "uuid", nullable: false),
                    AgentSessionId = table.Column<Guid>(type: "uuid", nullable: true),
                    WorktreeId = table.Column<Guid>(type: "uuid", nullable: true),
                    BoardWorkflowDefinitionId = table.Column<Guid>(type: "uuid", nullable: true),
                    AttemptNumber = table.Column<int>(type: "integer", nullable: false),
                    Phase = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastEventAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    PhaseStartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    PhaseDurationsJson = table.Column<string>(type: "jsonb", nullable: false),
                    Prompt = table.Column<string>(type: "text", nullable: false),
                    ErrorDetails = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    ExitCode = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RunAttempts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RunAttempts_AgentSessions_AgentSessionId",
                        column: x => x.AgentSessionId,
                        principalTable: "AgentSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_RunAttempts_BoardWorkflowDefinitions_BoardWorkflowDefinitio~",
                        column: x => x.BoardWorkflowDefinitionId,
                        principalTable: "BoardWorkflowDefinitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_RunAttempts_Cards_CardId",
                        column: x => x.CardId,
                        principalTable: "Cards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RunAttempts_Worktrees_WorktreeId",
                        column: x => x.WorktreeId,
                        principalTable: "Worktrees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "TokenUsages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RunAttemptId = table.Column<Guid>(type: "uuid", nullable: false),
                    TokensIn = table.Column<long>(type: "bigint", nullable: false),
                    TokensOut = table.Column<long>(type: "bigint", nullable: false),
                    CostUsd = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    ModelName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TokenUsages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TokenUsages_RunAttempts_RunAttemptId",
                        column: x => x.RunAttemptId,
                        principalTable: "RunAttempts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AgentSessions_CardId",
                table: "AgentSessions",
                column: "CardId");

            migrationBuilder.CreateIndex(
                name: "IX_AgentSessions_CardId_Status",
                table: "AgentSessions",
                columns: new[] { "CardId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_AgentSessions_WorktreeId",
                table: "AgentSessions",
                column: "WorktreeId");

            migrationBuilder.CreateIndex(
                name: "IX_BoardColumns_BoardId_ColumnOrder",
                table: "BoardColumns",
                columns: new[] { "BoardId", "ColumnOrder" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BoardColumns_BoardId_StateKey",
                table: "BoardColumns",
                columns: new[] { "BoardId", "StateKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Boards_ProjectId",
                table: "Boards",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_Boards_ProjectId_Name",
                table: "Boards",
                columns: new[] { "ProjectId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BoardWorkflowDefinitions_BoardId_IsActive",
                table: "BoardWorkflowDefinitions",
                columns: new[] { "BoardId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_BoardWorkflowDefinitions_BoardId_Version",
                table: "BoardWorkflowDefinitions",
                columns: new[] { "BoardId", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Cards_BoardColumnId",
                table: "Cards",
                column: "BoardColumnId");

            migrationBuilder.CreateIndex(
                name: "IX_Cards_BoardId",
                table: "Cards",
                column: "BoardId");

            migrationBuilder.CreateIndex(
                name: "IX_Cards_BoardId_Identifier",
                table: "Cards",
                columns: new[] { "BoardId", "Identifier" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Cards_BoardId_Status",
                table: "Cards",
                columns: new[] { "BoardId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Cards_CurrentWorktreeId",
                table: "Cards",
                column: "CurrentWorktreeId");

            migrationBuilder.CreateIndex(
                name: "IX_Cards_OwnerSessionId",
                table: "Cards",
                column: "OwnerSessionId");

            migrationBuilder.CreateIndex(
                name: "IX_ExternalIssueRefs_CardId",
                table: "ExternalIssueRefs",
                column: "CardId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExternalIssueRefs_TrackerKind_ExternalId",
                table: "ExternalIssueRefs",
                columns: new[] { "TrackerKind", "ExternalId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RetrySchedules_CardId",
                table: "RetrySchedules",
                column: "CardId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RetrySchedules_NextRetryAt",
                table: "RetrySchedules",
                column: "NextRetryAt");

            migrationBuilder.CreateIndex(
                name: "IX_RunAttempts_AgentSessionId",
                table: "RunAttempts",
                column: "AgentSessionId");

            migrationBuilder.CreateIndex(
                name: "IX_RunAttempts_BoardWorkflowDefinitionId",
                table: "RunAttempts",
                column: "BoardWorkflowDefinitionId");

            migrationBuilder.CreateIndex(
                name: "IX_RunAttempts_CardId",
                table: "RunAttempts",
                column: "CardId");

            migrationBuilder.CreateIndex(
                name: "IX_RunAttempts_CardId_AttemptNumber",
                table: "RunAttempts",
                columns: new[] { "CardId", "AttemptNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RunAttempts_WorktreeId",
                table: "RunAttempts",
                column: "WorktreeId");

            migrationBuilder.CreateIndex(
                name: "IX_TokenUsages_RunAttemptId",
                table: "TokenUsages",
                column: "RunAttemptId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Worktrees_Branch",
                table: "Worktrees",
                column: "Branch");

            migrationBuilder.CreateIndex(
                name: "IX_Worktrees_CardId",
                table: "Worktrees",
                column: "CardId");

            migrationBuilder.CreateIndex(
                name: "IX_Worktrees_Path",
                table: "Worktrees",
                column: "Path",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Worktrees_Status_LastTouchedAt",
                table: "Worktrees",
                columns: new[] { "Status", "LastTouchedAt" });

            migrationBuilder.AddForeignKey(
                name: "FK_AgentSessions_Cards_CardId",
                table: "AgentSessions",
                column: "CardId",
                principalTable: "Cards",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_AgentSessions_Worktrees_WorktreeId",
                table: "AgentSessions",
                column: "WorktreeId",
                principalTable: "Worktrees",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_Cards_Worktrees_CurrentWorktreeId",
                table: "Cards",
                column: "CurrentWorktreeId",
                principalTable: "Worktrees",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AgentSessions_Cards_CardId",
                table: "AgentSessions");

            migrationBuilder.DropForeignKey(
                name: "FK_Worktrees_Cards_CardId",
                table: "Worktrees");

            migrationBuilder.DropTable(
                name: "ExternalIssueRefs");

            migrationBuilder.DropTable(
                name: "RetrySchedules");

            migrationBuilder.DropTable(
                name: "TokenUsages");

            migrationBuilder.DropTable(
                name: "RunAttempts");

            migrationBuilder.DropTable(
                name: "BoardWorkflowDefinitions");

            migrationBuilder.DropTable(
                name: "Cards");

            migrationBuilder.DropTable(
                name: "AgentSessions");

            migrationBuilder.DropTable(
                name: "BoardColumns");

            migrationBuilder.DropTable(
                name: "Worktrees");

            migrationBuilder.DropTable(
                name: "Boards");
        }
    }
}
