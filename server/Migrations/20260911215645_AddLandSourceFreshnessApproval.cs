using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Antiphon.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddLandSourceFreshnessApproval : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ReviewedRepositoryPath",
                table: "StageOutcomes",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReviewedSourceRef",
                table: "StageOutcomes",
                type: "character varying(400)",
                maxLength: 400,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReviewedSourceSha",
                table: "StageOutcomes",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ApprovalKind",
                table: "AgentTaskLandRequests",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "ApprovedAt",
                table: "AgentTaskLandRequests",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CandidateSourceSha",
                table: "AgentTaskLandRequests",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExpectedSourceSha",
                table: "AgentTaskLandRequests",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LocalBeforeSha",
                table: "AgentTaskLandRequests",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RemoteSourceFingerprint",
                table: "AgentTaskLandRequests",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RemoteSourceRef",
                table: "AgentTaskLandRequests",
                type: "character varying(400)",
                maxLength: 400,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RemoteSourceSha",
                table: "AgentTaskLandRequests",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RepositoryPathSnapshot",
                table: "AgentTaskLandRequests",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ResolvedSourceSha",
                table: "AgentTaskLandRequests",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ReviewEvidenceId",
                table: "AgentTaskLandRequests",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SchemaVersion",
                table: "AgentTaskLandRequests",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<string>(
                name: "SourceAdvanceChildOperation",
                table: "AgentTaskLandRequests",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SourceAdvanceChildProcessId",
                table: "AgentTaskLandRequests",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "SourceAdvanceChildStartTicks",
                table: "AgentTaskLandRequests",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceCommonDirectory",
                table: "AgentTaskLandRequests",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceFullRefSnapshot",
                table: "AgentTaskLandRequests",
                type: "character varying(400)",
                maxLength: 400,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceGitDirectory",
                table: "AgentTaskLandRequests",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceObservationRef",
                table: "AgentTaskLandRequests",
                type: "character varying(400)",
                maxLength: 400,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "SourceObservedAt",
                table: "AgentTaskLandRequests",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceRefusalReason",
                table: "AgentTaskLandRequests",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SourceRelationship",
                table: "AgentTaskLandRequests",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "SourceResolutionState",
                table: "AgentTaskLandRequests",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "SourceWorktreePath",
                table: "AgentTaskLandRequests",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TargetFullRefSnapshot",
                table: "AgentTaskLandRequests",
                type: "character varying(400)",
                maxLength: 400,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WorktreePathSnapshot",
                table: "AgentTaskLandRequests",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ApprovalKind",
                table: "AgentTaskLandings",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "ApprovalLandRequestId",
                table: "AgentTaskLandings",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ApprovedAt",
                table: "AgentTaskLandings",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PreparationInputSha",
                table: "AgentTaskLandings",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PreviousPreparationOperationId",
                table: "AgentTaskLandings",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ReviewEvidenceId",
                table: "AgentTaskLandings",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReviewedSourceSha",
                table: "AgentTaskLandings",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceRemoteFingerprint",
                table: "AgentTaskLandings",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "SourceRemoteObservedAt",
                table: "AgentTaskLandings",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceRemoteRef",
                table: "AgentTaskLandings",
                type: "character varying(400)",
                maxLength: 400,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceRemoteSha",
                table: "AgentTaskLandings",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_StageOutcomes_SubjectTaskId",
                table: "StageOutcomes",
                column: "SubjectTaskId");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AgentTaskLandRequests_OidShape",
                table: "AgentTaskLandRequests",
                sql: "(\"ExpectedSourceSha\" IS NULL OR ((length(\"ExpectedSourceSha\") = 40 OR length(\"ExpectedSourceSha\") = 64) AND \"ExpectedSourceSha\" ~ '^[0-9a-f]+$')) AND (\"ResolvedSourceSha\" IS NULL OR ((length(\"ResolvedSourceSha\") = 40 OR length(\"ResolvedSourceSha\") = 64) AND \"ResolvedSourceSha\" ~ '^[0-9a-f]+$')) AND (\"LocalBeforeSha\" IS NULL OR ((length(\"LocalBeforeSha\") = 40 OR length(\"LocalBeforeSha\") = 64) AND \"LocalBeforeSha\" ~ '^[0-9a-f]+$')) AND (\"RemoteSourceSha\" IS NULL OR ((length(\"RemoteSourceSha\") = 40 OR length(\"RemoteSourceSha\") = 64) AND \"RemoteSourceSha\" ~ '^[0-9a-f]+$')) AND (\"CandidateSourceSha\" IS NULL OR ((length(\"CandidateSourceSha\") = 40 OR length(\"CandidateSourceSha\") = 64) AND \"CandidateSourceSha\" ~ '^[0-9a-f]+$'))");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AgentTaskLandRequests_V2Approval",
                table: "AgentTaskLandRequests",
                sql: "\"SchemaVersion\" <> 2 OR (\"ExpectedSourceSha\" IS NOT NULL AND (length(\"ExpectedSourceSha\") = 40 OR length(\"ExpectedSourceSha\") = 64) AND \"ExpectedSourceSha\" ~ '^[0-9a-f]+$')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AgentTaskLandings_OidShape",
                table: "AgentTaskLandings",
                sql: "(\"OriginalSourceSha\" = '' OR ((length(\"OriginalSourceSha\") = 40 OR length(\"OriginalSourceSha\") = 64) AND \"OriginalSourceSha\" ~ '^[0-9a-f]+$')) AND (\"ReviewedSourceSha\" IS NULL OR ((length(\"ReviewedSourceSha\") = 40 OR length(\"ReviewedSourceSha\") = 64) AND \"ReviewedSourceSha\" ~ '^[0-9a-f]+$')) AND (\"PreparationInputSha\" IS NULL OR ((length(\"PreparationInputSha\") = 40 OR length(\"PreparationInputSha\") = 64) AND \"PreparationInputSha\" ~ '^[0-9a-f]+$')) AND (\"SourceRemoteSha\" IS NULL OR ((length(\"SourceRemoteSha\") = 40 OR length(\"SourceRemoteSha\") = 64) AND \"SourceRemoteSha\" ~ '^[0-9a-f]+$'))");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AgentTaskLandings_V2Approval",
                table: "AgentTaskLandings",
                sql: "\"SchemaVersion\" <> 2 OR (\"ApprovalLandRequestId\" IS NOT NULL AND \"ReviewedSourceSha\" IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AgentTaskLandings_V2ApprovalEquality",
                table: "AgentTaskLandings",
                sql: "\"SchemaVersion\" <> 2 OR \"ReviewedSourceSha\" = \"OriginalSourceSha\"");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_StageOutcomes_SubjectTaskId",
                table: "StageOutcomes");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AgentTaskLandRequests_OidShape",
                table: "AgentTaskLandRequests");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AgentTaskLandRequests_V2Approval",
                table: "AgentTaskLandRequests");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AgentTaskLandings_OidShape",
                table: "AgentTaskLandings");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AgentTaskLandings_V2Approval",
                table: "AgentTaskLandings");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AgentTaskLandings_V2ApprovalEquality",
                table: "AgentTaskLandings");

            migrationBuilder.DropColumn(
                name: "ReviewedRepositoryPath",
                table: "StageOutcomes");

            migrationBuilder.DropColumn(
                name: "ReviewedSourceRef",
                table: "StageOutcomes");

            migrationBuilder.DropColumn(
                name: "ReviewedSourceSha",
                table: "StageOutcomes");

            migrationBuilder.DropColumn(
                name: "ApprovalKind",
                table: "AgentTaskLandRequests");

            migrationBuilder.DropColumn(
                name: "ApprovedAt",
                table: "AgentTaskLandRequests");

            migrationBuilder.DropColumn(
                name: "CandidateSourceSha",
                table: "AgentTaskLandRequests");

            migrationBuilder.DropColumn(
                name: "ExpectedSourceSha",
                table: "AgentTaskLandRequests");

            migrationBuilder.DropColumn(
                name: "LocalBeforeSha",
                table: "AgentTaskLandRequests");

            migrationBuilder.DropColumn(
                name: "RemoteSourceFingerprint",
                table: "AgentTaskLandRequests");

            migrationBuilder.DropColumn(
                name: "RemoteSourceRef",
                table: "AgentTaskLandRequests");

            migrationBuilder.DropColumn(
                name: "RemoteSourceSha",
                table: "AgentTaskLandRequests");

            migrationBuilder.DropColumn(
                name: "RepositoryPathSnapshot",
                table: "AgentTaskLandRequests");

            migrationBuilder.DropColumn(
                name: "ResolvedSourceSha",
                table: "AgentTaskLandRequests");

            migrationBuilder.DropColumn(
                name: "ReviewEvidenceId",
                table: "AgentTaskLandRequests");

            migrationBuilder.DropColumn(
                name: "SchemaVersion",
                table: "AgentTaskLandRequests");

            migrationBuilder.DropColumn(
                name: "SourceAdvanceChildOperation",
                table: "AgentTaskLandRequests");

            migrationBuilder.DropColumn(
                name: "SourceAdvanceChildProcessId",
                table: "AgentTaskLandRequests");

            migrationBuilder.DropColumn(
                name: "SourceAdvanceChildStartTicks",
                table: "AgentTaskLandRequests");

            migrationBuilder.DropColumn(
                name: "SourceCommonDirectory",
                table: "AgentTaskLandRequests");

            migrationBuilder.DropColumn(
                name: "SourceFullRefSnapshot",
                table: "AgentTaskLandRequests");

            migrationBuilder.DropColumn(
                name: "SourceGitDirectory",
                table: "AgentTaskLandRequests");

            migrationBuilder.DropColumn(
                name: "SourceObservationRef",
                table: "AgentTaskLandRequests");

            migrationBuilder.DropColumn(
                name: "SourceObservedAt",
                table: "AgentTaskLandRequests");

            migrationBuilder.DropColumn(
                name: "SourceRefusalReason",
                table: "AgentTaskLandRequests");

            migrationBuilder.DropColumn(
                name: "SourceRelationship",
                table: "AgentTaskLandRequests");

            migrationBuilder.DropColumn(
                name: "SourceResolutionState",
                table: "AgentTaskLandRequests");

            migrationBuilder.DropColumn(
                name: "SourceWorktreePath",
                table: "AgentTaskLandRequests");

            migrationBuilder.DropColumn(
                name: "TargetFullRefSnapshot",
                table: "AgentTaskLandRequests");

            migrationBuilder.DropColumn(
                name: "WorktreePathSnapshot",
                table: "AgentTaskLandRequests");

            migrationBuilder.DropColumn(
                name: "ApprovalKind",
                table: "AgentTaskLandings");

            migrationBuilder.DropColumn(
                name: "ApprovalLandRequestId",
                table: "AgentTaskLandings");

            migrationBuilder.DropColumn(
                name: "ApprovedAt",
                table: "AgentTaskLandings");

            migrationBuilder.DropColumn(
                name: "PreparationInputSha",
                table: "AgentTaskLandings");

            migrationBuilder.DropColumn(
                name: "PreviousPreparationOperationId",
                table: "AgentTaskLandings");

            migrationBuilder.DropColumn(
                name: "ReviewEvidenceId",
                table: "AgentTaskLandings");

            migrationBuilder.DropColumn(
                name: "ReviewedSourceSha",
                table: "AgentTaskLandings");

            migrationBuilder.DropColumn(
                name: "SourceRemoteFingerprint",
                table: "AgentTaskLandings");

            migrationBuilder.DropColumn(
                name: "SourceRemoteObservedAt",
                table: "AgentTaskLandings");

            migrationBuilder.DropColumn(
                name: "SourceRemoteRef",
                table: "AgentTaskLandings");

            migrationBuilder.DropColumn(
                name: "SourceRemoteSha",
                table: "AgentTaskLandings");
        }
    }
}
