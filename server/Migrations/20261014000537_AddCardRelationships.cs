using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Antiphon.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddCardRelationships : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "RelatedCardId",
                table: "CardRevisions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RelatedCardIdentifier",
                table: "CardRevisions",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RelationshipKind",
                table: "CardRevisions",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CardRelationships",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CardId = table.Column<Guid>(type: "uuid", nullable: false),
                    RelatedCardId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    Reason = table.Column<string>(type: "text", nullable: true),
                    CreatedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CardRelationships", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CardRelationships_Cards_CardId",
                        column: x => x.CardId,
                        principalTable: "Cards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CardRelationships_Cards_RelatedCardId",
                        column: x => x.RelatedCardId,
                        principalTable: "Cards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CardRelationships_CardId_RelatedCardId_Kind",
                table: "CardRelationships",
                columns: new[] { "CardId", "RelatedCardId", "Kind" });

            migrationBuilder.CreateIndex(
                name: "IX_CardRelationships_RelatedCardId",
                table: "CardRelationships",
                column: "RelatedCardId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CardRelationships");

            migrationBuilder.DropColumn(
                name: "RelatedCardId",
                table: "CardRevisions");

            migrationBuilder.DropColumn(
                name: "RelatedCardIdentifier",
                table: "CardRevisions");

            migrationBuilder.DropColumn(
                name: "RelationshipKind",
                table: "CardRevisions");
        }
    }
}
