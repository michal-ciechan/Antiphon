using Antiphon.Server.Application.Services;
using Antiphon.SessionRunner.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Antiphon.Tests.TestHelpers;

/// <summary>
/// CARD-1464: test-side transcript rows that must reach the LOADED working-state projection.
/// <c>SessionMessageQueueService.ReadWorkingAsync</c> prefers the registered <see cref="SessionStateStore"/>
/// (every <see cref="BridgeQueueHarness"/> provider registers it), so a row written straight through EF
/// after the store has loaded the session is invisible until the next reconcile and the queue keeps
/// reading the stale verdict: a "busy" row leaves the session idle (the receipt is typed), a raw TurnEnd
/// leaves it Working (the flush withholds, the kill is withheld). Every helper here publishes the way
/// ingestion does (CARD-1373 D-1): take the store's write lease for the session, commit the row(s) through
/// the harness's own insert, then reconcile the lease. The lease belongs to the harness whose queue the
/// test flushes; a second harness over the same database owns a second store, so pass that harness when
/// its queue is the one under test.
/// </summary>
internal static class StoreAwareTranscript
{
    /// <summary>Inserts one row for the session and reconciles the harness's loaded store under its write lease.</summary>
    public static async Task<long> InsertAsync(BridgeQueueHarness h, string kind, string? text = null,
        string? stopReason = null, Guid? sessionId = null)
    {
        var id = sessionId ?? h.SessionId;
        using var lease = await Store(h).BeginWriteAsync(id, CancellationToken.None);
        var seq = await h.InsertTranscriptEntryAsync(kind, text, stopReason, sessionId: id);
        await lease.ReconcileAsync(reset: false, CancellationToken.None);
        return seq;
    }

    /// <summary>Activity after the last TurnEnd: the session reads Working in the store and the database alike.</summary>
    public static Task MarkWorkingAsync(BridgeQueueHarness h, Guid? sessionId = null, string text = "working on it") =>
        InsertAsync(h, TranscriptKinds.AssistantText, text, sessionId: sessionId);

    /// <summary>Ends the current turn: the session reads idle. The twin of <see cref="MarkWorkingAsync"/>.</summary>
    public static Task MarkIdleAsync(BridgeQueueHarness h, Guid? sessionId = null, string? stopReason = null) =>
        InsertAsync(h, TranscriptKinds.TurnEnd, stopReason: stopReason, sessionId: sessionId);

    /// <summary>An end_turn TurnEnd, the row shape the raw inserts this replaces used to write.</summary>
    public static Task AppendTurnEndAsync(BridgeQueueHarness h, Guid? sessionId = null) =>
        MarkIdleAsync(h, sessionId, TranscriptKinds.StopReasons.EndTurn);

    /// <summary>The busy/idle toggle of the delivery fixtures: a "busy" AssistantText or an end_turn TurnEnd.</summary>
    public static Task SetWorkingAsync(BridgeQueueHarness h, Guid sessionId, bool working) =>
        working ? MarkWorkingAsync(h, sessionId, "busy") : AppendTurnEndAsync(h, sessionId);

    /// <summary>A full turn (UserPrompt, AssistantText, TurnEnd/end_turn) committed under one lease, then reconciled.</summary>
    public static async Task InsertTurnAsync(BridgeQueueHarness h, string prompt, string response, Guid? sessionId = null)
    {
        var id = sessionId ?? h.SessionId;
        using var lease = await Store(h).BeginWriteAsync(id, CancellationToken.None);
        await h.InsertTranscriptEntryAsync(TranscriptKinds.UserPrompt, prompt, sessionId: id);
        await h.InsertTranscriptEntryAsync(TranscriptKinds.AssistantText, response, sessionId: id);
        await h.InsertTranscriptEntryAsync(TranscriptKinds.TurnEnd, stopReason: TranscriptKinds.StopReasons.EndTurn, sessionId: id);
        await lease.ReconcileAsync(reset: false, CancellationToken.None);
    }

    private static SessionStateStore Store(BridgeQueueHarness h) => h.Provider.GetRequiredService<SessionStateStore>();
}
