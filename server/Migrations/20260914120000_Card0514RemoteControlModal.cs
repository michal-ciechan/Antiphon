using System;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Antiphon.Server.Migrations
{
    /// <summary>
    /// CARD-0514: remote-control modal episodes, typed automatic-arm queue fields, uniqueness,
    /// and legacy /remote-control row classification. Isolated-output CLI equivalent: default
    /// bin/ is held by the running Antiphon.Server process.
    /// </summary>
    [DbContext(typeof(AppDbContext))]
    [Migration("20260914120000_Card0514RemoteControlModal")]
    public partial class Card0514RemoteControlModal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "DeferredFromRunAttemptId",
                table: "SessionQueuedMessages",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "MaintenanceAcceptedStartedAt",
                table: "SessionQueuedMessages",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MaintenanceEvidence",
                table: "SessionQueuedMessages",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MaintenanceKind",
                table: "SessionQueuedMessages",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "MaintenanceResult",
                table: "SessionQueuedMessages",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "MaintenanceResultAt",
                table: "SessionQueuedMessages",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "MaintenanceSlotActive",
                table: "SessionQueuedMessages",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "SubmissionStartedAt",
                table: "SessionQueuedMessages",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "RemoteControlModalEpisodes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    AcceptedStartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    FirstObservedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastObservedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ChildPid = table.Column<int>(type: "integer", nullable: true),
                    BeforeOutputSequence = table.Column<long>(type: "bigint", nullable: true),
                    AfterOutputSequence = table.Column<long>(type: "bigint", nullable: true),
                    TranscriptCatchUpWatermark = table.Column<long>(type: "bigint", nullable: true),
                    TranscriptWorking = table.Column<bool>(type: "boolean", nullable: true),
                    RelatedMaintenanceQueueId = table.Column<Guid>(type: "uuid", nullable: true),
                    DismissalIntentAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DismissalSentAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DismissalVerifiedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DismissalResult = table.Column<int>(type: "integer", nullable: true),
                    ResolvedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Resolution = table.Column<int>(type: "integer", nullable: true),
                    LastTransition = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    LastIncidentAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ChannelBound = table.Column<bool>(type: "boolean", nullable: false),
                    ObservedEnqueueSequence = table.Column<long>(type: "bigint", nullable: true),
                    ObservedDrainSequence = table.Column<long>(type: "bigint", nullable: true),
                    ObservedPromptSequence = table.Column<long>(type: "bigint", nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RemoteControlModalEpisodes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RemoteControlModalEpisodes_AgentSessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "AgentSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SessionQueuedMessages_ActiveAutomaticArm",
                table: "SessionQueuedMessages",
                columns: new[] { "AgentSessionId", "MaintenanceAcceptedStartedAt" },
                unique: true,
                filter: "\"MaintenanceSlotActive\" = TRUE");

            migrationBuilder.CreateIndex(
                name: "IX_SessionQueuedMessages_DeferredFromRunAttemptId",
                table: "SessionQueuedMessages",
                column: "DeferredFromRunAttemptId",
                unique: true,
                filter: "\"DeferredFromRunAttemptId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_RemoteControlModalEpisodes_OpenGeneration",
                table: "RemoteControlModalEpisodes",
                columns: new[] { "SessionId", "AcceptedStartedAt" },
                unique: true,
                filter: "\"ResolvedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_RemoteControlModalEpisodes_ResolvedAt",
                table: "RemoteControlModalEpisodes",
                column: "ResolvedAt");

            migrationBuilder.Sql("""
                UPDATE "SessionQueuedMessages"
                SET "MaintenanceKind" = 2,
                    "MaintenanceEvidence" = 'legacy-unclassified'
                WHERE "MaintenanceKind" = 0
                  AND "Status" IN (0, 1)
                  AND "DeliveryVerdict" IS NULL
                  AND split_part(regexp_replace(btrim("Body"), E'[\\t\\n\\r]+', ' ', 'g'), ' ', 1) = '/remote-control';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "RemoteControlModalEpisodes");

            migrationBuilder.DropIndex(
                name: "IX_SessionQueuedMessages_ActiveAutomaticArm",
                table: "SessionQueuedMessages");

            migrationBuilder.DropIndex(
                name: "IX_SessionQueuedMessages_DeferredFromRunAttemptId",
                table: "SessionQueuedMessages");

            migrationBuilder.DropColumn(name: "DeferredFromRunAttemptId", table: "SessionQueuedMessages");
            migrationBuilder.DropColumn(name: "MaintenanceAcceptedStartedAt", table: "SessionQueuedMessages");
            migrationBuilder.DropColumn(name: "MaintenanceEvidence", table: "SessionQueuedMessages");
            migrationBuilder.DropColumn(name: "MaintenanceKind", table: "SessionQueuedMessages");
            migrationBuilder.DropColumn(name: "MaintenanceResult", table: "SessionQueuedMessages");
            migrationBuilder.DropColumn(name: "MaintenanceResultAt", table: "SessionQueuedMessages");
            migrationBuilder.DropColumn(name: "MaintenanceSlotActive", table: "SessionQueuedMessages");
            migrationBuilder.DropColumn(name: "SubmissionStartedAt", table: "SessionQueuedMessages");
        }
    }
}
