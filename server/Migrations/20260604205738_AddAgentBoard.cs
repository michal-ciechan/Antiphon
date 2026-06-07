using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Antiphon.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddAgentBoard : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "BoardId",
                table: "Agents",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Agents_BoardId",
                table: "Agents",
                column: "BoardId");

            migrationBuilder.AddForeignKey(
                name: "FK_Agents_Boards_BoardId",
                table: "Agents",
                column: "BoardId",
                principalTable: "Boards",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Agents_Boards_BoardId",
                table: "Agents");

            migrationBuilder.DropIndex(
                name: "IX_Agents_BoardId",
                table: "Agents");

            migrationBuilder.DropColumn(
                name: "BoardId",
                table: "Agents");
        }
    }
}
