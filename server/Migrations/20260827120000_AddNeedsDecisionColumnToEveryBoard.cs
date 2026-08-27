using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Antiphon.Server.Migrations
{
    /// <summary>
    /// CARD-0122: every existing board gains the durable, non-active Needs decision column.
    /// The enum slot remains integer 4, so this is data-only and has no model snapshot change.
    /// </summary>
    [DbContext(typeof(AppDbContext))]
    [Migration("20260827120000_AddNeedsDecisionColumnToEveryBoard")]
    public partial class AddNeedsDecisionColumnToEveryBoard : Migration
    {
        public const string UpSql =
            """
            INSERT INTO "BoardColumns" (
                "Id", "BoardId", "StateKey", "Name", "ColumnOrder", "CardStatus",
                "IsActive", "IsTerminal", "CreatedAt", "UpdatedAt")
            SELECT gen_random_uuid(), b."Id", 'needs-decision', 'Needs decision',
                COALESCE((SELECT MAX(c."ColumnOrder") FROM "BoardColumns" c WHERE c."BoardId" = b."Id"), -1) + 1,
                4, FALSE, FALSE, now(), now()
            FROM "Boards" b
            WHERE NOT EXISTS (
                SELECT 1 FROM "BoardColumns" c
                WHERE c."BoardId" = b."Id" AND c."CardStatus" = 4);
            """;

        public const string DownSql =
            """
            DO $$
            BEGIN
              IF EXISTS (
                SELECT 1
                FROM "Cards" c
                INNER JOIN "BoardColumns" bc ON bc."Id" = c."BoardColumnId"
                WHERE bc."CardStatus" = 4) THEN
                RAISE EXCEPTION 'Cannot remove Needs decision columns while cards reference them.';
              END IF;

              DELETE FROM "BoardColumns" WHERE "CardStatus" = 4;
            END $$;
            """;

        protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql(UpSql);

        protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql(DownSql);
    }
}
