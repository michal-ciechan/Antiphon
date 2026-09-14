using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Antiphon.Server.Migrations
{
    /// <summary>
    /// CARD-0508 S2b: dispatch-base warning intent table and nullable land-notification RequestId.
    /// No historical intent backfill.
    /// </summary>
    public partial class AddDispatchBaseWarningIntent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Guid>(
                name: "RequestId",
                table: "AgentTaskLandNotifications",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.CreateTable(
                name: "AgentTaskDispatchWarningIntents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DispatchEventId = table.Column<Guid>(type: "uuid", nullable: false),
                    TaskId = table.Column<Guid>(type: "uuid", nullable: false),
                    Attempt = table.Column<int>(type: "integer", nullable: false),
                    WarningKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    NotificationId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReplyTo = table.Column<int>(type: "integer", nullable: false),
                    ParentSessionId = table.Column<Guid>(type: "uuid", nullable: true),
                    Detail = table.Column<string>(type: "text", nullable: false),
                    Body = table.Column<string>(type: "text", nullable: false),
                    ContentDigest = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    InitialState = table.Column<int>(type: "integer", nullable: false),
                    MaterializedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    MaterializationAttempts = table.Column<int>(type: "integer", nullable: false),
                    NextAttemptAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastErrorCode = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    LastErrorAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ConcurrencyToken = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AgentTaskDispatchWarningIntents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AgentTaskDispatchWarningIntents_AgentTaskEvents_DispatchEve~",
                        column: x => x.DispatchEventId,
                        principalTable: "AgentTaskEvents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AgentTaskDispatchWarningIntents_AgentTasks_TaskId",
                        column: x => x.TaskId,
                        principalTable: "AgentTasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AgentTaskDispatchWarningIntents_DispatchEventId_WarningKey",
                table: "AgentTaskDispatchWarningIntents",
                columns: new[] { "DispatchEventId", "WarningKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AgentTaskDispatchWarningIntents_MaterializedAt_NextAttemptA~",
                table: "AgentTaskDispatchWarningIntents",
                columns: new[] { "MaterializedAt", "NextAttemptAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_AgentTaskDispatchWarningIntents_NotificationId",
                table: "AgentTaskDispatchWarningIntents",
                column: "NotificationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AgentTaskDispatchWarningIntents_TaskId",
                table: "AgentTaskDispatchWarningIntents",
                column: "TaskId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AgentTaskDispatchWarningIntents");

            migrationBuilder.AlterColumn<Guid>(
                name: "RequestId",
                table: "AgentTaskLandNotifications",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);
        }
    }
}
