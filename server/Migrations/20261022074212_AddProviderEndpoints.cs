using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Antiphon.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddProviderEndpoints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ProviderEndpointsJson",
                table: "Projects",
                type: "jsonb",
                nullable: false,
                defaultValue: "{}");

            migrationBuilder.AddColumn<string>(
                name: "ProviderEndpointJson",
                table: "Agents",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ProviderEndpointMode",
                table: "Agents",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ProviderEndpointsJson",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "ProviderEndpointJson",
                table: "Agents");

            migrationBuilder.DropColumn(
                name: "ProviderEndpointMode",
                table: "Agents");
        }
    }
}
