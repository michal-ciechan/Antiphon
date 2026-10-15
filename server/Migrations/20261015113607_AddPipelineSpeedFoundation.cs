using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Antiphon.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddPipelineSpeedFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CardCompletionAcceptances",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SchemaVersion = table.Column<int>(type: "integer", nullable: false),
                    CardId = table.Column<Guid>(type: "uuid", nullable: false),
                    BoardId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: true),
                    Revision = table.Column<int>(type: "integer", nullable: false),
                    ExpectedCardToken = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkflowRunId = table.Column<Guid>(type: "uuid", nullable: true),
                    PipelineDefinitionRevisionId = table.Column<Guid>(type: "uuid", nullable: true),
                    CurrentStageId = table.Column<Guid>(type: "uuid", nullable: true),
                    WorkflowUpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ImplementationTaskId = table.Column<Guid>(type: "uuid", nullable: false),
                    RepositoryPath = table.Column<string>(type: "text", nullable: false),
                    SourceRef = table.Column<string>(type: "text", nullable: false),
                    ReviewedSourceSha = table.Column<string>(type: "text", nullable: false),
                    ReviewEvidenceId = table.Column<Guid>(type: "uuid", nullable: false),
                    OperationId = table.Column<Guid>(type: "uuid", nullable: true),
                    LandedSha = table.Column<string>(type: "text", nullable: true),
                    PolicyRevision = table.Column<string>(type: "text", nullable: false),
                    DecisionRevision = table.Column<string>(type: "text", nullable: false),
                    AcceptanceItemsJson = table.Column<string>(type: "text", nullable: false),
                    DeploymentItemsJson = table.Column<string>(type: "text", nullable: false),
                    EmptyObligationsDecisionJson = table.Column<string>(type: "text", nullable: true),
                    CompanionObligationJson = table.Column<string>(type: "text", nullable: false),
                    ReadyForRelease = table.Column<bool>(type: "boolean", nullable: false),
                    Actor = table.Column<string>(type: "text", nullable: false),
                    RecordedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CardCompletionAcceptances", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PipelineHandoffActions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SchemaVersion = table.Column<int>(type: "integer", nullable: false),
                    PredecessorTaskId = table.Column<Guid>(type: "uuid", nullable: false),
                    Attempt = table.Column<int>(type: "integer", nullable: false),
                    SettlementEventId = table.Column<Guid>(type: "uuid", nullable: false),
                    NextStage = table.Column<int>(type: "integer", nullable: false),
                    CardId = table.Column<Guid>(type: "uuid", nullable: true),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: true),
                    WorkflowRunId = table.Column<Guid>(type: "uuid", nullable: true),
                    PipelineDefinitionRevisionId = table.Column<Guid>(type: "uuid", nullable: true),
                    CurrentStageId = table.Column<Guid>(type: "uuid", nullable: true),
                    WorkflowUpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    NotificationId = table.Column<Guid>(type: "uuid", nullable: true),
                    QueueId = table.Column<Guid>(type: "uuid", nullable: true),
                    RecipientSessionId = table.Column<Guid>(type: "uuid", nullable: true),
                    RecipientGeneration = table.Column<string>(type: "text", nullable: true),
                    ReceiptSequence = table.Column<long>(type: "bigint", nullable: true),
                    WireDigest = table.Column<string>(type: "text", nullable: true),
                    SettledAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    NotifiedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ReceivedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ConsumedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    EligibleAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ChildCreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ChildDispatchedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ReasonCode = table.Column<string>(type: "text", nullable: true),
                    NextObservationAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RequestDigest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Actor = table.Column<string>(type: "text", nullable: false),
                    ChildTaskId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PipelineHandoffActions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ReviewEvidenceCorrections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SchemaVersion = table.Column<int>(type: "integer", nullable: false),
                    ReviewTaskId = table.Column<Guid>(type: "uuid", nullable: false),
                    PredecessorOutcomeId = table.Column<Guid>(type: "uuid", nullable: false),
                    SubjectTaskId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: true),
                    BoardId = table.Column<Guid>(type: "uuid", nullable: true),
                    CardId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReportDigest = table.Column<string>(type: "text", nullable: false),
                    SourceSha = table.Column<string>(type: "text", nullable: false),
                    SourceRef = table.Column<string>(type: "text", nullable: false),
                    RepositoryPath = table.Column<string>(type: "text", nullable: false),
                    SelectionPath = table.Column<string>(type: "text", nullable: false),
                    SelectionCommitSha = table.Column<string>(type: "text", nullable: false),
                    SelectionSection = table.Column<string>(type: "text", nullable: false),
                    SelectionDigest = table.Column<string>(type: "text", nullable: false),
                    RequiredRowsJson = table.Column<string>(type: "text", nullable: false),
                    CanonicalBundleDigest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ImportedEvidenceIdentity = table.Column<string>(type: "text", nullable: false),
                    ProducerIdentity = table.Column<string>(type: "text", nullable: false),
                    ValidationResultJson = table.Column<string>(type: "text", nullable: false),
                    Actor = table.Column<string>(type: "text", nullable: false),
                    Reason = table.Column<string>(type: "text", nullable: false),
                    RecordedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SuccessorOutcomeId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReviewEvidenceCorrections", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CardCompletionReceipts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SchemaVersion = table.Column<int>(type: "integer", nullable: false),
                    CardId = table.Column<Guid>(type: "uuid", nullable: false),
                    OperationId = table.Column<Guid>(type: "uuid", nullable: false),
                    AcceptanceRevision = table.Column<int>(type: "integer", nullable: false),
                    AcceptanceId = table.Column<Guid>(type: "uuid", nullable: false),
                    ImplementationTaskId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReviewEvidenceId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReviewedSourceSha = table.Column<string>(type: "text", nullable: false),
                    LandedSha = table.Column<string>(type: "text", nullable: false),
                    OriginalCardToken = table.Column<Guid>(type: "uuid", nullable: false),
                    ResultCardToken = table.Column<Guid>(type: "uuid", nullable: false),
                    MoveRevisionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Actor = table.Column<string>(type: "text", nullable: false),
                    RecordedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CardCompletionReceipts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CardCompletionReceipts_CardCompletionAcceptances_Acceptance~",
                        column: x => x.AcceptanceId,
                        principalTable: "CardCompletionAcceptances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PipelineHandoffObservations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ActionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Sequence = table.Column<long>(type: "bigint", nullable: false),
                    ReasonCode = table.Column<string>(type: "text", nullable: false),
                    Detail = table.Column<string>(type: "text", nullable: false),
                    Actor = table.Column<string>(type: "text", nullable: false),
                    ObservedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    NextObservationAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PipelineHandoffObservations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PipelineHandoffObservations_PipelineHandoffActions_ActionId",
                        column: x => x.ActionId,
                        principalTable: "PipelineHandoffActions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CardCompletionAcceptances_CardId_Revision",
                table: "CardCompletionAcceptances",
                columns: new[] { "CardId", "Revision" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CardCompletionReceipts_AcceptanceId",
                table: "CardCompletionReceipts",
                column: "AcceptanceId");

            migrationBuilder.CreateIndex(
                name: "IX_CardCompletionReceipts_Card_Operation_Acceptance",
                table: "CardCompletionReceipts",
                columns: new[] { "CardId", "OperationId", "AcceptanceRevision" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PipelineHandoffActions_ChildTaskId",
                table: "PipelineHandoffActions",
                column: "ChildTaskId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PipelineHandoffActions_Identity",
                table: "PipelineHandoffActions",
                columns: new[] { "PredecessorTaskId", "Attempt", "SettlementEventId", "NextStage", "WorkflowRunId", "PipelineDefinitionRevisionId", "CurrentStageId", "WorkflowUpdatedAt" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "IX_PipelineHandoffObservations_ActionId_Sequence",
                table: "PipelineHandoffObservations",
                columns: new[] { "ActionId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReviewEvidenceCorrections_Predecessor_Bundle",
                table: "ReviewEvidenceCorrections",
                columns: new[] { "PredecessorOutcomeId", "CanonicalBundleDigest" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CardCompletionReceipts");

            migrationBuilder.DropTable(
                name: "PipelineHandoffObservations");

            migrationBuilder.DropTable(
                name: "ReviewEvidenceCorrections");

            migrationBuilder.DropTable(
                name: "CardCompletionAcceptances");

            migrationBuilder.DropTable(
                name: "PipelineHandoffActions");
        }
    }
}
