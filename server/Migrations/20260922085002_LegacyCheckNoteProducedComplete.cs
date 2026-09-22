using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Antiphon.Server.Migrations
{
    /// <inheritdoc />
    public partial class LegacyCheckNoteProducedComplete : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddCheckConstraint(
                name: "CK_LegacyCheckNotePublications_ProducedComplete",
                table: "LegacyCheckNotePublications",
                sql: "\"State\" <> 1 OR (\"Body\" IS NOT NULL AND length(btrim(\"Body\")) > 0 AND \"ContentDigest\" IS NOT NULL AND \"ProducedAt\" IS NOT NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_LegacyCheckNotePublications_ProducedComplete",
                table: "LegacyCheckNotePublications");
        }
    }
}
