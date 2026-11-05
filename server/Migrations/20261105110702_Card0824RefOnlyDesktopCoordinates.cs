using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Antiphon.Server.Migrations
{
    /// <inheritdoc />
    public partial class Card0824RefOnlyDesktopCoordinates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DesktopWorkspaceKind",
                table: "AgentTasks",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "TaskRefWorkspaceBindingId",
                table: "AgentTasks",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SourceDesktopWorkspaceKindSnapshot",
                table: "AgentTaskLandRequests",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "SourceTaskRefWorkspaceBindingIdSnapshot",
                table: "AgentTaskLandRequests",
                type: "uuid",
                nullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "GitDirectory",
                table: "AgentTaskLandings",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AddColumn<int>(
                name: "SourceDesktopWorkspaceKind",
                table: "AgentTaskLandings",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "SourceTaskRefWorkspaceBindingId",
                table: "AgentTaskLandings",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "TaskRefWorkspaceBindings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TaskId = table.Column<Guid>(type: "uuid", nullable: false),
                    RepositoryPath = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    CommonDirectory = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    IdentityPath = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    Branch = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    FullRef = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    SelectedBaseSha = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SelectedBaseRef = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    SelectedBaseSource = table.Column<int>(type: "integer", nullable: false),
                    SelectedBaseTaskId = table.Column<Guid>(type: "uuid", nullable: true),
                    PreparationState = table.Column<int>(type: "integer", nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaskRefWorkspaceBindings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TaskRefWorkspaceBindings_AgentTasks_TaskId",
                        column: x => x.TaskId,
                        principalTable: "AgentTasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TaskRefWorkspaceBindings_RepositoryPath_FullRef",
                table: "TaskRefWorkspaceBindings",
                columns: new[] { "RepositoryPath", "FullRef" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TaskRefWorkspaceBindings_TaskId",
                table: "TaskRefWorkspaceBindings",
                column: "TaskId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TaskRefWorkspaceBindings");

            migrationBuilder.DropColumn(
                name: "DesktopWorkspaceKind",
                table: "AgentTasks");

            migrationBuilder.DropColumn(
                name: "TaskRefWorkspaceBindingId",
                table: "AgentTasks");

            migrationBuilder.DropColumn(
                name: "SourceDesktopWorkspaceKindSnapshot",
                table: "AgentTaskLandRequests");

            migrationBuilder.DropColumn(
                name: "SourceTaskRefWorkspaceBindingIdSnapshot",
                table: "AgentTaskLandRequests");

            migrationBuilder.DropColumn(
                name: "SourceDesktopWorkspaceKind",
                table: "AgentTaskLandings");

            migrationBuilder.DropColumn(
                name: "SourceTaskRefWorkspaceBindingId",
                table: "AgentTaskLandings");

            migrationBuilder.AlterColumn<string>(
                name: "GitDirectory",
                table: "AgentTaskLandings",
                type: "text",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);
        }
    }
}
