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
    internal sealed record PromptRow(long Sequence, string? Text, DateTime? Timestamp);

    /// <param name="TurnPrompts">USER records a turn could actually be answering, in sequence order.</param>
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
                && t.Kind == TranscriptKinds.UserPrompt
                && (dispatchedAt == null || t.Timestamp == null || t.Timestamp > dispatchedAt))
            .OrderBy(t => t.Sequence)
            .Select(t => new PromptRow(t.Sequence, t.Text, t.Timestamp))
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
    /// True when this session has at least one USER record after <paramref name="dispatchedAt"/>
    /// that a turn could actually be answering. Degenerates to "any real prompt at all" on a
    /// fresh session (no inherited history, so the span is the whole transcript).
    /// </summary>
    internal static async Task<bool> HasTurnPromptSinceAsync(
        AppDbContext db, Guid sessionId, DateTime? dispatchedAt, CancellationToken ct) =>
        (await LoadAsync(db, sessionId, dispatchedAt, ct)).TurnPrompts.Count > 0;

    /// <summary>
    /// A USER record that no one typed as a prompt. SETTLEMENT AND the delivery watchdog
    /// (CARD-0077) — both ask whether a real prompt exists, not whether the session is working.
    /// Working/idle rules must not consume <see cref="TranscriptKinds.IsRawLocalCommandEcho"/>;
    /// this helper is not a working/idle rule.
    /// </summary>
    internal static bool IsHousekeepingPrompt(string? text, IReadOnlyCollection<string> invokedCommands) =>
        TranscriptKinds.IsLocalCommandRecord(TranscriptKinds.UserPrompt, text)
        || TranscriptKinds.IsCompactionContinuationPrompt(TranscriptKinds.UserPrompt, text)
        || TranscriptKinds.IsRawLocalCommandEcho(TranscriptKinds.UserPrompt, text, invokedCommands)
        || TranscriptKinds.IsTaskNotificationPrompt(TranscriptKinds.UserPrompt, text);
}
