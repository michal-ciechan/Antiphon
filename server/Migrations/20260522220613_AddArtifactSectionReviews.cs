using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Antiphon.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddArtifactSectionReviews : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ArtifactSectionReviews",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StageExecutionId = table.Column<Guid>(type: "uuid", nullable: false),
                    SectionPath = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    ContentHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ReviewedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ArtifactSectionReviews", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ArtifactSectionReviews_StageExecutions_StageExecutionId",
                        column: x => x.StageExecutionId,
                        principalTable: "StageExecutions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ArtifactSectionReviews_StageExecutionId",
                table: "ArtifactSectionReviews",
                column: "StageExecutionId");

            migrationBuilder.CreateIndex(
                name: "IX_ArtifactSectionReviews_StageExecutionId_SectionPath",
                table: "ArtifactSectionReviews",
                columns: new[] { "StageExecutionId", "SectionPath" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ArtifactSectionReviews");
        }
    }
}
