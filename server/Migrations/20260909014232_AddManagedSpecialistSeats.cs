using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Antiphon.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddManagedSpecialistSeats : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CapabilityFingerprint",
                table: "StandingSpecialistCandidateStates",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MaxInputUtf8Bytes",
                table: "StandingSpecialistCandidateStates",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "StandingSpecialistOwnerId",
                table: "Agents",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "StandingSpecialistRole",
                table: "Agents",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Agents_StandingSpecialistOwnerId",
                table: "Agents",
                column: "StandingSpecialistOwnerId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Agents_StandingSpecialistOwnerId",
                table: "Agents");

            migrationBuilder.DropColumn(
                name: "CapabilityFingerprint",
                table: "StandingSpecialistCandidateStates");

            migrationBuilder.DropColumn(
                name: "MaxInputUtf8Bytes",
                table: "StandingSpecialistCandidateStates");

            migrationBuilder.DropColumn(
                name: "StandingSpecialistOwnerId",
                table: "Agents");

            migrationBuilder.DropColumn(
                name: "StandingSpecialistRole",
                table: "Agents");
        }
    }
}
