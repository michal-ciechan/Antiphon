using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Antiphon.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddTaskWorktreeMirrorBinding : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte[]>(
                name: "ImmutableRequest",
                table: "CardWorktreeCleanupEndpoints",
                type: "bytea",
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "ImportedReceipt",
                table: "CardWorktreeCleanupEndpoints",
                type: "bytea",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "MirrorBindingId",
                table: "CardWorktreeCleanupEndpoints",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "TaskWorktreeMirrorBindings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TaskId = table.Column<Guid>(type: "uuid", nullable: false),
                    Attempt = table.Column<int>(type: "integer", nullable: false),
                    PreparationId = table.Column<Guid>(type: "uuid", nullable: false),
                    RunnerId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    RunnerStoreId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    BindingId = table.Column<Guid>(type: "uuid", nullable: false),
                    RepositoryIdentity = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    CommonDirectory = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    GitDirectory = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    WorktreePath = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    SourceFullRef = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    CreationBaselineSha = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaskWorktreeMirrorBindings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TaskWorktreeMirrorBindings_AgentTasks_TaskId",
                        column: x => x.TaskId,
                        principalTable: "AgentTasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CardWorktreeCleanupEndpoints_MirrorBindingId",
                table: "CardWorktreeCleanupEndpoints",
                column: "MirrorBindingId");

            migrationBuilder.CreateIndex(
                name: "IX_TaskWorktreeMirrorBindings_PreparationId",
                table: "TaskWorktreeMirrorBindings",
                column: "PreparationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TaskWorktreeMirrorBindings_Task_Attempt_Store_Binding",
                table: "TaskWorktreeMirrorBindings",
                columns: new[] { "TaskId", "Attempt", "RunnerStoreId", "BindingId" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_CardWorktreeCleanupEndpoints_TaskWorktreeMirrorBindings_Mir~",
                table: "CardWorktreeCleanupEndpoints",
                column: "MirrorBindingId",
                principalTable: "TaskWorktreeMirrorBindings",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CardWorktreeCleanupEndpoints_TaskWorktreeMirrorBindings_Mir~",
                table: "CardWorktreeCleanupEndpoints");

            migrationBuilder.DropTable(
                name: "TaskWorktreeMirrorBindings");

            migrationBuilder.DropIndex(
                name: "IX_CardWorktreeCleanupEndpoints_MirrorBindingId",
                table: "CardWorktreeCleanupEndpoints");

            migrationBuilder.DropColumn(
                name: "ImmutableRequest",
                table: "CardWorktreeCleanupEndpoints");

            migrationBuilder.DropColumn(
                name: "ImportedReceipt",
                table: "CardWorktreeCleanupEndpoints");

            migrationBuilder.DropColumn(
                name: "MirrorBindingId",
                table: "CardWorktreeCleanupEndpoints");
        }
    }
}
