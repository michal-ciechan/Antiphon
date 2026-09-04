using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// The fifth rung of the delivery evidence ladder: <b>did the agent answer?</b> (CARD-0312 S1).
/// Rungs 1–4 — <c>WaitForReadyOrThrowAsync</c>, composer evidence, submit evidence, transcript
/// confirm — are all about OUR bytes. Not one of them asks whether the model produced anything,
/// which is the whole of CARD-0312's gap and, on the same clock and the same event, CARD-0353's
/// hung first model call.
///
/// <para><b>One primitive, one setting.</b> The task-scoped arm
/// (<see cref="TaskDeadlinePolicy.DeadlineKind.BootModelWait"/>, CARD-0353 S1) and the
/// session-scoped watch (<see cref="Evaluate"/>, CARD-0312 S1) watch the identical event — the
/// first assistant/thinking/tool row after the boot prompt — on the identical clock, and both
/// read <c>DelegationSettings.BootModelWaitDeadlineMinutes</c>. Building two would be exactly the
/// "third overlapping mechanism" CARD-0312's plan forbids, so
/// <c>BootReplyDeadlineMinutes</c> deliberately does not exist.
///
/// <para><b>Pure and side-effect free.</b> <see cref="Evaluate"/> takes rows and a clock and
/// returns a status; nothing here fails, kills, restarts or writes. The same discipline
/// <see cref="TaskDeadlinePolicy"/> keeps, for the same reason: two callers act on it and a
/// verdict that differed between them would be a defect with no single place to fix.</para>
/// </summary>
internal static class BootReplyWatch
{
    /// <summary>
    /// A transcript row that proves the MODEL produced something. Deliberately narrow, and every
    /// exclusion is paid for:
    ///
    /// <list type="bullet">
    /// <item><c>TurnTitle</c> is excluded even though a provider writes it: measured 2026-09-04
    /// over the live corpus, 9 sessions carry a <c>TurnTitle</c> BEFORE their first model row
    /// (Grok titles a turn from the prompt text — CARD-0353 records the pointer text itself
    /// becoming a session's summary title). Counting it would mask exactly the stall this
    /// watches for.</item>
    /// <item><c>QueuedUserPrompt</c> is our input arriving late, not an answer
    /// (CARD-0135).</item>
    /// <item><c>QueueEnqueue</c>/<c>QueueDequeue</c>/<c>QueueRemove</c> are inert housekeeping
    /// whose timestamps can precede their file-order predecessors (CARD-0292).</item>
    /// <item><c>SessionRestartBoundary</c> is a row ANTIPHON synthesised
    /// (<c>AgentSessionService.WriteRestartBoundaryIfInterruptedAsync</c>), so it is evidence
    /// about us, not about the provider.</item>
    /// </list>
    ///
    /// <para><c>TurnEnd</c> IS counted. Measured on the same corpus, two Codex sessions answered
    /// their boot prompt with an API-error <c>TurnEnd</c> in ~1 s and then sat in CARD-0072's
    /// retry ladder for 43 minutes; treating that as silence would have killed sessions the
    /// API-error recovery was already correctly retrying. <c>ToolResult</c> is counted because it
    /// cannot exist without a <c>ToolCall</c> (measured: it never precedes one) — it is inert
    /// belt-and-braces, not a widening.</para>
    /// </summary>
    internal static bool IsModelReply(string kind) => kind
        is TranscriptKinds.AssistantText
        or TranscriptKinds.Thinking
        or TranscriptKinds.ToolCall
        or TranscriptKinds.ToolResult
        or TranscriptKinds.TurnEnd;

    /// <summary>A prompt row: ours, never an answer.</summary>
    internal static bool IsPromptRow(string kind) => kind
        is TranscriptKinds.UserPrompt
        or TranscriptKinds.QueuedUserPrompt;

    internal enum Status
    {
        /// <summary>No watch is armed, or the arming data is incomplete.</summary>
        Disarmed = 0,

        /// <summary>A qualifying model row landed past the boot prompt.</summary>
        Answered = 1,

        /// <summary>Nothing yet, and the deadline has not passed.</summary>
        Waiting = 2,

        /// <summary>Nothing yet, and the deadline has passed.</summary>
        Overdue = 3,
    }

    /// <summary>A transcript row as the watch reads it: kind and sequence, nothing else.</summary>
    internal sealed record Row(long Sequence, string Kind);

