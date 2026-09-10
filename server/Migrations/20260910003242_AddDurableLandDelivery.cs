using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Antiphon.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddDurableLandDelivery : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "SourceLandNotificationId",
                table: "SessionQueuedMessages",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CurrentLandRequestId",
                table: "AgentTasks",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsLandTerminal",
                table: "AgentTaskEvents",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "LandRequestId",
                table: "AgentTaskEvents",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AgentTaskLandRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TaskId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    VerifyFilter = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    ReplyTo = table.Column<int>(type: "integer", nullable: false),
                    ParentSessionId = table.Column<Guid>(type: "uuid", nullable: true),
                    State = table.Column<int>(type: "integer", nullable: false),
                    IsPending = table.Column<bool>(type: "boolean", nullable: false),
                    LandingOperationId = table.Column<Guid>(type: "uuid", nullable: true),
                    TerminalEventId = table.Column<Guid>(type: "uuid", nullable: true),
                    StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Attempt = table.Column<int>(type: "integer", nullable: false),
                    LastAttemptAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastEvaluatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastProgressAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    HighestProgress = table.Column<int>(type: "integer", nullable: false),
                    HoldReasonCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    HoldDetail = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    HoldingTaskId = table.Column<Guid>(type: "uuid", nullable: true),
                    HoldingTaskStatus = table.Column<int>(type: "integer", nullable: true),
                    HeldSince = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    HoldEpisode = table.Column<int>(type: "integer", nullable: false),
                    WarningAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ErrorAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ReconciliationError = table.Column<string>(type: "text", nullable: true),
                    ConcurrencyToken = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AgentTaskLandRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AgentTaskLandRequests_AgentTasks_TaskId",
                        column: x => x.TaskId,
                        principalTable: "AgentTasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AgentTaskLandNotifications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    TaskId = table.Column<Guid>(type: "uuid", nullable: false),
                    LandingOperationId = table.Column<Guid>(type: "uuid", nullable: true),
                    SourceEventId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    ReplyTo = table.Column<int>(type: "integer", nullable: false),
                    ParentSessionId = table.Column<Guid>(type: "uuid", nullable: true),
                    Body = table.Column<string>(type: "text", nullable: false),
                    ContentDigest = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    NextAttemptAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EnqueueAttempts = table.Column<int>(type: "integer", nullable: false),
                    State = table.Column<int>(type: "integer", nullable: false),
                    LastErrorCode = table.Column<string>(type: "text", nullable: true),
                    LastErrorAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    QueueMessageId = table.Column<Guid>(type: "uuid", nullable: true),
                    EnqueuedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ConfirmedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ConfirmingPromptSequence = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AgentTaskLandNotifications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AgentTaskLandNotifications_AgentTaskEvents_SourceEventId",
                        column: x => x.SourceEventId,
                        principalTable: "AgentTaskEvents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AgentTaskLandNotifications_AgentTaskLandRequests_RequestId",
                        column: x => x.RequestId,
                        principalTable: "AgentTaskLandRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SessionQueuedMessages_SourceLandNotificationId",
                table: "SessionQueuedMessages",
                column: "SourceLandNotificationId",
                unique: true,
                filter: "\"SourceLandNotificationId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AgentTaskEvents_LandRequestId",
                table: "AgentTaskEvents",
                column: "LandRequestId",
                unique: true,
                filter: "\"IsLandTerminal\" = TRUE");

            migrationBuilder.CreateIndex(
                name: "IX_AgentTaskLandNotifications_RequestId",
                table: "AgentTaskLandNotifications",
                column: "RequestId");

            migrationBuilder.CreateIndex(
                name: "IX_AgentTaskLandNotifications_SourceEventId",
                table: "AgentTaskLandNotifications",
                column: "SourceEventId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AgentTaskLandNotifications_State_NextAttemptAt_Id",
                table: "AgentTaskLandNotifications",
                columns: new[] { "State", "NextAttemptAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_AgentTaskLandRequests_IsPending_LastProgressAt",
                table: "AgentTaskLandRequests",
                columns: new[] { "IsPending", "LastProgressAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AgentTaskLandRequests_TaskId",
                table: "AgentTaskLandRequests",
                column: "TaskId",
                unique: true,
                filter: "\"IsPending\" = TRUE");
            migrationBuilder.Sql("""
                INSERT INTO "AgentTaskLandRequests"
                    ("Id", "TaskId", "RequestedAt", "VerifyFilter", "ReplyTo", "ParentSessionId", "State", "IsPending",
                     "StartedAt", "Attempt", "LastAttemptAt", "LastEvaluatedAt", "LastProgressAt", "HighestProgress", "HoldEpisode", "ConcurrencyToken")
                SELECT gen_random_uuid(), "Id", "LandRequestedAt", "LandVerifyFilter", "ReplyTo", "ParentSessionId", 0, TRUE,
                    "LandStartedAt", "LandAttempt", "LandStartedAt", "LandRequestedAt", "LandRequestedAt", -2, 0, gen_random_uuid()
                FROM "AgentTasks" WHERE "LandRequestedAt" IS NOT NULL;
                UPDATE "AgentTasks" t SET "CurrentLandRequestId" = r."Id"
                FROM "AgentTaskLandRequests" r WHERE r."TaskId" = t."Id";
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AgentTaskLandNotifications");

            migrationBuilder.DropTable(
                name: "AgentTaskLandRequests");

            migrationBuilder.DropIndex(
                name: "IX_SessionQueuedMessages_SourceLandNotificationId",
                table: "SessionQueuedMessages");

            migrationBuilder.DropIndex(
                name: "IX_AgentTaskEvents_LandRequestId",
                table: "AgentTaskEvents");

            migrationBuilder.DropColumn(
                name: "SourceLandNotificationId",
                table: "SessionQueuedMessages");

            migrationBuilder.DropColumn(
                name: "CurrentLandRequestId",
                table: "AgentTasks");

            migrationBuilder.DropColumn(
                name: "IsLandTerminal",
                table: "AgentTaskEvents");

            migrationBuilder.DropColumn(
                name: "LandRequestId",
                table: "AgentTaskEvents");
        }
    }
}
