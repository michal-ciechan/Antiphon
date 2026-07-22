using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Antiphon.Server.Migrations
{
    // Hand-written (no .Designer.cs): the running dev server locks bin/, so `dotnet ef migrations
    // add` couldn't build. The [DbContext]/[Migration] attributes normally generated into the
    // Designer live here instead; the snapshot was updated by hand and is verified by an
    // empty-diff `dotnet ef migrations add` check whenever the server is next stopped.
    /// <inheritdoc />
    [DbContext(typeof(AppDbContext))]
    [Migration("20260722100000_AddAgentSystemPromptAppend")]
    public partial class AddAgentSystemPromptAppend : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SystemPromptAppend",
                table: "Agents",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SystemPromptAppend",
                table: "Agents");
        }
    }
}