    /// <summary>
    /// The verdict on one armed watch.
    ///
    /// <para><b>Sequence is the bound, not time.</b> Stored sequences are arrival-ordered and a
    /// backfill rebases what it missed, so a timestamp comparison against the boot prompt would
    /// judge a reused warm session on the PREVIOUS task's rows — the CARD-0077 trap
    /// <c>FailNeverStartedAsync</c> documents. Only rows strictly past
    /// <paramref name="bootPromptSequence"/> may answer it.</para>
    /// </summary>
    /// <param name="bootPromptSequence">The confirmed boot prompt's own transcript sequence.</param>
    /// <param name="dueAt">When the wait expires. Null disarms.</param>
    /// <param name="now">The caller's clock.</param>
    /// <param name="rowsSincePrompt">
    /// Rows on this session; only those with <c>Sequence &gt; bootPromptSequence</c> are read, so
    /// a caller may pass the whole tail without filtering.
    /// </param>
    internal static Status Evaluate(
        long? bootPromptSequence,
        DateTime? dueAt,
        DateTime now,
        IReadOnlyList<Row> rowsSincePrompt)
    {
        if (bootPromptSequence is not long sequence || dueAt is not DateTime due)
            return Status.Disarmed;

        foreach (var row in rowsSincePrompt)
        {
            if (row.Sequence > sequence && IsModelReply(row.Kind))
                return Status.Answered;
        }

        return now >= due ? Status.Overdue : Status.Waiting;
    }

    /// <summary>
    /// The boot turn as the DATABASE sees it, shared by CARD-0353's task-scoped classification and
    /// CARD-0312's session-scoped sweep: every row on this session at or after
    /// <paramref name="clock"/> is one of our own prompts and nothing else.
    ///
    /// <para><paramref name="clock"/> is <c>max(DispatchedAt, LaunchResumedAt)</c> for a task
    /// (CARD-0340 S2 — a resumed launch's boot turn starts at the resume) or the session's own
    /// start for a standing agent. A warm-pool session's INHERITED rows predate it and are
    /// therefore invisible here: nothing is ever failed for a stall it inherited.</para>
    ///
    /// <para>Housekeeping prompt records — the <c>&lt;command-name&gt;</c> wrapper, its stdout, the
    /// compaction continuation, a background-subagent notification — are neither evidence nor
    /// disqualifiers (CARD-0041/CARD-0046), exactly as <see cref="TranscriptPromptSpan"/> treats
    /// them. Returns the LATEST real prompt's timestamp when this is a boot turn, and null
    /// otherwise (including when no real prompt has landed at all — that is
    /// <c>FailNeverStartedAsync</c>'s question, not this one). A refinement typed into a
    /// still-silent session therefore restarts the wait, which is right: it is a new request.</para>
    /// </summary>
    internal static async Task<BootTurn?> LoadBootTurnAsync(
        AppDbContext db, Guid sessionId, DateTime clock, CancellationToken ct)
    {
        var rows = await db.TranscriptEntries.AsNoTracking()
            .Where(t => t.AgentSessionId == sessionId
                && (t.Timestamp ?? t.CreatedAt) >= clock)
            .OrderBy(t => t.Sequence)
            .Select(t => new { t.Sequence, t.Kind, t.Text, At = t.Timestamp ?? t.CreatedAt })
            .ToListAsync(ct);
        if (rows.Count == 0)
            return null;

        // Any model row since the clock means the boot turn is over — the general ModelWait arm
        // (or nothing at all) applies from here.
        if (rows.Any(r => IsModelReply(r.Kind)))
            return null;

        var prompts = rows.Where(r => IsPromptRow(r.Kind)).ToList();
        if (prompts.Count == 0)
            return null;

        var invoked = prompts
            .Select(r => TranscriptKinds.TryReadLocalCommandName(TranscriptKinds.UserPrompt, r.Text))
            .OfType<string>()
            .ToHashSet(StringComparer.Ordinal);
        var real = prompts
            .Where(r => r.Kind == TranscriptKinds.QueuedUserPrompt
                || !TranscriptPromptSpan.IsHousekeepingPrompt(r.Text, invoked))
            .ToList();
        if (real.Count == 0)
            return null;

        var latest = real[^1];
        return new BootTurn(latest.Sequence, latest.At, real.Count);
    }

