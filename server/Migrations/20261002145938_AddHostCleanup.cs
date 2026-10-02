using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Antiphon.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddHostCleanup : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "HostCleanupHolds",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BoardId = table.Column<Guid>(type: "uuid", nullable: false),
                    HostId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    StorageId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CanonicalPath = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    TaskId = table.Column<Guid>(type: "uuid", nullable: true),
                    OwnerGeneration = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    UnresolvedTaskPrefix = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Reason = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    Creator = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DisposedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DispositionReason = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    Revision = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HostCleanupHolds", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "HostCleanupRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BoardId = table.Column<Guid>(type: "uuid", nullable: false),
                    HostId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    StorageId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    RunnerStoreId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ProcessBootId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    SourceSha = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ConfigDigest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PlanDigest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ReceiptDigest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    LocalDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Daily = table.Column<bool>(type: "boolean", nullable: false),
                    Execute = table.Column<bool>(type: "boolean", nullable: false),
                    Complete = table.Column<bool>(type: "boolean", nullable: false),
                    Status = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PlannedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    FinishedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SampledAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SampleComplete = table.Column<bool>(type: "boolean", nullable: false),
                    NamespaceAllocatedBytes = table.Column<long>(type: "bigint", nullable: true),
                    DiskCapacityBytes = table.Column<long>(type: "bigint", nullable: true),
                    FreeBytesBefore = table.Column<long>(type: "bigint", nullable: true),
                    FreeBytesAfter = table.Column<long>(type: "bigint", nullable: true),
                    AttemptLimit = table.Column<int>(type: "integer", nullable: false),
                    ByteLimit = table.Column<long>(type: "bigint", nullable: false),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    ReservedBytes = table.Column<long>(type: "bigint", nullable: false),
                    ReclaimedBytes = table.Column<long>(type: "bigint", nullable: false),
                    EligibleWorktreeBytes = table.Column<long>(type: "bigint", nullable: false),
                    NextCursor = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    InvalidationPending = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HostCleanupRuns", x => x.Id);
                    table.CheckConstraint("CK_HostCleanupRuns_Budget", "\"AttemptLimit\" > 0 AND \"ByteLimit\" > 0 AND \"Attempts\" >= 0 AND \"Attempts\" <= \"AttemptLimit\" AND \"ReservedBytes\" >= 0 AND \"ReservedBytes\" <= \"ByteLimit\" AND \"ReclaimedBytes\" >= 0 AND \"EligibleWorktreeBytes\" >= 0");
                });

            migrationBuilder.CreateTable(
                name: "HostMaintenanceActivities",
                columns: table => new
                {
                    StorageId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    HostId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Epoch = table.Column<long>(type: "bigint", nullable: false),
                    CustodyReconciled = table.Column<bool>(type: "boolean", nullable: false),
                    MaintenanceIntentId = table.Column<Guid>(type: "uuid", nullable: true),
                    MaintenanceKind = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    MaintenanceRequestedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CleanupOperationId = table.Column<Guid>(type: "uuid", nullable: true),
                    WorkerStoreId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    WorkerBootId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    WorkerPid = table.Column<int>(type: "integer", nullable: true),
                    WorkerStartToken = table.Column<long>(type: "bigint", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Revision = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HostMaintenanceActivities", x => x.StorageId);
                });

            migrationBuilder.CreateTable(
                name: "HostCleanupCandidates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RunId = table.Column<Guid>(type: "uuid", nullable: false),
                    Ordinal = table.Column<int>(type: "integer", nullable: false),
                    CanonicalPath = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    StorageId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    FileId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    OwnerGeneration = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    TaskId = table.Column<Guid>(type: "uuid", nullable: true),
                    SessionId = table.Column<Guid>(type: "uuid", nullable: true),
                    Family = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Worktree = table.Column<bool>(type: "boolean", nullable: false),
                    Disposition = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ReasonCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ContentClass = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ExistingOwner = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    OwnerRefusalCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Branch = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    SourceSha = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    PushedSha = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    TargetSha = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    NewestWriteAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ScanComplete = table.Column<bool>(type: "boolean", nullable: false),
                    LogicalBytes = table.Column<long>(type: "bigint", nullable: true),
                    AllocatedBytes = table.Column<long>(type: "bigint", nullable: true),
                    ReservedBytes = table.Column<long>(type: "bigint", nullable: true),
                    Outcome = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ReclaimedBytes = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HostCleanupCandidates", x => x.Id);
                    table.CheckConstraint("CK_HostCleanupCandidates_WorktreeInventory", "\"ReclaimedBytes\" >= 0 AND (NOT \"Worktree\" OR (\"ReclaimedBytes\" = 0 AND \"ReservedBytes\" IS NULL AND (\"Outcome\" IS NULL OR \"Outcome\" = 'inventory_only')))");
                    table.ForeignKey(
                        name: "FK_HostCleanupCandidates_HostCleanupRuns_RunId",
                        column: x => x.RunId,
                        principalTable: "HostCleanupRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_HostCleanupCandidates_RunId_Ordinal",
                table: "HostCleanupCandidates",
                columns: new[] { "RunId", "Ordinal" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HostCleanupHolds_BoardId_StorageId_DisposedAt",
                table: "HostCleanupHolds",
                columns: new[] { "BoardId", "StorageId", "DisposedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_HostCleanupRuns_BoardId_HostId_PlannedAt",
                table: "HostCleanupRuns",
                columns: new[] { "BoardId", "HostId", "PlannedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_HostCleanupRuns_StorageId_LocalDate",
                table: "HostCleanupRuns",
                columns: new[] { "StorageId", "LocalDate" },
                unique: true,
                filter: "\"Daily\" = TRUE");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "HostCleanupCandidates");

            migrationBuilder.DropTable(
                name: "HostCleanupHolds");

            migrationBuilder.DropTable(
                name: "HostMaintenanceActivities");

            migrationBuilder.DropTable(
                name: "HostCleanupRuns");
        }
    }
}
