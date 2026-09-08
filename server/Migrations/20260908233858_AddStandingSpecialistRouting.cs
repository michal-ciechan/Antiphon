using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Antiphon.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddStandingSpecialistRouting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "StandingSpecialistCandidateStates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AgentId = table.Column<Guid>(type: "uuid", nullable: false),
                    AgentKind = table.Column<int>(type: "integer", nullable: false),
                    ModelLevel = table.Column<int>(type: "integer", nullable: false),
                    PhysicalAgentId = table.Column<Guid>(type: "uuid", nullable: true),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    ModelAlias = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Reason = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    DeclaredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UnprovisionedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastAdmissionRefusedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    NextEligibleAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SessionId = table.Column<Guid>(type: "uuid", nullable: true),
                    SessionStartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ProfileRevisionId = table.Column<Guid>(type: "uuid", nullable: true),
                    Fingerprint = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    QualificationEvidenceJson = table.Column<string>(type: "character varying(16000)", maxLength: 16000, nullable: true),
                    QualifiedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    TransientFailures = table.Column<int>(type: "integer", nullable: false),
                    QualificationAuthorization = table.Column<Guid>(type: "uuid", nullable: false),
                    ClaimedQualificationAuthorization = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StandingSpecialistCandidateStates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "StandingSpecialistRoutings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AgentId = table.Column<Guid>(type: "uuid", nullable: false),
                    CandidatesJson = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    ConcurrencyToken = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StandingSpecialistRoutings", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StandingSpecialistCandidateStates_AgentId_AgentKind_ModelLe~",
                table: "StandingSpecialistCandidateStates",
                columns: new[] { "AgentId", "AgentKind", "ModelLevel" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StandingSpecialistCandidateStates_PhysicalAgentId",
                table: "StandingSpecialistCandidateStates",
                column: "PhysicalAgentId",
                unique: true,
                filter: "\"PhysicalAgentId\" IS NOT NULL AND \"Enabled\" = TRUE");

            migrationBuilder.CreateIndex(
                name: "IX_StandingSpecialistRoutings_AgentId",
                table: "StandingSpecialistRoutings",
                column: "AgentId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StandingSpecialistCandidateStates");

            migrationBuilder.DropTable(
                name: "StandingSpecialistRoutings");
        }
    }
}
