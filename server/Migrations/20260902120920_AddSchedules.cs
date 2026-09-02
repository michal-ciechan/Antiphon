using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Antiphon.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddSchedules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "SourceScheduleId",
                table: "SessionQueuedMessages",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Schedules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    Repeat = table.Column<int>(type: "integer", nullable: false),
                    TimeZoneId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    NextFireAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    MissedGraceMinutes = table.Column<int>(type: "integer", nullable: true),
                    FireCount = table.Column<int>(type: "integer", nullable: false),
                    LastFiredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastOutcome = table.Column<int>(type: "integer", nullable: true),
                    LastOutcomeDetail = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CreatedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ConcurrencyToken = table.Column<Guid>(type: "uuid", nullable: false),
                    AgentId = table.Column<Guid>(type: "uuid", nullable: true),
                    PromptText = table.Column<string>(type: "text", nullable: true),
                    WhenTargetDown = table.Column<int>(type: "integer", nullable: false),
                    CardId = table.Column<Guid>(type: "uuid", nullable: true),
                    TargetStatus = table.Column<int>(type: "integer", nullable: true),
                    Start = table.Column<int>(type: "integer", nullable: false),
                    SpendAcceptedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SpendAcceptedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    FireAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    EveryMinutes = table.Column<int>(type: "integer", nullable: true),
                    AnchorAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    AtLocal = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: true),
                    DaysOfWeek = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Schedules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Schedules_Agents_AgentId",
                        column: x => x.AgentId,
                        principalTable: "Agents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Schedules_Cards_CardId",
                        column: x => x.CardId,
                        principalTable: "Cards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ScheduleFires",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ScheduleId = table.Column<Guid>(type: "uuid", nullable: false),
                    FireNumber = table.Column<int>(type: "integer", nullable: false),
                    DueAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ClaimedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Outcome = table.Column<int>(type: "integer", nullable: false),
                    Detail = table.Column<string>(type: "text", nullable: true),
                    QueuedMessageId = table.Column<Guid>(type: "uuid", nullable: true),
                    SpawnedSessionId = table.Column<Guid>(type: "uuid", nullable: true),
                    Manual = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScheduleFires", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ScheduleFires_Schedules_ScheduleId",
                        column: x => x.ScheduleId,
                        principalTable: "Schedules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SessionQueuedMessages_SourceScheduleId",
                table: "SessionQueuedMessages",
                column: "SourceScheduleId");

            migrationBuilder.CreateIndex(
                name: "IX_ScheduleFires_ScheduleId_FireNumber",
                table: "ScheduleFires",
                columns: new[] { "ScheduleId", "FireNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Schedules_AgentId",
                table: "Schedules",
                column: "AgentId");

            migrationBuilder.CreateIndex(
                name: "IX_Schedules_CardId",
                table: "Schedules",
                column: "CardId");

            migrationBuilder.CreateIndex(
                name: "IX_Schedules_Enabled_NextFireAt",
                table: "Schedules",
                columns: new[] { "Enabled", "NextFireAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ScheduleFires");

            migrationBuilder.DropTable(
                name: "Schedules");

            migrationBuilder.DropIndex(
                name: "IX_SessionQueuedMessages_SourceScheduleId",
                table: "SessionQueuedMessages");

            migrationBuilder.DropColumn(
                name: "SourceScheduleId",
                table: "SessionQueuedMessages");
        }
    }
}
