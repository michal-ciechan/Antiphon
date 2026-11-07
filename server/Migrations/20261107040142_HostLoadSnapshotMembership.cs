using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Antiphon.Server.Migrations
{
    /// <inheritdoc />
    public partial class HostLoadSnapshotMembership : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "CreatedTransactionId",
                table: "HostLoadSamples",
                type: "bigint",
                nullable: false,
                defaultValueSql: "pg_current_xact_id()::text::bigint");

            migrationBuilder.AddColumn<long>(
                name: "CreatedTransactionId",
                table: "HostLoadHourlyRollups",
                type: "bigint",
                nullable: false,
                defaultValueSql: "pg_current_xact_id()::text::bigint");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException("Stable history page membership cannot be discarded by a downgrade.");
        }
    }
}
