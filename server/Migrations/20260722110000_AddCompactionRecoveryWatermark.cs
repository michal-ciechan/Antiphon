using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Antiphon.Server.Migrations
{
    // Hand-written (no .Designer.cs) for the same reason as AddAgentSystemPromptAppend: the
    // running dev server locks bin/, so `dotnet ef migrations add` couldn't build. Snapshot
    // updated by hand; verified by an empty-diff check when the server is next stopped.
    /// <inheritdoc />
    [DbContext(typeof(AppDbContext))]
    [Migration("20260722110000_AddCompactionRecoveryWatermark")]
    public partial class AddCompactionRecoveryWatermark : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "CompactionRecoveryWatermark",
                table: "AgentSessions",
                type: "bigint",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CompactionRecoveryWatermark",
                table: "AgentSessions");
        }
    }
}
