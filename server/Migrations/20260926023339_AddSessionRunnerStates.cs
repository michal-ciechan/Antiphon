using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Antiphon.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddSessionRunnerStates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SessionRunnerStates",
                columns: table => new
                {
                    RunnerId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Draining = table.Column<bool>(type: "boolean", nullable: false),
                    DrainedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DrainReason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    RedirectTo = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    RetireWhenIdle = table.Column<bool>(type: "boolean", nullable: false),
                    IdleObservedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RetiredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RetireReason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedByTaskId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SessionRunnerStates", x => x.RunnerId);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SessionRunnerStates");
        }
    }
}
