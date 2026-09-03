using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Antiphon.Server.Migrations
{
    /// <summary>
    /// CARD-0147 S3: uncleared worktree-health findings for stuck feat/card-task-* registrations.
    /// Generated with <c>dotnet ef migrations add</c>. No backfill.
    /// </summary>
    public partial class AddWorktreeHealthFindings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WorktreeHealthFindings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RepoPath = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    Branch = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Path = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    TaskId = table.Column<Guid>(type: "uuid", nullable: true),
                    Shape = table.Column<int>(type: "integer", nullable: false),
                    Detail = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    FirstSeenAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastSeenAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ClearedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorktreeHealthFindings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorktreeHealthFindings_AgentTasks_TaskId",
                        column: x => x.TaskId,
                        principalTable: "AgentTasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WorktreeHealthFindings_RepoPath_Branch_Shape_Uncleared",
                table: "WorktreeHealthFindings",
                columns: new[] { "RepoPath", "Branch", "Shape" },
                unique: true,
                filter: "\"ClearedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_WorktreeHealthFindings_TaskId",
                table: "WorktreeHealthFindings",
                column: "TaskId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WorktreeHealthFindings");
        }
    }
}
