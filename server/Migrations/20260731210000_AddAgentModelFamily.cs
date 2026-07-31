using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Antiphon.Server.Migrations
{
    // Hand-written (no .Designer.cs) — same bin-lock reason as the migrations before it.
    /// <inheritdoc />
    [DbContext(typeof(AppDbContext))]
    [Migration("20260731210000_AddAgentModelFamily")]
    public partial class AddAgentModelFamily : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 0 = Opus — the default; existing agents backfill to it.
            migrationBuilder.AddColumn<int>(
                name: "ModelFamily",
                table: "Agents",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "ModelFamily", table: "Agents");
        }
    }
}
