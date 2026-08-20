using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// How long an OPEN task is allowed to run, and which clock says so (CARD-0020 S2/S3). One
/// implementation, shared by the sweep that acts on it (<c>AgentTaskDispatcher.FailOverdueTasksAsync</c>)
/// and the projection that previews it (<c>AttentionService</c>, <see cref="AttentionKind.Overdue"/>)
/// — the same reason <c>AgentTaskLiveness</c> is shared by the dead-session pair: a row the view
/// shows that the sweep would not fail (or the reverse) is a defect with no single place to fix it.
///
/// <para><b>Two clocks, and the tighter one wins.</b> The hard ceiling
/// (<c>RolePolicyEntry.TimeoutMinutes</c>, 240) is wall-clock from <c>DispatchedAt</c> and applies
/// whatever the session is doing. The phase deadline (20 min model wait / 90 min local execution)
/// is a TIGHTENING of it, not a replacement: it only ever applies while the session is mid-turn,
/// which is precisely the case <see cref="AttentionKind.PastExpectedIdle"/> declines by
/// construction ("a session that is mid-turn is never listed for being slow"). The two conditions
/// therefore partition the open tasks between them rather than overlapping.</para>
///
/// <para><b>The phase is read ONLY through the shared working verdict</b>
/// (<c>SessionMessageQueueService.IsWorkingAsync</c>). The last entry's <c>Kind</c> is not a phase
/// signal on its own: <c>UserPrompt</c> carries at least four different things and three of them
/// mean no API call is coming (a <c>&lt;local-command-stdout&gt;</c> record, an interrupt marker, a
/// compaction continuation prompt — CARD-0041). Measured 2026-08-20: every one of the six longest
/// <c>UserPrompt</c>-headed gaps in ten days, up to 45.5 HOURS, was a <c>/compact</c> housekeeping
/// record, so a deadline keyed on raw Kind would have failed three healthy sessions 45 hours early.
/// <c>IsWorkingAsync</c> already excludes all three, and it is the THIRD implementation of that rule
/// — a fourth would be a defect by this repo's own standing rule. Kind is used only to pick WHICH
/// deadline applies once working is true.</para>
///
/// <para><b>Read-only and side-effect free.</b> Nothing here fails, kills, settles or writes; it
/// answers "which deadline, how far through it" and the caller decides. Both callers pull the
/// transcript first (CARD-0055) — this reads whatever the caller has already made current.</para>
/// </summary>
internal static class TaskDeadlinePolicy
{
    /// <summary>
    /// How far through a deadline a task must be before it is worth SAYING anything. The attention
    /// projection lists it here; the sweep still only acts at 1.0. It is the same relationship
    /// <c>NeverStartedGrace</c> (2 min) has with <c>DeliveryFailTimeoutMinutes</c> (10) — surface
    /// the condition while it is still recoverable, rather than reporting it afterwards in the
    /// failure list.
    /// </summary>
    internal const double PreviewFraction = 0.8;

    internal enum DeadlineKind
    {
        /// <summary>The role's hard wall-clock ceiling, measured from <c>DispatchedAt</c>.</summary>
        Ceiling = 0,

        /// <summary>Mid-turn with the model owing the next token.</summary>
        ModelWait = 1,

        /// <summary>Mid-turn with a local tool (build, test suite, grep) owing its result.</summary>
        LocalExecution = 2,
    }

    /// <summary>
    /// The session's last transcript entry, with <paramref name="At"/> already resolved to the
    /// record's own <c>Timestamp</c> where it has one and its arrival <c>CreatedAt</c> otherwise.
    /// </summary>
    internal sealed record LastEntry(long Sequence, string Kind, DateTime At);

    /// <param name="Elapsed">
    /// Time spent against THIS deadline: wall clock since dispatch for the ceiling, and for a phase
    /// deadline the smaller of the last entry's age and that same wall clock — a warm-pool session's
    /// inherited last entry can predate this task's dispatch by hours, and a task must never be
    /// failed for time it did not own.
    /// </param>
    /// <param name="Summary">
    /// One line naming the clock, the phase and the last entry's age — the failure reason and the
    /// attention headline are the same sentence, so a row and the failure it becomes read alike.
    /// </param>
    internal sealed record Verdict(
        DeadlineKind Kind,
        TimeSpan Limit,
        TimeSpan Elapsed,
        string? Phase,
        TimeSpan? LastEntryAge,
        string Summary)
    {
        /// <summary>The deadline has been passed. Only this may fail a task.</summary>
        internal bool Breached => Limit > TimeSpan.Zero && Elapsed >= Limit;

        /// <summary>0..1+ against the limit. The tighter of two clocks is the one further through.</summary>
        internal double Fraction =>
            Limit <= TimeSpan.Zero ? 0d : Elapsed.TotalSeconds / Limit.TotalSeconds;

        /// <summary>Worth surfacing to a human, whether or not it has fired yet.</summary>
        internal bool WorthSurfacing => Fraction >= PreviewFraction;
    }

