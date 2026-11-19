using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Antiphon.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddLandVerificationFailure : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("SET LOCAL lock_timeout = '5s';");

            migrationBuilder.AddColumn<string>(
                name: "VerificationFailingProjects",
                table: "AgentTaskLandings",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VerificationFailureOutput",
                table: "AgentTaskLandings",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VerificationFailureStep",
                table: "AgentTaskLandings",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_AgentTaskLandings_TargetBuildFailure",
                table: "AgentTaskLandings",
                columns: new[] { "TargetBeforeSha", "UpdatedAt" },
                filter: "\"VerificationFailureStep\" = 'build'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException(
                "Recorded land verification failures cannot be discarded by a downgrade.");
        }
    }
}
