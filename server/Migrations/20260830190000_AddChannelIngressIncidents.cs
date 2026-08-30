using System;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Antiphon.Server.Migrations
{
    // Hand-written (no .Designer.cs): the running daemons lock bin/, so `dotnet ef migrations add`
    // couldn't build. Snapshot updated by hand.
    /// <inheritdoc />
    [DbContext(typeof(AppDbContext))]
    [Migration("20260830190000_AddChannelIngressIncidents")]
    public partial class AddChannelIngressIncidents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ChannelIngressIncidents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Provider = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ConversationId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    OriginalMessageId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Topic = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Partition = table.Column<int>(type: "integer", nullable: false),
                    Offset = table.Column<long>(type: "bigint", nullable: false),
                    FirstSeenAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DetectedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Acknowledged = table.Column<bool>(type: "boolean", nullable: false),
                    AcknowledgementError = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    AppHostHealth = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChannelIngressIncidents", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ChannelIngressIncidents_DetectedAt",
                table: "ChannelIngressIncidents",
                column: "DetectedAt");

            migrationBuilder.CreateIndex(
                name: "IX_ChannelIngressIncidents_Topic_Partition_Offset",
                table: "ChannelIngressIncidents",
                columns: new[] { "Topic", "Partition", "Offset" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "ChannelIngressIncidents");
        }
    }
}
