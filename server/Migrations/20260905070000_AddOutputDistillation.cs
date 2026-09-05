using System;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Antiphon.Server.Migrations
{
    /// <summary>
    /// CARD-0330 S3: OutputDistillations ledger, AgentTasks.DistilledResult, SessionQueuedMessages.HoldUntil.
    /// No backfill.
    /// </summary>
    [DbContext(typeof(AppDbContext))]
    [Migration("20260905070000_AddOutputDistillation")]
    public partial class AddOutputDistillation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DistilledResult",
                table: "AgentTasks",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "HoldUntil",
                table: "SessionQueuedMessages",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "OutputDistillations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TaskId = table.Column<Guid>(type: "uuid", nullable: false),
                    DistillTaskId = table.Column<Guid>(type: "uuid", nullable: true),
                    QueuedMessageId = table.Column<Guid>(type: "uuid", nullable: true),
                    BundleStamp = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    Mode = table.Column<int>(type: "integer", nullable: false),
                    RawChars = table.Column<int>(type: "integer", nullable: false),
                    DistilledChars = table.Column<int>(type: "integer", nullable: false),
                    WaitMs = table.Column<int>(type: "integer", nullable: false),
                    CostUsd = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    Outcome = table.Column<int>(type: "integer", nullable: false),
                    MissingAnchors = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Feedback = table.Column<int>(type: "integer", nullable: false),
                    FeedbackNote = table.Column<string>(type: "text", nullable: true),
                    FeedbackBy = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    FeedbackAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FullReadAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutputDistillations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OutputDistillations_AgentTasks_DistillTaskId",
                        column: x => x.DistillTaskId,
                        principalTable: "AgentTasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_OutputDistillations_AgentTasks_TaskId",
                        column: x => x.TaskId,
                        principalTable: "AgentTasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OutputDistillations_CreatedAt",
                table: "OutputDistillations",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_OutputDistillations_DistillTaskId",
                table: "OutputDistillations",
                column: "DistillTaskId");

            migrationBuilder.CreateIndex(
                name: "IX_OutputDistillations_Outcome_CreatedAt",
                table: "OutputDistillations",
                columns: new[] { "Outcome", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_OutputDistillations_TaskId",
                table: "OutputDistillations",
                column: "TaskId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OutputDistillations");

            migrationBuilder.DropColumn(
                name: "DistilledResult",
                table: "AgentTasks");

            migrationBuilder.DropColumn(
                name: "HoldUntil",
                table: "SessionQueuedMessages");
        }
    }
}
