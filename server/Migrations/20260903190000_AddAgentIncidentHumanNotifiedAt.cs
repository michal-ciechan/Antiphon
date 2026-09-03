using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Antiphon.Server.Migrations
{
    /// <summary>
    /// CARD-0338 S3: stamp when the digest pager notified a human about an incident.
    /// Backfill existing rows with CreatedAt so a deploy never pages history.
    /// Hand-written (running daemons lock bin/); the [DbContext]/[Migration] attributes
    /// normally generated into the Designer live here. Snapshot is updated to match.
    /// </summary>
    [DbContext(typeof(AppDbContext))]
    [Migration("20260903190000_AddAgentIncidentHumanNotifiedAt")]
    public partial class AddAgentIncidentHumanNotifiedAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "HumanNotifiedAt",
                table: "AgentIncidents",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.Sql(
                """UPDATE "AgentIncidents" SET "HumanNotifiedAt" = "CreatedAt" WHERE "HumanNotifiedAt" IS NULL;""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "HumanNotifiedAt",
                table: "AgentIncidents");
        }
    }
}
