using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Antiphon.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddAgentTaskLandingEvidence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ActiveLandingId",
                table: "AgentTasks",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AgentTaskLandings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TaskId = table.Column<Guid>(type: "uuid", nullable: false),
                    SchemaVersion = table.Column<int>(type: "integer", nullable: false),
                    Active = table.Column<bool>(type: "boolean", nullable: false),
                    ConcurrencyToken = table.Column<Guid>(type: "uuid", nullable: false),
                    Phase = table.Column<int>(type: "integer", nullable: false),
                    Publication = table.Column<int>(type: "integer", nullable: false),
                    Cleanup = table.Column<int>(type: "integer", nullable: false),
                    Mode = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastReason = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    RepositoryPath = table.Column<string>(type: "text", nullable: false),
                    CommonDirectory = table.Column<string>(type: "text", nullable: false),
                    WorktreePath = table.Column<string>(type: "text", nullable: false),
                    GitDirectory = table.Column<string>(type: "text", nullable: false),
                    SourceFullRef = table.Column<string>(type: "text", nullable: false),
                    OriginalSourceSha = table.Column<string>(type: "text", nullable: false),
                    RebasedSourceSha = table.Column<string>(type: "text", nullable: true),
                    VerifiedSourceSha = table.Column<string>(type: "text", nullable: true),
                    VerificationCommand = table.Column<string>(type: "text", nullable: true),
                    VerificationFilter = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    VerificationPassed = table.Column<bool>(type: "boolean", nullable: false),
                    VerificationSkipReason = table.Column<string>(type: "text", nullable: true),
                    VerifiedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    TargetFullRef = table.Column<string>(type: "text", nullable: false),
                    TargetBeforeSha = table.Column<string>(type: "text", nullable: false),
                    LocalTargetAfterSha = table.Column<string>(type: "text", nullable: true),
                    RemoteName = table.Column<string>(type: "text", nullable: false),
                    DestinationFullRef = table.Column<string>(type: "text", nullable: false),
                    RemoteFingerprint = table.Column<string>(type: "text", nullable: false),
                    RemoteBeforeSha = table.Column<string>(type: "text", nullable: true),
                    ObservedRemoteTargetSha = table.Column<string>(type: "text", nullable: true),
                    RemoteConfirmedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ConfirmationMethod = table.Column<string>(type: "text", nullable: true),
                    PushStartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    PushExitCode = table.Column<int>(type: "integer", nullable: true),
                    RecoveryRefPrefix = table.Column<string>(type: "text", nullable: false),
                    SourcePinned = table.Column<bool>(type: "boolean", nullable: false),
                    TargetPinned = table.Column<bool>(type: "boolean", nullable: false),
                    PreparedPinned = table.Column<bool>(type: "boolean", nullable: false),
                    CleanupStartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ExpectedDeletionSha = table.Column<string>(type: "text", nullable: true),
                    DirectoryRemoved = table.Column<bool>(type: "boolean", nullable: false),
                    RegistrationRemoved = table.Column<bool>(type: "boolean", nullable: false),
                    BranchRemoved = table.Column<bool>(type: "boolean", nullable: false),
                    ChildProcessId = table.Column<int>(type: "integer", nullable: true),
                    ChildProcessStartTicks = table.Column<long>(type: "bigint", nullable: true),
                    ChildOperation = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AgentTaskLandings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AgentTaskLandings_AgentTasks_TaskId",
                        column: x => x.TaskId,
                        principalTable: "AgentTasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AgentTasks_ActiveLandingId",
                table: "AgentTasks",
                column: "ActiveLandingId");

            migrationBuilder.CreateIndex(
                name: "IX_AgentTaskLandings_TaskId",
                table: "AgentTaskLandings",
                column: "TaskId",
                unique: true,
                filter: "\"Active\" = TRUE");

            migrationBuilder.AddForeignKey(
                name: "FK_AgentTasks_AgentTaskLandings_ActiveLandingId",
                table: "AgentTasks",
                column: "ActiveLandingId",
                principalTable: "AgentTaskLandings",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AgentTasks_AgentTaskLandings_ActiveLandingId",
                table: "AgentTasks");

            migrationBuilder.DropTable(
                name: "AgentTaskLandings");

            migrationBuilder.DropIndex(
                name: "IX_AgentTasks_ActiveLandingId",
                table: "AgentTasks");

            migrationBuilder.DropColumn(
                name: "ActiveLandingId",
                table: "AgentTasks");
        }
    }
}
