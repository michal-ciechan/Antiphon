using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Antiphon.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddHerdrLabelFollow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "HerdrLabelFollowSequence",
                table: "Agents",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<Guid>(
                name: "HerdrLabelFollowSessionId",
                table: "Agents",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "HerdrLabelFollowStartedAt",
                table: "Agents",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "HerdrPlacementEditToken",
                table: "Agents",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "HerdrLabelFollowSequence",
                table: "Agents");

            migrationBuilder.DropColumn(
                name: "HerdrLabelFollowSessionId",
                table: "Agents");

            migrationBuilder.DropColumn(
                name: "HerdrLabelFollowStartedAt",
                table: "Agents");

            migrationBuilder.DropColumn(
                name: "HerdrPlacementEditToken",
                table: "Agents");
        }
    }
}
