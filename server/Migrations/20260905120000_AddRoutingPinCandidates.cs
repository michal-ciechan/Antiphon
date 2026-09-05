using System;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Antiphon.Server.Migrations
{
    /// <summary>
    /// CARD-0322: RoutingPins.CandidatesJson replaces AgentKind/ModelLevel; AgentTasks.RoutingPinId
    /// marks a walked create. Hand-written (running daemons lock bin/); the [DbContext]/[Migration]
    /// attributes normally generated into the Designer live here. Snapshot is updated to match.
    /// </summary>
    [DbContext(typeof(AppDbContext))]
    [Migration("20260905120000_AddRoutingPinCandidates")]
    public partial class AddRoutingPinCandidates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CandidatesJson",
                table: "RoutingPins",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RoutingPinId",
                table: "AgentTasks",
                type: "uuid",
                nullable: true);

            // Ordinals from AgentKind.cs / AgentModelLevel.cs at code time (CARD-0322):
            // Raw=0, ClaudeCode=1, Codex=2, OpenCode=3, Grok=4; Frontier=0 .. Low=3.
            migrationBuilder.Sql(
                """
                UPDATE "RoutingPins"
                SET "CandidatesJson" = json_build_array(
                    json_build_object(
                        'agentKind', CASE "AgentKind"
                            WHEN 0 THEN 'Raw'
                            WHEN 1 THEN 'ClaudeCode'
                            WHEN 2 THEN 'Codex'
                            WHEN 3 THEN 'OpenCode'
                            WHEN 4 THEN 'Grok'
                            ELSE NULL
                        END,
                        'modelLevel', CASE "ModelLevel"
                            WHEN 0 THEN 'Frontier'
                            WHEN 1 THEN 'High'
                            WHEN 2 THEN 'Medium'
                            WHEN 3 THEN 'Low'
                            ELSE NULL
                        END
                    )
                )::text
                WHERE "AgentKind" IS NOT NULL OR "ModelLevel" IS NOT NULL;
                """);

            migrationBuilder.DropColumn(
                name: "AgentKind",
                table: "RoutingPins");

            migrationBuilder.DropColumn(
                name: "ModelLevel",
                table: "RoutingPins");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AgentKind",
                table: "RoutingPins",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ModelLevel",
                table: "RoutingPins",
                type: "integer",
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE "RoutingPins"
                SET
                    "AgentKind" = CASE ("CandidatesJson"::jsonb -> 0 ->> 'agentKind')
                        WHEN 'Raw' THEN 0
                        WHEN 'ClaudeCode' THEN 1
                        WHEN 'Codex' THEN 2
                        WHEN 'OpenCode' THEN 3
                        WHEN 'Grok' THEN 4
                        ELSE NULL
                    END,
                    "ModelLevel" = CASE ("CandidatesJson"::jsonb -> 0 ->> 'modelLevel')
                        WHEN 'Frontier' THEN 0
                        WHEN 'High' THEN 1
                        WHEN 'Medium' THEN 2
                        WHEN 'Low' THEN 3
                        ELSE NULL
                    END
                WHERE "CandidatesJson" IS NOT NULL AND "CandidatesJson" <> '';
                """);

            migrationBuilder.DropColumn(
                name: "CandidatesJson",
                table: "RoutingPins");

            migrationBuilder.DropColumn(
                name: "RoutingPinId",
                table: "AgentTasks");
        }
    }
}
