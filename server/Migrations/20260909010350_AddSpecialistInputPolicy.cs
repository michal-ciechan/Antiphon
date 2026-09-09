using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Antiphon.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddSpecialistInputPolicy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SpecialistInputPolicyJson",
                table: "SessionQueuedMessages",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SpecialistInputPolicyJson",
                table: "AgentTasks",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SpecialistInputPolicyJson",
                table: "SessionQueuedMessages");

            migrationBuilder.DropColumn(
                name: "SpecialistInputPolicyJson",
                table: "AgentTasks");
        }
    }
}
