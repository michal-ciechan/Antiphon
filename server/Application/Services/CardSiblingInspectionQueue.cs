using System.Threading.Channels;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Antiphon.Server.Application.Services;

public sealed record CardSiblingInspectionIntent(Guid TaskId, int Attempt, Guid? CardId,
    string Repository, string? RunnerId, RequestedWorktreeBaseMode Mode, string? RequestedRef,
    Guid? BaseTaskId, string? MergeTarget, string Fallback)
{
    public static CardSiblingInspectionIntent From(AgentTask task, string fallback) => new(
        task.Id, task.Attempt, task.CardId, Path.GetFullPath(task.RepoPath!), task.RunnerId,
        task.RequestedWorktreeBaseMode, task.WorktreeBaseRequestedRef, task.RequestedWorktreeBaseTaskId,
        task.MergeTargetRef, fallback);
}

/// <summary>
/// Owned, bounded observations. This service has no claim or launch authority. The workers, the
/// channel and shutdown are here; each task's entry lifetime is <see cref="SiblingInspectionResults"/> (CARD-1428 D-4).
/// </summary>
public sealed class CardSiblingInspectionQueue(IServiceScopeFactory scopes, TimeProvider clock,
    ILogger<CardSiblingInspectionQueue> logger) : IAsyncDisposable
{
    private readonly SiblingInspectionResults _results = new(clock);
    private readonly object _sync = new();
    private readonly Dictionary<Guid, long> _attempted = [];
    private readonly Channel<SiblingInspectionResults.Entry> _pending = Channel.CreateBounded<SiblingInspectionResults.Entry>(32);
    private readonly CancellationTokenSource _stopping = new();
    private Task[]? _workers;
    private int _active;
    public int Active => Volatile.Read(ref _active);

    /// <summary>Every entry, in flight or completed.</summary>
    public int Outstanding => _results.Count;

    /// <summary>Entries the workers have not completed.</summary>
    public int InFlight => _results.InFlightCount;

    public void Start()
    {
        lock (_sync)
        {
            if (_workers is not null || _stopping.IsCancellationRequested) return;
            _workers = [Task.Run(WorkerAsync), Task.Run(WorkerAsync)];
        }
    }

    public bool Attempted(Guid taskId) { lock (_sync) return _attempted.ContainsKey(taskId); }

    public void Reconcile(IReadOnlySet<Guid> queued)
    {
        _results.Reconcile(queued);
        lock (_sync)
            foreach (var id in _attempted.Keys.Where(id => !queued.Contains(id)).ToArray()) _attempted.Remove(id);
    }

    public bool TryEnqueue(CardSiblingInspectionIntent intent)
    {
        lock (_sync)
        {
            if (_stopping.IsCancellationRequested) return false;
            var entry = _results.Begin(intent, out var started);
            if (entry is null) return false;
            if (!started) return true;
            if (!_pending.Writer.TryWrite(entry)) { _results.Abandon(entry); return false; }
            _attempted[intent.TaskId] = clock.GetTimestamp();
            if (_attempted.Count > 32)
                _attempted.Remove(_attempted.MinBy(pair => pair.Value).Key);
            return true;
        }
    }

    public SiblingBaseGuard? Take(CardSiblingInspectionIntent intent) => _results.Peek(intent);

    public async Task WhenIdleAsync(CancellationToken ct = default) =>
        await Task.WhenAll(_results.Completions()).WaitAsync(ct);

    private async Task WorkerAsync()
    {
        try
        {
            await foreach (var entry in _pending.Reader.ReadAllAsync(_stopping.Token))
            {
                using var operation = CancellationTokenSource.CreateLinkedTokenSource(_stopping.Token, entry.Cancellation);
                Interlocked.Increment(ref _active);
                SiblingBaseGuard? result = null;
                try
                {
                    operation.Token.ThrowIfCancellationRequested();
                    await using var scope = scopes.CreateAsyncScope();
                    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                    var service = scope.ServiceProvider.GetRequiredService<CardSiblingInspectionService>();
                    using var inspection = service.Begin(entry.Intent.Repository, operation.Token);
                    var task = await inspection.ReadAsync(token => db.AgentTasks.AsNoTracking()
                        .SingleOrDefaultAsync(t => t.Id == entry.Intent.TaskId, token));
                    if (task is null || task.Status != AgentTaskStatus.Queued || task.WorktreePath is not null
                        || task.RepoPath is null) continue;
                    var fallback = await service.ConfiguredDefaultAsync(task, inspection, operation.Token);
                    if (CardSiblingInspectionIntent.From(task, fallback) != entry.Intent) continue;
                    result = await service.InspectAsync(task, inspection, operation.Token);
                }
                catch (OperationCanceledException) when (operation.IsCancellationRequested) { }
                catch (Exception ex)
                {
                    result = new SiblingBaseGuard(null, [], null,
                        ex is WorktreeBaseInspectionLimitException limit ? limit.Reason : "git_inspection_error",
                        PendingKnown: false);
                    logger.LogWarning("Sibling inspection failed for task {TaskId} attempt {Attempt} reason {Reason}",
                        entry.Intent.TaskId, entry.Intent.Attempt, result.Reason);
                }
                finally
                {
                    Interlocked.Decrement(ref _active);
                    _results.Complete(entry, result);
                }
            }
        }
        catch (OperationCanceledException) when (_stopping.IsCancellationRequested) { }
    }

    public async Task StopAsync(CancellationToken ct)
    {
        await _stopping.CancelAsync();
        Task[] workers;
        lock (_sync) workers = _workers ?? [];
        // Caller cancellation does not abandon the workers this service owns.
        await Task.WhenAll(workers);
        _results.Clear();
    }

    public async ValueTask DisposeAsync() => await StopAsync(CancellationToken.None);
}
