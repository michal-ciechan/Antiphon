using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Antiphon.Server.Migrations
{
    /// <inheritdoc />
    public partial class ChannelInboundHoldIncidents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ContinuityHoldIncidentAt",
                table: "ChannelInbounds",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LivenessHoldIncidentAt",
                table: "ChannelInbounds",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "SuspensionHoldIncidentAt",
                table: "ChannelInbounds",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ContinuityHoldIncidentAt",
                table: "ChannelInbounds");

            migrationBuilder.DropColumn(
                name: "LivenessHoldIncidentAt",
                table: "ChannelInbounds");

            migrationBuilder.DropColumn(
                name: "SuspensionHoldIncidentAt",
                table: "ChannelInbounds");
        }
    }
}
