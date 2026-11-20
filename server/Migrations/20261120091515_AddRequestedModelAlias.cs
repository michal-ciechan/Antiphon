using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Antiphon.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddRequestedModelAlias : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("SET LOCAL lock_timeout = '5s';");

            migrationBuilder.AddColumn<string>(
                name: "RequestedModelAlias",
                table: "AgentTasks",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException(
                "An explicit requested model alias cannot be discarded by a downgrade.");
        }
    }
}
