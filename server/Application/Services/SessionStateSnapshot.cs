using Antiphon.Server.Domain.Entities;

namespace Antiphon.Server.Application.Services;

public enum SessionStateReadiness { Cold, Loading, Ready, Missing, Faulted }

/// <summary>Metadata of committed rows only. Producer time is independent of arrival order.</summary>
public sealed record SessionStateSnapshot(Guid SessionId)
{
    public Guid ServerEpoch { get; init; }
    public long Revision { get; init; }
    public long ResetEpoch { get; init; }
    public SessionStateReadiness Readiness { get; init; }
    public DateTime? AcceptedGeneration { get; init; }
    public bool Pinned { get; init; }
    public long Count { get; init; }
    public long LastSequence { get; init; }
    public Guid? LastEntryId { get; init; }
    public string? LastKind { get; init; }
    public DateTime? LastTimestamp { get; init; }
    public DateTime? LastCreatedAt { get; init; }
    public DateTime? NewestEffectiveTimestamp { get; init; }
    public long? LastTurnEndSequence { get; init; }
    public long? LastTurnTitleSequence { get; init; }
    public long? LastUserPromptSequence { get; init; }
    public long? EndSequence { get; init; }
    public DateTime? EndTimestamp { get; init; }
    public bool HasActivity { get; init; }
    public bool HasNullTimestampActivity { get; init; }
    public DateTime? ActivityTimestamp { get; init; }
    public bool Working => HasActivity && (EndSequence is null || HasNullTimestampActivity
        || EndTimestamp is null || ActivityTimestamp >= EndTimestamp);

    public SessionStateSnapshot Append(IEnumerable<TranscriptEntry> committedRows)
    {
        var state = this;
        foreach (var row in committedRows.OrderBy(r => r.Sequence))
        {
            if (row.AgentSessionId != SessionId || (state.Count > 0 && row.Sequence <= state.LastSequence))
                throw new InvalidOperationException("Non-append transcript mutation requires a durable reseed.");
            var end = IsEnd(row.Kind, row.Text);
            var activity = IsActivity(row.Kind, row.Text);
            state = state with
            {
                Count = state.Count + 1, LastSequence = row.Sequence, LastEntryId = row.Id,
                LastKind = row.Kind, LastTimestamp = row.Timestamp, LastCreatedAt = row.CreatedAt,
                NewestEffectiveTimestamp = Max(state.NewestEffectiveTimestamp, row.Timestamp ?? row.CreatedAt),
                LastTurnEndSequence = row.Kind == "TurnEnd" ? row.Sequence : state.LastTurnEndSequence,
                LastTurnTitleSequence = row.Kind == "TurnTitle" ? row.Sequence : state.LastTurnTitleSequence,
                LastUserPromptSequence = row.Kind == "UserPrompt" ? row.Sequence : state.LastUserPromptSequence,
                EndSequence = end ? row.Sequence : state.EndSequence,
                EndTimestamp = end ? Max(state.EndTimestamp, row.Timestamp) : state.EndTimestamp,
                HasActivity = !end && (state.HasActivity || activity),
                HasNullTimestampActivity = !end && (state.HasNullTimestampActivity || (activity && row.Timestamp is null)),
                ActivityTimestamp = end ? null : activity ? Max(state.ActivityTimestamp, row.Timestamp) : state.ActivityTimestamp
            };
        }
        return state;
    }

    // Do not use the runner's TrimStart helpers: these are the server SQL oracle's exact rules.
    private static bool Prefix(string? text, string prefix) => text?.StartsWith(prefix, StringComparison.Ordinal) == true;
    private static bool IsEnd(string kind, string? text) => kind is "TurnEnd" or "SessionRestartBoundary"
        || (kind == "CompactBoundary" && text?.Contains("(manual)", StringComparison.Ordinal) == true)
        || (kind == "UserPrompt" && Prefix(text, "[Request interrupted"));
    private static bool IsActivity(string kind, string? text) =>
        kind is not ("TurnEnd" or "TurnTitle" or "SessionRestartBoundary" or "QueuedUserPrompt"
            or "QueueEnqueue" or "QueueDequeue" or "QueueRemove" or "CompactBoundary")
        && (kind != "UserPrompt" || !(Prefix(text, "<command-name>") || Prefix(text, "<local-command-stdout>")
            || Prefix(text, "This session is being continued from a previous conversation") || Prefix(text, "[Request interrupted")));
    private static DateTime? Max(DateTime? a, DateTime? b) => a is null ? b : b is null || a >= b ? a : b;
}
