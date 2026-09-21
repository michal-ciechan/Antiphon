using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Antiphon.Server.Migrations
{
    /// <inheritdoc />
    public partial class CheckCompactionRecovery : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ActiveCompactionRecoveryId",
                table: "AgentSupervisionStates",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "CompactionRestartReceiptEligible",
                table: "AgentSupervisionStates",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastAutomaticCompactionRestartAt",
                table: "AgentSupervisionStates",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CheckCompactionRecoveries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PhysicalAgentId = table.Column<Guid>(type: "uuid", nullable: false),
                    SessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    AcceptedStartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    BoundaryIdentity = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    BoundarySequence = table.Column<long>(type: "bigint", nullable: true),
                    ContinuationSequence = table.Column<long>(type: "bigint", nullable: true),
                    NativeContinuationIdentity = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    OwningCheckTaskId = table.Column<Guid>(type: "uuid", nullable: true),
                    OrdinaryPromptSequence = table.Column<long>(type: "bigint", nullable: true),
                    BoundaryTimestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    BoundaryCreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ContinuationTimestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ContinuationCreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ConfiguredThresholdMinutes = table.Column<int>(type: "integer", nullable: false),
                    DetectedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ObservedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    EvidenceJson = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    State = table.Column<int>(type: "integer", nullable: false),
                    Reason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    AttemptId = table.Column<Guid>(type: "uuid", nullable: true),
                    StopRequestedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    StopOutcomeAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ResumeSessionId = table.Column<Guid>(type: "uuid", nullable: true),
                    ResumeAcceptedStartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LaunchOutcome = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    UsefulCheckTaskId = table.Column<Guid>(type: "uuid", nullable: true),
                    CallerReceiptNotificationId = table.Column<Guid>(type: "uuid", nullable: true),
                    ConfirmingPromptSequence = table.Column<long>(type: "bigint", nullable: true),
                    ObservationBindingIdentity = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    ObservationTranscriptRevision = table.Column<long>(type: "bigint", nullable: true),
                    ObservationOutputRevision = table.Column<long>(type: "bigint", nullable: true),
                    ConcurrencyToken = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CheckCompactionRecoveries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CheckCompactionRecoveries_Agents_PhysicalAgentId",
                        column: x => x.PhysicalAgentId,
                        principalTable: "Agents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CheckCompactionRecoveries_Agent_State",
                table: "CheckCompactionRecoveries",
                columns: new[] { "PhysicalAgentId", "State" });

            migrationBuilder.CreateIndex(
                name: "IX_CheckCompactionRecoveries_Episode",
                table: "CheckCompactionRecoveries",
                columns: new[] { "PhysicalAgentId", "SessionId", "AcceptedStartedAt", "BoundaryIdentity" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CheckCompactionRecoveries");

            migrationBuilder.DropColumn(
                name: "ActiveCompactionRecoveryId",
                table: "AgentSupervisionStates");

            migrationBuilder.DropColumn(
                name: "CompactionRestartReceiptEligible",
                table: "AgentSupervisionStates");

            migrationBuilder.DropColumn(
                name: "LastAutomaticCompactionRestartAt",
                table: "AgentSupervisionStates");
        }
    }
}
