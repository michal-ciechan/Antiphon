using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Antiphon.Server.Migrations
{
    /// <inheritdoc />
    public partial class ExtendExpectationWatchdog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "AnswerDueAt",
                table: "ExpectationNudges",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "AnsweredSequence",
                table: "ExpectationNudges",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "AttemptStartedAt",
                table: "ExpectationNudges",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ConfigDigest",
                table: "ExpectationNudges",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "OperatorClaimExpiresAt",
                table: "ExpectationNudges",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "OperatorClaimToken",
                table: "ExpectationNudges",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "OperatorFirstDueAt",
                table: "ExpectationNudges",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "OperatorLastAttemptAt",
                table: "ExpectationNudges",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OperatorPageBody",
                table: "ExpectationNudges",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OperatorPageConversationId",
                table: "ExpectationNudges",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OperatorPageDigest",
                table: "ExpectationNudges",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OperatorPageProvider",
                table: "ExpectationNudges",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "OperatorPublicationOrdinal",
                table: "ExpectationNudges",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<long>(
                name: "ReceiptSequence",
                table: "ExpectationNudges",
                type: "bigint",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AnswerDueAt",
                table: "ExpectationNudges");

            migrationBuilder.DropColumn(
                name: "AnsweredSequence",
                table: "ExpectationNudges");

            migrationBuilder.DropColumn(
                name: "AttemptStartedAt",
                table: "ExpectationNudges");

            migrationBuilder.DropColumn(
                name: "ConfigDigest",
                table: "ExpectationNudges");

            migrationBuilder.DropColumn(
                name: "OperatorClaimExpiresAt",
                table: "ExpectationNudges");

            migrationBuilder.DropColumn(
                name: "OperatorClaimToken",
                table: "ExpectationNudges");

            migrationBuilder.DropColumn(
                name: "OperatorFirstDueAt",
                table: "ExpectationNudges");

            migrationBuilder.DropColumn(
                name: "OperatorLastAttemptAt",
                table: "ExpectationNudges");

            migrationBuilder.DropColumn(
                name: "OperatorPageBody",
                table: "ExpectationNudges");

            migrationBuilder.DropColumn(
                name: "OperatorPageConversationId",
                table: "ExpectationNudges");

            migrationBuilder.DropColumn(
                name: "OperatorPageDigest",
                table: "ExpectationNudges");

            migrationBuilder.DropColumn(
                name: "OperatorPageProvider",
                table: "ExpectationNudges");

            migrationBuilder.DropColumn(
                name: "OperatorPublicationOrdinal",
                table: "ExpectationNudges");

            migrationBuilder.DropColumn(
                name: "ReceiptSequence",
                table: "ExpectationNudges");
        }
    }
}
