using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Antiphon.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddReviewedLandRecovery : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "RecoveryAdoptedAt",
                table: "AgentTaskLandRequests",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RecoveryLocalBeforeSha",
                table: "AgentTaskLandRequests",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RecoveryMode",
                table: "AgentTaskLandRequests",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "RecoveryOwnerRemoteAfterSha",
                table: "AgentTaskLandRequests",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RecoveryOwnerRemoteBeforeSha",
                table: "AgentTaskLandRequests",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RecoveryOwnerStatus",
                table: "AgentTaskLandRequests",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RecoveryRelationship",
                table: "AgentTaskLandRequests",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RecoverySourceFingerprint",
                table: "AgentTaskLandRequests",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RecoverySourceFullRef",
                table: "AgentTaskLandRequests",
                type: "character varying(400)",
                maxLength: 400,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RecoverySourceTaskId",
                table: "AgentTaskLandRequests",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RecoveryStartBaseSha",
                table: "AgentTaskLandRequests",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SupersedesRequestId",
                table: "AgentTaskLandRequests",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RecoveryLocalBeforeSha",
                table: "AgentTaskLandings",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RecoveryMode",
                table: "AgentTaskLandings",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "RecoveryOwnerRemoteAfterSha",
                table: "AgentTaskLandings",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RecoveryOwnerRemoteBeforeSha",
                table: "AgentTaskLandings",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RecoveryOwnerStatus",
                table: "AgentTaskLandings",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RecoveryRelationship",
                table: "AgentTaskLandings",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RecoverySourceFullRef",
                table: "AgentTaskLandings",
                type: "character varying(400)",
                maxLength: 400,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RecoverySourceTaskId",
                table: "AgentTaskLandings",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RecoveryStartBaseSha",
                table: "AgentTaskLandings",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SupersedesRequestId",
                table: "AgentTaskLandings",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RecoveryAdoptedAt",
                table: "AgentTaskLandRequests");

            migrationBuilder.DropColumn(
                name: "RecoveryLocalBeforeSha",
                table: "AgentTaskLandRequests");

            migrationBuilder.DropColumn(
                name: "RecoveryMode",
                table: "AgentTaskLandRequests");

            migrationBuilder.DropColumn(
                name: "RecoveryOwnerRemoteAfterSha",
                table: "AgentTaskLandRequests");

            migrationBuilder.DropColumn(
                name: "RecoveryOwnerRemoteBeforeSha",
                table: "AgentTaskLandRequests");

            migrationBuilder.DropColumn(
                name: "RecoveryOwnerStatus",
                table: "AgentTaskLandRequests");

            migrationBuilder.DropColumn(
                name: "RecoveryRelationship",
                table: "AgentTaskLandRequests");

            migrationBuilder.DropColumn(
                name: "RecoverySourceFingerprint",
                table: "AgentTaskLandRequests");

            migrationBuilder.DropColumn(
                name: "RecoverySourceFullRef",
                table: "AgentTaskLandRequests");

            migrationBuilder.DropColumn(
                name: "RecoverySourceTaskId",
                table: "AgentTaskLandRequests");

            migrationBuilder.DropColumn(
                name: "RecoveryStartBaseSha",
                table: "AgentTaskLandRequests");

            migrationBuilder.DropColumn(
                name: "SupersedesRequestId",
                table: "AgentTaskLandRequests");

            migrationBuilder.DropColumn(
                name: "RecoveryLocalBeforeSha",
                table: "AgentTaskLandings");

            migrationBuilder.DropColumn(
                name: "RecoveryMode",
                table: "AgentTaskLandings");

            migrationBuilder.DropColumn(
                name: "RecoveryOwnerRemoteAfterSha",
                table: "AgentTaskLandings");

            migrationBuilder.DropColumn(
                name: "RecoveryOwnerRemoteBeforeSha",
                table: "AgentTaskLandings");

            migrationBuilder.DropColumn(
                name: "RecoveryOwnerStatus",
                table: "AgentTaskLandings");

            migrationBuilder.DropColumn(
                name: "RecoveryRelationship",
                table: "AgentTaskLandings");

            migrationBuilder.DropColumn(
                name: "RecoverySourceFullRef",
                table: "AgentTaskLandings");

            migrationBuilder.DropColumn(
                name: "RecoverySourceTaskId",
                table: "AgentTaskLandings");

            migrationBuilder.DropColumn(
                name: "RecoveryStartBaseSha",
                table: "AgentTaskLandings");

            migrationBuilder.DropColumn(
                name: "SupersedesRequestId",
                table: "AgentTaskLandings");
        }
    }
}
