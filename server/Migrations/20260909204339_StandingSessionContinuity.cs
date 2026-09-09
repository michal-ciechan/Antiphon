using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Antiphon.Server.Migrations
{
    /// <inheritdoc />
    public partial class StandingSessionContinuity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ContinuityEvidence",
                table: "AgentSupervisionStates",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ContinuityHeldAt",
                table: "AgentSupervisionStates",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ContinuityReason",
                table: "AgentSupervisionStates",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ContinuitySessionId",
                table: "AgentSupervisionStates",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "LastObservedRestartSessionId",
                table: "AgentSupervisionStates",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastObservedRestartStartedAt",
                table: "AgentSupervisionStates",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RestartBackoffFailures",
                table: "AgentSupervisionStates",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "InteractiveLaunchCompletedAt",
                table: "AgentSessions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RestartFailureKind",
                table: "AgentSessions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "StandingAgentId",
                table: "AgentSessions",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_AgentSessions_StandingAgentId_CreatedAt",
                table: "AgentSessions",
                columns: new[] { "StandingAgentId", "CreatedAt" });
            migrationBuilder.Sql("""
                UPDATE "AgentSupervisionStates"
                SET "RestartBackoffFailures" = "ConsecutiveFailures", "ConsecutiveFailures" = 0;
                WITH evidence AS (
                    SELECT s."Id" AS session_id, a."Id" AS agent_id
                    FROM "AgentSessions" s JOIN "Agents" a ON a."PersistentSessionId" = s."Id"::text
                    WHERE NOT a."IsPoolDelegate"
                    UNION
                    SELECT t."AgentSessionId", a."Id" FROM "AgentTasks" t
                    JOIN "Agents" a ON a."Id" = t."AgentId"
                    WHERE NOT a."IsPoolDelegate" AND t."AgentSessionId" IS NOT NULL
                    UNION
                    SELECT i."SessionId", a."Id" FROM "AgentIncidents" i
                    JOIN "Agents" a ON a."Id" = i."AgentId"
                    WHERE NOT a."IsPoolDelegate" AND i."SessionId" IS NOT NULL AND i."Kind" IN (0, 2, 3)
                ), unique_owner AS (
                    SELECT session_id, (array_agg(DISTINCT agent_id))[1] AS agent_id
                    FROM evidence GROUP BY session_id HAVING count(DISTINCT agent_id) = 1
                )
                UPDATE "AgentSessions" s SET "StandingAgentId" = u.agent_id
                FROM unique_owner u
                WHERE s."Id" = u.session_id AND s."CardId" IS NULL AND s."WorktreeId" IS NULL
                  AND NOT EXISTS (SELECT 1 FROM "Agents" a WHERE a."PersistentSessionId" = s."Id"::text
                      AND (a."Id" <> u.agent_id OR a."IsPoolDelegate"));
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AgentSessions_StandingAgentId_CreatedAt",
                table: "AgentSessions");

            migrationBuilder.DropColumn(
                name: "ContinuityEvidence",
                table: "AgentSupervisionStates");

            migrationBuilder.DropColumn(
                name: "ContinuityHeldAt",
                table: "AgentSupervisionStates");

            migrationBuilder.DropColumn(
                name: "ContinuityReason",
                table: "AgentSupervisionStates");

            migrationBuilder.DropColumn(
                name: "ContinuitySessionId",
                table: "AgentSupervisionStates");

            migrationBuilder.DropColumn(
                name: "LastObservedRestartSessionId",
                table: "AgentSupervisionStates");

            migrationBuilder.DropColumn(
                name: "LastObservedRestartStartedAt",
                table: "AgentSupervisionStates");

            migrationBuilder.DropColumn(
                name: "RestartBackoffFailures",
                table: "AgentSupervisionStates");

            migrationBuilder.DropColumn(
                name: "InteractiveLaunchCompletedAt",
                table: "AgentSessions");

            migrationBuilder.DropColumn(
                name: "RestartFailureKind",
                table: "AgentSessions");

            migrationBuilder.DropColumn(
                name: "StandingAgentId",
                table: "AgentSessions");
        }
    }
}
