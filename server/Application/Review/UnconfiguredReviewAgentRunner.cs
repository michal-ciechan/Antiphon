using Antiphon.Server.Application.Interfaces;

namespace Antiphon.Server.Application.Review;

/// <summary>
/// The registered reviewer runner until the real one lands: every call fails loudly, so a run stops
/// at its reviewer node with this reason instead of reviewing nothing.
/// <para>
/// TODO(review-engine reviewers, design 4.7): dispatch a read-only Review-role delegate through
/// <c>AgentTaskService.CreateAsync</c> with the pack as its prompt and a checkout of the head SHA,
/// return <see cref="ReviewAgentStatus.Pending"/> with the task id as the handle, and complete when
/// the task's report arrives. It needs the per-repo mirror (design 8.4) for that checkout, and a
/// caller identity for server-originated tasks; neither exists yet.
/// </para>
/// </summary>
public sealed class UnconfiguredReviewAgentRunner : IReviewAgentRunner
{
    public const string Reason = "review agent runner not configured: no reviewer dispatch is registered yet (design 4.7)";

    public Task<ReviewAgentResult> RunAsync(ReviewAgentRequest request, CancellationToken ct) =>
        throw new ReviewAgentRunnerException(Reason);
}
