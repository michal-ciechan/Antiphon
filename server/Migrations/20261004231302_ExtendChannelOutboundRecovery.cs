using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Antiphon.Server.Migrations
{
    /// <inheritdoc />
    public partial class ExtendChannelOutboundRecovery : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ChannelReplyDiscoveryClosedAt",
                table: "SessionQueuedMessages",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CaptureJson",
                table: "ChannelOutboundDeliveries",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "FailureEpisode",
                table: "ChannelOutboundDeliveries",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "FailureReportedEpisode",
                table: "ChannelOutboundDeliveries",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "MetadataAppliedAt",
                table: "ChannelOutboundDeliveries",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "NextAttemptAt",
                table: "ChannelOutboundDeliveries",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PreparationAttempts",
                table: "ChannelOutboundDeliveries",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "PreparationDeadlineAt",
                table: "ChannelOutboundDeliveries",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PublicationAttemptBudgetBase",
                table: "ChannelOutboundDeliveries",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<long>(
                name: "ReservedThroughSequence",
                table: "ChannelOutboundDeliveries",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RootDeliveryId",
                table: "ChannelOutboundDeliveries",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "TailClosedAt",
                table: "ChannelOutboundDeliveries",
                type: "timestamp with time zone",
                nullable: true);

            // Legacy acceptance already applied its projections. Do not schedule it as fresh
            // repair work, or invent a publication timestamp for incomplete historical data.
            migrationBuilder.Sql("""
                UPDATE "ChannelOutboundDeliveries"
                SET "MetadataAppliedAt" = COALESCE("PublishedAt", "CreatedAt")
                WHERE "State" = 4
                """);

            migrationBuilder.CreateIndex(
                name: "IX_ChannelOutboundDeliveries_MetadataRepair",
                table: "ChannelOutboundDeliveries",
                columns: new[] { "PublishedAt", "Id" },
                filter: "\"State\" = 4 AND \"MetadataAppliedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ChannelOutboundDeliveries_RootIdentity",
                table: "ChannelOutboundDeliveries",
                columns: new[] { "SourceSessionId", "PromptSequence", "ChannelId" },
                unique: true,
                filter: "\"CaptureJson\" IS NOT NULL AND \"RootDeliveryId\" IS NULL AND \"SendKind\" IN ('main', 'machine')");

            migrationBuilder.CreateIndex(
                name: "IX_ChannelOutboundDeliveries_RootLookup",
                table: "ChannelOutboundDeliveries",
                columns: new[] { "SourceSessionId", "PromptSequence", "ChannelId" });

            migrationBuilder.CreateIndex(
                name: "IX_ChannelOutboundDeliveries_State_NextAttemptAt",
                table: "ChannelOutboundDeliveries",
                columns: new[] { "State", "NextAttemptAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ChannelOutboundDeliveries_TailStart",
                table: "ChannelOutboundDeliveries",
                columns: new[] { "RootDeliveryId", "FirstTextSequence" },
                unique: true,
                filter: "\"RootDeliveryId\" IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_ChannelOutboundDeliveries_ChannelOutboundDeliveries_RootDel~",
                table: "ChannelOutboundDeliveries",
                column: "RootDeliveryId",
                principalTable: "ChannelOutboundDeliveries",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ChannelOutboundDeliveries_ChannelOutboundDeliveries_RootDel~",
                table: "ChannelOutboundDeliveries");

            migrationBuilder.DropIndex(
                name: "IX_ChannelOutboundDeliveries_MetadataRepair",
                table: "ChannelOutboundDeliveries");

            migrationBuilder.DropIndex(
                name: "IX_ChannelOutboundDeliveries_RootIdentity",
                table: "ChannelOutboundDeliveries");

            migrationBuilder.DropIndex(
                name: "IX_ChannelOutboundDeliveries_RootLookup",
                table: "ChannelOutboundDeliveries");

            migrationBuilder.DropIndex(
                name: "IX_ChannelOutboundDeliveries_State_NextAttemptAt",
                table: "ChannelOutboundDeliveries");

            migrationBuilder.DropIndex(
                name: "IX_ChannelOutboundDeliveries_TailStart",
                table: "ChannelOutboundDeliveries");

            migrationBuilder.DropColumn(
                name: "ChannelReplyDiscoveryClosedAt",
                table: "SessionQueuedMessages");

            migrationBuilder.DropColumn(
                name: "CaptureJson",
                table: "ChannelOutboundDeliveries");

            migrationBuilder.DropColumn(
                name: "FailureEpisode",
                table: "ChannelOutboundDeliveries");

            migrationBuilder.DropColumn(
                name: "FailureReportedEpisode",
                table: "ChannelOutboundDeliveries");

            migrationBuilder.DropColumn(
                name: "MetadataAppliedAt",
                table: "ChannelOutboundDeliveries");

            migrationBuilder.DropColumn(
                name: "NextAttemptAt",
                table: "ChannelOutboundDeliveries");

            migrationBuilder.DropColumn(
                name: "PreparationAttempts",
                table: "ChannelOutboundDeliveries");

            migrationBuilder.DropColumn(
                name: "PreparationDeadlineAt",
                table: "ChannelOutboundDeliveries");

            migrationBuilder.DropColumn(
                name: "PublicationAttemptBudgetBase",
                table: "ChannelOutboundDeliveries");

            migrationBuilder.DropColumn(
                name: "ReservedThroughSequence",
                table: "ChannelOutboundDeliveries");

            migrationBuilder.DropColumn(
                name: "RootDeliveryId",
                table: "ChannelOutboundDeliveries");

            migrationBuilder.DropColumn(
                name: "TailClosedAt",
                table: "ChannelOutboundDeliveries");
        }
    }
}
