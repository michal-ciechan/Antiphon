using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Antiphon.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddCallerReportSettlementReceipt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("SET LOCAL lock_timeout = '5s';");

            migrationBuilder.AddColumn<DateTime>(
                name: "ReceivedReportCompletedAt",
                table: "AgentTasks",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "ReceivedReportPromptSequence",
                table: "AgentTasks",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ReceivedReportSessionId",
                table: "AgentTasks",
                type: "uuid",
                nullable: true);

            // The previous version recorded an authenticated parent read as LastPolledResultAt and
            // the DelegationNoteDigest of the report it returned. A read made after this settlement
            // of exactly this settlement's report is its receipt; carry it across the upgrade. The
            // digest is the C# normalisation (LF line endings, each line's trailing whitespace and
            // the whole text trimmed). A report this SQL normalises differently stays owed: an
            // extra wake, never a lost one.
            migrationBuilder.Sql("""
                UPDATE "AgentTasks"
                SET "ReceivedReportCompletedAt" = "CompletedAt", "ReceivedReportSessionId" = "ParentSessionId"
                WHERE "ParentSessionId" IS NOT NULL AND "CompletedAt" IS NOT NULL
                  AND "LastPolledResultAt" IS NOT NULL AND "LastPolledResultAt" >= "CompletedAt"
                  AND "LastPolledResultHash" = encode(sha256(convert_to(btrim(regexp_replace(
                      replace(replace(COALESCE(NULLIF("Result", ''), "FailureReason"), E'\r\n', E'\n'), E'\r', E'\n'),
                      E'[ \t\f]+(\n|$)', E'\\1', 'g'), E' \t\n\f'), 'UTF8')), 'hex');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException(
                "A caller's settlement receipt cannot be discarded by a downgrade.");
        }
    }
}
