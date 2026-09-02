using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Antiphon.Server.Migrations
{
    /// <summary>
    /// CARD-0039: rename Cards/CardRevisions.Priority to Importance, add Urgency/DueAt/UrgentSince,
    /// remap the old client-scale integers, and insert a ContentEdit revision on every open
    /// unarchived card so the history records the number the mapping consumed.
    /// </summary>
    public partial class AddCardImportanceUrgency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "Priority",
                table: "Cards",
                newName: "Importance");

            migrationBuilder.AlterColumn<int>(
                name: "Importance",
                table: "Cards",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "Urgency",
                table: "Cards",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "DueAt",
                table: "Cards",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "UrgentSince",
                table: "Cards",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.RenameColumn(
                name: "Priority",
                table: "CardRevisions",
                newName: "Importance");

            migrationBuilder.AddColumn<int>(
                name: "Urgency",
                table: "CardRevisions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DueAt",
                table: "CardRevisions",
                type: "timestamp with time zone",
                nullable: true);

            // Insert BEFORE the CASE remap so the reason text still reads the old integer.
            // All superseded columns stay null: the old number lives only in Reason.
            migrationBuilder.Sql(
                """
                INSERT INTO "CardRevisions" (
                    "Id", "CardId", "RevisionNumber", "Kind",
                    "Reason", "EditedBy", "CreatedAt")
                SELECT
                    gen_random_uuid(),
                    c."Id",
                    c."RevisionCount" + 1,
                    0,
                    'CARD-0039: importance derived from legacy priority ' || c."Importance"::text,
                    'migration',
                    NOW()
                FROM "Cards" c
                WHERE c."ArchivedAt" IS NULL
                  AND c."Status" NOT IN (3, 5);

                UPDATE "Cards" c
                SET "RevisionCount" = "RevisionCount" + 1
                WHERE c."ArchivedAt" IS NULL
                  AND c."Status" NOT IN (3, 5);
                """);

            migrationBuilder.Sql(
                """
                UPDATE "Cards"
                SET "Importance" = CASE "Importance"
                    WHEN 0 THEN 1
                    WHEN 1 THEN 2
                    WHEN 2 THEN 1
                    WHEN 3 THEN 0
                    WHEN 4 THEN 0
                    ELSE 1
                END;

                UPDATE "CardRevisions"
                SET "Importance" = CASE "Importance"
                    WHEN 0 THEN 1
                    WHEN 1 THEN 2
                    WHEN 2 THEN 1
                    WHEN 3 THEN 0
                    WHEN 4 THEN 0
                    ELSE 1
                END
                WHERE "Importance" IS NOT NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DueAt",
                table: "Cards");

            migrationBuilder.DropColumn(
                name: "Urgency",
                table: "Cards");

            migrationBuilder.DropColumn(
                name: "UrgentSince",
                table: "Cards");

            migrationBuilder.DropColumn(
                name: "DueAt",
                table: "CardRevisions");

            migrationBuilder.DropColumn(
                name: "Urgency",
                table: "CardRevisions");

            migrationBuilder.RenameColumn(
                name: "Importance",
                table: "Cards",
                newName: "Priority");

            migrationBuilder.AlterColumn<int>(
                name: "Priority",
                table: "Cards",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.RenameColumn(
                name: "Importance",
                table: "CardRevisions",
                newName: "Priority");
        }
    }
}
