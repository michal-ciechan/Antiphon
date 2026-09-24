using Antiphon.Server.Application.Interfaces;

namespace Antiphon.Server.Application.Services;

public sealed partial class SessionMessageQueueService
{
    /// <summary>CARD-0650 S4 direct watchdog send. Scaffold: refuses until S4 lands.</summary>
    internal Task<ExpectationSendResult> SendExpectationNowAsync(
        Guid sessionId,
        DateTime expectedGeneration,
        Guid ownerAgentId,
        string body,
        Func<ExpectationSendAttempt, CancellationToken, Task<bool>> commitAttempt,
        CancellationToken ct) =>
        Task.FromResult(ExpectationSendResult.Refuse("not_implemented"));
}

/// <summary>Adapter from the watchdog's I/O seam to the real queue service.</summary>
public sealed class SessionQueueExpectationPromptSender(SessionMessageQueueService queue) : IExpectationPromptSender
{
    public Task<ExpectationSendResult> SendAsync(
        Guid sessionId,
        DateTime expectedGeneration,
        Guid ownerAgentId,
        string body,
        Func<ExpectationSendAttempt, CancellationToken, Task<bool>> commitAttempt,
        CancellationToken ct) =>
        queue.SendExpectationNowAsync(sessionId, expectedGeneration, ownerAgentId, body, commitAttempt, ct);
}
