using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Antiphon.Server.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260925120000_EnablePgStatStatements")]
public sealed class EnablePgStatStatements : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // CREATE EXTENSION succeeds even when the library is not preloaded, as in test and E2E.
        migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS pg_stat_statements;");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // The extension may predate this migration. Do not remove operator-owned statistics.
    }
}
