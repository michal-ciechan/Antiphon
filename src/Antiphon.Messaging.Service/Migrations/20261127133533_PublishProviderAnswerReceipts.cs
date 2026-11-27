using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Antiphon.Messaging.Service.Migrations
{
    /// <inheritdoc />
    public partial class PublishProviderAnswerReceipts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "PublishedAt",
                table: "InboxAnswerReceipts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_InboxAnswerReceipts_PublishedAt",
                table: "InboxAnswerReceipts",
                column: "PublishedAt",
                filter: "\"PublishedAt\" IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                LOCK TABLE "InboxAnswerReceipts" IN ACCESS EXCLUSIVE MODE;
                DO $guard$ BEGIN
                    IF EXISTS (SELECT FROM "InboxAnswerReceipts" WHERE "PublishedAt" IS NOT NULL) THEN
                        RAISE EXCEPTION 'Rollback refused: InboxAnswerReceipts contains publication evidence.';
                    END IF;
                END; $guard$;
                """);
            migrationBuilder.DropIndex(
                name: "IX_InboxAnswerReceipts_PublishedAt",
                table: "InboxAnswerReceipts");

            migrationBuilder.DropColumn(
                name: "PublishedAt",
                table: "InboxAnswerReceipts");
        }
    }
}
