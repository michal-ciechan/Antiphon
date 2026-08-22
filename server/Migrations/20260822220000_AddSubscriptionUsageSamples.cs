using System;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Antiphon.Server.Migrations
{
    /// <summary>
    /// CARD-0143: append-only subscription-usage samples. Hand-written (running daemons lock
    /// bin/); the [DbContext]/[Migration] attributes normally generated into the Designer live
    /// here. Snapshot is updated to match.
    /// </summary>
    [DbContext(typeof(AppDbContext))]
    [Migration("20260822220000_AddSubscriptionUsageSamples")]
    public partial class AddSubscriptionUsageSamples : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SubscriptionUsageSamples",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Provider = table.Column<int>(type: "integer", nullable: false),
                    SubscriptionKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PlanLabel = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    RemainingPercent = table.Column<double>(type: "double precision", nullable: true),
                    ResetsAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ResetsAtRaw = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ObservedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AgentId = table.Column<Guid>(type: "uuid", nullable: true),
                    AgentSessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceCommand = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ParseStatus = table.Column<int>(type: "integer", nullable: false),
                    RawExcerpt = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubscriptionUsageSamples", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionUsageSamples_Provider_SubscriptionKey_ObservedAt",
                table: "SubscriptionUsageSamples",
                columns: new[] { "Provider", "SubscriptionKey", "ObservedAt" },
                descending: new[] { false, false, true });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SubscriptionUsageSamples");
        }
    }
}
