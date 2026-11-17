using System.Collections.Immutable;
using System.Data;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// The prompts a turn of THIS task could have answered, in sequence order. Shared by settlement
/// (<c>AgentTaskReplyService</c>) and the delivery watchdog
/// (<c>AgentTaskDispatcher.FailNeverStartedAsync</c>) so the two cannot disagree on what counts as
/// "this task started". CARD-0077: a reused session inherits the previous task's transcript, so
/// "any entry at all" is not the same question as "a real prompt landed after this dispatch".
///
/// <para><b>Bounded at the task's dispatch.</b> A warm-pool delegate's session outlives its tasks,
/// so the newest TurnEnd on it stays the PREVIOUS task's until this one ends a turn of its own.
/// A prompt written before this task was dispatched cannot be its brief.</para>
///
/// <para><b>A drained composer-queue command is a prompt too</b> (CARD-0135). Claude Code writes a
/// body accepted while the TUI was busy as an <c>attachment</c>/<c>queued_command</c>, which
/// normalizes to <see cref="TranscriptKinds.QueuedUserPrompt"/> — there is no accompanying
/// <c>user</c> record. Counting only <see cref="TranscriptKinds.UserPrompt"/> made the watchdog
/// fail a task whose brief had arrived and been worked on, and made settlement walk past the
/// queued row onto someone else's answer. Both consumers read this type, so both kinds move
/// together; a watchdog-only widening would convert the 10-minute false kill into a silent strand
/// whose report is discarded at the role deadline.</para>
///
/// <para><b>The enqueue clock is the right clock for the bound.</b> <c>QueuedUserPrompt</c>
/// carries the attachment's timestamp, which is when the body was typed, not when it drained. A
/// body typed before this task was dispatched cannot be its brief, however late it drains, so
/// <c>Timestamp &gt; dispatchedAt</c> is the same question it is for a typed row. CARD-0132 S2.4
/// kept this kind inert for working/idle because those rules rank one record against another
/// record's timestamp and the enqueue clock is earlier than records that precede it in file order.
/// This type never does that: it orders by <c>Sequence</c> (drain order) and uses
/// <c>Timestamp</c> only as a lower bound against <c>dispatchedAt</c>, an external clock. The
/// working/idle lockstep does not consume this type and must not start.</para>
///
/// <para><b>Compaction's own records are not prompts.</b> Between the <c>/compact</c> and the brief
/// a manual compaction writes four USER records that nobody typed as a prompt: the
/// <c>&lt;command-name&gt;</c> wrapper, the <c>&lt;local-command-stdout&gt;</c> result, the
/// synthetic continuation prompt, and the raw echo of the typed command line (CARD-0041). Skipping
/// them is what lets settlement walk back to the brief, and what lets the watchdog see a reuse
/// dispatch whose brief never arrived.</para>
///
/// <para><see cref="Domain.Entities.TranscriptEntry.Timestamp"/> is the clock, not <c>CreatedAt</c>:
/// stored sequences are ARRIVAL-ordered and a backfill re-persists old records long after the fact
/// (2026-08-08), while the record's own timestamp survives reordering. An entry with no timestamp
/// cannot be placed in time and is KEPT rather than dropped — the conservative direction is to let
/// the marker gate (settlement) or "started" (watchdog) judge it.</para>
///
/// <para><b>A background subagent's notification is not a prompt either</b> (CARD-0046 slice 4).
/// Settlement keeps those separately because the launches they answer are what it waits for; the
/// watchdog treats them as housekeeping, same as the other three, because nobody typed them as
/// this task's brief.</para>
/// </summary>
internal static class TranscriptPromptSpan
{
    /// <summary>A candidate turn-opening prompt: everything the walk-back needs to judge one.</summary>
    internal sealed record PromptRow(long Sequence, string? Text, DateTime? Timestamp, string Kind);

