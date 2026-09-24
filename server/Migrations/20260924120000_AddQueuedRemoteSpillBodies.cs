using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Antiphon.Server.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260924120000_AddQueuedRemoteSpillBodies")]
public sealed class AddQueuedRemoteSpillBodies : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>("RemoteSpillBody", "SessionQueuedMessages",
            type: "text", nullable: true);
        migrationBuilder.AddColumn<string>("RemoteSpillRelativePath", "SessionQueuedMessages",
            type: "character varying(160)", maxLength: 160, nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn("RemoteSpillBody", "SessionQueuedMessages");
        migrationBuilder.DropColumn("RemoteSpillRelativePath", "SessionQueuedMessages");
    }
}
