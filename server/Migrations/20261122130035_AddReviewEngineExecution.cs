using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Antiphon.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddReviewEngineExecution : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BaseTipSha",
                table: "ReviewRuns",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OutputJson",
                table: "ReviewNodeExecutions",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Sequence",
                table: "ReviewNodeExecutions",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "ExternalCommentId",
                table: "ReviewFindings",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExternalThreadId",
                table: "ReviewFindings",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ReviewContextPacks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RunId = table.Column<Guid>(type: "uuid", nullable: false),
                    Iteration = table.Column<int>(type: "integer", nullable: false),
                    GroupKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PrefixHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Content = table.Column<string>(type: "text", nullable: false),
                    TokenEstimate = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReviewContextPacks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReviewContextPacks_ReviewRuns_RunId",
                        column: x => x.RunId,
                        principalTable: "ReviewRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ReviewPackLedgerEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RunId = table.Column<Guid>(type: "uuid", nullable: false),
                    Iteration = table.Column<int>(type: "integer", nullable: false),
                    Path = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    Source = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    Score = table.Column<double>(type: "double precision", nullable: false),
                    Tokens = table.Column<int>(type: "integer", nullable: false),
                    Included = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReviewPackLedgerEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReviewPackLedgerEntries_ReviewRuns_RunId",
                        column: x => x.RunId,
                        principalTable: "ReviewRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ReviewContextPacks_RunId_Iteration_GroupKey",
                table: "ReviewContextPacks",
                columns: new[] { "RunId", "Iteration", "GroupKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReviewPackLedgerEntries_RunId_Iteration",
                table: "ReviewPackLedgerEntries",
                columns: new[] { "RunId", "Iteration" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Operator rule (2026-11-07): Down fails safely. Dropping these tables and columns would
            // lose the review engine's node outputs, context packs and code-host thread links.
            throw new NotSupportedException(
                "The review engine execution columns and tables hold review history; a downgrade cannot drop them.");
        }
    }
}