    /// <param name="TurnPrompts">
    /// Typed or queued prompt records a turn could actually be answering, in sequence order.
    /// Time-filtered and housekeeping-filtered, including the provider allowlist (CARD-0714).
    /// </param>
    /// <param name="Notifications">
    /// The background-subagent notifications among the time-filtered rows — skipped as turn
    /// prompts, kept because they are what proves a launch came back (CARD-0046 slice 4).
    /// </param>
    /// <param name="RawPrompts">
    /// Every typed or queued prompt row, including housekeeping and prompts whose native
    /// timestamp is at or before dispatch. Settlement uses these as delimiters and as the
    /// pre-dispatch candidate the time check then rejects. Existing consumers keep reading
    /// <see cref="TurnPrompts"/>.
    /// </param>
    internal sealed record Result(
        IReadOnlyList<PromptRow> TurnPrompts,
        IReadOnlyList<PromptRow> Notifications,
        IReadOnlyList<PromptRow> RawPrompts);

    internal static Task<Result> LoadAsync(
        AppDbContext db, Guid sessionId, DateTime? dispatchedAt, CancellationToken ct) =>
        LoadAsync(db, sessionId, dispatchedAt, PromptCache.Shared, ct);

    internal static async Task<Result> LoadAsync(
        AppDbContext db, Guid sessionId, DateTime? dispatchedAt, PromptCache cache, CancellationToken ct)
    {
        var sessionKind = await db.AgentSessions.AsNoTracking()
            .Where(s => s.Id == sessionId)
            .Select(s => (AgentKind?)s.AgentKind)
            .FirstOrDefaultAsync(ct) ?? AgentKind.Raw;

        var rows = await LoadPromptRowsAsync(db, sessionId, cache, ct);

        var inWindow = rows
            .Where(r => dispatchedAt == null || r.Timestamp == null || r.Timestamp > dispatchedAt)
            .ToList();

        // The wrappers are the PROOF that a raw "/compact …" line was a command rather than a
        // prompt that happens to start with a slash — so the names come out of the span itself.
        var invoked = inWindow
            .Select(r => TranscriptKinds.TryReadLocalCommandName(TranscriptKinds.UserPrompt, r.Text))
            .OfType<string>()
            .ToHashSet(StringComparer.Ordinal);

        var rulesIds = (await db.SessionQueuedMessages.AsNoTracking()
            .Where(m => m.AgentSessionId == sessionId
                && m.Origin == QueuedMessageOrigin.System
                && m.RulesRefreshKey != null)
            .Select(m => m.Id)
            .ToListAsync(ct)).ToHashSet();

        return new Result(
            inWindow.Where(r => !IsHousekeepingPrompt(r.Text, invoked)
                && !TaskReportHousekeeping.IsAllowlistedPrompt(sessionKind, r.Text, rulesIds)).ToList(),
            inWindow.Where(r => TranscriptKinds.IsTaskNotificationPrompt(TranscriptKinds.UserPrompt, r.Text))
                .ToList(),
            rows);
    }

    // CARD-1969 S2 (docs/superpowers/plans/2026-11-17-card-1969-prompt-span-plan.md §3). The
    // unfiltered prompt rows are cached per (database, session) with their ids. The stored stamp
    // is DERIVED from the stored rows, never read by a second statement, so a cached list and its
    // stamp always describe one snapshot. A hit is one statement (the live stamp) and reads no
    // prompt text. A miss proves the cached prefix and reads only the tail, both inside one
    // REPEATABLE READ snapshot; when the proof fails it is today's full read in that snapshot.
    // LoadAsync still filters the complete list in C#, so nothing downstream sees a partial span.
    // Accepted hole (plan D-4): an in-place update of a stored prompt row's Text, Timestamp, or a
    // UserPrompt/QueuedUserPrompt Kind swap does not move the stamp. No production writer does
    // that (docs/session-runtime-invariants.md, Prompt-span cache).
    private static async Task<IReadOnlyList<PromptRow>> LoadPromptRowsAsync(
        AppDbContext db, Guid sessionId, PromptCache cache, CancellationToken ct)
    {
        // An ambient transaction: no nested transaction, and no entry published from its snapshot.
        if (db.Database.CurrentTransaction is not null
            || db.Database.GetConnectionString() is not { } connection)
            return (await ReadRowsAsync(db, sessionId, after: null, ReadTag, ct)).Rows;

        var key = new PromptCache.Key(connection, sessionId);
        var cached = cache.Lookup(key);
        if (cached is null)
        {
            // Cold: one statement is one snapshot, and the stamp is derived from its rows.
            var cold = await ReadRowsAsync(db, sessionId, after: null, ReadTag, ct);
            cache.Store(key, cold);
            return cold.Rows;
        }

        if ((await ReadStampAsync(db, sessionId, ct)).Stamp == cached.Stamp)
            return cached.Rows;

        PromptCache.Entry fresh;
        await using (var tx = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct))
        {
            fresh = await ReadTailOrAllAsync(db, sessionId, cached, ct);
            await tx.CommitAsync(ct);
        }

