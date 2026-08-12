using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Antiphon.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddAgentTuiProfiles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "EffectiveModelId",
                table: "AgentSessions",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "TuiProfileRevisionId",
                table: "AgentSessions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ModelId",
                table: "Agents",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "TuiProfileId",
                table: "Agents",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AgentTuiModels",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProfileId = table.Column<Guid>(type: "uuid", nullable: false),
                    Identifier = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Family = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Source = table.Column<int>(type: "integer", nullable: false),
                    Availability = table.Column<int>(type: "integer", nullable: false),
                    DiscoveredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RunnerVersion = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    IsSuggestedDefault = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AgentTuiModels", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AgentTuiProfileRevisions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProfileId = table.Column<Guid>(type: "uuid", nullable: false),
                    RevisionNumber = table.Column<int>(type: "integer", nullable: false),
                    Executable = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    ArgumentsJson = table.Column<string>(type: "jsonb", nullable: false),
                    DiscoveryArgumentsJson = table.Column<string>(type: "jsonb", nullable: false),
                    VersionArgumentsJson = table.Column<string>(type: "jsonb", nullable: false),
                    WorkingDirectory = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    AuthenticationMode = table.Column<int>(type: "integer", nullable: false),
                    NonSecretEnvironmentJson = table.Column<string>(type: "jsonb", nullable: false),
                    SecretEnvironmentNamesJson = table.Column<string>(type: "jsonb", nullable: false),
                    ModelArgumentName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Guidance = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AgentTuiProfileRevisions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AgentTuiProfiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    IsDefault = table.Column<bool>(type: "boolean", nullable: false),
                    Source = table.Column<int>(type: "integer", nullable: false),
                    SourceDefinitionName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ActiveRevisionId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AgentTuiProfiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AgentTuiProfiles_AgentTuiProfileRevisions_ActiveRevisionId",
                        column: x => x.ActiveRevisionId,
                        principalTable: "AgentTuiProfileRevisions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "AgentTuiSecrets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProfileId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Ciphertext = table.Column<string>(type: "text", nullable: false),
                    ProtectionVersion = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AgentTuiSecrets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AgentTuiSecrets_AgentTuiProfiles_ProfileId",
                        column: x => x.ProfileId,
                        principalTable: "AgentTuiProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AgentTuiValidationRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProfileId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProfileRevisionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Operation = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ResultsJson = table.Column<string>(type: "jsonb", nullable: false),
                    CapabilitiesJson = table.Column<string>(type: "jsonb", nullable: false),
                    RunnerVersion = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Summary = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AgentTuiValidationRuns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AgentTuiValidationRuns_AgentTuiProfileRevisions_ProfileRevi~",
                        column: x => x.ProfileRevisionId,
                        principalTable: "AgentTuiProfileRevisions",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_AgentTuiValidationRuns_AgentTuiProfiles_ProfileId",
                        column: x => x.ProfileId,
                        principalTable: "AgentTuiProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AgentSessions_TuiProfileRevisionId",
                table: "AgentSessions",
                column: "TuiProfileRevisionId");

            migrationBuilder.CreateIndex(
                name: "IX_Agents_TuiProfileId",
                table: "Agents",
                column: "TuiProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_AgentTuiModels_ProfileId_Identifier",
                table: "AgentTuiModels",
                columns: new[] { "ProfileId", "Identifier" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AgentTuiProfileRevisions_ProfileId_RevisionNumber",
                table: "AgentTuiProfileRevisions",
                columns: new[] { "ProfileId", "RevisionNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AgentTuiProfiles_ActiveRevisionId",
                table: "AgentTuiProfiles",
                column: "ActiveRevisionId");

            migrationBuilder.CreateIndex(
                name: "IX_AgentTuiProfiles_DisplayName",
                table: "AgentTuiProfiles",
                column: "DisplayName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AgentTuiSecrets_ProfileId_Name",
                table: "AgentTuiSecrets",
                columns: new[] { "ProfileId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AgentTuiValidationRuns_ProfileId_CreatedAt",
                table: "AgentTuiValidationRuns",
                columns: new[] { "ProfileId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AgentTuiValidationRuns_ProfileRevisionId",
                table: "AgentTuiValidationRuns",
                column: "ProfileRevisionId");

            migrationBuilder.AddForeignKey(
                name: "FK_Agents_AgentTuiProfiles_TuiProfileId",
                table: "Agents",
                column: "TuiProfileId",
                principalTable: "AgentTuiProfiles",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_AgentSessions_AgentTuiProfileRevisions_TuiProfileRevisionId",
                table: "AgentSessions",
                column: "TuiProfileRevisionId",
                principalTable: "AgentTuiProfileRevisions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_AgentTuiModels_AgentTuiProfiles_ProfileId",
                table: "AgentTuiModels",
                column: "ProfileId",
                principalTable: "AgentTuiProfiles",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_AgentTuiProfileRevisions_AgentTuiProfiles_ProfileId",
                table: "AgentTuiProfileRevisions",
                column: "ProfileId",
                principalTable: "AgentTuiProfiles",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Agents_AgentTuiProfiles_TuiProfileId",
                table: "Agents");

            migrationBuilder.DropForeignKey(
                name: "FK_AgentSessions_AgentTuiProfileRevisions_TuiProfileRevisionId",
                table: "AgentSessions");

            migrationBuilder.DropForeignKey(
                name: "FK_AgentTuiProfileRevisions_AgentTuiProfiles_ProfileId",
                table: "AgentTuiProfileRevisions");

            migrationBuilder.DropTable(
                name: "AgentTuiModels");

            migrationBuilder.DropTable(
                name: "AgentTuiSecrets");

            migrationBuilder.DropTable(
                name: "AgentTuiValidationRuns");

            migrationBuilder.DropTable(
                name: "AgentTuiProfiles");

            migrationBuilder.DropTable(
                name: "AgentTuiProfileRevisions");

            migrationBuilder.DropIndex(
                name: "IX_AgentSessions_TuiProfileRevisionId",
                table: "AgentSessions");

            migrationBuilder.DropIndex(
                name: "IX_Agents_TuiProfileId",
                table: "Agents");

            migrationBuilder.DropColumn(
                name: "EffectiveModelId",
                table: "AgentSessions");

            migrationBuilder.DropColumn(
                name: "TuiProfileRevisionId",
                table: "AgentSessions");

            migrationBuilder.DropColumn(
                name: "ModelId",
                table: "Agents");

            migrationBuilder.DropColumn(
                name: "TuiProfileId",
                table: "Agents");
        }
    }
}
