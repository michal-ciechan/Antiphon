using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Antiphon.Messaging.Service.Migrations
{
    /// <inheritdoc />
    public partial class JournalGatewayOutboundOperations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GatewayOutboundOperations",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    Channel = table.Column<string>(type: "text", nullable: false),
                    ConversationId = table.Column<string>(type: "text", nullable: true),
                    AnsweredMessageIdsJson = table.Column<string>(type: "text", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    AcceptedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ProviderMessageId = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GatewayOutboundOperations", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                LOCK TABLE "GatewayOutboundOperations" IN ACCESS EXCLUSIVE MODE;
                DO $guard$ BEGIN
                    IF EXISTS (SELECT FROM "GatewayOutboundOperations") THEN
                        RAISE EXCEPTION 'Rollback refused: GatewayOutboundOperations contains provider send evidence.';
                    END IF;
                END; $guard$;
                """);
            migrationBuilder.DropTable(
                name: "GatewayOutboundOperations");
        }
    }
}
