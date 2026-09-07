using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Antiphon.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddDistillationDeadlines : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ExecutionDeadlineAt",
                table: "SessionQueuedMessages",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ExecutionTaskId",
                table: "SessionQueuedMessages",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AvailabilityAlias",
                table: "OutputDistillations",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AvailabilityKind",
                table: "OutputDistillations",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "AvailabilityObservedAt",
                table: "OutputDistillations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CleanupMs",
                table: "OutputDistillations",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeadlineAt",
                table: "OutputDistillations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DecisionAt",
                table: "OutputDistillations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DequeuedAt",
                table: "OutputDistillations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExpiryPhase",
                table: "OutputDistillations",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "QueueWaitMs",
                table: "OutputDistillations",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Reason",
                table: "OutputDistillations",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "RequestedAt",
                table: "OutputDistillations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "RunCreatedAt",
                table: "OutputDistillations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SpecialistWaitMs",
                table: "OutputDistillations",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ExecutionDeadlineAt",
                table: "AgentTasks",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ExecutionDeadlineAt",
                table: "SessionQueuedMessages");

            migrationBuilder.DropColumn(
                name: "ExecutionTaskId",
                table: "SessionQueuedMessages");

            migrationBuilder.DropColumn(
                name: "AvailabilityAlias",
                table: "OutputDistillations");

            migrationBuilder.DropColumn(
                name: "AvailabilityKind",
                table: "OutputDistillations");

            migrationBuilder.DropColumn(
                name: "AvailabilityObservedAt",
                table: "OutputDistillations");

            migrationBuilder.DropColumn(
                name: "CleanupMs",
                table: "OutputDistillations");

            migrationBuilder.DropColumn(
                name: "DeadlineAt",
                table: "OutputDistillations");

            migrationBuilder.DropColumn(
                name: "DecisionAt",
                table: "OutputDistillations");

            migrationBuilder.DropColumn(
                name: "DequeuedAt",
                table: "OutputDistillations");

            migrationBuilder.DropColumn(
                name: "ExpiryPhase",
                table: "OutputDistillations");

            migrationBuilder.DropColumn(
                name: "QueueWaitMs",
                table: "OutputDistillations");

            migrationBuilder.DropColumn(
                name: "Reason",
                table: "OutputDistillations");

            migrationBuilder.DropColumn(
                name: "RequestedAt",
                table: "OutputDistillations");

            migrationBuilder.DropColumn(
                name: "RunCreatedAt",
                table: "OutputDistillations");

            migrationBuilder.DropColumn(
                name: "SpecialistWaitMs",
                table: "OutputDistillations");

            migrationBuilder.DropColumn(
                name: "ExecutionDeadlineAt",
                table: "AgentTasks");
        }
    }
}
