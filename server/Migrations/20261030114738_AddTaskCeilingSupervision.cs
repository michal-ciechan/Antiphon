using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Antiphon.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddTaskCeilingSupervision : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "CeilingAcknowledgedAt",
                table: "AgentTasks",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CeilingHeldAt",
                table: "AgentTasks",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CeilingHoldAttempt",
                table: "AgentTasks",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CeilingHoldDispatchedAt",
                table: "AgentTasks",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CeilingHoldEpisodeId",
                table: "AgentTasks",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CeilingHoldSessionId",
                table: "AgentTasks",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CeilingHoldSessionStartedAt",
                table: "AgentTasks",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "CeilingOperatorExtended",
                table: "AgentTasks",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<double>(
                name: "NextCeilingMinutes",
                table: "AgentTasks",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RequestedCeilingMinutes",
                table: "AgentTasks",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RequestedCeilingReason",
                table: "AgentTasks",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ResolvedCeilingMinutes",
                table: "AgentTasks",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ResolvedCeilingSource",
                table: "AgentTasks",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "TaskSupervisionNotices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TaskId = table.Column<Guid>(type: "uuid", nullable: false),
                    Attempt = table.Column<int>(type: "integer", nullable: false),
                    SourceEventId = table.Column<Guid>(type: "uuid", nullable: false),
                    Purpose = table.Column<int>(type: "integer", nullable: false),
                    EpisodeId = table.Column<Guid>(type: "uuid", nullable: true),
                    SessionId = table.Column<Guid>(type: "uuid", nullable: true),
                    SessionStartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DispatchedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ParentSessionId = table.Column<Guid>(type: "uuid", nullable: true),
                    ParentStartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RecipientSessionId = table.Column<Guid>(type: "uuid", nullable: true),
                    RecipientStartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RecipientFloor = table.Column<long>(type: "bigint", nullable: false),
                    Body = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    Digest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    State = table.Column<int>(type: "integer", nullable: false),
                    ReasonCode = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    NextAttemptAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    QueueMessageId = table.Column<Guid>(type: "uuid", nullable: true),
                    ToldAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ConfirmingPromptSequence = table.Column<long>(type: "bigint", nullable: true),
                    FailureFrontier = table.Column<long>(type: "bigint", nullable: false),
                    ReportFloor = table.Column<long>(type: "bigint", nullable: false),
                    ScanFrontier = table.Column<long>(type: "bigint", nullable: false),
                    ReportText = table.Column<string>(type: "character varying(16000)", maxLength: 16000, nullable: true),
                    ReportDigest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ReportFromSequence = table.Column<long>(type: "bigint", nullable: true),
                    ReportThroughSequence = table.Column<long>(type: "bigint", nullable: true),
                    ReportForwardedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaskSupervisionNotices", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TaskSupervisionNotices_NextAttemptAt_Id",
                table: "TaskSupervisionNotices",
                columns: new[] { "NextAttemptAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_TaskSupervisionNotices_PurposeIdentity",
                table: "TaskSupervisionNotices",
                columns: new[] { "TaskId", "Attempt", "SourceEventId", "Purpose" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TaskSupervisionNotices");

            migrationBuilder.DropColumn(
                name: "CeilingAcknowledgedAt",
                table: "AgentTasks");

            migrationBuilder.DropColumn(
                name: "CeilingHeldAt",
                table: "AgentTasks");

            migrationBuilder.DropColumn(
                name: "CeilingHoldAttempt",
                table: "AgentTasks");

            migrationBuilder.DropColumn(
                name: "CeilingHoldDispatchedAt",
                table: "AgentTasks");

            migrationBuilder.DropColumn(
                name: "CeilingHoldEpisodeId",
                table: "AgentTasks");

            migrationBuilder.DropColumn(
                name: "CeilingHoldSessionId",
                table: "AgentTasks");

            migrationBuilder.DropColumn(
                name: "CeilingHoldSessionStartedAt",
                table: "AgentTasks");

            migrationBuilder.DropColumn(
                name: "CeilingOperatorExtended",
                table: "AgentTasks");

            migrationBuilder.DropColumn(
                name: "NextCeilingMinutes",
                table: "AgentTasks");

            migrationBuilder.DropColumn(
                name: "RequestedCeilingMinutes",
                table: "AgentTasks");

            migrationBuilder.DropColumn(
                name: "RequestedCeilingReason",
                table: "AgentTasks");

            migrationBuilder.DropColumn(
                name: "ResolvedCeilingMinutes",
                table: "AgentTasks");

            migrationBuilder.DropColumn(
                name: "ResolvedCeilingSource",
                table: "AgentTasks");
        }
    }
}
