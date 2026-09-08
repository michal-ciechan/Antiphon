using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Antiphon.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddSpecialistExecutionIdentity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SpecialistEffectiveModelId",
                table: "AgentTasks",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SpecialistModelAlias",
                table: "AgentTasks",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SpecialistModelId",
                table: "AgentTasks",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SpecialistProfileRevisionId",
                table: "AgentTasks",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SpecialistSessionId",
                table: "AgentTasks",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "SpecialistSessionStartedAt",
                table: "AgentTasks",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SpecialistEffectiveModelId",
                table: "AgentTasks");

            migrationBuilder.DropColumn(
                name: "SpecialistModelAlias",
                table: "AgentTasks");

            migrationBuilder.DropColumn(
                name: "SpecialistModelId",
                table: "AgentTasks");

            migrationBuilder.DropColumn(
                name: "SpecialistProfileRevisionId",
                table: "AgentTasks");

            migrationBuilder.DropColumn(
                name: "SpecialistSessionId",
                table: "AgentTasks");

            migrationBuilder.DropColumn(
                name: "SpecialistSessionStartedAt",
                table: "AgentTasks");
        }
    }
}
