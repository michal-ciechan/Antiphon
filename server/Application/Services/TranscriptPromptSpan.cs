using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Microsoft.EntityFrameworkCore;

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
    /// </param>
    /// <param name="Notifications">
    /// The background-subagent notifications among them — skipped as turn prompts, kept because
    /// they are what proves a launch came back (CARD-0046 slice 4).
    /// </param>
    internal sealed record Result(
        IReadOnlyList<PromptRow> TurnPrompts, IReadOnlyList<PromptRow> Notifications);

    internal static async Task<Result> LoadAsync(
        AppDbContext db, Guid sessionId, DateTime? dispatchedAt, CancellationToken ct)
    {
        var rows = await db.TranscriptEntries
            .Where(t => t.AgentSessionId == sessionId
                && (t.Kind == TranscriptKinds.UserPrompt
                    || t.Kind == TranscriptKinds.QueuedUserPrompt)
                && (dispatchedAt == null || t.Timestamp == null || t.Timestamp > dispatchedAt))
            .OrderBy(t => t.Sequence)
            .Select(t => new PromptRow(t.Sequence, t.Text, t.Timestamp, t.Kind))
            .ToListAsync(ct);

        // The wrappers are the PROOF that a raw "/compact …" line was a command rather than a
        // prompt that happens to start with a slash — so the names come out of the span itself.
        var invoked = rows
            .Select(r => TranscriptKinds.TryReadLocalCommandName(TranscriptKinds.UserPrompt, r.Text))
            .OfType<string>()
            .ToHashSet(StringComparer.Ordinal);

        return new Result(
            rows.Where(r => !IsHousekeepingPrompt(r.Text, invoked)).ToList(),
            rows.Where(r => TranscriptKinds.IsTaskNotificationPrompt(TranscriptKinds.UserPrompt, r.Text))
                .ToList());
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
}
