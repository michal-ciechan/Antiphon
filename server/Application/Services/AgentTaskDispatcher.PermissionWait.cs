using Antiphon.Server.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Antiphon.Server.Application.Services;

public sealed partial class AgentTaskDispatcher
{
    internal async Task<int> DetectProviderWaitsAsync(CancellationToken ct)
    {
        if (_providerWait is null || _scopeFactory is null) return 0;
        var tasks = await _db.AgentTasks.AsNoTracking()
            .Where(t => (t.Status == AgentTaskStatus.Dispatched || t.Status == AgentTaskStatus.Working)
                && t.AgentKind == AgentKind.ClaudeCode && t.AgentSessionId != null && t.DispatchedAt != null)
            .OrderBy(t => t.DispatchedAt).ThenBy(t => t.Id).Select(t => new { t.Id, t.AgentSessionId }).ToListAsync(ct);
        var recorded = 0;
        await Parallel.ForEachAsync(tasks, new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = ct }, async (task, token) =>
        {
            try
            {
                var facts = await _providerWait.ReadAsync(task.Id, false, token);
                // Runtime owns its own persistence scopes. No shared dispatcher DbContext enters this batch.
                if (facts is { IsWait: true } && _runtime is not null)
                    await _runtime.CatchUpTranscriptAsync(task.AgentSessionId!.Value, token);
                await using var scope = _scopeFactory.CreateAsyncScope();
                if (await scope.ServiceProvider.GetRequiredService<ProviderWaitWarningWriter>().RecordAsync(task.Id, facts, token))
                    Interlocked.Increment(ref recorded);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !token.IsCancellationRequested)
            { _logger.LogWarning(ex, "Provider wait observation failed for task {TaskId}; other tasks remain eligible", task.Id); }
        });
        return recorded;
    }
}
