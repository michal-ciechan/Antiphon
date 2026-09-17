using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Antiphon.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddLandingVerificationCard : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "VerificationCardId",
                table: "AgentTaskLandings",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_AgentTaskLandings_VerificationCardId",
                table: "AgentTaskLandings",
                column: "VerificationCardId");

            migrationBuilder.AddForeignKey(
                name: "FK_AgentTaskLandings_Cards_VerificationCardId",
                table: "AgentTaskLandings",
                column: "VerificationCardId",
                principalTable: "Cards",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AgentTaskLandings_Cards_VerificationCardId",
                table: "AgentTaskLandings");

            migrationBuilder.DropIndex(
                name: "IX_AgentTaskLandings_VerificationCardId",
                table: "AgentTaskLandings");

            migrationBuilder.DropColumn(
                name: "VerificationCardId",
                table: "AgentTaskLandings");
        }
    }
}
