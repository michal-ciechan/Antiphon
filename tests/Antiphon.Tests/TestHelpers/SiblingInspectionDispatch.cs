using Antiphon.Server.Application.Services;

namespace Antiphon.Tests.TestHelpers;

internal static class SiblingInspectionDispatch
{
    /// <summary>Enqueue tick, owned-worker completion, then consumption tick. No inline inspection.</summary>
    public static async Task<AgentTaskDispatcher.TickResult> TickInspectedAsync(
        this AgentTaskDispatcher dispatcher, CancellationToken ct)
    {
        var queue = dispatcher.SiblingInspections;
        queue?.Start();
        var initial = await dispatcher.TickAsync(ct);
        if (queue is null || queue.Outstanding == 0) return initial;
        await queue.WhenIdleAsync(ct);
        return await dispatcher.TickAsync(ct);
    }
}
