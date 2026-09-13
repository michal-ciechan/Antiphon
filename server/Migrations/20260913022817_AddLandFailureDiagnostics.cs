using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Antiphon.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddLandFailureDiagnostics : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "FailureDiagnosticId",
                table: "AgentTaskLandRequests",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FailureExceptionType",
                table: "AgentTaskLandRequests",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceDiagnosticCode",
                table: "AgentTaskLandRequests",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceDiagnosticCommand",
                table: "AgentTaskLandRequests",
                type: "character varying(160)",
                maxLength: 160,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceDiagnosticExceptionType",
                table: "AgentTaskLandRequests",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SourceDiagnosticExitCode",
                table: "AgentTaskLandRequests",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TerminalFailureCode",
                table: "AgentTaskLandRequests",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FailureDiagnosticId",
                table: "AgentTaskLandRequests");

            migrationBuilder.DropColumn(
                name: "FailureExceptionType",
                table: "AgentTaskLandRequests");

            migrationBuilder.DropColumn(
                name: "SourceDiagnosticCode",
                table: "AgentTaskLandRequests");

            migrationBuilder.DropColumn(
                name: "SourceDiagnosticCommand",
                table: "AgentTaskLandRequests");

            migrationBuilder.DropColumn(
                name: "SourceDiagnosticExceptionType",
                table: "AgentTaskLandRequests");

            migrationBuilder.DropColumn(
                name: "SourceDiagnosticExitCode",
                table: "AgentTaskLandRequests");

            migrationBuilder.DropColumn(
                name: "TerminalFailureCode",
                table: "AgentTaskLandRequests");
        }
    }
}
