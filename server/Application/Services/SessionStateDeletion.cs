using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// Board and project deletion remove sessions with bulk SQL, outside the ingest path.
/// The affected gates are held across that transaction. A commit that lands reseeds
/// (a deleted session becomes Missing). A commit that does not land leaves the cache
/// alone. When rollback itself fails, the outcome is unknown and the cache reloads.
/// </summary>
internal static class SessionStateDeletion
{
    public static async Task RunAsync(
        AppDbContext db,
        SessionStateStore? states,
        IReadOnlyList<Guid> boardIds,
        Func<CancellationToken, Task> deleteWithinTransaction,
        CancellationToken ct)
    {
        if (db.Database.CurrentTransaction is not null || System.Transactions.Transaction.Current is not null)
            throw new InvalidOperationException("Cascade deletion must own its commit boundary.");

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var mutation = await FenceAsync(db, states, boardIds, ct);
        try
        {
            try
            {
                await deleteWithinTransaction(ct);
                await transaction.CommitAsync(ct);
            }
            catch
            {
                if (!await RolledBackAsync(transaction))
                    await mutation.ReconcileAsync();
                throw;
            }

            await mutation.PublishCommittedAsync(CancellationToken.None);
        }
        finally
        {
            await mutation.DisposeAsync();
        }
    }

    private static async Task<SessionStateStore.SessionStateMutation> FenceAsync(
        AppDbContext db, SessionStateStore? states, IReadOnlyList<Guid> boardIds, CancellationToken ct)
    {
        if (states is null)
            return SessionStateStore.InactiveMutation();

        for (var attempt = 0; attempt < 8; attempt++)
        {
            var ids = await SessionIdsAsync(db, boardIds, ct);
            var mutation = await states.BeginMutationAsync(ids, ct);
            var again = await SessionIdsAsync(db, boardIds, ct);
            if (Same(ids, again))
                return mutation;
            await mutation.DisposeAsync();
        }

        throw new InvalidOperationException("Session membership did not settle during cascade deletion.");
    }

    private static async Task<Guid[]> SessionIdsAsync(
        AppDbContext db, IReadOnlyList<Guid> boardIds, CancellationToken ct)
    {
        if (boardIds.Count == 0)
            return [];

        return await (
            from session in db.AgentSessions
            join card in db.Cards on session.CardId equals card.Id
            where boardIds.Contains(card.BoardId)
            select session.Id).ToArrayAsync(ct);
    }

    private static bool Same(Guid[] left, Guid[] right) =>
        left.Length == right.Length && new HashSet<Guid>(left).SetEquals(right);

    private static async Task<bool> RolledBackAsync(IDbContextTransaction transaction)
    {
        try
        {
            await transaction.RollbackAsync(CancellationToken.None);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
