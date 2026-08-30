using System;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Antiphon.Server.Migrations
{
    // Hand-written (no .Designer.cs): the running daemons lock bin/, so `dotnet ef migrations add`
    // couldn't build. Snapshot updated by hand.
    /// <inheritdoc />
    [DbContext(typeof(AppDbContext))]
    [Migration("20260830180000_MakeAgentIncidentAgentIdNullable")]
    public partial class MakeAgentIncidentAgentIdNullable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AgentIncidents_Agents_AgentId",
                table: "AgentIncidents");

            migrationBuilder.AlterColumn<Guid>(
                name: "AgentId",
                table: "AgentIncidents",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddForeignKey(
                name: "FK_AgentIncidents_Agents_AgentId",
                table: "AgentIncidents",
                column: "AgentId",
                principalTable: "Agents",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AgentIncidents_Agents_AgentId",
                table: "AgentIncidents");

            migrationBuilder.AlterColumn<Guid>(
                name: "AgentId",
                table: "AgentIncidents",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AddForeignKey(
                name: "FK_AgentIncidents_Agents_AgentId",
                table: "AgentIncidents",
                column: "AgentId",
                principalTable: "Agents",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
