using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Antiphon.Server.Migrations
{
    /// <inheritdoc />
    public partial class HostLoadTelemetryS1 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "LoadAttempt",
                table: "AgentTaskEvents",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LoadDispatchedAt",
                table: "AgentTaskEvents",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LoadQueuedAt",
                table: "AgentTaskEvents",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LoadRunnerId",
                table: "AgentTaskEvents",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "LoadSessionId",
                table: "AgentTaskEvents",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LoadStatus",
                table: "AgentTaskEvents",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AgentTaskLoadWindows",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TaskId = table.Column<Guid>(type: "uuid", nullable: false),
                    Attempt = table.Column<int>(type: "integer", nullable: false),
                    PhysicalHostKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    RunnerBootId = table.Column<Guid>(type: "uuid", nullable: false),
                    WindowOrdinal = table.Column<int>(type: "integer", nullable: false),
                    StartEventId = table.Column<Guid>(type: "uuid", nullable: false),
                    EndEventId = table.Column<Guid>(type: "uuid", nullable: true),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EndedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DetailsJson = table.Column<string>(type: "text", nullable: false),
                    Complete = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AgentTaskLoadWindows", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "HostLoadHourlyRollups",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PhysicalHostKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    SourceEpoch = table.Column<Guid>(type: "uuid", nullable: false),
                    HourUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Metric = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    HistogramJson = table.Column<string>(type: "text", nullable: false),
                    StatisticsJson = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HostLoadHourlyRollups", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "HostLoadIngestCursors",
                columns: table => new
                {
                    RunnerId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RunnerStoreId = table.Column<Guid>(type: "uuid", nullable: false),
                    Cursor = table.Column<string>(type: "text", nullable: true),
                    GapsJson = table.Column<string>(type: "text", nullable: false),
                    Complete = table.Column<bool>(type: "boolean", nullable: false),
                    CommittedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HostLoadIngestCursors", x => x.RunnerId);
                });

            migrationBuilder.CreateTable(
                name: "HostLoadObservedEvents",
                columns: table => new
                {
                    EventId = table.Column<Guid>(type: "uuid", nullable: false),
                    ObservedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HostLoadObservedEvents", x => x.EventId);
                });

            migrationBuilder.CreateTable(
                name: "HostLoadSampleIdentities",
                columns: table => new
                {
                    RunnerStoreId = table.Column<Guid>(type: "uuid", nullable: false),
                    BootId = table.Column<Guid>(type: "uuid", nullable: false),
                    Sequence = table.Column<long>(type: "bigint", nullable: false),
                    PayloadHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    IngestedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HostLoadSampleIdentities", x => new { x.RunnerStoreId, x.BootId, x.Sequence });
                });

            migrationBuilder.CreateTable(
                name: "HostLoadSamples",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RunnerStoreId = table.Column<Guid>(type: "uuid", nullable: false),
                    BootId = table.Column<Guid>(type: "uuid", nullable: false),
                    Sequence = table.Column<long>(type: "bigint", nullable: false),
                    RunnerId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PhysicalHostKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    SourceEpoch = table.Column<Guid>(type: "uuid", nullable: true),
                    SampledAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    IngestedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DerivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    IsPrimary = table.Column<bool>(type: "boolean", nullable: false),
                    PayloadJson = table.Column<string>(type: "text", nullable: false),
                    CountersJson = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HostLoadSamples", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "HostLoadTaskMetricBuckets",
                columns: table => new
                {
                    WindowId = table.Column<Guid>(type: "uuid", nullable: false),
                    HourUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Metric = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    HistogramJson = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HostLoadTaskMetricBuckets", x => new { x.WindowId, x.HourUtc, x.Metric });
                    table.ForeignKey(
                        name: "FK_HostLoadTaskMetricBuckets_AgentTaskLoadWindows_WindowId",
                        column: x => x.WindowId,
                        principalTable: "AgentTaskLoadWindows",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AgentTaskEvents_LoadObservation",
                table: "AgentTaskEvents",
                columns: new[] { "At", "Id" },
                filter: "\"LoadAttempt\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AgentTaskLoadWindows_EndedAt",
                table: "AgentTaskLoadWindows",
                column: "EndedAt");

            migrationBuilder.CreateIndex(
                name: "IX_AgentTaskLoadWindows_PhysicalHostKey_StartedAt_EndedAt",
                table: "AgentTaskLoadWindows",
                columns: new[] { "PhysicalHostKey", "StartedAt", "EndedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AgentTaskLoadWindows_TaskId_Attempt_PhysicalHostKey_RunnerB~",
                table: "AgentTaskLoadWindows",
                columns: new[] { "TaskId", "Attempt", "PhysicalHostKey", "RunnerBootId", "WindowOrdinal" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HostLoadHourlyRollups_PhysicalHostKey_HourUtc_Id",
                table: "HostLoadHourlyRollups",
                columns: new[] { "PhysicalHostKey", "HourUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_HostLoadHourlyRollups_PhysicalHostKey_SourceEpoch_HourUtc_M~",
                table: "HostLoadHourlyRollups",
                columns: new[] { "PhysicalHostKey", "SourceEpoch", "HourUtc", "Metric" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HostLoadObservedEvents_ObservedAt",
                table: "HostLoadObservedEvents",
                column: "ObservedAt");

            migrationBuilder.CreateIndex(
                name: "IX_HostLoadSampleIdentities_IngestedAt",
                table: "HostLoadSampleIdentities",
                column: "IngestedAt");

            migrationBuilder.CreateIndex(
                name: "IX_HostLoadSamples_PendingDerived",
                table: "HostLoadSamples",
                column: "Id",
                filter: "\"DerivedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_HostLoadSamples_PhysicalHostKey_SampledAt_Id",
                table: "HostLoadSamples",
                columns: new[] { "PhysicalHostKey", "SampledAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_HostLoadSamples_RunnerStoreId_BootId_Sequence",
                table: "HostLoadSamples",
                columns: new[] { "RunnerStoreId", "BootId", "Sequence" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException("Host load history and event provenance cannot be discarded by a downgrade. Restore a reviewed database backup instead.");
        }
    }
}
