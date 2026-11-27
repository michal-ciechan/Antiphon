using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Antiphon.Server.Application.Review;

internal static class ReviewRunWrites
{
    // Take the run lock before EF can lock any of its child rows. PostgreSQL's
    // NO KEY UPDATE conflicts with the SHARE lock used by admitted external writes.
    public static Task LockAsync(AppDbContext db, Guid runId, CancellationToken ct) =>
        db.Database.ExecuteSqlInterpolatedAsync(
            $"""SELECT 1 FROM "ReviewRuns" WHERE "Id" = {runId} FOR NO KEY UPDATE""", ct);

    public static async Task SaveAsync(AppDbContext db, Guid runId, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await LockAsync(db, runId, ct);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }
}
