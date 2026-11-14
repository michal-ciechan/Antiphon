using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Antiphon.Tests.TestHelpers;

/// <summary>Detaches the forward-only summary addition from an owned tip clone before a historical rewind.</summary>
internal static class SessionTranscriptSummaryMigrationFixture
{
    public static Task SetMaintenanceAsync(AppDbContext db, bool enabled) =>
        db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"SessionTranscriptSummaryMaintenance\" SET \"Enabled\"={enabled} WHERE \"Id\"");

    public static async Task DetachFromOwnedCloneAsync(IsolatedTestSchema owner)
    {
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(owner.ConnectionString));
        await db.Database.ExecuteSqlRawAsync(
            """
            DROP TRIGGER IF EXISTS c1915_insert ON "TranscriptEntries";
            DROP TRIGGER IF EXISTS c1915_update ON "TranscriptEntries";
            DROP TRIGGER IF EXISTS c1915_delete ON "TranscriptEntries";
            DROP TRIGGER IF EXISTS c1915_truncate ON "TranscriptEntries";
            DROP FUNCTION IF EXISTS c1915_insert();
            DROP FUNCTION IF EXISTS c1915_update();
            DROP FUNCTION IF EXISTS c1915_delete();
            DROP FUNCTION IF EXISTS c1915_truncate();
            DROP TABLE IF EXISTS "SessionTranscriptSummaryMaintenance";
            DROP FUNCTION IF EXISTS c1915_maintenance_transition();
            DROP FUNCTION IF EXISTS c1915_maintenance_row_only();
            DROP TABLE IF EXISTS "SessionTranscriptSummaries";
            DELETE FROM "__EFMigrationsHistory"
            WHERE "MigrationId" = '20261114063858_AddSessionTranscriptSummaries';
            """);
    }
}
