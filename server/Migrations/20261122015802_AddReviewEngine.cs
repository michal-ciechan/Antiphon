using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Antiphon.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddReviewEngine : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ReviewSubjects",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Host = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Repo = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    PrNumber = table.Column<int>(type: "integer", nullable: true),
                    Title = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    Url = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    AuthorLogin = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    AuthorDisplayName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    OpenedByLogin = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    AuthorResolvedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    BaseRef = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    HeadRef = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    BaseSha = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    HeadSha = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    OwnerTeams = table.Column<List<string>>(type: "text[]", nullable: false),
                    OwnerUsers = table.Column<List<string>>(type: "text[]", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReviewSubjects", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ReviewWorkflowRevisions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    GraphJson = table.Column<string>(type: "jsonb", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReviewWorkflowRevisions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ReviewRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SubjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Stage = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Round = table.Column<int>(type: "integer", nullable: false),
                    BotOnlyRounds = table.Column<int>(type: "integer", nullable: false),
                    NeedsHumanApproval = table.Column<bool>(type: "boolean", nullable: true),
                    WaitingOn = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    CostUsd = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    Categories = table.Column<List<string>>(type: "text[]", nullable: false),
                    BlastReach = table.Column<int>(type: "integer", nullable: true),
                    BlastExternalContract = table.Column<int>(type: "integer", nullable: true),
                    BlastData = table.Column<int>(type: "integer", nullable: true),
                    BlastRollbackAtRelease = table.Column<int>(type: "integer", nullable: true),
                    BlastReversalAfterMonths = table.Column<int>(type: "integer", nullable: true),
                    BlastExposure = table.Column<int>(type: "integer", nullable: true),
                    WorkflowName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    WorkflowRevisionId = table.Column<Guid>(type: "uuid", nullable: true),
                    WorkflowHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    SteerPrompt = table.Column<string>(type: "text", nullable: true),
                    StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReviewRuns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReviewRuns_ReviewSubjects_SubjectId",
                        column: x => x.SubjectId,
                        principalTable: "ReviewSubjects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReviewRuns_ReviewWorkflowRevisions_WorkflowRevisionId",
                        column: x => x.WorkflowRevisionId,
                        principalTable: "ReviewWorkflowRevisions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ReviewFindings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RunId = table.Column<Guid>(type: "uuid", nullable: false),
                    Key = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CheckId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    CheckWhy = table.Column<string>(type: "text", nullable: true),
                    Severity = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Flags = table.Column<List<string>>(type: "text[]", nullable: false),
                    BypassesRoundBar = table.Column<bool>(type: "boolean", nullable: false),
                    Confidence = table.Column<double>(type: "double precision", nullable: false),
                    FilePath = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    LineStart = table.Column<int>(type: "integer", nullable: true),
                    LineEnd = table.Column<int>(type: "integer", nullable: true),
                    Anchor = table.Column<string>(type: "text", nullable: false),
                    Title = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    Body = table.Column<string>(type: "text", nullable: false),
                    Suggestion = table.Column<string>(type: "text", nullable: true),
                    Hunk = table.Column<string>(type: "text", nullable: true),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    RoundRaised = table.Column<int>(type: "integer", nullable: false),
                    Posted = table.Column<bool>(type: "boolean", nullable: false),
                    SyncState = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ExternalThreadUrl = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReviewFindings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReviewFindings_ReviewRuns_RunId",
                        column: x => x.RunId,
                        principalTable: "ReviewRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ReviewGates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RunId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    NodeId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Iteration = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    TeamKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    AssigneeLogin = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    AssigneeDisplayName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    OpenedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DecidedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Decision = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    DecidedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    SteerPrompt = table.Column<string>(type: "text", nullable: true),
                    Reasons = table.Column<List<string>>(type: "text[]", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReviewGates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReviewGates_ReviewRuns_RunId",
                        column: x => x.RunId,
                        principalTable: "ReviewRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ReviewIterations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RunId = table.Column<Guid>(type: "uuid", nullable: false),
                    Iteration = table.Column<int>(type: "integer", nullable: false),
                    Round = table.Column<int>(type: "integer", nullable: false),
                    Trigger = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReviewIterations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReviewIterations_ReviewRuns_RunId",
                        column: x => x.RunId,
                        principalTable: "ReviewRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ReviewNodeExecutions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RunId = table.Column<Guid>(type: "uuid", nullable: false),
                    NodeId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Iteration = table.Column<int>(type: "integer", nullable: false),
                    Kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ItemKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Outcome = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Provider = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Model = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Transport = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    InputTokens = table.Column<long>(type: "bigint", nullable: false),
                    OutputTokens = table.Column<long>(type: "bigint", nullable: false),
                    CostUsd = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    CostBasis = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    InputsHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Summary = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReviewNodeExecutions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReviewNodeExecutions_ReviewRuns_RunId",
                        column: x => x.RunId,
                        principalTable: "ReviewRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ReviewFindingEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FindingId = table.Column<Guid>(type: "uuid", nullable: false),
                    Action = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ActorLogin = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ActorDisplayName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ReasonCategory = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    Scope = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    Text = table.Column<string>(type: "text", nullable: true),
                    SyncState = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReviewFindingEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReviewFindingEvents_ReviewFindings_FindingId",
                        column: x => x.FindingId,
                        principalTable: "ReviewFindings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ReviewGatePasses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    GateId = table.Column<Guid>(type: "uuid", nullable: false),
                    Login = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    At = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReviewGatePasses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReviewGatePasses_ReviewGates_GateId",
                        column: x => x.GateId,
                        principalTable: "ReviewGates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ReviewFindingEvents_FindingId_CreatedAt",
                table: "ReviewFindingEvents",
                columns: new[] { "FindingId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ReviewFindings_RunId_Key",
                table: "ReviewFindings",
                columns: new[] { "RunId", "Key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReviewFindings_RunId_Status",
                table: "ReviewFindings",
                columns: new[] { "RunId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ReviewGatePasses_GateId_At",
                table: "ReviewGatePasses",
                columns: new[] { "GateId", "At" });

            migrationBuilder.CreateIndex(
                name: "IX_ReviewGates_RunId",
                table: "ReviewGates",
                column: "RunId");

            migrationBuilder.CreateIndex(
                name: "IX_ReviewGates_Status_OpenedAt",
                table: "ReviewGates",
                columns: new[] { "Status", "OpenedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ReviewIterations_RunId_Iteration",
                table: "ReviewIterations",
                columns: new[] { "RunId", "Iteration" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReviewNodeExecutions_RunId_Iteration",
                table: "ReviewNodeExecutions",
                columns: new[] { "RunId", "Iteration" });

            migrationBuilder.CreateIndex(
                name: "IX_ReviewRuns_Status_UpdatedAt",
                table: "ReviewRuns",
                columns: new[] { "Status", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ReviewRuns_SubjectId",
                table: "ReviewRuns",
                column: "SubjectId");

            migrationBuilder.CreateIndex(
                name: "IX_ReviewRuns_WorkflowRevisionId",
                table: "ReviewRuns",
                column: "WorkflowRevisionId");

            migrationBuilder.CreateIndex(
                name: "IX_ReviewSubjects_Host_Repo_PrNumber",
                table: "ReviewSubjects",
                columns: new[] { "Host", "Repo", "PrNumber" });

            migrationBuilder.CreateIndex(
                name: "IX_ReviewWorkflowRevisions_Hash",
                table: "ReviewWorkflowRevisions",
                column: "Hash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Operator rule (2026-11-07): Down fails safely. Dropping these tables would lose every
            // review run, finding, response and gate decision.
            throw new NotSupportedException(
                "The review engine tables hold review history; a downgrade cannot drop them.");
        }
    }
}
