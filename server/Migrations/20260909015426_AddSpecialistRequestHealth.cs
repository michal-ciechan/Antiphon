using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Antiphon.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddSpecialistRequestHealth : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SpecialistAttempts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    CandidateId = table.Column<Guid>(type: "uuid", nullable: false),
                    PhysicalAgentId = table.Column<Guid>(type: "uuid", nullable: false),
                    TaskId = table.Column<Guid>(type: "uuid", nullable: false),
                    Ordinal = table.Column<int>(type: "integer", nullable: false),
                    SessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    SessionStartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Fingerprint = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    CapabilityFingerprint = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DeadlineAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Outcome = table.Column<int>(type: "integer", nullable: true),
                    Reason = table.Column<string>(type: "character varying(800)", maxLength: 800, nullable: true),
                    Reading = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: true),
                    PromptSequence = table.Column<long>(type: "bigint", nullable: true),
                    ReportSequence = table.Column<long>(type: "bigint", nullable: true),
                    CostUsd = table.Column<decimal>(type: "numeric", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SpecialistAttempts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SpecialistRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AgentId = table.Column<Guid>(type: "uuid", nullable: false),
                    CheckedTaskId = table.Column<Guid>(type: "uuid", nullable: true),
                    CheckNumber = table.Column<int>(type: "integer", nullable: false),
                    Purpose = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Outcome = table.Column<int>(type: "integer", nullable: true),
                    ConfigurationRevision = table.Column<Guid>(type: "uuid", nullable: true),
                    QualificationCandidateId = table.Column<Guid>(type: "uuid", nullable: true),
                    QualificationAuthorization = table.Column<Guid>(type: "uuid", nullable: true),
                    StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DeadlineAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Title = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    Facts = table.Column<string>(type: "text", nullable: false),
                    WinnerAttemptId = table.Column<Guid>(type: "uuid", nullable: true),
                    Reading = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: true),
                    Reason = table.Column<string>(type: "character varying(800)", maxLength: 800, nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    HealthAppliedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CallerMessageId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SpecialistRequests", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "StandingSpecialistHealths",
                columns: table => new
                {
                    AgentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ActiveCandidateId = table.Column<Guid>(type: "uuid", nullable: true),
                    ActiveSince = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FirstFailureAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    StarvedSince = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UnavailableSince = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ResolvedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastValidCheckAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ConsecutiveFailedRequests = table.Column<int>(type: "integer", nullable: false),
                    LastRequestId = table.Column<Guid>(type: "uuid", nullable: true),
                    LastAttemptTaskId = table.Column<Guid>(type: "uuid", nullable: true),
                    Reason = table.Column<string>(type: "character varying(800)", maxLength: 800, nullable: true),
                    CandidateSummary = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StandingSpecialistHealths", x => x.AgentId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SpecialistAttempts_RequestId_Ordinal",
                table: "SpecialistAttempts",
                columns: new[] { "RequestId", "Ordinal" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SpecialistAttempts_TaskId",
                table: "SpecialistAttempts",
                column: "TaskId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SpecialistRequests_CheckedTaskId_CheckNumber",
                table: "SpecialistRequests",
                columns: new[] { "CheckedTaskId", "CheckNumber" },
                unique: true,
                filter: "\"CheckedTaskId\" IS NOT NULL AND \"Purpose\" = 0");

            migrationBuilder.CreateIndex(
                name: "IX_SpecialistRequests_QualificationCandidateId_QualificationAu~",
                table: "SpecialistRequests",
                columns: new[] { "QualificationCandidateId", "QualificationAuthorization" },
                unique: true,
                filter: "\"QualificationCandidateId\" IS NOT NULL AND \"Purpose\" = 1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SpecialistAttempts");

            migrationBuilder.DropTable(
                name: "SpecialistRequests");

            migrationBuilder.DropTable(
                name: "StandingSpecialistHealths");
        }
    }
}
