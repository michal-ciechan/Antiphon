using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Antiphon.Server.Migrations
{
    /// <summary>
    /// CARD-0160: SessionBackend dimension on Agents (operator knob) and AgentSessions (snapshot
    /// stamped at launch). 0 = PtyHost — every pre-existing row stays on the only lane that
    /// existed. Hand-written (running daemons lock bin/); snapshot is updated to match.
    /// </summary>
    [DbContext(typeof(AppDbContext))]
    [Migration("20260823220000_AddSessionBackend")]
    public partial class AddSessionBackend : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "SessionBackend",
                table: "Agents",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "SessionBackend",
                table: "AgentSessions",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SessionBackend",
                table: "Agents");

            migrationBuilder.DropColumn(
                name: "SessionBackend",
                table: "AgentSessions");
        }
    }
}
