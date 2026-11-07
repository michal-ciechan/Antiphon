using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Antiphon.Server.Migrations
{
    /// <inheritdoc />
    public partial class HostLoadRetentionCatchup : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "HostLoadRetentionStates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false),
                    Backlog = table.Column<bool>(type: "boolean", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HostLoadRetentionStates", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_HostLoadSamples_DerivedAge",
                table: "HostLoadSamples",
                columns: new[] { "SampledAt", "Id" },
                filter: "\"DerivedAt\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_HostLoadHourlyRollups_Age",
                table: "HostLoadHourlyRollups",
                columns: new[] { "HourUtc", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException("Retention catch-up state cannot be discarded by a downgrade.");
        }
    }
}
