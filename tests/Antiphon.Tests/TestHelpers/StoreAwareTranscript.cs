using Antiphon.Server.Application.Services;
using Antiphon.SessionRunner.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Antiphon.Tests.TestHelpers;

/// <summary>
/// Fixture transcript writes the loaded <see cref="SessionStateStore"/> can see (CARD-1464).
/// Ingestion commits under the session's store write lease and publishes before releasing it, and
/// <c>SessionMessageQueueService.ReadWorkingAsync</c> prefers that projection whenever the store is
/// enabled. A row written straight through EF stays invisible to it until the next reload, so a
/// fixture that marks a session Working or idle that way leaves the queue reading the previous state.
/// Every write here inserts under the same lease and reconciles before releasing it, the CARD-1373
/// <see cref="BridgeQueueHarness.MarkWorkingAsync"/> pattern. A provider without the store keeps the
/// plain insert, which is then the only state there is.
/// </summary>
internal static class StoreAwareTranscript
{
    public static async Task WriteAsync(IServiceProvider? provider, Guid sessionId, Func<Task> insert)
    {
        var store = provider?.GetService<SessionStateStore>();
        if (store is null)
        {
            await insert();
            return;
        }

        using var lease = await store.BeginWriteAsync(sessionId, CancellationToken.None);
        await insert();
        await lease.ReconcileAsync(reset: false, CancellationToken.None);
    }

    /// <summary>
    /// Working is an AssistantText "busy" row after the last TurnEnd; idle is a TurnEnd/end_turn.
    /// Both carry a record timestamp, as the raw fixture rows they replace did.
    /// </summary>
    public static Task SetWorkingAsync(IServiceProvider? provider, string? connectionString, Guid sessionId, bool working) =>
        WriteAsync(provider, sessionId, () => working
            ? BridgeQueueHarness.InsertEntryAsync(sessionId, TranscriptKinds.AssistantText, "busy",
                timestamp: DateTime.UtcNow, connectionString: connectionString)
            : BridgeQueueHarness.InsertEntryAsync(sessionId, TranscriptKinds.TurnEnd,
                stopReason: TranscriptKinds.StopReasons.EndTurn, timestamp: DateTime.UtcNow,
                connectionString: connectionString));
}
