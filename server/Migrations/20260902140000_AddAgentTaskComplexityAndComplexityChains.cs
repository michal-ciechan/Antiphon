using System;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Antiphon.Server.Migrations
{
    /// <summary>
    /// CARD-0090: nullable AgentTasks.Complexity plus the ComplexityChains table. Hand-written
    /// (running daemons lock bin/); the [DbContext]/[Migration] attributes normally generated
    /// into the Designer live here. Snapshot is updated to match.
    /// </summary>
    [DbContext(typeof(AppDbContext))]
    [Migration("20260902140000_AddAgentTaskComplexityAndComplexityChains")]
    public partial class AddAgentTaskComplexityAndComplexityChains : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Complexity",
                table: "AgentTasks",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ComplexityChains",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Complexity = table.Column<int>(type: "integer", nullable: false),
                    CandidatesJson = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    Provenance = table.Column<int>(type: "integer", nullable: false),
                    Reason = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    NotAfter = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SourceTaskId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ClearedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ComplexityChains", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ComplexityChains_Complexity_Active",
                table: "ComplexityChains",
                column: "Complexity",
                unique: true,
                filter: "\"ClearedAt\" IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ComplexityChains");

            migrationBuilder.DropColumn(
                name: "Complexity",
                table: "AgentTasks");
        }
    }
}
