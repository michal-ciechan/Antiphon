using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Antiphon.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddDelegationCapabilities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DelegationCapabilities",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TokenHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RootsJson = table.Column<string>(type: "jsonb", nullable: false),
                    BoardId = table.Column<Guid>(type: "uuid", nullable: true),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastUsedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RotatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RevokedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DelegationCapabilities", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DelegationCapabilities_Boards_BoardId",
                        column: x => x.BoardId,
                        principalTable: "Boards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_DelegationCapabilities_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "DelegationCapabilityEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CapabilityId = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    Detail = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    At = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DelegationCapabilityEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DelegationCapabilityEvents_DelegationCapabilities_Capabilit~",
                        column: x => x.CapabilityId,
                        principalTable: "DelegationCapabilities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DelegationCapabilities_BoardId",
                table: "DelegationCapabilities",
                column: "BoardId");

            migrationBuilder.CreateIndex(
                name: "IX_DelegationCapabilities_Name_Active",
                table: "DelegationCapabilities",
                column: "Name",
                unique: true,
                filter: "\"RevokedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_DelegationCapabilities_ProjectId",
                table: "DelegationCapabilities",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_DelegationCapabilities_TokenHash",
                table: "DelegationCapabilities",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DelegationCapabilityEvents_CapabilityId_At",
                table: "DelegationCapabilityEvents",
                columns: new[] { "CapabilityId", "At" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DelegationCapabilityEvents");

            migrationBuilder.DropTable(
                name: "DelegationCapabilities");
        }
    }
}
