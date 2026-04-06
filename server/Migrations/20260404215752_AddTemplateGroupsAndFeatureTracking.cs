using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Antiphon.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddTemplateGroupsAndFeatureTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "TemplateGroupId",
                table: "WorkflowTemplates",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FeatureName",
                table: "Workflows",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "TemplateGroups",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    IsBuiltIn = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TemplateGroups", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowTemplates_TemplateGroupId",
                table: "WorkflowTemplates",
                column: "TemplateGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_Workflows_ProjectId_FeatureName",
                table: "Workflows",
                columns: new[] { "ProjectId", "FeatureName" });

            migrationBuilder.CreateIndex(
                name: "IX_TemplateGroups_Name",
                table: "TemplateGroups",
                column: "Name",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_WorkflowTemplates_TemplateGroups_TemplateGroupId",
                table: "WorkflowTemplates",
                column: "TemplateGroupId",
                principalTable: "TemplateGroups",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_WorkflowTemplates_TemplateGroups_TemplateGroupId",
                table: "WorkflowTemplates");

            migrationBuilder.DropTable(
                name: "TemplateGroups");

            migrationBuilder.DropIndex(
                name: "IX_WorkflowTemplates_TemplateGroupId",
                table: "WorkflowTemplates");

            migrationBuilder.DropIndex(
                name: "IX_Workflows_ProjectId_FeatureName",
                table: "Workflows");

            migrationBuilder.DropColumn(
                name: "TemplateGroupId",
                table: "WorkflowTemplates");

            migrationBuilder.DropColumn(
                name: "FeatureName",
                table: "Workflows");
        }
    }
}
