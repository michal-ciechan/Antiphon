using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Antiphon.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddLandingEventEvidence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "LandingCleanup",
                table: "AgentTaskEvents",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LandingMode",
                table: "AgentTaskEvents",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "LandingOperationId",
                table: "AgentTaskEvents",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LandingPublication",
                table: "AgentTaskEvents",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LandingCleanup",
                table: "AgentTaskEvents");

            migrationBuilder.DropColumn(
                name: "LandingMode",
                table: "AgentTaskEvents");

            migrationBuilder.DropColumn(
                name: "LandingOperationId",
                table: "AgentTaskEvents");

            migrationBuilder.DropColumn(
                name: "LandingPublication",
                table: "AgentTaskEvents");
        }
    }
}
