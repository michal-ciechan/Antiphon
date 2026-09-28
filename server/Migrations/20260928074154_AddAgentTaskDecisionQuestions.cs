using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Antiphon.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddAgentTaskDecisionQuestions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AgentTaskDecisionQuestions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AgentTaskId = table.Column<Guid>(type: "uuid", nullable: false),
                    Attempt = table.Column<int>(type: "integer", nullable: false),
                    AgentSessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    CanonicalPayloadJson = table.Column<string>(type: "text", nullable: false),
                    PayloadHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PolicyVersion = table.Column<int>(type: "integer", nullable: true),
                    PolicyHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    GrantId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Disposition = table.Column<int>(type: "integer", nullable: false),
                    Reason = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Answer = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AgentTaskDecisionQuestions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AgentTaskDecisionQuestions_AgentSessions_AgentSessionId",
                        column: x => x.AgentSessionId,
                        principalTable: "AgentSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AgentTaskDecisionQuestions_AgentTasks_AgentTaskId",
                        column: x => x.AgentTaskId,
                        principalTable: "AgentTasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AgentTaskDecisionQuestions_AgentSessionId",
                table: "AgentTaskDecisionQuestions",
                column: "AgentSessionId");

            migrationBuilder.CreateIndex(
                name: "IX_AgentTaskDecisionQuestions_AgentTaskId_Attempt_RequestId",
                table: "AgentTaskDecisionQuestions",
                columns: new[] { "AgentTaskId", "Attempt", "RequestId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AgentTaskDecisionQuestions_AgentTaskId_CreatedAt",
                table: "AgentTaskDecisionQuestions",
                columns: new[] { "AgentTaskId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AgentTaskDecisionQuestions");
        }
    }
}
