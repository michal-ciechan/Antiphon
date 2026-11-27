using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Antiphon.Server.Migrations
{
    /// <inheritdoc />
    public partial class TrackChannelProviderAnswersAndOperatorNotifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ReplyClockId",
                table: "ChannelInbounds",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "ReplyClockTimestamp",
                table: "ChannelInbounds",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "OperatorToastClaimedAt",
                table: "AgentIncidents",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ChannelProviderAnswers",
                columns: table => new
                {
                    Provider = table.Column<string>(type: "text", nullable: false),
                    ConversationId = table.Column<string>(type: "text", nullable: false),
                    NativeMessageId = table.Column<string>(type: "text", nullable: false),
                    AnsweredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChannelProviderAnswers", x => new { x.Provider, x.ConversationId, x.NativeMessageId });
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                LOCK TABLE "AgentIncidents", "ChannelInbounds", "ChannelProviderAnswers" IN ACCESS EXCLUSIVE MODE;
                DO $guard$ BEGIN
                    IF EXISTS (SELECT FROM "ChannelProviderAnswers")
                        OR EXISTS (SELECT FROM "AgentIncidents" WHERE "OperatorToastClaimedAt" IS NOT NULL)
                        OR EXISTS (SELECT FROM "ChannelInbounds" WHERE "ReplyClockId" IS NOT NULL OR "ReplyClockTimestamp" IS NOT NULL) THEN
                        RAISE EXCEPTION 'Rollback refused: channel acceptance, deadline or operator notification evidence exists.';
                    END IF;
                END; $guard$;
                """);
            migrationBuilder.DropTable(
                name: "ChannelProviderAnswers");

            migrationBuilder.DropColumn(
                name: "ReplyClockId",
                table: "ChannelInbounds");

            migrationBuilder.DropColumn(
                name: "ReplyClockTimestamp",
                table: "ChannelInbounds");

            migrationBuilder.DropColumn(
                name: "OperatorToastClaimedAt",
                table: "AgentIncidents");
        }
    }
}
