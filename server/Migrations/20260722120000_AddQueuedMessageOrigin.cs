using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Antiphon.Server.Migrations
{
    // Hand-written (no .Designer.cs) — same bin-lock reason as the two migrations before it.
    /// <inheritdoc />
    [DbContext(typeof(AppDbContext))]
    [Migration("20260722120000_AddQueuedMessageOrigin")]
    public partial class AddQueuedMessageOrigin : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Origin",
                table: "SessionQueuedMessages",
                type: "integer",
                nullable: false,
                defaultValue: 0); // pre-existing rows were all UI-enqueued

            migrationBuilder.AddColumn<string>(
                name: "ConversationKey",
                table: "SessionQueuedMessages",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Origin",
                table: "SessionQueuedMessages");

            migrationBuilder.DropColumn(
                name: "ConversationKey",
                table: "SessionQueuedMessages");
        }
    }
}
