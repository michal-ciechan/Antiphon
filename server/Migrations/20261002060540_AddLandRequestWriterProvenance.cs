using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Antiphon.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddLandRequestWriterProvenance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "LastWriterAt",
                table: "AgentTaskLandRequests",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastWriterOperation",
                table: "AgentTaskLandRequests",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "LastWriterToken",
                table: "AgentTaskLandRequests",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RecoveryWitnessRequestId",
                table: "AgentTaskLandRequests",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LastWriterAt",
                table: "AgentTaskLandRequests");

            migrationBuilder.DropColumn(
                name: "LastWriterOperation",
                table: "AgentTaskLandRequests");

            migrationBuilder.DropColumn(
                name: "LastWriterToken",
                table: "AgentTaskLandRequests");

            migrationBuilder.DropColumn(
                name: "RecoveryWitnessRequestId",
                table: "AgentTaskLandRequests");
        }
    }
}
