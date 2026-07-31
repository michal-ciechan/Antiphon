using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Antiphon.Server.Migrations
{
    // Hand-written (no .Designer.cs) — same bin-lock reason as the migrations before it.
    /// <summary>
    /// ModelFamily (provider-specific: Opus=0, Sonnet=1, Fable=2, Haiku=3) becomes the generic
    /// ModelLevel (Frontier=0, High=1, Medium=2, Low=3), remapping stored values so every agent
    /// keeps launching with the same model: Opus→High, Sonnet→Medium, Fable→Frontier, Haiku→Low.
    /// Default moves from 0 (Opus) to 1 (High — the same Opus tier under the new scale).
    /// </summary>
    [DbContext(typeof(AppDbContext))]
    [Migration("20260731220000_RenameModelFamilyToLevel")]
    public partial class RenameModelFamilyToLevel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(name: "ModelFamily", table: "Agents", newName: "ModelLevel");
            migrationBuilder.Sql("""
                UPDATE "Agents" SET "ModelLevel" = CASE "ModelLevel"
                    WHEN 0 THEN 1  -- Opus   -> High
                    WHEN 1 THEN 2  -- Sonnet -> Medium
                    WHEN 2 THEN 0  -- Fable  -> Frontier
                    WHEN 3 THEN 3  -- Haiku  -> Low
                    ELSE 1 END;
                """);
            migrationBuilder.AlterColumn<int>(
                name: "ModelLevel",
                table: "Agents",
                type: "integer",
                nullable: false,
                defaultValue: 1,
                oldClrType: typeof(int),
                oldType: "integer",
                oldDefaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE "Agents" SET "ModelLevel" = CASE "ModelLevel"
                    WHEN 1 THEN 0  -- High     -> Opus
                    WHEN 2 THEN 1  -- Medium   -> Sonnet
                    WHEN 0 THEN 2  -- Frontier -> Fable
                    WHEN 3 THEN 3  -- Low      -> Haiku
                    ELSE 0 END;
                """);
            migrationBuilder.AlterColumn<int>(
                name: "ModelLevel",
                table: "Agents",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer",
                oldDefaultValue: 1);
            migrationBuilder.RenameColumn(name: "ModelLevel", table: "Agents", newName: "ModelFamily");
        }
    }
}
