using Antiphon.Server.Application.Services;

namespace Antiphon.Server.Infrastructure.Agents;

/// <summary>Pulls the runner transcript through the existing persistence owner.</summary>
public sealed class ExpectationTranscriptCatchUp(AgentSessionRuntime runtime) : IExpectationCatchUp
{
    public async Task CatchUpAsync(IReadOnlyCollection<Guid> sessionIds, CancellationToken ct)
    {
        foreach (var sessionId in sessionIds)
        {
            ct.ThrowIfCancellationRequested();
            await runtime.CatchUpTranscriptAsync(sessionId, ct);
        }
    }
}
