using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Antiphon.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddLlmProviderAndModelRouting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LlmProviders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ProviderType = table.Column<int>(type: "integer", nullable: false),
                    ApiKey = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    BaseUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    DefaultModel = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LlmProviders", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ModelRoutings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StageName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ModelName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ProviderId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ModelRoutings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ModelRoutings_LlmProviders_ProviderId",
                        column: x => x.ProviderId,
                        principalTable: "LlmProviders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LlmProviders_Name",
                table: "LlmProviders",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ModelRoutings_ProviderId",
                table: "ModelRoutings",
                column: "ProviderId");

            migrationBuilder.CreateIndex(
                name: "IX_ModelRoutings_StageName",
                table: "ModelRoutings",
                column: "StageName",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ModelRoutings");

            migrationBuilder.DropTable(
                name: "LlmProviders");
        }
    }
}
