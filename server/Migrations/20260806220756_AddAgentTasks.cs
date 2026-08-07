using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Antiphon.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddAgentTasks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AgentTasks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RootTaskId = table.Column<Guid>(type: "uuid", nullable: false),
                    ParentTaskId = table.Column<Guid>(type: "uuid", nullable: true),
                    ParentSessionId = table.Column<Guid>(type: "uuid", nullable: true),
                    Depth = table.Column<int>(type: "integer", nullable: false),
                    Title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Goal = table.Column<string>(type: "character varying(20000)", maxLength: 20000, nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    Role = table.Column<int>(type: "integer", nullable: false),
                    ModelLevel = table.Column<int>(type: "integer", nullable: false),
                    EscalatedFrom = table.Column<int>(type: "integer", nullable: true),
                    Attempt = table.Column<int>(type: "integer", nullable: false),
                    MaxAttempts = table.Column<int>(type: "integer", nullable: false),
                    Workspace = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    WorkingDirectory = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    RepoPath = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    WorktreeId = table.Column<Guid>(type: "uuid", nullable: true),
                    MergeTargetRef = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    ScopeGlob = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    AgentId = table.Column<Guid>(type: "uuid", nullable: true),
                    AgentSessionId = table.Column<Guid>(type: "uuid", nullable: true),
                    Ephemeral = table.Column<bool>(type: "boolean", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ReplyTo = table.Column<int>(type: "integer", nullable: false),
                    Result = table.Column<string>(type: "text", nullable: true),
                    ResultFilePath = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    FailureReason = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    ConcurrencyToken = table.Column<Guid>(type: "uuid", nullable: false),
                    TokenHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DispatchedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    TokensIn = table.Column<long>(type: "bigint", nullable: false),
                    TokensOut = table.Column<long>(type: "bigint", nullable: false),
                    CostUsd = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AgentTasks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AgentTasks_AgentTasks_ParentTaskId",
                        column: x => x.ParentTaskId,
                        principalTable: "AgentTasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AgentTaskEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AgentTaskId = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    ModelLevel = table.Column<int>(type: "integer", nullable: true),
                    Detail = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    At = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AgentTaskEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AgentTaskEvents_AgentTasks_AgentTaskId",
                        column: x => x.AgentTaskId,
                        principalTable: "AgentTasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AgentTaskEvents_AgentTaskId_At",
                table: "AgentTaskEvents",
                columns: new[] { "AgentTaskId", "At" });

            migrationBuilder.CreateIndex(
                name: "IX_AgentTasks_AgentSessionId",
                table: "AgentTasks",
                column: "AgentSessionId");

            migrationBuilder.CreateIndex(
                name: "IX_AgentTasks_ParentTaskId",
                table: "AgentTasks",
                column: "ParentTaskId");

            migrationBuilder.CreateIndex(
                name: "IX_AgentTasks_RootTaskId_CreatedAt",
                table: "AgentTasks",
                columns: new[] { "RootTaskId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AgentTasks_Status",
                table: "AgentTasks",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_AgentTasks_TokenHash",
                table: "AgentTasks",
                column: "TokenHash");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AgentTaskEvents");

            migrationBuilder.DropTable(
                name: "AgentTasks");
        }
    }
}
