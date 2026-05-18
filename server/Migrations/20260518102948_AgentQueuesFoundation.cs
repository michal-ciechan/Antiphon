using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Antiphon.Server.Migrations
{
    /// <inheritdoc />
    public partial class AgentQueuesFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ActiveWorkflowRunId",
                table: "Cards",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AgentQueuePosition",
                table: "Cards",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "AssignedAgentId",
                table: "Cards",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Agents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Slug = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    WorkingDirectory = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    Details = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    DefaultWorkflowTemplateId = table.Column<Guid>(type: "uuid", nullable: true),
                    AssignmentPolicy = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    PersistentSessionId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CurrentCardId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Agents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Agents_Cards_CurrentCardId",
                        column: x => x.CurrentCardId,
                        principalTable: "Cards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Agents_WorkflowTemplates_DefaultWorkflowTemplateId",
                        column: x => x.DefaultWorkflowTemplateId,
                        principalTable: "WorkflowTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "CardWorkflowRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CardId = table.Column<Guid>(type: "uuid", nullable: false),
                    AgentId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkflowTemplateId = table.Column<Guid>(type: "uuid", nullable: true),
                    WorkflowName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    WorkflowDefinitionSnapshot = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CurrentStageId = table.Column<Guid>(type: "uuid", nullable: true),
                    FailureReason = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CardWorkflowRuns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CardWorkflowRuns_Agents_AgentId",
                        column: x => x.AgentId,
                        principalTable: "Agents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CardWorkflowRuns_Cards_CardId",
                        column: x => x.CardId,
                        principalTable: "Cards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CardWorkflowRuns_WorkflowTemplates_WorkflowTemplateId",
                        column: x => x.WorkflowTemplateId,
                        principalTable: "WorkflowTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "CardWorkflowStages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CardWorkflowRunId = table.Column<Guid>(type: "uuid", nullable: false),
                    StageOrder = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ExecutorType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ModelName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    GateRequired = table.Column<bool>(type: "boolean", nullable: false),
                    SystemPrompt = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ResultSummary = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    FailureReason = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CardWorkflowStages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CardWorkflowStages_CardWorkflowRuns_CardWorkflowRunId",
                        column: x => x.CardWorkflowRunId,
                        principalTable: "CardWorkflowRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Cards_ActiveWorkflowRunId",
                table: "Cards",
                column: "ActiveWorkflowRunId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Cards_AssignedAgentId",
                table: "Cards",
                column: "AssignedAgentId");

            migrationBuilder.CreateIndex(
                name: "IX_Cards_AssignedAgentId_AgentQueuePosition",
                table: "Cards",
                columns: new[] { "AssignedAgentId", "AgentQueuePosition" });

            migrationBuilder.CreateIndex(
                name: "IX_Agents_CurrentCardId",
                table: "Agents",
                column: "CurrentCardId");

            migrationBuilder.CreateIndex(
                name: "IX_Agents_DefaultWorkflowTemplateId",
                table: "Agents",
                column: "DefaultWorkflowTemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_Agents_Slug",
                table: "Agents",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Agents_Status",
                table: "Agents",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_CardWorkflowRuns_AgentId",
                table: "CardWorkflowRuns",
                column: "AgentId");

            migrationBuilder.CreateIndex(
                name: "IX_CardWorkflowRuns_CardId",
                table: "CardWorkflowRuns",
                column: "CardId");

            migrationBuilder.CreateIndex(
                name: "IX_CardWorkflowRuns_CardId_Id",
                table: "CardWorkflowRuns",
                columns: new[] { "CardId", "Id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CardWorkflowRuns_CardId_Status",
                table: "CardWorkflowRuns",
                columns: new[] { "CardId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_CardWorkflowRuns_CurrentStageId",
                table: "CardWorkflowRuns",
                column: "CurrentStageId");

            migrationBuilder.CreateIndex(
                name: "IX_CardWorkflowRuns_WorkflowTemplateId",
                table: "CardWorkflowRuns",
                column: "WorkflowTemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_CardWorkflowStages_RunId_Id",
                table: "CardWorkflowStages",
                columns: new[] { "CardWorkflowRunId", "Id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CardWorkflowStages_RunId_StageOrder",
                table: "CardWorkflowStages",
                columns: new[] { "CardWorkflowRunId", "StageOrder" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Cards_Agents_AssignedAgentId",
                table: "Cards",
                column: "AssignedAgentId",
                principalTable: "Agents",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_Cards_CardWorkflowRuns_ActiveWorkflowRunId",
                table: "Cards",
                column: "ActiveWorkflowRunId",
                principalTable: "CardWorkflowRuns",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            // Manual PostgreSQL composite FK: EF maps the nullable navigation by Id only,
            // but the database must also enforce that the active run belongs to the same card.
            migrationBuilder.Sql(
                """
                ALTER TABLE "Cards"
                ADD CONSTRAINT "FK_Cards_CardWorkflowRuns_Id_ActiveWorkflowRunId"
                FOREIGN KEY ("Id", "ActiveWorkflowRunId")
                REFERENCES "CardWorkflowRuns" ("CardId", "Id")
                ON DELETE SET NULL ("ActiveWorkflowRunId")
                """);

            migrationBuilder.AddForeignKey(
                name: "FK_CardWorkflowRuns_CardWorkflowStages_CurrentStageId",
                table: "CardWorkflowRuns",
                column: "CurrentStageId",
                principalTable: "CardWorkflowStages",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            // Manual PostgreSQL composite FK: EF maps the nullable navigation by Id only,
            // but the database must also enforce that the current stage belongs to the same run.
            migrationBuilder.Sql(
                """
                ALTER TABLE "CardWorkflowRuns"
                ADD CONSTRAINT "FK_CardWorkflowRuns_CardWorkflowStages_Id_CurrentStageId"
                FOREIGN KEY ("Id", "CurrentStageId")
                REFERENCES "CardWorkflowStages" ("CardWorkflowRunId", "Id")
                ON DELETE SET NULL ("CurrentStageId")
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Cards_Agents_AssignedAgentId",
                table: "Cards");

            migrationBuilder.DropForeignKey(
                name: "FK_Cards_CardWorkflowRuns_ActiveWorkflowRunId",
                table: "Cards");

            migrationBuilder.Sql(
                """
                ALTER TABLE "Cards"
                DROP CONSTRAINT "FK_Cards_CardWorkflowRuns_Id_ActiveWorkflowRunId"
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_CardWorkflowRuns_Agents_AgentId",
                table: "CardWorkflowRuns");

            migrationBuilder.DropForeignKey(
                name: "FK_CardWorkflowRuns_CardWorkflowStages_CurrentStageId",
                table: "CardWorkflowRuns");

            migrationBuilder.Sql(
                """
                ALTER TABLE "CardWorkflowRuns"
                DROP CONSTRAINT "FK_CardWorkflowRuns_CardWorkflowStages_Id_CurrentStageId"
                """);

            migrationBuilder.DropTable(
                name: "Agents");

            migrationBuilder.DropTable(
                name: "CardWorkflowStages");

            migrationBuilder.DropTable(
                name: "CardWorkflowRuns");

            migrationBuilder.DropIndex(
                name: "IX_Cards_ActiveWorkflowRunId",
                table: "Cards");

            migrationBuilder.DropIndex(
                name: "IX_Cards_AssignedAgentId",
                table: "Cards");

            migrationBuilder.DropIndex(
                name: "IX_Cards_AssignedAgentId_AgentQueuePosition",
                table: "Cards");

            migrationBuilder.DropColumn(
                name: "ActiveWorkflowRunId",
                table: "Cards");

            migrationBuilder.DropColumn(
                name: "AgentQueuePosition",
                table: "Cards");

            migrationBuilder.DropColumn(
                name: "AssignedAgentId",
                table: "Cards");
        }
    }
}
