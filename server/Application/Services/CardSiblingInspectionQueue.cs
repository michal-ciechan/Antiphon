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

/// <summary>Owned, bounded observations. This service has no claim or launch authority.</summary>
public sealed class CardSiblingInspectionQueue(IServiceScopeFactory scopes, TimeProvider clock,
    ILogger<CardSiblingInspectionQueue> logger) : IAsyncDisposable
{
    private sealed class Entry(CardSiblingInspectionIntent intent)
    {
        public CardSiblingInspectionIntent Intent { get; } = intent;
        public CancellationTokenSource Cancel { get; } = new();
        public TaskCompletionSource Done { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public SiblingBaseGuard? Result { get; set; }
        public long CompletedAt { get; set; }
    }

    private readonly object _sync = new();
    private readonly Dictionary<Guid, Entry> _entries = [];
    private readonly Dictionary<Guid, long> _attempted = [];
    private readonly Channel<Entry> _pending = Channel.CreateBounded<Entry>(32);
    private readonly CancellationTokenSource _stopping = new();
    private Task[]? _workers;
    private int _active;
    public int Active => Volatile.Read(ref _active);
    public int Outstanding { get { lock (_sync) return _entries.Count; } }

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
        lock (_sync)
        {
            foreach (var entry in _entries.Values.ToArray())
                if (!queued.Contains(entry.Intent.TaskId) || entry.Done.Task.IsCompleted
                    && clock.GetElapsedTime(entry.CompletedAt) >= TimeSpan.FromSeconds(30))
                    Evict(entry);
            foreach (var id in _attempted.Keys.Where(id => !queued.Contains(id)).ToArray()) _attempted.Remove(id);
        }
    }

    private void Evict(Entry entry)
    {
        entry.Cancel.Cancel();
        // Keep canceled work counted until the owned worker has joined it.
        if (entry.Done.Task.IsCompleted)
        {
            _entries.Remove(entry.Intent.TaskId);
            entry.Cancel.Dispose();
        }
    }

    public bool TryEnqueue(CardSiblingInspectionIntent intent)
    {
        lock (_sync)
        {
            if (_stopping.IsCancellationRequested) return false;
            if (_entries.TryGetValue(intent.TaskId, out var existing))
            {
                if (existing.Intent == intent && !existing.Cancel.IsCancellationRequested) return true;
                Evict(existing);
                if (_entries.ContainsKey(intent.TaskId)) return false;
            }
            if (_entries.Count >= 32) return false;
            var entry = new Entry(intent);
            if (!_pending.Writer.TryWrite(entry)) { entry.Cancel.Dispose(); return false; }
            _entries.Add(intent.TaskId, entry);
            _attempted[intent.TaskId] = clock.GetTimestamp();
            if (_attempted.Count > 32)
                _attempted.Remove(_attempted.MinBy(pair => pair.Value).Key);
            return true;
        }
    }

    public SiblingBaseGuard? Take(CardSiblingInspectionIntent intent)
    {
        lock (_sync)
        {
            if (!_entries.TryGetValue(intent.TaskId, out var entry)) return null;
            if (entry.Intent != intent) { Evict(entry); return null; }
            if (!entry.Done.Task.IsCompleted) return null;
            var result = !entry.Cancel.IsCancellationRequested
                && clock.GetElapsedTime(entry.CompletedAt) < TimeSpan.FromSeconds(30) ? entry.Result : null;
            _entries.Remove(intent.TaskId);
            entry.Cancel.Dispose();
            return result;
        }
    }

    public async Task WhenIdleAsync(CancellationToken ct = default)
    {
        Task[] pending;
        lock (_sync) pending = _entries.Values.Select(entry => entry.Done.Task).ToArray();
        await Task.WhenAll(pending).WaitAsync(ct);
    }

    private async Task WorkerAsync()
    {
        try
        {
            await foreach (var entry in _pending.Reader.ReadAllAsync(_stopping.Token))
            {
                using var operation = CancellationTokenSource.CreateLinkedTokenSource(_stopping.Token, entry.Cancel.Token);
                Interlocked.Increment(ref _active);
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
                    entry.Result = await service.InspectAsync(task, inspection, operation.Token);
                }
                catch (OperationCanceledException) when (operation.IsCancellationRequested) { }
                catch (Exception ex)
                {
                    entry.Result = new SiblingBaseGuard(null, [], null,
                        ex is WorktreeBaseInspectionLimitException limit ? limit.Reason : "git_inspection_error",
                        PendingKnown: false);
                    logger.LogWarning("Sibling inspection failed for task {TaskId} attempt {Attempt} reason {Reason}",
                        entry.Intent.TaskId, entry.Intent.Attempt, entry.Result.Reason);
                }
                finally
                {
                    Interlocked.Decrement(ref _active);
                    lock (_sync)
                    {
                        entry.CompletedAt = clock.GetTimestamp();
                        entry.Done.TrySetResult();
                        if (entry.Cancel.IsCancellationRequested) Evict(entry);
                    }
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
        lock (_sync)
        {
            foreach (var entry in _entries.Values)
            {
                entry.Done.TrySetResult();
                entry.Cancel.Dispose();
            }
            _entries.Clear();
        }
    }

    public async ValueTask DisposeAsync() => await StopAsync(CancellationToken.None);
}
