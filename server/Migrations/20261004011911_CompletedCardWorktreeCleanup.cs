using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Antiphon.Server.Migrations
{
    /// <inheritdoc />
    public partial class CompletedCardWorktreeCleanup : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CardWorktreeCleanups",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CardId = table.Column<Guid>(type: "uuid", nullable: false),
                    DoneRevisionId = table.Column<Guid>(type: "uuid", nullable: false),
                    DoneAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastDiscoveredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CardWorktreeCleanups", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CardWorktreeCleanups_CardRevisions_DoneRevisionId",
                        column: x => x.DoneRevisionId,
                        principalTable: "CardRevisions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CardWorktreeCleanups_Cards_CardId",
                        column: x => x.CardId,
                        principalTable: "Cards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CardWorktreeCleanupTargets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CleanupId = table.Column<Guid>(type: "uuid", nullable: false),
                    TaskId = table.Column<Guid>(type: "uuid", nullable: false),
                    TaskAttempt = table.Column<int>(type: "integer", nullable: false),
                    WorkspaceIdentity = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RepositoryPath = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    WorktreePath = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    SourceFullRef = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    RetirementId = table.Column<Guid>(type: "uuid", nullable: true),
                    LandingOperationId = table.Column<Guid>(type: "uuid", nullable: true),
                    ExclusionReason = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CardWorktreeCleanupTargets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CardWorktreeCleanupTargets_AgentTaskLandings_LandingOperati~",
                        column: x => x.LandingOperationId,
                        principalTable: "AgentTaskLandings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CardWorktreeCleanupTargets_AgentTasks_TaskId",
                        column: x => x.TaskId,
                        principalTable: "AgentTasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CardWorktreeCleanupTargets_CardWorktreeCleanups_CleanupId",
                        column: x => x.CleanupId,
                        principalTable: "CardWorktreeCleanups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CardWorktreeCleanupTargets_TaskWorktreeRetirements_Retireme~",
                        column: x => x.RetirementId,
                        principalTable: "TaskWorktreeRetirements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CardWorktreeCleanupEndpoints",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TargetId = table.Column<Guid>(type: "uuid", nullable: false),
                    EndpointIdentity = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RunnerId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    RunnerStoreId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    RepositoryPath = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    WorktreePath = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    SourceFullRef = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    SourceSha = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CommonDirectory = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    GitDirectory = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    ReportDigest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    OperationId = table.Column<Guid>(type: "uuid", nullable: true),
                    RequestDigest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    IntentAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    State = table.Column<int>(type: "integer", nullable: false),
                    Reason = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    NextAttemptAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Retryable = table.Column<bool>(type: "boolean", nullable: false),
                    DirectoryRemoved = table.Column<bool>(type: "boolean", nullable: true),
                    RegistrationRemoved = table.Column<bool>(type: "boolean", nullable: true),
                    BranchRemoved = table.Column<bool>(type: "boolean", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ConcurrencyToken = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CardWorktreeCleanupEndpoints", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CardWorktreeCleanupEndpoints_CardWorktreeCleanupTargets_Tar~",
                        column: x => x.TargetId,
                        principalTable: "CardWorktreeCleanupTargets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CardWorktreeCleanupEndpoints_OperationId",
                table: "CardWorktreeCleanupEndpoints",
                column: "OperationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CardWorktreeCleanupEndpoints_TargetId_EndpointIdentity",
                table: "CardWorktreeCleanupEndpoints",
                columns: new[] { "TargetId", "EndpointIdentity" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CardWorktreeCleanups_CardId_DoneRevisionId",
                table: "CardWorktreeCleanups",
                columns: new[] { "CardId", "DoneRevisionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CardWorktreeCleanups_DoneRevisionId",
                table: "CardWorktreeCleanups",
                column: "DoneRevisionId");

            migrationBuilder.CreateIndex(
                name: "IX_CardWorktreeCleanupTargets_CleanupId_TaskId_TaskAttempt_Wor~",
                table: "CardWorktreeCleanupTargets",
                columns: new[] { "CleanupId", "TaskId", "TaskAttempt", "WorkspaceIdentity" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CardWorktreeCleanupTargets_LandingOperationId",
                table: "CardWorktreeCleanupTargets",
                column: "LandingOperationId");

            migrationBuilder.CreateIndex(
                name: "IX_CardWorktreeCleanupTargets_RetirementId",
                table: "CardWorktreeCleanupTargets",
                column: "RetirementId");

            migrationBuilder.CreateIndex(
                name: "IX_CardWorktreeCleanupTargets_TaskId",
                table: "CardWorktreeCleanupTargets",
                column: "TaskId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CardWorktreeCleanupEndpoints");

            migrationBuilder.DropTable(
                name: "CardWorktreeCleanupTargets");

            migrationBuilder.DropTable(
                name: "CardWorktreeCleanups");
        }
    }
}
