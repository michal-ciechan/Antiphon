using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Antiphon.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddAgentPinnedInstructions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PinRefreshKey",
                table: "SessionQueuedMessages",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PinRequestedHash",
                table: "SessionQueuedMessages",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PinRequestedLocationGeneration",
                table: "SessionQueuedMessages",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PinRequestedRevision",
                table: "SessionQueuedMessages",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PinLastNotifiedHash",
                table: "AgentSessions",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PinLastNotifiedLocationGeneration",
                table: "AgentSessions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PinLastNotifiedRevision",
                table: "AgentSessions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PinLaunchAbsolutePath",
                table: "AgentSessions",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PinLaunchHash",
                table: "AgentSessions",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PinLaunchLocationGeneration",
                table: "AgentSessions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PinLaunchRevision",
                table: "AgentSessions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PinProjectionId",
                table: "AgentSessions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PinClaudeImportMode",
                table: "Agents",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "AgentPinCleanupRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OriginalAgentId = table.Column<Guid>(type: "uuid", nullable: false),
                    CanonicalHost = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CanonicalCwd = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    TargetRelativePath = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    TargetAbsolutePath = table.Column<string>(type: "character varying(1400)", maxLength: 1400, nullable: false),
                    PathSchemaVersion = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Error = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AgentPinCleanupRecords", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AgentPinnedInstructions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AgentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Text = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Source = table.Column<int>(type: "integer", nullable: false),
                    SourceNamespace = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    SourceKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    SourceRef = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedBySessionId = table.Column<Guid>(type: "uuid", nullable: true),
                    RevokedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RevokedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    RevokedBySessionId = table.Column<Guid>(type: "uuid", nullable: true),
                    SupersedesPinId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AgentPinnedInstructions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AgentPinnedInstructions_Agents_AgentId",
                        column: x => x.AgentId,
                        principalTable: "Agents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AgentPinnedInstructionStates",
                columns: table => new
                {
                    AgentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Revision = table.Column<int>(type: "integer", nullable: false),
                    ConcurrencyToken = table.Column<Guid>(type: "uuid", nullable: false),
                    ContentHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    FirstUsedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AgentPinnedInstructionStates", x => x.AgentId);
                    table.ForeignKey(
                        name: "FK_AgentPinnedInstructionStates_Agents_AgentId",
                        column: x => x.AgentId,
                        principalTable: "Agents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AgentPinOperations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AgentId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    Fingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ResultPinId = table.Column<Guid>(type: "uuid", nullable: true),
                    ResultRevokedPinId = table.Column<Guid>(type: "uuid", nullable: true),
                    ResultRevision = table.Column<int>(type: "integer", nullable: false),
                    ResultHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedNewRow = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AgentPinOperations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AgentPinOperations_Agents_AgentId",
                        column: x => x.AgentId,
                        principalTable: "Agents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AgentPinProjections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AgentId = table.Column<Guid>(type: "uuid", nullable: false),
                    CanonicalHost = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CanonicalCwd = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    PathSchemaVersion = table.Column<int>(type: "integer", nullable: false),
                    TargetRelativePath = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    TargetAbsolutePath = table.Column<string>(type: "character varying(1400)", maxLength: 1400, nullable: false),
                    LocationGeneration = table.Column<int>(type: "integer", nullable: false),
                    DesiredRevision = table.Column<int>(type: "integer", nullable: false),
                    ProjectedRevision = table.Column<int>(type: "integer", nullable: true),
                    LastWrittenByteHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    MarkerVersion = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Error = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    ImportStatus = table.Column<int>(type: "integer", nullable: false),
                    ImportMode = table.Column<int>(type: "integer", nullable: false),
                    ImportTarget = table.Column<string>(type: "character varying(1400)", maxLength: 1400, nullable: true),
                    ImportOwnedStart = table.Column<int>(type: "integer", nullable: true),
                    ImportOwnedLength = table.Column<int>(type: "integer", nullable: true),
                    IntendedBeforeHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    IntendedAfterHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    HasConfiguredConsumer = table.Column<bool>(type: "boolean", nullable: false),
                    HasLiveSessionConsumer = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AgentPinProjections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AgentPinProjections_Agents_AgentId",
                        column: x => x.AgentId,
                        principalTable: "Agents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AgentPinReconciliations",
                columns: table => new
                {
                    AgentId = table.Column<Guid>(type: "uuid", nullable: false),
                    DesiredRevision = table.Column<int>(type: "integer", nullable: false),
                    DesiredHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Error = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AgentPinReconciliations", x => x.AgentId);
                    table.ForeignKey(
                        name: "FK_AgentPinReconciliations_Agents_AgentId",
                        column: x => x.AgentId,
                        principalTable: "Agents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SessionQueuedMessages_AgentSessionId_PinRefreshKey",
                table: "SessionQueuedMessages",
                columns: new[] { "AgentSessionId", "PinRefreshKey" },
                unique: true,
                filter: "\"PinRefreshKey\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AgentPinCleanupRecords_OriginalAgentId",
                table: "AgentPinCleanupRecords",
                column: "OriginalAgentId");

            migrationBuilder.CreateIndex(
                name: "IX_AgentPinnedInstructions_ActiveSource",
                table: "AgentPinnedInstructions",
                columns: new[] { "AgentId", "SourceNamespace", "SourceKey" },
                unique: true,
                filter: "\"RevokedAt\" IS NULL AND \"SourceNamespace\" IS NOT NULL AND \"SourceKey\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AgentPinnedInstructions_AgentId",
                table: "AgentPinnedInstructions",
                column: "AgentId");

            migrationBuilder.CreateIndex(
                name: "IX_AgentPinOperations_AgentId_RequestId",
                table: "AgentPinOperations",
                columns: new[] { "AgentId", "RequestId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AgentPinProjections_AgentHostCwd",
                table: "AgentPinProjections",
                columns: new[] { "AgentId", "CanonicalHost", "CanonicalCwd" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AgentPinProjections_HostTarget",
                table: "AgentPinProjections",
                columns: new[] { "CanonicalHost", "TargetAbsolutePath" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AgentPinCleanupRecords");

            migrationBuilder.DropTable(
                name: "AgentPinnedInstructions");

            migrationBuilder.DropTable(
                name: "AgentPinnedInstructionStates");

            migrationBuilder.DropTable(
                name: "AgentPinOperations");

            migrationBuilder.DropTable(
                name: "AgentPinProjections");

            migrationBuilder.DropTable(
                name: "AgentPinReconciliations");

            migrationBuilder.DropIndex(
                name: "IX_SessionQueuedMessages_AgentSessionId_PinRefreshKey",
                table: "SessionQueuedMessages");

            migrationBuilder.DropColumn(
                name: "PinRefreshKey",
                table: "SessionQueuedMessages");

            migrationBuilder.DropColumn(
                name: "PinRequestedHash",
                table: "SessionQueuedMessages");

            migrationBuilder.DropColumn(
                name: "PinRequestedLocationGeneration",
                table: "SessionQueuedMessages");

            migrationBuilder.DropColumn(
                name: "PinRequestedRevision",
                table: "SessionQueuedMessages");

            migrationBuilder.DropColumn(
                name: "PinLastNotifiedHash",
                table: "AgentSessions");

            migrationBuilder.DropColumn(
                name: "PinLastNotifiedLocationGeneration",
                table: "AgentSessions");

            migrationBuilder.DropColumn(
                name: "PinLastNotifiedRevision",
                table: "AgentSessions");

            migrationBuilder.DropColumn(
                name: "PinLaunchAbsolutePath",
                table: "AgentSessions");

            migrationBuilder.DropColumn(
                name: "PinLaunchHash",
                table: "AgentSessions");

            migrationBuilder.DropColumn(
                name: "PinLaunchLocationGeneration",
                table: "AgentSessions");

            migrationBuilder.DropColumn(
                name: "PinLaunchRevision",
                table: "AgentSessions");

            migrationBuilder.DropColumn(
                name: "PinProjectionId",
                table: "AgentSessions");

            migrationBuilder.DropColumn(
                name: "PinClaudeImportMode",
                table: "Agents");
        }
    }
}