        cache.Store(key, fresh);
        return fresh.Rows;
    }

    /// <summary>
    /// Inside the miss transaction. The tail is taken only when the proof, read in THIS snapshot
    /// (not the stamp that missed before the transaction), shows the cached rows are still the
    /// prompt rows at or below the cached anchor; otherwise the full projection, same snapshot.
    /// </summary>
    private static async Task<PromptCache.Entry> ReadTailOrAllAsync(
        AppDbContext db, Guid sessionId, PromptCache.Entry cached, CancellationToken ct)
    {
        if (cached.Stamp.MaxSequence is long anchor)
        {
            var proof = await ReadStampAsync(db, sessionId, ct, anchor);
            if (proof.Stamp.FirstId == cached.Stamp.FirstId
                && proof.PrefixCount == cached.Stamp.Count
                && proof.AnchorId == cached.Stamp.TopId
                && proof.Stamp.MaxSequence > anchor)
            {
                var tail = await ReadRowsAsync(db, sessionId, anchor, TailTag, ct);
                if (tail.Rows.Length > 0
                    && tail.Rows.Length == proof.Stamp.Count - cached.Stamp.Count
                    && tail.Ids[^1] == proof.Stamp.TopId)
                    return cached.Append(tail);
            }
        }

        return await ReadRowsAsync(db, sessionId, after: null, ReadTag, ct);
    }

    private const string ReadTag = "prompt-span.read";
    private const string TailTag = "prompt-span.tail";

    private static async Task<PromptCache.Entry> ReadRowsAsync(
        AppDbContext db, Guid sessionId, long? after, string tag, CancellationToken ct)
    {
        var query = db.TranscriptEntries.AsNoTracking()
            .TagWith(tag)
            .Where(t => t.AgentSessionId == sessionId
                && (t.Kind == TranscriptKinds.UserPrompt
                    || t.Kind == TranscriptKinds.QueuedUserPrompt));
        if (after is long anchor)
            query = query.Where(t => t.Sequence > anchor);
        var read = await query
            .OrderBy(t => t.Sequence)
            .Select(t => new { t.Id, Row = new PromptRow(t.Sequence, t.Text, t.Timestamp, t.Kind) })
            .ToListAsync(ct);

        var rows = ImmutableArray.CreateBuilder<PromptRow>(read.Count);
        var ids = ImmutableArray.CreateBuilder<Guid>(read.Count);
        foreach (var r in read)
        {
            rows.Add(r.Row);
            ids.Add(r.Id);
        }

        return PromptCache.Entry.Derive(rows.MoveToImmutable(), ids.MoveToImmutable());
    }

    // One statement, one snapshot, no Text. A count plus one LIMIT 1 lateral per kind and
    // direction: each lateral is a one-row range of IX_TranscriptEntries_AgentSessionId_Kind_Sequence
    // (Kind IN (...) ORDER BY Sequence LIMIT 1 would scan every prompt entry; plan review A-1).
    private const string StampSelect = """
        SELECT c."Count",
               fu."Sequence" AS "FirstUserSeq", fu."Id" AS "FirstUserId",
               fq."Sequence" AS "FirstQueuedSeq", fq."Id" AS "FirstQueuedId",
               tu."Sequence" AS "TopUserSeq", tu."Id" AS "TopUserId",
               tq."Sequence" AS "TopQueuedSeq", tq."Id" AS "TopQueuedId"
        """;

    // The miss proof for the cached anchor @anchor: prompt rows at or below it, and the prompt id
    // stored at it (the unique (AgentSessionId, Sequence) index).
    private const string ProofSelect = """
        ,
               (SELECT count(*) FROM "TranscriptEntries"
                WHERE "AgentSessionId" = @session AND "Kind" IN (@user, @queued)
                  AND "Sequence" <= @anchor) AS "PrefixCount",
               (SELECT "Id" FROM "TranscriptEntries"
                WHERE "AgentSessionId" = @session AND "Kind" IN (@user, @queued)
                  AND "Sequence" = @anchor) AS "AnchorId"
        """;

    private const string StampFrom = """
        FROM (SELECT count(*) AS "Count" FROM "TranscriptEntries"
              WHERE "AgentSessionId" = @session AND "Kind" IN (@user, @queued)) AS c
        LEFT JOIN LATERAL (SELECT "Sequence", "Id" FROM "TranscriptEntries"
              WHERE "AgentSessionId" = @session AND "Kind" = @user
              ORDER BY "Sequence" ASC LIMIT 1) AS fu ON true
        LEFT JOIN LATERAL (SELECT "Sequence", "Id" FROM "TranscriptEntries"
              WHERE "AgentSessionId" = @session AND "Kind" = @queued
              ORDER BY "Sequence" ASC LIMIT 1) AS fq ON true
        LEFT JOIN LATERAL (SELECT "Sequence", "Id" FROM "TranscriptEntries"
              WHERE "AgentSessionId" = @session AND "Kind" = @user
              ORDER BY "Sequence" DESC LIMIT 1) AS tu ON true
        LEFT JOIN LATERAL (SELECT "Sequence", "Id" FROM "TranscriptEntries"
              WHERE "AgentSessionId" = @session AND "Kind" = @queued
              ORDER BY "Sequence" DESC LIMIT 1) AS tq ON true
        """;

    private const string StampSql = StampSelect + "\n" + StampFrom;
    private const string ProofSql = StampSelect + ProofSelect + "\n" + StampFrom;

    /// <summary>
    /// The live stamp (tag <c>prompt-span.stamp</c>), or with <paramref name="anchor"/> the miss
    /// proof (tag <c>prompt-span.proof</c>): the same stamp plus the prefix count and anchor id.
    /// </summary>
    private static async Task<(Stamp Stamp, long PrefixCount, Guid? AnchorId)> ReadStampAsync(
        AppDbContext db, Guid sessionId, CancellationToken ct, long? anchor = null)
    {
        var parameters = new List<object>
        {
            new NpgsqlParameter("session", sessionId),
            new NpgsqlParameter("user", NpgsqlDbType.Varchar) { Value = TranscriptKinds.UserPrompt },
            new NpgsqlParameter("queued", NpgsqlDbType.Varchar) { Value = TranscriptKinds.QueuedUserPrompt },
        };
        if (anchor is not long at)
        {
            var row = (await db.Database.SqlQueryRaw<StampRow>(StampSql, parameters.ToArray())
                .TagWith("prompt-span.stamp").ToListAsync(ct)).Single();
            return (Stamp.Combine(row.Count, row.FirstUserSeq, row.FirstUserId, row.FirstQueuedSeq,
                row.FirstQueuedId, row.TopUserSeq, row.TopUserId, row.TopQueuedSeq, row.TopQueuedId), 0, null);
        }

        parameters.Add(new NpgsqlParameter("anchor", at));
        var proof = (await db.Database.SqlQueryRaw<ProofRow>(ProofSql, parameters.ToArray())
            .TagWith("prompt-span.proof").ToListAsync(ct)).Single();
        return (Stamp.Combine(proof.Count, proof.FirstUserSeq, proof.FirstUserId, proof.FirstQueuedSeq,
                proof.FirstQueuedId, proof.TopUserSeq, proof.TopUserId, proof.TopQueuedSeq, proof.TopQueuedId),
            proof.PrefixCount, proof.AnchorId);
    }

    /// <summary>Count, first id, max sequence and top id of a session's prompt rows.</summary>
    internal readonly record struct Stamp(long Count, Guid? FirstId, long? MaxSequence, Guid? TopId)
    {
        internal static Stamp Empty { get; } = new(0, null, null, null);

        /// <summary>Sequences are unique per session, so the two kinds never tie.</summary>
        internal static Stamp Combine(
            long count,
            long? firstUserSeq, Guid? firstUserId, long? firstQueuedSeq, Guid? firstQueuedId,
            long? topUserSeq, Guid? topUserId, long? topQueuedSeq, Guid? topQueuedId)
        {
            var firstId = firstQueuedSeq is null || (firstUserSeq is not null && firstUserSeq < firstQueuedSeq)
                ? firstUserId
                : firstQueuedId;
            var userIsTop = topQueuedSeq is null || (topUserSeq is not null && topUserSeq > topQueuedSeq);
            return new Stamp(
                count, firstId, userIsTop ? topUserSeq : topQueuedSeq, userIsTop ? topUserId : topQueuedId);
        }
    }

    private sealed class StampRow
    {
        public long Count { get; set; }
        public long? FirstUserSeq { get; set; }
        public Guid? FirstUserId { get; set; }
        public long? FirstQueuedSeq { get; set; }
        public Guid? FirstQueuedId { get; set; }
        public long? TopUserSeq { get; set; }
        public Guid? TopUserId { get; set; }
        public long? TopQueuedSeq { get; set; }
        public Guid? TopQueuedId { get; set; }
    }

    private sealed class ProofRow
    {
        public long Count { get; set; }
        public long? FirstUserSeq { get; set; }
        public Guid? FirstUserId { get; set; }
        public long? FirstQueuedSeq { get; set; }
        public Guid? FirstQueuedId { get; set; }
        public long? TopUserSeq { get; set; }
        public Guid? TopUserId { get; set; }
        public long? TopQueuedSeq { get; set; }
        public Guid? TopQueuedId { get; set; }
        public long PrefixCount { get; set; }
        public Guid? AnchorId { get; set; }
    }

    /// <summary>
    /// True when this session has at least one typed or queued prompt after
    /// <paramref name="dispatchedAt"/> that a turn could actually be answering. Degenerates to
    /// "any real prompt at all" on a fresh session (no inherited history, so the span is the
    /// whole transcript).
    /// </summary>
    internal static async Task<bool> HasTurnPromptSinceAsync(
        AppDbContext db, Guid sessionId, DateTime? dispatchedAt, CancellationToken ct) =>
        (await LoadAsync(db, sessionId, dispatchedAt, ct)).TurnPrompts.Count > 0;

    /// <summary>A real typed or queued prompt supersedes an older API-error boundary.</summary>
    internal static async Task<bool> HasTurnPromptAfterAsync(
        AppDbContext db, Guid sessionId, long afterSequence, CancellationToken ct) =>
        (await LoadAsync(db, sessionId, dispatchedAt: null, ct)).TurnPrompts
            .Any(p => p.Sequence > afterSequence);

    /// <summary>
    /// A prompt record that no one typed as a prompt. SETTLEMENT AND the delivery watchdog
    /// (CARD-0077) — both ask whether a real prompt exists, not whether the session is working.
    /// Working/idle rules must not consume <see cref="TranscriptKinds.IsRawLocalCommandEcho"/>;
    /// this helper is not a working/idle rule.
    ///
    /// <para>The span has already decided the row is a prompt by kind
    /// (<see cref="TranscriptKinds.UserPrompt"/> or <see cref="TranscriptKinds.QueuedUserPrompt"/>).
    /// The helpers below all hard-gate <c>kind == UserPrompt</c>, so the literal passed here is a
    /// question about <b>text shape</b>, not a second kind filter. Passing the row's own kind
    /// would push <c>QueuedUserPrompt</c> into those helpers' public contract, which the
    /// working/idle implementations also read — the leak CARD-0132 S2.4 forbids.</para>
    /// </summary>
    internal static bool IsHousekeepingPrompt(string? text, IReadOnlyCollection<string> invokedCommands) =>
        TranscriptKinds.IsLocalCommandRecord(TranscriptKinds.UserPrompt, text)
        || TranscriptKinds.IsCompactionContinuationPrompt(TranscriptKinds.UserPrompt, text)
        || TranscriptKinds.IsRawLocalCommandEcho(TranscriptKinds.UserPrompt, text, invokedCommands)
        || TranscriptKinds.IsTaskNotificationPrompt(TranscriptKinds.UserPrompt, text);

    /// <summary>
    /// CARD-1969 S2: process-local prompt rows per (connection string, session). One lock guards
    /// the map, the LRU list, the last-used times and the character total. Entries are immutable
    /// and replaced whole (last writer wins: every entry's stamp is derived from its own rows). No
    /// I/O, no await and no row-proportional allocation runs under the lock. The connection string
    /// is held in memory only; it is never logged or returned.
    ///
    /// <para>Bounds are chosen, not measured (UNOBSERVED; plan D-7, M-7): 256 entries, 4,000,000
    /// characters per entry, 32,000,000 in total, and 120 s idle. A list over the per-entry
    /// ceiling is returned and not stored. Eviction, idle expiry and the ceiling all leave the next
    /// call on today's full read. The idle expiry is what clears a session whose last open task
    /// ended (nothing reloads it); a reload, rebase or delete is a stamp change that replaces the
    /// entry whole. Not a setting.</para>
    /// </summary>
    internal sealed class PromptCache
    {
        /// <summary>Characters charged per row for the row object, its Kind and its id (a stand-in, UNOBSERVED).</summary>
        private const long RowOverheadChars = 48;

        internal static PromptCache Shared { get; } = new(
            256, 4_000_000, 32_000_000, TimeSpan.FromSeconds(120), () => Environment.TickCount64);

        private readonly object _gate = new();
        private readonly Dictionary<Key, LinkedListNode<Slot>> _slots = new();
        private readonly LinkedList<Slot> _lru = new(); // most recently used first
        private readonly int _maxEntries;
        private readonly long _maxEntryChars;
        private readonly long _maxTotalChars;
        private readonly long _idleMs;
        private readonly Func<long> _nowMs;
        private long _chars;

        internal PromptCache(
            int maxEntries, long maxEntryChars, long maxTotalChars, TimeSpan idle, Func<long> nowMs)
        {
            _maxEntries = maxEntries;
            _maxEntryChars = maxEntryChars;
            _maxTotalChars = maxTotalChars;
            _idleMs = (long)idle.TotalMilliseconds;
            _nowMs = nowMs;
        }

        internal readonly record struct Key(string Connection, Guid SessionId);

        /// <summary>One session's prompt rows in sequence order, their ids, and the stamp derived from them.</summary>
        internal sealed record Entry(Stamp Stamp, ImmutableArray<PromptRow> Rows, ImmutableArray<Guid> Ids, long Chars)
        {
            internal static Entry Derive(ImmutableArray<PromptRow> rows, ImmutableArray<Guid> ids)
            {
                var chars = 0L;
                foreach (var row in rows)
                    chars += (row.Text?.Length ?? 0) + RowOverheadChars;
                var stamp = rows.IsEmpty ? Stamp.Empty : new Stamp(rows.Length, ids[0], rows[^1].Sequence, ids[^1]);
                return new Entry(stamp, rows, ids, chars);
            }

            internal Entry Append(Entry tail) => Derive(Rows.AddRange(tail.Rows), Ids.AddRange(tail.Ids));
        }

        private sealed class Slot(Key key, Entry entry, long lastUsedMs)
        {
            public Key Key { get; } = key;
            public Entry Entry { get; } = entry;
            public long LastUsedMs { get; set; } = lastUsedMs;
        }

        internal Entry? Lookup(Key key)
        {
            lock (_gate)
            {
                var now = _nowMs();
                ExpireIdle(now);
                if (!_slots.TryGetValue(key, out var node))
                    return null;
                node.Value.LastUsedMs = now;
                _lru.Remove(node);
                _lru.AddFirst(node);
                return node.Value.Entry;
            }
        }

        internal void Store(Key key, Entry entry)
        {
            lock (_gate)
            {
                var now = _nowMs();
                ExpireIdle(now);
                if (_slots.TryGetValue(key, out var previous))
                    Remove(previous);
                if (entry.Chars > _maxEntryChars)
                    return;
                _slots[key] = _lru.AddFirst(new Slot(key, entry, now));
                _chars += entry.Chars;
                while (_lru.Last is { } oldest && (_slots.Count > _maxEntries || _chars > _maxTotalChars))
                    Remove(oldest);
            }
        }

        internal void Forget(AppDbContext db, Guid sessionId)
        {
            if (db.Database.GetConnectionString() is not { } connection)
                return;
            lock (_gate)
            {
                if (_slots.TryGetValue(new Key(connection, sessionId), out var node))
                    Remove(node);
            }
        }

        internal (int Entries, long Chars) Snapshot()
        {
            lock (_gate)
                return (_slots.Count, _chars);
        }

        private void ExpireIdle(long now)
        {
            while (_lru.Last is { } oldest && now - oldest.Value.LastUsedMs > _idleMs)
                Remove(oldest);
        }

        private void Remove(LinkedListNode<Slot> node)
        {
            _lru.Remove(node);
            _slots.Remove(node.Value.Key);
            _chars -= node.Value.Entry.Chars;
        }
    }
}
