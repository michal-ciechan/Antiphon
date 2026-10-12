using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Antiphon.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddCardCleanupScheduling : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "AcceptedAt",
                table: "WorktreeResidueRunCandidates",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ActionKey",
                table: "WorktreeResidueRunCandidates",
                type: "character varying(400)",
                maxLength: 400,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CardId",
                table: "WorktreeResidueRunCandidates",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DoneRevisionId",
                table: "WorktreeResidueRunCandidates",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "EndpointId",
                table: "WorktreeResidueRunCandidates",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EndpointIdentity",
                table: "WorktreeResidueRunCandidates",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "EndpointState",
                table: "WorktreeResidueRunCandidates",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "NextAttemptAt",
                table: "WorktreeResidueRunCandidates",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "OperationId",
                table: "WorktreeResidueRunCandidates",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RequestDigest",
                table: "WorktreeResidueRunCandidates",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "Retryable",
                table: "WorktreeResidueRunCandidates",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RunnerId",
                table: "WorktreeResidueRunCandidates",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RunnerStoreId",
                table: "WorktreeResidueRunCandidates",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "TargetId",
                table: "WorktreeResidueRunCandidates",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TaskAttempt",
                table: "WorktreeResidueRunCandidates",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EvidenceDigest",
                table: "WorktreeResidueCandidateCursors",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EvidenceDigest",
                table: "CardWorktreeCleanupEndpoints",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorktreeResidueRunCandidates_RunId_ActionKey",
                table: "WorktreeResidueRunCandidates",
                columns: new[] { "RunId", "ActionKey" },
                unique: true,
                filter: "\"ActionKey\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_WorktreeResidueRunCandidates_RunId_ActionKey",
                table: "WorktreeResidueRunCandidates");

            migrationBuilder.DropColumn(
                name: "AcceptedAt",
                table: "WorktreeResidueRunCandidates");

            migrationBuilder.DropColumn(
                name: "ActionKey",
                table: "WorktreeResidueRunCandidates");

            migrationBuilder.DropColumn(
                name: "CardId",
                table: "WorktreeResidueRunCandidates");

            migrationBuilder.DropColumn(
                name: "DoneRevisionId",
                table: "WorktreeResidueRunCandidates");

            migrationBuilder.DropColumn(
                name: "EndpointId",
                table: "WorktreeResidueRunCandidates");

            migrationBuilder.DropColumn(
                name: "EndpointIdentity",
                table: "WorktreeResidueRunCandidates");

            migrationBuilder.DropColumn(
                name: "EndpointState",
                table: "WorktreeResidueRunCandidates");

            migrationBuilder.DropColumn(
                name: "NextAttemptAt",
                table: "WorktreeResidueRunCandidates");

            migrationBuilder.DropColumn(
                name: "OperationId",
                table: "WorktreeResidueRunCandidates");

            migrationBuilder.DropColumn(
                name: "RequestDigest",
                table: "WorktreeResidueRunCandidates");

            migrationBuilder.DropColumn(
                name: "Retryable",
                table: "WorktreeResidueRunCandidates");

            migrationBuilder.DropColumn(
                name: "RunnerId",
                table: "WorktreeResidueRunCandidates");

            migrationBuilder.DropColumn(
                name: "RunnerStoreId",
                table: "WorktreeResidueRunCandidates");

            migrationBuilder.DropColumn(
                name: "TargetId",
                table: "WorktreeResidueRunCandidates");

            migrationBuilder.DropColumn(
                name: "TaskAttempt",
                table: "WorktreeResidueRunCandidates");

            migrationBuilder.DropColumn(
                name: "EvidenceDigest",
                table: "WorktreeResidueCandidateCursors");

            migrationBuilder.DropColumn(
                name: "EvidenceDigest",
                table: "CardWorktreeCleanupEndpoints");
        }
    }
}
