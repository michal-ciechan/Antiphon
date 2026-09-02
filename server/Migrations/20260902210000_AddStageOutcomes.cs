using System;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Antiphon.Server.Migrations
{
    /// <summary>
    /// CARD-0272 S1: StageOutcomes table plus AgentTasks.Stage and AgentTasks.FollowUpOfTaskId.
    /// Hand-written (running daemons lock bin/); the [DbContext]/[Migration] attributes
    /// normally generated into the Designer live here. Snapshot is updated to match.
    /// </summary>
    [DbContext(typeof(AppDbContext))]
    [Migration("20260902210000_AddStageOutcomes")]
    public partial class AddStageOutcomes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "FollowUpOfTaskId",
                table: "AgentTasks",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Stage",
                table: "AgentTasks",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "StageOutcomes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Stage = table.Column<int>(type: "integer", nullable: false),
                    Outcome = table.Column<int>(type: "integer", nullable: false),
                    Source = table.Column<int>(type: "integer", nullable: false),
                    SubjectTaskId = table.Column<Guid>(type: "uuid", nullable: true),
                    StageTaskId = table.Column<Guid>(type: "uuid", nullable: true),
                    CardId = table.Column<Guid>(type: "uuid", nullable: true),
                    CostUsd = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    TokensIn = table.Column<long>(type: "bigint", nullable: true),
                    TokensOut = table.Column<long>(type: "bigint", nullable: true),
                    DurationSeconds = table.Column<int>(type: "integer", nullable: false),
                    ResolutionTaskId = table.Column<Guid>(type: "uuid", nullable: true),
                    ResolutionCostUsd = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    Detail = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    Ref = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    SupersedesId = table.Column<Guid>(type: "uuid", nullable: true),
                    RecordedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StageOutcomes", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StageOutcomes_CardId",
                table: "StageOutcomes",
                column: "CardId");

            migrationBuilder.CreateIndex(
                name: "IX_StageOutcomes_RecordedAt",
                table: "StageOutcomes",
                column: "RecordedAt");

            migrationBuilder.CreateIndex(
                name: "IX_StageOutcomes_Stage_RecordedAt",
                table: "StageOutcomes",
                columns: new[] { "Stage", "RecordedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_StageOutcomes_StageTaskId",
                table: "StageOutcomes",
                column: "StageTaskId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StageOutcomes");

            migrationBuilder.DropColumn(
                name: "FollowUpOfTaskId",
                table: "AgentTasks");

            migrationBuilder.DropColumn(
                name: "Stage",
                table: "AgentTasks");
        }
    }
}
