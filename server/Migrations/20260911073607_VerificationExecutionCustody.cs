using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Antiphon.Server.Migrations
{
    /// <inheritdoc />
    public partial class VerificationExecutionCustody : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "SourceLandingOperationId",
                table: "AgentTasks",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceLandingSha",
                table: "AgentTasks",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "VerificationBranchRemoved",
                table: "AgentTasks",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "VerificationCleanupResidue",
                table: "AgentTasks",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VerificationCleanupSealJson",
                table: "AgentTasks",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "VerificationCleanupStartedAt",
                table: "AgentTasks",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VerificationCreationJson",
                table: "AgentTasks",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "VerificationCustodyContractVersion",
                table: "AgentTasks",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "VerificationDirectoryRemoved",
                table: "AgentTasks",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<long>(
                name: "VerificationExecutionRevision",
                table: "AgentTasks",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<bool>(
                name: "VerificationRegistrationRemoved",
                table: "AgentTasks",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "VerificationExecutions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TaskId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceLandingOperationId = table.Column<Guid>(type: "uuid", nullable: false),
                    SessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    AcceptedStartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    BindingJson = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RunnerCallIntentAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    HostIdentityJson = table.Column<string>(type: "text", nullable: true),
                    ReceiptBytes = table.Column<byte[]>(type: "bytea", nullable: true),
                    ReceiptDigest = table.Column<string>(type: "text", nullable: true),
                    ReceiptImportedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CustodyReason = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VerificationExecutions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VerificationExecutions_AgentTaskLandings_SourceLandingOpera~",
                        column: x => x.SourceLandingOperationId,
                        principalTable: "AgentTaskLandings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_VerificationExecutions_AgentTasks_TaskId",
                        column: x => x.TaskId,
                        principalTable: "AgentTasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AgentTasks_SourceLandingOperationId",
                table: "AgentTasks",
                column: "SourceLandingOperationId");

            migrationBuilder.CreateIndex(
                name: "IX_VerificationExecutions_SourceLandingOperationId",
                table: "VerificationExecutions",
                column: "SourceLandingOperationId");

            migrationBuilder.CreateIndex(
                name: "IX_VerificationExecutions_TaskId_SessionId_AcceptedStartedAt",
                table: "VerificationExecutions",
                columns: new[] { "TaskId", "SessionId", "AcceptedStartedAt" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_AgentTasks_AgentTaskLandings_SourceLandingOperationId",
                table: "AgentTasks",
                column: "SourceLandingOperationId",
                principalTable: "AgentTaskLandings",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AgentTasks_AgentTaskLandings_SourceLandingOperationId",
                table: "AgentTasks");

            migrationBuilder.DropTable(
                name: "VerificationExecutions");

            migrationBuilder.DropIndex(
                name: "IX_AgentTasks_SourceLandingOperationId",
                table: "AgentTasks");

            migrationBuilder.DropColumn(
                name: "SourceLandingOperationId",
                table: "AgentTasks");

            migrationBuilder.DropColumn(
                name: "SourceLandingSha",
                table: "AgentTasks");

            migrationBuilder.DropColumn(
                name: "VerificationBranchRemoved",
                table: "AgentTasks");

            migrationBuilder.DropColumn(
                name: "VerificationCleanupResidue",
                table: "AgentTasks");

            migrationBuilder.DropColumn(
                name: "VerificationCleanupSealJson",
                table: "AgentTasks");

            migrationBuilder.DropColumn(
                name: "VerificationCleanupStartedAt",
                table: "AgentTasks");

            migrationBuilder.DropColumn(
                name: "VerificationCreationJson",
                table: "AgentTasks");

            migrationBuilder.DropColumn(
                name: "VerificationCustodyContractVersion",
                table: "AgentTasks");

            migrationBuilder.DropColumn(
                name: "VerificationDirectoryRemoved",
                table: "AgentTasks");

            migrationBuilder.DropColumn(
                name: "VerificationExecutionRevision",
                table: "AgentTasks");

            migrationBuilder.DropColumn(
                name: "VerificationRegistrationRemoved",
                table: "AgentTasks");
        }
    }
}
