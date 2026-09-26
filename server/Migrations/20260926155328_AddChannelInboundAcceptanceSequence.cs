using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Antiphon.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddChannelInboundAcceptanceSequence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ChannelInbounds_AgentId_QueueMessageId_AcceptedAt",
                table: "ChannelInbounds");

            migrationBuilder.AddColumn<long>(
                name: "AcceptanceSequence",
                table: "ChannelInbounds",
                type: "bigint",
                nullable: false,
                defaultValue: 0L)
                .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn);

            migrationBuilder.CreateIndex(
                name: "IX_ChannelInbounds_AgentId_QueueMessageId_AcceptanceSequence",
                table: "ChannelInbounds",
                columns: new[] { "AgentId", "QueueMessageId", "AcceptanceSequence" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ChannelInbounds_AgentId_QueueMessageId_AcceptanceSequence",
                table: "ChannelInbounds");

            migrationBuilder.DropColumn(
                name: "AcceptanceSequence",
                table: "ChannelInbounds");

            migrationBuilder.CreateIndex(
                name: "IX_ChannelInbounds_AgentId_QueueMessageId_AcceptedAt",
                table: "ChannelInbounds",
                columns: new[] { "AgentId", "QueueMessageId", "AcceptedAt" });
        }
    }
}
