using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Antiphon.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddTranscriptEntries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TranscriptEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AgentSessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Sequence = table.Column<long>(type: "bigint", nullable: false),
                    Kind = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Uuid = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ParentUuid = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Role = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    Text = table.Column<string>(type: "text", nullable: true),
                    ToolName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ToolInput = table.Column<string>(type: "text", nullable: true),
                    ToolUseId = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    ToolIsError = table.Column<bool>(type: "boolean", nullable: true),
                    StopReason = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TranscriptEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TranscriptEntries_AgentSessions_AgentSessionId",
                        column: x => x.AgentSessionId,
                        principalTable: "AgentSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TranscriptEntries_AgentSessionId_Sequence",
                table: "TranscriptEntries",
                columns: new[] { "AgentSessionId", "Sequence" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TranscriptEntries");
        }
    }
}
