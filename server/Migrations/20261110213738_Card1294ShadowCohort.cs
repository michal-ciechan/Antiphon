using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Antiphon.Server.Migrations
{
    /// <inheritdoc />
    public partial class Card1294ShadowCohort : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CardCompletionShadowEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SchemaVersion = table.Column<int>(type: "integer", nullable: false),
                    CardId = table.Column<Guid>(type: "uuid", nullable: false),
                    AcceptanceId = table.Column<Guid>(type: "uuid", nullable: false),
                    OperationId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProposedClose = table.Column<bool>(type: "boolean", nullable: false),
                    ExpectedClose = table.Column<bool>(type: "boolean", nullable: true),
                    Adjudicator = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    AdjudicatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Actor = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    RecordedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CardCompletionShadowEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CardCompletionShadowEvents_AgentTaskLandings_OperationId",
                        column: x => x.OperationId,
                        principalTable: "AgentTaskLandings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CardCompletionShadowEvents_CardCompletionAcceptances_Accept~",
                        column: x => x.AcceptanceId,
                        principalTable: "CardCompletionAcceptances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CardCompletionShadowEvents_AcceptanceId",
                table: "CardCompletionShadowEvents",
                column: "AcceptanceId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CardCompletionShadowEvents_CardId",
                table: "CardCompletionShadowEvents",
                column: "CardId");

            migrationBuilder.CreateIndex(
                name: "IX_CardCompletionShadowEvents_OperationId",
                table: "CardCompletionShadowEvents",
                column: "OperationId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException("Shadow publication-close events cannot be discarded by a downgrade.");
        }
    }
}
