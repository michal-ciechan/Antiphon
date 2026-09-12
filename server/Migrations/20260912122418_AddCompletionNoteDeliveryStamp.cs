using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Antiphon.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddCompletionNoteDeliveryStamp : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CompletionNoteDigest",
                table: "AgentTasks",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CompletionNoteQueuedAt",
                table: "AgentTasks",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE "AgentTasks" t
                SET "CompletionNoteQueuedAt" = s.queued_at,
                    "CompletionNoteDigest" = s.digest
                FROM (
                    SELECT DISTINCT ON (m."SourceTaskId")
                        m."SourceTaskId" AS task_id,
                        m."CreatedAt" AS queued_at,
                        m."ContentDigest" AS digest
                    FROM "SessionQueuedMessages" m
                    WHERE m."Origin" = 3
                      AND m."SourceLandNotificationId" IS NULL
                      AND m."SourceTaskId" IS NOT NULL
                      AND m."Status" <> 2
                    ORDER BY m."SourceTaskId", m."CreatedAt" DESC
                ) s
                WHERE t."Id" = s.task_id
                  AND t."CompletionNoteQueuedAt" IS NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CompletionNoteDigest",
                table: "AgentTasks");

            migrationBuilder.DropColumn(
                name: "CompletionNoteQueuedAt",
                table: "AgentTasks");
        }
    }
}
