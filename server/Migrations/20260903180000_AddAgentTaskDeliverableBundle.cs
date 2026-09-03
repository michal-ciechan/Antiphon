using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Antiphon.Server.Migrations
{
    /// <summary>
    /// CARD-0337 S1: five columns on AgentTasks for the settlement document bundle.
    /// Hand-written (running daemons lock bin/); the [DbContext]/[Migration] attributes
    /// normally generated into the Designer live here. Snapshot is updated to match.
    /// </summary>
    [DbContext(typeof(AppDbContext))]
    [Migration("20260903180000_AddAgentTaskDeliverableBundle")]
    public partial class AddAgentTaskDeliverableBundle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DeliverableBundleDir",
                table: "AgentTasks",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeliverableDeliveredAt",
                table: "AgentTasks",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DeliverableFileCount",
                table: "AgentTasks",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "DeliverablePdfPath",
                table: "AgentTasks",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeliverableRenderError",
                table: "AgentTasks",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DeliverableBundleDir",
                table: "AgentTasks");

            migrationBuilder.DropColumn(
                name: "DeliverableDeliveredAt",
                table: "AgentTasks");

            migrationBuilder.DropColumn(
                name: "DeliverableFileCount",
                table: "AgentTasks");

            migrationBuilder.DropColumn(
                name: "DeliverablePdfPath",
                table: "AgentTasks");

            migrationBuilder.DropColumn(
                name: "DeliverableRenderError",
                table: "AgentTasks");
        }
    }
}
