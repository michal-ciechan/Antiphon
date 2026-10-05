using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Antiphon.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddRunnerSeatReleases : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ReleasedSeatAnswer",
                table: "AgentTasks",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReleasedSeatAnswerAcceptedAt",
                table: "AgentTasks",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ReleasedSeatAnswerId",
                table: "AgentTasks",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ReleasedSeatAnswerReleaseId",
                table: "AgentTasks",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ReleasedSeatAnswerRoundId",
                table: "AgentTasks",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ReleasedSeatAnswerTargetAttempt",
                table: "AgentTasks",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "RunnerSeatReleases",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RunnerId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    RunnerStoreId = table.Column<Guid>(type: "uuid", nullable: false),
                    SessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    AcceptedStartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    TaskId = table.Column<Guid>(type: "uuid", nullable: true),
                    Attempt = table.Column<int>(type: "integer", nullable: true),
                    AgentId = table.Column<Guid>(type: "uuid", nullable: true),
                    SettlementEventId = table.Column<Guid>(type: "uuid", nullable: true),
                    SettlementRevision = table.Column<Guid>(type: "uuid", nullable: true),
                    SettledAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    State = table.Column<int>(type: "integer", nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    ActionId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReasonCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    OutcomeCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ObservationToken = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    BindingIdentity = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    FileRevision = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    TranscriptRevision = table.Column<long>(type: "bigint", nullable: true),
                    FirstStableObservedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastObservedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ConfirmedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RunnerSeatReleases", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RunnerSeatReleases_ActionId",
                table: "RunnerSeatReleases",
                column: "ActionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RunnerSeatReleases_RunnerId_RunnerStoreId_SessionId_Accepte~",
                table: "RunnerSeatReleases",
                columns: new[] { "RunnerId", "RunnerStoreId", "SessionId", "AcceptedStartedAt" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RunnerSeatReleases_State_UpdatedAt",
                table: "RunnerSeatReleases",
                columns: new[] { "State", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_RunnerSeatReleases_TaskId_Attempt",
                table: "RunnerSeatReleases",
                columns: new[] { "TaskId", "Attempt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RunnerSeatReleases");

            migrationBuilder.DropColumn(
                name: "ReleasedSeatAnswer",
                table: "AgentTasks");

            migrationBuilder.DropColumn(
                name: "ReleasedSeatAnswerAcceptedAt",
                table: "AgentTasks");

            migrationBuilder.DropColumn(
                name: "ReleasedSeatAnswerId",
                table: "AgentTasks");

            migrationBuilder.DropColumn(
                name: "ReleasedSeatAnswerReleaseId",
                table: "AgentTasks");

            migrationBuilder.DropColumn(
                name: "ReleasedSeatAnswerRoundId",
                table: "AgentTasks");

            migrationBuilder.DropColumn(
                name: "ReleasedSeatAnswerTargetAttempt",
                table: "AgentTasks");
        }
    }
}
