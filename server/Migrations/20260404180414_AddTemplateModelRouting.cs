using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Antiphon.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddTemplateModelRouting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ModelRoutings_StageName",
                table: "ModelRoutings");

            migrationBuilder.AddColumn<Guid>(
                name: "WorkflowTemplateId",
                table: "ModelRoutings",
                type: "uuid",
                nullable: true);

            // Backfill: assign seeded BMAD Full routings to their template
            migrationBuilder.Sql(@"
                UPDATE ""ModelRoutings"" SET ""WorkflowTemplateId"" = 'b0000000-0000-0000-0000-000000000001'
                WHERE ""Id"" IN (
                    'd0000000-0000-0000-0000-000000000001',
                    'd0000000-0000-0000-0000-000000000002',
                    'd0000000-0000-0000-0000-000000000003',
                    'd0000000-0000-0000-0000-000000000004',
                    'd0000000-0000-0000-0000-000000000005',
                    'd0000000-0000-0000-0000-000000000006'
                );
                UPDATE ""ModelRoutings"" SET ""WorkflowTemplateId"" = 'b0000000-0000-0000-0000-000000000002'
                WHERE ""Id"" IN (
                    'd0000000-0000-0000-0000-000000000007',
                    'd0000000-0000-0000-0000-000000000008',
                    'd0000000-0000-0000-0000-000000000009'
                );
            ");

            migrationBuilder.CreateIndex(
                name: "IX_ModelRoutings_WorkflowTemplateId_StageName",
                table: "ModelRoutings",
                columns: new[] { "WorkflowTemplateId", "StageName" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_ModelRoutings_WorkflowTemplates_WorkflowTemplateId",
                table: "ModelRoutings",
                column: "WorkflowTemplateId",
                principalTable: "WorkflowTemplates",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ModelRoutings_WorkflowTemplates_WorkflowTemplateId",
                table: "ModelRoutings");

            migrationBuilder.DropIndex(
                name: "IX_ModelRoutings_WorkflowTemplateId_StageName",
                table: "ModelRoutings");

            migrationBuilder.DropColumn(
                name: "WorkflowTemplateId",
                table: "ModelRoutings");

            migrationBuilder.CreateIndex(
                name: "IX_ModelRoutings_StageName",
                table: "ModelRoutings",
                column: "StageName",
                unique: true);
        }
    }
}
