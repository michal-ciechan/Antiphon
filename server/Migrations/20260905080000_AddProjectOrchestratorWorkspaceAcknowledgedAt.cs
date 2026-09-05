using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Antiphon.Server.Migrations
{
    /// <summary>
    /// CARD-0251 S2: nullable Project.OrchestratorWorkspaceAcknowledgedAt. Hand-written
    /// (running daemons lock bin/); the [DbContext]/[Migration] attributes normally
    /// generated into the Designer live here. Snapshot is updated to match.
    /// </summary>
    [DbContext(typeof(AppDbContext))]
    [Migration("20260905080000_AddProjectOrchestratorWorkspaceAcknowledgedAt")]
    public partial class AddProjectOrchestratorWorkspaceAcknowledgedAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "OrchestratorWorkspaceAcknowledgedAt",
                table: "Projects",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "OrchestratorWorkspaceAcknowledgedAt",
                table: "Projects");
        }
    }
}
