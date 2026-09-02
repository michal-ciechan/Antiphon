using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Antiphon.Server.Migrations
{
    /// <summary>
    /// CARD-0324: nullable AgentSessions.LaunchBlock (SessionLaunchBlock enum).
    /// Hand-written (running daemons lock bin/); the [DbContext]/[Migration] attributes
    /// normally generated into the Designer live here. Snapshot is updated to match.
    /// </summary>
    [DbContext(typeof(AppDbContext))]
    [Migration("20260902230000_AddSessionLaunchBlock")]
    public partial class AddSessionLaunchBlock : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "LaunchBlock",
                table: "AgentSessions",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LaunchBlock",
                table: "AgentSessions");
        }
    }
}
