using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Antiphon.Server.Migrations
{
    /// <inheritdoc />
    public partial class Card0412CapacityRecovery : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CapacityRecoveryActionKey",
                table: "SessionQueuedMessages",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CapacityWaitId",
                table: "SessionQueuedMessages",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CapacityWaitVersion",
                table: "SessionQueuedMessages",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ClearCause",
                table: "ModelAvailabilityHolds",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "EvidenceRecoveryId",
                table: "ModelAvailabilityHolds",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReleaseConsumedAt",
                table: "ModelAvailabilityHolds",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReleasePendingAt",
                table: "ModelAvailabilityHolds",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Revision",
                table: "ModelAvailabilityHolds",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "AppliedHoldId",
                table: "ApiErrorRecoveries",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AppliedHoldRevision",
                table: "ApiErrorRecoveries",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CapacityWaitId",
                table: "ApiErrorRecoveries",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "EvidenceAt",
                table: "ApiErrorRecoveries",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EvidenceDigest",
                table: "ApiErrorRecoveries",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EvidenceStatus",
                table: "ApiErrorRecoveries",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "EvidenceTimestampSource",
                table: "ApiErrorRecoveries",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ParseVersion",
                table: "ApiErrorRecoveries",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CapacityWaitId",
                table: "AgentTasks",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CapacityWaitReason",
                table: "AgentTasks",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "CapacityWaitRetained",
                table: "AgentTasks",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "CapacityNextDueAt",
                table: "AgentSupervisionStates",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CapacityRecoveryActionKey",
                table: "AgentSupervisionStates",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CapacityWaitId",
                table: "AgentSupervisionStates",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CapacityLaunchReceipt",
                table: "AgentSessions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CapacityRecoveryActionKey",
                table: "AgentSessions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CapacityWaitId",
                table: "AgentSessions",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CapacityRecoveryProviderStates",
                columns: table => new
                {
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    NextAdmissionAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastActionKey = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    LastActionAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    WaveRevision = table.Column<int>(type: "integer", nullable: false),
                    GrantedWaitId = table.Column<Guid>(type: "uuid", nullable: true),
                    GrantedActionKey = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    GrantedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    GrantVersion = table.Column<int>(type: "integer", nullable: false),
                    ExpectedWaitVersion = table.Column<int>(type: "integer", nullable: false),
                    ExpectedWaveRevision = table.Column<int>(type: "integer", nullable: false),
                    ExpectedOwnerId = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CapacityRecoveryProviderStates", x => x.Kind);
                });

            migrationBuilder.CreateTable(
                name: "CapacityRecoveryWaits",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ConsumerKind = table.Column<int>(type: "integer", nullable: false),
                    ConsumerKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    BlockedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AgentId = table.Column<Guid>(type: "uuid", nullable: true),
                    SessionId = table.Column<Guid>(type: "uuid", nullable: true),
                    SessionStartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    TaskId = table.Column<Guid>(type: "uuid", nullable: true),
                    TaskAttempt = table.Column<int>(type: "integer", nullable: true),
                    CardId = table.Column<Guid>(type: "uuid", nullable: true),
                    RequestedKind = table.Column<int>(type: "integer", nullable: false),
                    RequestedAlias = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ExecutionKind = table.Column<int>(type: "integer", nullable: false),
                    ActionOrdinal = table.Column<int>(type: "integer", nullable: false),
                    ActionKey = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    State = table.Column<int>(type: "integer", nullable: false),
                    DueAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    AdmissionCount = table.Column<int>(type: "integer", nullable: false),
                    SelectedMessageId = table.Column<Guid>(type: "uuid", nullable: true),
                    LaunchSessionId = table.Column<Guid>(type: "uuid", nullable: true),
                    LaunchReceipt = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    DispatchAttemptId = table.Column<Guid>(type: "uuid", nullable: true),
                    ConfirmedPromptSequence = table.Column<long>(type: "bigint", nullable: true),
                    Outcome = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    OutcomeReason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    RefusalDigest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    AuthorizationSnapshot = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    CompatibilityResult = table.Column<int>(type: "integer", nullable: true),
                    CompatibilityVersion = table.Column<int>(type: "integer", nullable: false),
                    LatestClearObservedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ObservedClearCauses = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    NeedsRevalidationGrant = table.Column<bool>(type: "boolean", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CapacityRecoveryWaits", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CapacityRecoveryWaitHolds",
                columns: table => new
                {
                    WaitId = table.Column<Guid>(type: "uuid", nullable: false),
                    HoldId = table.Column<Guid>(type: "uuid", nullable: false),
                    ObservedRevision = table.Column<int>(type: "integer", nullable: false),
                    ReleaseAcknowledgedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CapacityRecoveryWaitHolds", x => new { x.WaitId, x.HoldId });
                    table.ForeignKey(
                        name: "FK_CapacityRecoveryWaitHolds_CapacityRecoveryWaits_WaitId",
                        column: x => x.WaitId,
                        principalTable: "CapacityRecoveryWaits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CapacityRecoveryWaitHolds_ModelAvailabilityHolds_HoldId",
                        column: x => x.HoldId,
                        principalTable: "ModelAvailabilityHolds",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SessionQueuedMessages_CapacityRecoveryActionKey",
                table: "SessionQueuedMessages",
                column: "CapacityRecoveryActionKey",
                unique: true,
                filter: "\"CapacityRecoveryActionKey\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ModelAvailabilityHolds_ReleasePending",
                table: "ModelAvailabilityHolds",
                column: "ReleasePendingAt",
                filter: "\"ReleasePendingAt\" IS NOT NULL AND \"ReleaseConsumedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_CapacityRecoveryWaitHolds_HoldId",
                table: "CapacityRecoveryWaitHolds",
                column: "HoldId");

            migrationBuilder.CreateIndex(
                name: "IX_CapacityRecoveryWaits_ConsumerKey_Active",
                table: "CapacityRecoveryWaits",
                column: "ConsumerKey",
                unique: true,
                filter: "\"State\" NOT IN (7, 9, 10, 12)");

            migrationBuilder.CreateIndex(
                name: "IX_CapacityRecoveryWaits_DueAt",
                table: "CapacityRecoveryWaits",
                column: "DueAt");

            migrationBuilder.CreateIndex(
                name: "IX_CapacityRecoveryWaits_Kind_BlockedAt",
                table: "CapacityRecoveryWaits",
                columns: new[] { "ExecutionKind", "BlockedAt", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CapacityRecoveryProviderStates");

            migrationBuilder.DropTable(
                name: "CapacityRecoveryWaitHolds");

            migrationBuilder.DropTable(
                name: "CapacityRecoveryWaits");

            migrationBuilder.DropIndex(
                name: "IX_SessionQueuedMessages_CapacityRecoveryActionKey",
                table: "SessionQueuedMessages");

            migrationBuilder.DropIndex(
                name: "IX_ModelAvailabilityHolds_ReleasePending",
                table: "ModelAvailabilityHolds");

            migrationBuilder.DropColumn(
                name: "CapacityRecoveryActionKey",
                table: "SessionQueuedMessages");

            migrationBuilder.DropColumn(
                name: "CapacityWaitId",
                table: "SessionQueuedMessages");

            migrationBuilder.DropColumn(
                name: "CapacityWaitVersion",
                table: "SessionQueuedMessages");

            migrationBuilder.DropColumn(
                name: "ClearCause",
                table: "ModelAvailabilityHolds");

            migrationBuilder.DropColumn(
                name: "EvidenceRecoveryId",
                table: "ModelAvailabilityHolds");

            migrationBuilder.DropColumn(
                name: "ReleaseConsumedAt",
                table: "ModelAvailabilityHolds");

            migrationBuilder.DropColumn(
                name: "ReleasePendingAt",
                table: "ModelAvailabilityHolds");

            migrationBuilder.DropColumn(
                name: "Revision",
                table: "ModelAvailabilityHolds");

            migrationBuilder.DropColumn(
                name: "AppliedHoldId",
                table: "ApiErrorRecoveries");

            migrationBuilder.DropColumn(
                name: "AppliedHoldRevision",
                table: "ApiErrorRecoveries");

            migrationBuilder.DropColumn(
                name: "CapacityWaitId",
                table: "ApiErrorRecoveries");

            migrationBuilder.DropColumn(
                name: "EvidenceAt",
                table: "ApiErrorRecoveries");

            migrationBuilder.DropColumn(
                name: "EvidenceDigest",
                table: "ApiErrorRecoveries");

            migrationBuilder.DropColumn(
                name: "EvidenceStatus",
                table: "ApiErrorRecoveries");

            migrationBuilder.DropColumn(
                name: "EvidenceTimestampSource",
                table: "ApiErrorRecoveries");

            migrationBuilder.DropColumn(
                name: "ParseVersion",
                table: "ApiErrorRecoveries");

            migrationBuilder.DropColumn(
                name: "CapacityWaitId",
                table: "AgentTasks");

            migrationBuilder.DropColumn(
                name: "CapacityWaitReason",
                table: "AgentTasks");

            migrationBuilder.DropColumn(
                name: "CapacityWaitRetained",
                table: "AgentTasks");

            migrationBuilder.DropColumn(
                name: "CapacityNextDueAt",
                table: "AgentSupervisionStates");

            migrationBuilder.DropColumn(
                name: "CapacityRecoveryActionKey",
                table: "AgentSupervisionStates");

            migrationBuilder.DropColumn(
                name: "CapacityWaitId",
                table: "AgentSupervisionStates");

            migrationBuilder.DropColumn(
                name: "CapacityLaunchReceipt",
                table: "AgentSessions");

            migrationBuilder.DropColumn(
                name: "CapacityRecoveryActionKey",
                table: "AgentSessions");

            migrationBuilder.DropColumn(
                name: "CapacityWaitId",
                table: "AgentSessions");
        }
    }
}
