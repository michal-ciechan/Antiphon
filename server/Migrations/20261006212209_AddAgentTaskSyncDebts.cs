using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Antiphon.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddAgentTaskSyncDebts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AgentTaskSyncDebts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TaskId = table.Column<Guid>(type: "uuid", nullable: false),
                    Attempt = table.Column<int>(type: "integer", nullable: false),
                    SettlementEventId = table.Column<Guid>(type: "uuid", nullable: false),
                    RunnerId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    WorktreePath = table.Column<string>(type: "text", nullable: true),
                    RemoteWorktreePath = table.Column<string>(type: "text", nullable: true),
                    RepositoryPath = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    FullRef = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    BaselineSha = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    SourceSha = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    DesktopBeforeSha = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    EndpointFingerprint = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    State = table.Column<int>(type: "integer", nullable: false),
                    ReasonCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    NextAttemptAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SourceReadyAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ConfirmedSha = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AgentTaskSyncDebts", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AgentTaskSyncDebts_State_NextAttemptAt",
                table: "AgentTaskSyncDebts",
                columns: new[] { "State", "NextAttemptAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AgentTaskSyncDebts_TaskId_Attempt",
                table: "AgentTaskSyncDebts",
                columns: new[] { "TaskId", "Attempt" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AgentTaskSyncDebts");
        }
    }
}
