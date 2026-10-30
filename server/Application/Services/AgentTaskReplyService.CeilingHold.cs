using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;

namespace Antiphon.Server.Application.Services;

internal sealed record CeilingOperatorAuthorization(string Actor);

public sealed partial class AgentTaskReplyService
{
    internal async Task<T> WithSessionSettlementAsync<T>(Guid sessionId, Func<Task<T>> action, CancellationToken ct)
    {
        var gate = _settleLocks.GetOrAdd(sessionId, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try { return await action(); }
        finally { gate.Release(); }
    }

    // Compatible dispatch scaffold. Typed controls remain refused until their red witnesses run.
    internal Task<AgentTaskSummaryDto> AnswerCeilingAsync(Guid taskId, ReplyToAgentTaskRequest request,
        CeilingOperatorAuthorization? authority, CancellationToken ct) =>
        throw new ConflictException("Typed ceiling control is not implemented.", "ceiling_control_unavailable");
}
