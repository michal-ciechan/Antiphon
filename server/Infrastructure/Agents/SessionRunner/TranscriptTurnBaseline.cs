namespace Antiphon.Server.Infrastructure.Agents.SessionRunner;

/// <summary>
/// CARD-0113 — the floor a turn's transcript verdicts (and Codex submit confirmation) are
/// measured against.
///
/// <para>Sequence 0 is a real floor: a first-ever turn whose rollout does not exist yet. A
/// transient fetch failure must not be collapsed onto 0 when this adapter has already observed
/// a transcript, because that makes the previous turn's <c>TurnEnd</c> look like this one's.</para>
/// </summary>
internal static class TranscriptTurnBaseline
{
    /// <param name="fetchedLastSequence">
    /// <c>LastSequence</c> from a successful read, including 0 for an empty transcript.
    /// Null means the read failed (missing endpoint, session-not-found, transport).
    /// </param>
    /// <param name="lastKnownSequence">
    /// The last <c>LastSequence</c> this adapter successfully observed, or null if it never has.
    /// </param>
    public static long Resolve(long? fetchedLastSequence, long? lastKnownSequence) =>
        fetchedLastSequence ?? lastKnownSequence ?? 0;

    public static bool PreservedLastKnown(long? fetchedLastSequence, long? lastKnownSequence) =>
        fetchedLastSequence is null && lastKnownSequence is not null;
}
