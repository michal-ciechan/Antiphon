using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Antiphon.Server.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260928073000_AddHostBudgets")]
public sealed class AddHostBudgets : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.CreateTable(
        name: "HostBudgets",
        columns: table => new
        {
            HostId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
            MaxInFlight = table.Column<int>(type: "integer", nullable: true),
            Reason = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
            UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
            Revision = table.Column<int>(type: "integer", nullable: false)
        },
        constraints: table => table.PrimaryKey("PK_HostBudgets", x => x.HostId));

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.DropTable("HostBudgets");
}