    /// <summary>
    /// The role's ceiling in minutes. A role with no <c>RolePolicy</c> entry (<c>Custom</c>,
    /// <c>Check</c>) falls back to <see cref="DelegationSettings.DefaultTimeoutMinutes"/>, mirroring
    /// how <c>DefaultLevel</c> already covers an unlisted role: unconfigured is not unwatched.
    /// </summary>
    internal static int CeilingMinutes(DelegationSettings settings, AgentTaskRole role) =>
        settings.RolePolicy.TryGetValue(role.ToString(), out var policy)
            ? policy.TimeoutMinutes
            : settings.DefaultTimeoutMinutes;

    /// <summary>
    /// The tightest deadline this task is closest to breaching, or null when none is armed, none
    /// applies, or the task is nowhere near one yet.
    /// </summary>
    internal static async Task<Verdict?> EvaluateAsync(
        AppDbContext db, AgentTask task, DateTime now, DelegationSettings settings, CancellationToken ct)
    {
        if (task.AgentSessionId is not Guid sessionId || task.DispatchedAt is not DateTime dispatched)
            return null;

        var ceiling = Minutes(CeilingMinutes(settings, task.Role));
        var modelWait = Minutes(settings.ModelWaitDeadlineMinutes);
        var localExecution = Minutes(settings.LocalExecutionDeadlineMinutes);
        if (ceiling <= TimeSpan.Zero && modelWait <= TimeSpan.Zero && localExecution <= TimeSpan.Zero)
            return null;

        var elapsed = NonNegative(now - dispatched);

        // The cheap gate that keeps this off the hot path. EVERY clock here is bounded below by the
        // wall clock since dispatch (a phase deadline uses min(last-entry age, elapsed)), so a task
        // younger than the smallest armed limit cannot be near any of them and needs no query at
        // all — which is the ordinary case on every 5 s tick.
        var smallest = SmallestArmed(ceiling, modelWait, localExecution);
        if (smallest <= TimeSpan.Zero || elapsed < smallest * PreviewFraction)
            return null;

        var last = await LoadLastEntryAsync(db, sessionId, ct);
        var lastAge = last is null ? (TimeSpan?)null : NonNegative(now - last.At);

        Verdict? best = null;
        if (ceiling > TimeSpan.Zero)
        {
            best = new Verdict(
                DeadlineKind.Ceiling, ceiling, elapsed, last?.Kind, lastAge,
                $"Ran {Duration(elapsed)} against the {(int)ceiling.TotalMinutes}-minute ceiling for "
                + $"role {task.Role}. {DescribeLast(last, lastAge)}");
        }

        // The phase arm. Working is asked BEFORE Kind is looked at, and only for a task that could
        // actually be near a phase deadline — see the class comment for why that order is the whole
        // point rather than an optimisation.
        var phaseLimitFloor = SmallestArmed(modelWait, localExecution);
        if (last is not null
            && lastAge is TimeSpan age
            && phaseLimitFloor > TimeSpan.Zero
            && Smaller(age, elapsed) >= phaseLimitFloor * PreviewFraction
            && await SessionMessageQueueService.IsWorkingAsync(db, sessionId, ct))
        {
            var (phaseKind, phaseLimit) = ClassifyPhase(last.Kind, modelWait, localExecution);
            if (phaseLimit > TimeSpan.Zero)
            {
                // A warm delegate's session carries the PREVIOUS task's tail, so the phase clock is
                // capped at this task's own age: nothing is ever failed for a stall it inherited.
                var phaseElapsed = Smaller(age, elapsed);
                var label = phaseKind == DeadlineKind.ModelWait
                    ? "waiting on the model"
                    : "waiting on a local tool";
                var candidate = new Verdict(
                    phaseKind, phaseLimit, phaseElapsed, last.Kind, lastAge,
                    $"Mid-turn and {label} for {Duration(phaseElapsed)}, against a "
                    + $"{(int)phaseLimit.TotalMinutes}-minute deadline. {DescribeLast(last, lastAge)}");
                if (best is null || candidate.Fraction > best.Fraction)
                    best = candidate;
            }
        }

        return best is { WorthSurfacing: true } ? best : null;
    }

