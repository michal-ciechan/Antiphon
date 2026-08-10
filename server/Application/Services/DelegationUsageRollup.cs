using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// One task's token spend, read off its delegate session's stored transcript.
///
/// Two details are load-bearing.
///
/// GROUPING: every JSONL line of a single API call carries the same <c>ApiCallId</c> and REPEATS
/// that call's usage numbers verbatim (see <see cref="Domain.Entities.TranscriptEntry"/>), so
/// summing per entry counts each call once per line it produced. On the measured CARD-0023 session
/// that was 104 entries for 57 calls; across the whole dev database it is ~3x. Count each call once.
///
/// WINDOW: a session is not always one task's. A pooled warm delegate takes further work in the
/// same session, and a session can adopt another's transcript entirely (CARD-0006) — so an
/// unbounded whole-session sum charges one task for another's tokens. Bounding by the task's own
/// dispatch-to-settle window is a no-op for a dedicated session and the difference between $0.03
/// and $0.86 for a 22-second delegate whose session outlived it.
/// </summary>
public static class DelegationUsageRollup
{
    /// <param name="afterUtc">Exclusive lower bound — the task's dispatch. Null means no bound.</param>
    /// <param name="untilUtc">Inclusive upper bound — when the task settled (or "now" at settle time).</param>
    public static async Task<TokenSpend> ForSessionAsync(
        AppDbContext db, Guid sessionId, DateTime? afterUtc, DateTime untilUtc, CancellationToken ct)
    {
        var rows = db.TranscriptEntries.Where(t =>
            t.AgentSessionId == sessionId
            && t.InputTokens != null
            // An entry with no timestamp cannot be placed in time. Those are counted rather than
            // dropped — a ceiling is better over-fed than starved — but in practice every stored
            // entry carries one.
            && (t.Timestamp == null
                || ((afterUtc == null || t.Timestamp > afterUtc) && t.Timestamp <= untilUtc)));

        // One row per API call. Max() rather than First() only because it translates — the entries
        // of a call all carry identical usage, so any of them would do.
        var perCall = await rows
            .Where(t => t.ApiCallId != null)
            .GroupBy(t => t.ApiCallId)
            .Select(g => new
            {
                In = g.Max(t => t.InputTokens) ?? 0,
                Read = g.Max(t => t.CacheReadTokens) ?? 0,
                Write = g.Max(t => t.CacheCreationTokens) ?? 0,
                Out = g.Max(t => t.OutputTokens) ?? 0,
            })
            .ToListAsync(ct);

        // Assistant records always carry an ApiCallId in practice, but a synthetic or
        // partially-normalized entry might not — those cannot be attributed to a call, so they are
        // counted per entry. Grouping them by a NULL key would collapse unrelated calls into one.
        var unattributed = await rows
            .Where(t => t.ApiCallId == null)
            .GroupBy(_ => 1)
            .Select(g => new
            {
                In = g.Sum(t => (long?)(t.InputTokens ?? 0)) ?? 0,
                Read = g.Sum(t => (long?)(t.CacheReadTokens ?? 0)) ?? 0,
                Write = g.Sum(t => (long?)(t.CacheCreationTokens ?? 0)) ?? 0,
                Out = g.Sum(t => (long?)(t.OutputTokens ?? 0)) ?? 0,
            })
            .FirstOrDefaultAsync(ct);

        return new TokenSpend(
            perCall.Sum(c => (long)c.In) + (unattributed?.In ?? 0),
            perCall.Sum(c => (long)c.Read) + (unattributed?.Read ?? 0),
            perCall.Sum(c => (long)c.Write) + (unattributed?.Write ?? 0),
            perCall.Sum(c => (long)c.Out) + (unattributed?.Out ?? 0));
    }
}
