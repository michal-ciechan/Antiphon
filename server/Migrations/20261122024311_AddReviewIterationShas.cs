using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Antiphon.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddReviewIterationShas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BaseSha",
                table: "ReviewIterations",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HeadSha",
                table: "ReviewIterations",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Operator rule (2026-11-07): Down fails safely. Dropping these columns would lose the
            // SHAs each review iteration pinned, which the next iteration's delta reviews from.
            throw new NotSupportedException(
                "The review iteration SHAs hold review history; a downgrade cannot drop them.");
        }
    }
}
