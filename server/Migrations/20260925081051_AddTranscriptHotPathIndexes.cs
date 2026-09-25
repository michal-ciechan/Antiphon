using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Antiphon.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddTranscriptHotPathIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            CreateRetryableIndex(migrationBuilder,
                name: "IX_TranscriptEntries_AgentSessionId_Uuid",
                columns: "\"AgentSessionId\", \"Uuid\"",
                filter: "\"Uuid\" IS NOT NULL",
                include: " INCLUDE (\"Kind\")");

            CreateRetryableIndex(migrationBuilder,
                name: "IX_TranscriptEntries_End_AgentSessionId_Sequence",
                columns: "\"AgentSessionId\", \"Sequence\"",
                filter: "\"Kind\" IN ('TurnEnd', 'SessionRestartBoundary')\nOR (\"Kind\" = 'CompactBoundary' AND \"Text\" IS NOT NULL AND strpos(\"Text\", '(manual)') > 0)\nOR (\"Kind\" = 'UserPrompt' AND \"Text\" IS NOT NULL AND \"Text\" LIKE '[Request interrupted%')");

            CreateRetryableIndex(migrationBuilder,
                name: "IX_TranscriptEntries_End_AgentSessionId_Timestamp",
                columns: "\"AgentSessionId\", \"Timestamp\"",
                filter: "(\"Kind\" IN ('TurnEnd', 'SessionRestartBoundary')\nOR (\"Kind\" = 'CompactBoundary' AND \"Text\" IS NOT NULL AND strpos(\"Text\", '(manual)') > 0)\nOR (\"Kind\" = 'UserPrompt' AND \"Text\" IS NOT NULL AND \"Text\" LIKE '[Request interrupted%')) AND \"Timestamp\" IS NOT NULL");
        }

        private static void CreateRetryableIndex(MigrationBuilder migrationBuilder,
            string name, string columns, string filter, string include = "")
        {
            // PostgreSQL forbids concurrent DDL inside DO. Rename only an invalid index in
            // the conditional step, then drop it in a separate autocommit command. The fixed
            // cleanup name also survives interruption between rename and drop. All arguments
            // are this migration's historical literals, never runtime input or model metadata.
            migrationBuilder.Sql($"""
                DO $retry$
                BEGIN
                    IF to_regclass('"{name}_invalid"') IS NOT NULL AND NOT EXISTS (
                        SELECT 1 FROM pg_index
                        WHERE indexrelid = to_regclass('"{name}_invalid"')
                          AND indrelid = '"TranscriptEntries"'::regclass AND NOT indisvalid
                    ) THEN
                        RAISE EXCEPTION 'Unexpected relation at migration cleanup name: {name}_invalid';
                    END IF;
                    IF EXISTS (
                        SELECT 1 FROM pg_index
                        WHERE indexrelid = to_regclass('"{name}"')
                          AND indrelid = '"TranscriptEntries"'::regclass AND NOT indisvalid
                    ) THEN
                        ALTER INDEX "{name}" RENAME TO "{name}_invalid";
                    END IF;
                END
                $retry$;
                """, suppressTransaction: true);
            migrationBuilder.Sql($"DROP INDEX CONCURRENTLY IF EXISTS \"{name}_invalid\";",
                suppressTransaction: true);
            migrationBuilder.Sql($"""
                CREATE INDEX CONCURRENTLY IF NOT EXISTS "{name}"
                ON "TranscriptEntries" ({columns}){include} WHERE {filter};
                """, suppressTransaction: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var name in new[]
            {
                "IX_TranscriptEntries_AgentSessionId_Uuid",
                "IX_TranscriptEntries_End_AgentSessionId_Sequence",
                "IX_TranscriptEntries_End_AgentSessionId_Timestamp"
            })
            {
                migrationBuilder.Sql($"DROP INDEX CONCURRENTLY IF EXISTS \"{name}\";",
                    suppressTransaction: true);
                migrationBuilder.Sql($"DROP INDEX CONCURRENTLY IF EXISTS \"{name}_invalid\";",
                    suppressTransaction: true);
            }
        }
    }
}
