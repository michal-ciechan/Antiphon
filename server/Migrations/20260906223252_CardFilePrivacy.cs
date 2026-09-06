using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Antiphon.Server.Migrations
{
    /// <inheritdoc />
    public partial class CardFilePrivacy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RepositoryVisibility",
                table: "Projects",
                type: "text",
                nullable: false,
                defaultValue: "Unknown");

            migrationBuilder.AddColumn<string>(
                name: "CardFileVisibility",
                table: "Cards",
                type: "text",
                nullable: false,
                defaultValue: "Inherit");

            migrationBuilder.AddColumn<string>(
                name: "PrivateNotes",
                table: "Cards",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "CardFileVisibility",
                table: "CardRevisions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PrivateNotes",
                table: "CardRevisions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CardFilesDirectorySlug",
                table: "Boards",
                type: "character varying(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CardFilesRepositoryPath",
                table: "Boards",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "SyncCardFiles",
                table: "Boards",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RepositoryVisibility",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "CardFileVisibility",
                table: "Cards");

            migrationBuilder.DropColumn(
                name: "PrivateNotes",
                table: "Cards");

            migrationBuilder.DropColumn(
                name: "CardFileVisibility",
                table: "CardRevisions");

            migrationBuilder.DropColumn(
                name: "PrivateNotes",
                table: "CardRevisions");

            migrationBuilder.DropColumn(
                name: "CardFilesDirectorySlug",
                table: "Boards");

            migrationBuilder.DropColumn(
                name: "CardFilesRepositoryPath",
                table: "Boards");

            migrationBuilder.DropColumn(
                name: "SyncCardFiles",
                table: "Boards");
        }
    }
}
