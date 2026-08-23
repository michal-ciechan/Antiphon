using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Antiphon.Server.Migrations
{
    /// <summary>
    /// CARD-0106 gap 2: project-level default launch env, inherited by agents and pool
    /// delegates under the project unless a more specific layer sets the same variable.
    /// Hand-written (running daemons lock bin/); snapshot is updated to match.
    /// </summary>
    [DbContext(typeof(AppDbContext))]
    [Migration("20260823210100_AddProjectDefaultLaunchEnv")]
    public partial class AddProjectDefaultLaunchEnv : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DefaultLaunchEnvJson",
                table: "Projects",
                type: "jsonb",
                nullable: false,
                defaultValue: "{}");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DefaultLaunchEnvJson",
                table: "Projects");
        }
    }
}
