using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Antiphon.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddVerificationProfile : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CommissionedRound",
                table: "StageOutcomes",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "OrdinaryScopeCompleted",
                table: "StageOutcomes",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "VerificationProfileVersion",
                table: "StageOutcomes",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CodeVerificationPolicy",
                table: "Cards",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ReviewVerificationPolicy",
                table: "Cards",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "CodeVerificationPolicy",
                table: "CardRevisions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ReviewVerificationPolicy",
                table: "CardRevisions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "RequiresFinalVerificationReview",
                table: "AgentTasks",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "VerificationAdmissionJson",
                table: "AgentTasks",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "VerificationBaselineOutcomeId",
                table: "AgentTasks",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "VerificationProfileVersion",
                table: "AgentTasks",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "VerificationRound",
                table: "AgentTasks",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "VerificationSubjectTaskId",
                table: "AgentTasks",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CompletionDeliveryJson",
                table: "AgentTaskLandNotifications",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CompletionSnapshotJson",
                table: "AgentTaskLandNotifications",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CommissionedRound",
                table: "StageOutcomes");

            migrationBuilder.DropColumn(
                name: "OrdinaryScopeCompleted",
                table: "StageOutcomes");

            migrationBuilder.DropColumn(
                name: "VerificationProfileVersion",
                table: "StageOutcomes");

            migrationBuilder.DropColumn(
                name: "CodeVerificationPolicy",
                table: "Cards");

            migrationBuilder.DropColumn(
                name: "ReviewVerificationPolicy",
                table: "Cards");

            migrationBuilder.DropColumn(
                name: "CodeVerificationPolicy",
                table: "CardRevisions");

            migrationBuilder.DropColumn(
                name: "ReviewVerificationPolicy",
                table: "CardRevisions");

            migrationBuilder.DropColumn(
                name: "RequiresFinalVerificationReview",
                table: "AgentTasks");

            migrationBuilder.DropColumn(
                name: "VerificationAdmissionJson",
                table: "AgentTasks");

            migrationBuilder.DropColumn(
                name: "VerificationBaselineOutcomeId",
                table: "AgentTasks");

            migrationBuilder.DropColumn(
                name: "VerificationProfileVersion",
                table: "AgentTasks");

            migrationBuilder.DropColumn(
                name: "VerificationRound",
                table: "AgentTasks");

            migrationBuilder.DropColumn(
                name: "VerificationSubjectTaskId",
                table: "AgentTasks");

            migrationBuilder.DropColumn(
                name: "CompletionDeliveryJson",
                table: "AgentTaskLandNotifications");

            migrationBuilder.DropColumn(
                name: "CompletionSnapshotJson",
                table: "AgentTaskLandNotifications");
        }
    }
}