    /// <summary>
    /// Arm (or re-arm, or clear) the watch on one session from the boot predicate, and return the
    /// due time when it is armed.
    ///
    /// <para><b>The due time is <c>promptAt + deadline</c>, not <c>now + deadline</c>.</b> One
    /// clock, computed from the prompt's own timestamp, so the session-scoped watch and the
    /// task-scoped <see cref="TaskDeadlinePolicy.DeadlineKind.BootModelWait"/> arm can never
    /// disagree about when a stall began, and so an arm that happens late (a sweep re-deriving
    /// after a restart) does not silently extend the wait.</para>
    ///
    /// <para><b>Re-arming on a later prompt is deliberate.</b> A refinement typed into a session
    /// that has still produced nothing is a NEW request and restarts the wait — CARD-0353 S1 says
    /// so for the task arm, and the two must agree. It cannot re-arm a session that has already
    /// answered: one model row since the launch clock makes the predicate false, so this clears
    /// the watch instead.</para>
    ///
    /// <para>Idempotent and cheap, so every caller may run it after any confirmed delivery and the
    /// sweep may run it to re-derive a watch a restart lost (the CARD-0331 mistake this avoids).
    /// The caller owns <c>SaveChangesAsync</c>.</para>
    /// </summary>
    internal static async Task<DateTime?> TryArmAsync(
        AppDbContext db, Guid sessionId, int deadlineMinutes, CancellationToken ct)
    {
        var session = await db.AgentSessions.FirstOrDefaultAsync(s => s.Id == sessionId, ct);
        if (session is null)
            return null;

        if (deadlineMinutes <= 0)
        {
            session.BootPromptSequence = null;
            session.BootReplyDueAt = null;
            return null;
        }

        var clock = session.LaunchResumedAt is DateTime resumed && resumed > session.StartedAt
            ? resumed
            : session.StartedAt;
        var boot = await LoadBootTurnAsync(db, sessionId, clock, ct);
        if (boot is null)
        {
            session.BootPromptSequence = null;
            session.BootReplyDueAt = null;
            return null;
        }

        session.BootPromptSequence = boot.PromptSequence;
        session.BootReplyDueAt = boot.PromptAt.AddMinutes(deadlineMinutes);
        return session.BootReplyDueAt;
    }

    /// <summary>
    /// Clear the watch. Called when the session terminates, when a human types into it, and when
    /// the sweep sees it answered — the three disarm conditions CARD-0312 S1 names. The caller owns
    /// <c>SaveChangesAsync</c>; a session row that is already clear costs one no-op update.
    /// </summary>
    internal static async Task DisarmAsync(AppDbContext db, Guid sessionId, CancellationToken ct)
    {
        var session = await db.AgentSessions.FirstOrDefaultAsync(s => s.Id == sessionId, ct);
        if (session is null || (session.BootPromptSequence is null && session.BootReplyDueAt is null))
            return;
        session.BootPromptSequence = null;
        session.BootReplyDueAt = null;
    }

    /// <summary>
    /// The watch's verdict for one armed session, read from the stored columns and the rows past
    /// the boot prompt. <c>CatchUpTranscriptAsync</c> is the CALLER's job before it may act on
    /// <see cref="Status.Overdue"/> — the live stream is not a reliable clock (CARD-0055).
    /// </summary>
    internal static async Task<Status> EvaluateSessionAsync(
        AppDbContext db, AgentSession session, DateTime now, CancellationToken ct)
    {
        if (session.BootPromptSequence is not long sequence || session.BootReplyDueAt is null)
            return Status.Disarmed;

        var rows = await db.TranscriptEntries.AsNoTracking()
            .Where(t => t.AgentSessionId == session.Id && t.Sequence > sequence)
            .OrderBy(t => t.Sequence)
            .Select(t => new Row(t.Sequence, t.Kind))
            .ToListAsync(ct);
        return Evaluate(session.BootPromptSequence, session.BootReplyDueAt, now, rows);
    }

    /// <param name="PromptSequence">The latest real prompt's sequence — the watch's lower bound.</param>
    /// <param name="PromptAt">Its timestamp — the clock the deadline is measured from.</param>
    /// <param name="PromptCount">How many real prompts have landed since the launch clock.</param>
    internal sealed record BootTurn(long PromptSequence, DateTime PromptAt, int PromptCount);
}
