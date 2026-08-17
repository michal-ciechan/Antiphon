using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Antiphon.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddAgentBundleAttachments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // CARD-0058 slice 6. Deliberately NOT backfilled: null means "no launch recorded a
            // composition here", which is the truth about every session that predates this column,
            // and the drift check treats null as no evidence. Backfilling it with today's
            // composition would claim those sessions were launched with instructions they may never
            // have carried — and would then silently clear a badge that should have been raised.
            // The empty string is the OTHER answer, written by a launch that composed nothing, and
            // only a launch is allowed to write it.
            migrationBuilder.AddColumn<string>(
                name: "ComposedBundleStamp",
                table: "AgentSessions",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            // Attachment state, and nothing else. The bundles themselves stay markdown files in the
            // repo (server/Bundles/*.md), embedded and hashed — BundleKey is a plain string with no
            // foreign key, because the catalog is code and cannot be one.
            migrationBuilder.CreateTable(
                name: "AgentBundleAttachments",
                columns: table => new
                {
                    AgentId = table.Column<Guid>(type: "uuid", nullable: false),
                    BundleKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Position = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AgentBundleAttachments", x => new { x.AgentId, x.BundleKey });
                    table.ForeignKey(
                        name: "FK_AgentBundleAttachments_Agents_AgentId",
                        column: x => x.AgentId,
                        principalTable: "Agents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AgentBundleAttachments");

            migrationBuilder.DropColumn(
                name: "ComposedBundleStamp",
                table: "AgentSessions");
        }
    }
}
