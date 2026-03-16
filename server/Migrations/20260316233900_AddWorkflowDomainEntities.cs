using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Antiphon.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkflowDomainEntities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GateDecisions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StageId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkflowId = table.Column<Guid>(type: "uuid", nullable: false),
                    Action = table.Column<int>(type: "integer", nullable: false),
                    Feedback = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    DecidedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GateDecisions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GateDecisions_Users_DecidedBy",
                        column: x => x.DecidedBy,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StageExecutions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StageId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkflowId = table.Column<Guid>(type: "uuid", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ErrorDetails = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    GitTagName = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    TokensIn = table.Column<long>(type: "bigint", nullable: false),
                    TokensOut = table.Column<long>(type: "bigint", nullable: false),
                    EstimatedCostUsd = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StageExecutions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Stages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkflowId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    StageOrder = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ExecutorType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ModelName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    GateRequired = table.Column<bool>(type: "boolean", nullable: false),
                    CurrentVersion = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Stages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Workflows",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    TemplateId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CurrentStageId = table.Column<Guid>(type: "uuid", nullable: true),
                    InitialContext = table.Column<string>(type: "text", nullable: false),
                    GitBranchName = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Workflows", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Workflows_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Workflows_Stages_CurrentStageId",
                        column: x => x.CurrentStageId,
                        principalTable: "Stages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Workflows_WorkflowTemplates_TemplateId",
                        column: x => x.TemplateId,
                        principalTable: "WorkflowTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GateDecisions_DecidedBy",
                table: "GateDecisions",
                column: "DecidedBy");

            migrationBuilder.CreateIndex(
                name: "IX_GateDecisions_StageId",
                table: "GateDecisions",
                column: "StageId");

            migrationBuilder.CreateIndex(
                name: "IX_GateDecisions_WorkflowId",
                table: "GateDecisions",
                column: "WorkflowId");

            migrationBuilder.CreateIndex(
                name: "IX_StageExecutions_StageId",
                table: "StageExecutions",
                column: "StageId");

            migrationBuilder.CreateIndex(
                name: "IX_StageExecutions_StageId_Version",
                table: "StageExecutions",
                columns: new[] { "StageId", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StageExecutions_WorkflowId",
                table: "StageExecutions",
                column: "WorkflowId");

            migrationBuilder.CreateIndex(
                name: "IX_Stages_WorkflowId",
                table: "Stages",
                column: "WorkflowId");

            migrationBuilder.CreateIndex(
                name: "IX_Stages_WorkflowId_StageOrder",
                table: "Stages",
                columns: new[] { "WorkflowId", "StageOrder" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Workflows_CurrentStageId",
                table: "Workflows",
                column: "CurrentStageId");

            migrationBuilder.CreateIndex(
                name: "IX_Workflows_ProjectId",
                table: "Workflows",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_Workflows_Status",
                table: "Workflows",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_Workflows_TemplateId",
                table: "Workflows",
                column: "TemplateId");

            migrationBuilder.AddForeignKey(
                name: "FK_GateDecisions_Stages_StageId",
                table: "GateDecisions",
                column: "StageId",
                principalTable: "Stages",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_GateDecisions_Workflows_WorkflowId",
                table: "GateDecisions",
                column: "WorkflowId",
                principalTable: "Workflows",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_StageExecutions_Stages_StageId",
                table: "StageExecutions",
                column: "StageId",
                principalTable: "Stages",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_StageExecutions_Workflows_WorkflowId",
                table: "StageExecutions",
                column: "WorkflowId",
                principalTable: "Workflows",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Stages_Workflows_WorkflowId",
                table: "Stages",
                column: "WorkflowId",
                principalTable: "Workflows",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Workflows_Stages_CurrentStageId",
                table: "Workflows");

            migrationBuilder.DropTable(
                name: "GateDecisions");

            migrationBuilder.DropTable(
                name: "StageExecutions");

            migrationBuilder.DropTable(
                name: "Stages");

            migrationBuilder.DropTable(
                name: "Workflows");
        }
    }
}