    /// <summary>
    /// Which deadline a mid-turn session's last entry selects. <c>TurnEnd</c> is deliberately absent
    /// — an idle tail is measured in DAYS (p99 39 732 s) and is
    /// <see cref="AttentionKind.PastExpectedIdle"/>'s business — and so is every kind not named
    /// here (<c>TurnTitle</c>, the compaction and restart boundaries): an unrecognised tail gets the
    /// wall-clock ceiling and nothing tighter.
    /// </summary>
    internal static (DeadlineKind Kind, TimeSpan Limit) ClassifyPhase(
        string kind, TimeSpan modelWait, TimeSpan localExecution) => kind switch
        {
            // The model owes the next token. Four kinds, one phase: after a prompt or a tool result
            // it is the first token that is late, and mid-stream (Thinking/AssistantText) it is the
            // next chunk — measured maxima 217 s, 1 478 s, 33 s and 146 s respectively.
            TranscriptKinds.UserPrompt
                or TranscriptKinds.ToolResult
                or TranscriptKinds.Thinking
                or TranscriptKinds.AssistantText => (DeadlineKind.ModelWait, modelWait),

            // A local tool is running. Legitimately long — a build or a test suite — which is the
            // whole reason a flat timeout is wrong: measured max 5 311 s.
            TranscriptKinds.ToolCall => (DeadlineKind.LocalExecution, localExecution),

            _ => (DeadlineKind.Ceiling, TimeSpan.Zero),
        };

    /// <summary>
    /// The session's last transcript entry, with the timestamp tie-break.
    ///
    /// <para>Stored sequences are ARRIVAL-ordered, not time-ordered: a backfill rebases entries it
    /// missed past the session's max, so the row with the highest <c>Sequence</c> is not always the
    /// newest. Measured 2026-08-20 over 71 801 entries: 195 adjacent pairs (0.27%) run backwards in
    /// time, 72 of them by more than a minute. Sequence stays the default verdict — same as
    /// <c>IsWorkingAsync</c> — and a strictly later timestamp overrides it, which is the direction
    /// that can only ever make a stall look YOUNGER and so can only ever withhold a failure.</para>
    /// </summary>
    internal static async Task<LastEntry?> LoadLastEntryAsync(
        AppDbContext db, Guid sessionId, CancellationToken ct)
    {
        var bySequence = await db.TranscriptEntries.AsNoTracking()
            .Where(t => t.AgentSessionId == sessionId)
            .OrderByDescending(t => t.Sequence)
            .Select(t => new LastEntry(t.Sequence, t.Kind, t.Timestamp ?? t.CreatedAt))
            .FirstOrDefaultAsync(ct);
        if (bySequence is null)
            return null;

        var byTimestamp = await db.TranscriptEntries.AsNoTracking()
            .Where(t => t.AgentSessionId == sessionId)
            .OrderByDescending(t => t.Timestamp ?? t.CreatedAt)
            .ThenByDescending(t => t.Sequence)
            .Select(t => new LastEntry(t.Sequence, t.Kind, t.Timestamp ?? t.CreatedAt))
            .FirstOrDefaultAsync(ct);

        return byTimestamp is not null && byTimestamp.At > bySequence.At ? byTimestamp : bySequence;
    }

    /// <summary>The phase clause both the failure reason and the attention row end with.</summary>
    internal static string DescribeLast(LastEntry? last, TimeSpan? age) =>
        last is null
            ? "The session has never written a transcript entry."
            : $"Last transcript entry: {last.Kind}"
              + (age is TimeSpan a ? $", {Duration(a)} ago." : ".");

    internal static string Duration(TimeSpan span)
    {
        span = NonNegative(span);
        return span.TotalHours >= 1
            ? $"{(int)span.TotalHours}h{span.Minutes:00}m"
            : $"{(int)span.TotalMinutes}m";
    }

    private static TimeSpan Minutes(int minutes) =>
        minutes <= 0 ? TimeSpan.Zero : TimeSpan.FromMinutes(minutes);

    private static TimeSpan NonNegative(TimeSpan span) => span < TimeSpan.Zero ? TimeSpan.Zero : span;

    private static TimeSpan Smaller(TimeSpan a, TimeSpan b) => a < b ? a : b;

    /// <summary>The smallest ARMED limit; <see cref="TimeSpan.Zero"/> when none is armed.</summary>
    private static TimeSpan SmallestArmed(params TimeSpan[] limits)
    {
        var smallest = TimeSpan.Zero;
        foreach (var limit in limits)
        {
            if (limit <= TimeSpan.Zero)
                continue;
            if (smallest <= TimeSpan.Zero || limit < smallest)
                smallest = limit;
        }

        return smallest;
    }
}
