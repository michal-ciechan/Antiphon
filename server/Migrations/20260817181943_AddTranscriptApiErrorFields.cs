using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Antiphon.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddTranscriptApiErrorFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ApiErrorClass",
                table: "TranscriptEntries",
                type: "character varying(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ApiErrorStatus",
                table: "TranscriptEntries",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsApiError",
                table: "TranscriptEntries",
                type: "boolean",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ApiErrorClass",
                table: "TranscriptEntries");

            migrationBuilder.DropColumn(
                name: "ApiErrorStatus",
                table: "TranscriptEntries");

            migrationBuilder.DropColumn(
                name: "IsApiError",
                table: "TranscriptEntries");
        }
    }
}
