using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Antiphon.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddPipelineDefinitions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Every pre-A row has the gen-1 shape and cannot name an immutable revision.
            // Cards.ActiveWorkflowRunId is SetNull, so a linked card survives this deletion.
            migrationBuilder.Sql("DELETE FROM \"CardWorkflowStages\"; DELETE FROM \"CardWorkflowRuns\";");

            migrationBuilder.DropForeignKey(
                name: "FK_CardWorkflowRuns_Agents_AgentId",
                table: "CardWorkflowRuns");

            migrationBuilder.DropForeignKey(
                name: "FK_CardWorkflowRuns_WorkflowTemplates_WorkflowTemplateId",
                table: "CardWorkflowRuns");

            migrationBuilder.DropIndex(
                name: "IX_CardWorkflowRuns_CardId",
                table: "CardWorkflowRuns");

            migrationBuilder.DropIndex(
                name: "IX_CardWorkflowRuns_WorkflowTemplateId",
                table: "CardWorkflowRuns");

            migrationBuilder.DropColumn(
                name: "ExecutorType",
                table: "CardWorkflowStages");

            migrationBuilder.DropColumn(
                name: "GateRequired",
                table: "CardWorkflowStages");

            migrationBuilder.DropColumn(
                name: "ModelName",
                table: "CardWorkflowStages");

            migrationBuilder.DropColumn(
                name: "SystemPrompt",
                table: "CardWorkflowStages");

            migrationBuilder.DropColumn(
                name: "WorkflowDefinitionSnapshot",
                table: "CardWorkflowRuns");

            migrationBuilder.DropColumn(
                name: "WorkflowTemplateId",
                table: "CardWorkflowRuns");

            migrationBuilder.DropIndex(
                name: "IX_CardWorkflowRuns_AgentId",
                table: "CardWorkflowRuns");

            migrationBuilder.DropColumn(
                name: "AgentId",
                table: "CardWorkflowRuns");

            migrationBuilder.AddColumn<Guid>(
                name: "DefaultPipelineDefinitionId",
                table: "Projects",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AllowedNextJson",
                table: "CardWorkflowStages",
                type: "jsonb",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<string>(
                name: "BundleKey",
                table: "CardWorkflowStages",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "Role",
                table: "CardWorkflowStages",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "PipelineDefinitionId",
                table: "CardWorkflowRuns",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "PipelineDefinitionRevisionId",
                table: "CardWorkflowRuns",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "PipelineDefinitionId",
                table: "Boards",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CardWorkflowStageId",
                table: "AgentTasks",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PipelineDefinitionRevisions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DefinitionId = table.Column<Guid>(type: "uuid", nullable: false),
                    RevisionNumber = table.Column<int>(type: "integer", nullable: false),
                    StagesJson = table.Column<string>(type: "jsonb", nullable: false),
                    ContentHash = table.Column<string>(type: "character(8)", fixedLength: true, maxLength: 8, nullable: false),
                    ChangeNote = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PipelineDefinitionRevisions", x => x.Id);
                    table.UniqueConstraint("AK_PipelineDefinitionRevisions_DefinitionId_Id", x => new { x.DefinitionId, x.Id });
                    table.CheckConstraint("CK_PipelineDefinitionRevisions_RevisionNumber_Positive", "\"RevisionNumber\" > 0");
                });

            migrationBuilder.CreateTable(
                name: "PipelineDefinitions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    Source = table.Column<int>(type: "integer", nullable: false),
                    ActiveRevisionId = table.Column<Guid>(type: "uuid", nullable: true),
                    ArchivedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ArchivedReason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    ArchivedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PipelineDefinitions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PipelineDefinitions_PipelineDefinitionRevisions_Id_ActiveRe~",
                        columns: x => new { x.Id, x.ActiveRevisionId },
                        principalTable: "PipelineDefinitionRevisions",
                        principalColumns: new[] { "DefinitionId", "Id" });
                });

            migrationBuilder.CreateIndex(
                name: "IX_Projects_DefaultPipelineDefinitionId",
                table: "Projects",
                column: "DefaultPipelineDefinitionId");

            migrationBuilder.CreateIndex(
                name: "IX_CardWorkflowStages_RunId_Role",
                table: "CardWorkflowStages",
                columns: new[] { "CardWorkflowRunId", "Role" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CardWorkflowRuns_CardId_Open",
                table: "CardWorkflowRuns",
                column: "CardId",
                unique: true,
                filter: "\"Status\" IN (0, 1)");

            migrationBuilder.CreateIndex(
                name: "IX_CardWorkflowRuns_PipelineDefinitionId",
                table: "CardWorkflowRuns",
                column: "PipelineDefinitionId");

            migrationBuilder.CreateIndex(
                name: "IX_CardWorkflowRuns_PipelineDefinitionRevisionId",
                table: "CardWorkflowRuns",
                column: "PipelineDefinitionRevisionId");

            migrationBuilder.CreateIndex(
                name: "IX_Boards_PipelineDefinitionId",
                table: "Boards",
                column: "PipelineDefinitionId");

            migrationBuilder.CreateIndex(
                name: "IX_AgentTasks_CardWorkflowStageId",
                table: "AgentTasks",
                column: "CardWorkflowStageId");

            migrationBuilder.CreateIndex(
                name: "IX_PipelineDefinitionRevisions_DefinitionId_RevisionNumber",
                table: "PipelineDefinitionRevisions",
                columns: new[] { "DefinitionId", "RevisionNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PipelineDefinitions_Id_ActiveRevisionId",
                table: "PipelineDefinitions",
                columns: new[] { "Id", "ActiveRevisionId" });

            migrationBuilder.CreateIndex(
                name: "IX_PipelineDefinitions_Name",
                table: "PipelineDefinitions",
                column: "Name",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_AgentTasks_CardWorkflowStages_CardWorkflowStageId",
                table: "AgentTasks",
                column: "CardWorkflowStageId",
                principalTable: "CardWorkflowStages",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_Boards_PipelineDefinitions_PipelineDefinitionId",
                table: "Boards",
                column: "PipelineDefinitionId",
                principalTable: "PipelineDefinitions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_CardWorkflowRuns_PipelineDefinitionRevisions_PipelineDefini~",
                table: "CardWorkflowRuns",
                column: "PipelineDefinitionRevisionId",
                principalTable: "PipelineDefinitionRevisions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_CardWorkflowRuns_PipelineDefinitions_PipelineDefinitionId",
                table: "CardWorkflowRuns",
                column: "PipelineDefinitionId",
                principalTable: "PipelineDefinitions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Projects_PipelineDefinitions_DefaultPipelineDefinitionId",
                table: "Projects",
                column: "DefaultPipelineDefinitionId",
                principalTable: "PipelineDefinitions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PipelineDefinitionRevisions_PipelineDefinitions_DefinitionId",
                table: "PipelineDefinitionRevisions",
                column: "DefinitionId",
                principalTable: "PipelineDefinitions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Post-A rows have no gen-1 agent/template identity. Remove them before restoring
            // AgentId NOT NULL and its FK, including rows linked from Cards.
            migrationBuilder.Sql("DELETE FROM \"CardWorkflowStages\"; DELETE FROM \"CardWorkflowRuns\";");

            migrationBuilder.DropForeignKey(
                name: "FK_AgentTasks_CardWorkflowStages_CardWorkflowStageId",
                table: "AgentTasks");

            migrationBuilder.DropForeignKey(
                name: "FK_Boards_PipelineDefinitions_PipelineDefinitionId",
                table: "Boards");

            migrationBuilder.DropForeignKey(
                name: "FK_CardWorkflowRuns_PipelineDefinitionRevisions_PipelineDefini~",
                table: "CardWorkflowRuns");

            migrationBuilder.DropForeignKey(
                name: "FK_CardWorkflowRuns_PipelineDefinitions_PipelineDefinitionId",
                table: "CardWorkflowRuns");

            migrationBuilder.DropForeignKey(
                name: "FK_Projects_PipelineDefinitions_DefaultPipelineDefinitionId",
                table: "Projects");

            migrationBuilder.DropForeignKey(
                name: "FK_PipelineDefinitionRevisions_PipelineDefinitions_DefinitionId",
                table: "PipelineDefinitionRevisions");

            migrationBuilder.DropTable(
                name: "PipelineDefinitions");

            migrationBuilder.DropTable(
                name: "PipelineDefinitionRevisions");

            migrationBuilder.DropIndex(
                name: "IX_Projects_DefaultPipelineDefinitionId",
                table: "Projects");

            migrationBuilder.DropIndex(
                name: "IX_CardWorkflowStages_RunId_Role",
                table: "CardWorkflowStages");

            migrationBuilder.DropIndex(
                name: "IX_CardWorkflowRuns_CardId_Open",
                table: "CardWorkflowRuns");

            migrationBuilder.DropIndex(
                name: "IX_CardWorkflowRuns_PipelineDefinitionId",
                table: "CardWorkflowRuns");

            migrationBuilder.DropIndex(
                name: "IX_CardWorkflowRuns_PipelineDefinitionRevisionId",
                table: "CardWorkflowRuns");

            migrationBuilder.DropIndex(
                name: "IX_Boards_PipelineDefinitionId",
                table: "Boards");

            migrationBuilder.DropIndex(
                name: "IX_AgentTasks_CardWorkflowStageId",
                table: "AgentTasks");

            migrationBuilder.DropColumn(
                name: "DefaultPipelineDefinitionId",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "AllowedNextJson",
                table: "CardWorkflowStages");

            migrationBuilder.DropColumn(
                name: "BundleKey",
                table: "CardWorkflowStages");

            migrationBuilder.DropColumn(
                name: "Role",
                table: "CardWorkflowStages");

            migrationBuilder.DropColumn(
                name: "PipelineDefinitionId",
                table: "CardWorkflowRuns");

            migrationBuilder.DropColumn(
                name: "PipelineDefinitionRevisionId",
                table: "CardWorkflowRuns");

            migrationBuilder.DropColumn(
                name: "PipelineDefinitionId",
                table: "Boards");

            migrationBuilder.DropColumn(
                name: "CardWorkflowStageId",
                table: "AgentTasks");

            migrationBuilder.AddColumn<Guid>(
                name: "AgentId",
                table: "CardWorkflowRuns",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<string>(
                name: "ExecutorType",
                table: "CardWorkflowStages",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "GateRequired",
                table: "CardWorkflowStages",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "ModelName",
                table: "CardWorkflowStages",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SystemPrompt",
                table: "CardWorkflowStages",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WorkflowDefinitionSnapshot",
                table: "CardWorkflowRuns",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<Guid>(
                name: "WorkflowTemplateId",
                table: "CardWorkflowRuns",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_CardWorkflowRuns_CardId",
                table: "CardWorkflowRuns",
                column: "CardId");

            migrationBuilder.CreateIndex(
                name: "IX_CardWorkflowRuns_AgentId",
                table: "CardWorkflowRuns",
                column: "AgentId");

            migrationBuilder.CreateIndex(
                name: "IX_CardWorkflowRuns_WorkflowTemplateId",
                table: "CardWorkflowRuns",
                column: "WorkflowTemplateId");

            migrationBuilder.AddForeignKey(
                name: "FK_CardWorkflowRuns_Agents_AgentId",
                table: "CardWorkflowRuns",
                column: "AgentId",
                principalTable: "Agents",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_CardWorkflowRuns_WorkflowTemplates_WorkflowTemplateId",
                table: "CardWorkflowRuns",
                column: "WorkflowTemplateId",
                principalTable: "WorkflowTemplates",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }
    }
}
