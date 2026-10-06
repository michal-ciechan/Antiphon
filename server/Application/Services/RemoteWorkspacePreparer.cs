using System.Collections.Concurrent;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Application.Services;

/// <summary>CARD-1076 D-4. Where one remote-prep operation is, for the pipeline's heldBy.</summary>
public enum RemotePrepPhase
{
    WaitingForPushTurn,
    Pushing,
    Mirroring,
}

/// <summary>CARD-1076 D-4. In-flight remote preparation as the preparer sees it.</summary>
public readonly record struct RemotePrepProgress(DateTime Since, RemotePrepPhase Phase, Guid? BehindTaskId);

/// <summary>
/// CARD-0633 D-4. Prepares a runner-bound task's remote workspace (branch push to origin, then the
/// runner's mirror of it) as an owned background operation, OFF the dispatcher's serial tick and
/// outside any claim transaction. A silent runner used to hold the tick for the whole mirror budget
/// per task, per tick, with the task row locked (CARD-0629).
///
/// Each operation runs on its own service scope and writes its outcome keyed by task id only:
/// success records <see cref="AgentTask.RemoteWorktreePath"/> and resets the backoff; failure adds
/// a Warning event and pushes <see cref="AgentTask.DispatchNotBeforeAt"/> out by
/// <see cref="Backoff"/>. The in-flight registry is memory-only on purpose: a restart loses it,
/// the next tick re-arms, and the runner's mirror of the same sha is idempotent. Shutdown
/// cancellation writes nothing.
/// </summary>
public sealed class RemoteWorkspacePreparer : IAsyncDisposable
{
    private readonly IServiceScopeFactory _scopes;
    private readonly DelegationSettings _settings;
    private readonly TimeProvider _clock;
    private readonly ILogger<RemoteWorkspacePreparer> _logger;
    private readonly CancellationTokenSource _stopping;
    private readonly ConcurrentDictionary<Guid, InFlight> _inFlight = new();
    private readonly ConcurrentDictionary<string, PushGate> _pushGates = new(RepoPathComparer);

    private static readonly StringComparer RepoPathComparer = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    private sealed class InFlight
    {
        public InFlight(string? runnerId, DateTime since, TaskCompletionSource done)
        {
            RunnerId = runnerId;
            Since = since;
            Done = done;
        }

        public string? RunnerId { get; }
        public DateTime Since { get; }
        public TaskCompletionSource Done { get; }
        public RemotePrepPhase Phase { get; set; } = RemotePrepPhase.Pushing;
        public Guid? BehindTaskId { get; set; }
    }

    /// <summary>One push at a time for a repository. The wait is not part of the push budget.</summary>
    private sealed class PushGate
    {
        public readonly object Sync = new();
        public readonly SemaphoreSlim Turn = new(1, 1);
        public Guid Holder;
    }

    public RemoteWorkspacePreparer(
        IServiceScopeFactory scopes,
        IOptions<DelegationSettings> settings,
        TimeProvider clock,
        ILogger<RemoteWorkspacePreparer> logger,
        IHostApplicationLifetime? lifetime = null)
    {
        _scopes = scopes;
        _settings = settings.Value;
        _clock = clock;
        _logger = logger;
        _stopping = lifetime is null
            ? new CancellationTokenSource()
            : CancellationTokenSource.CreateLinkedTokenSource(lifetime.ApplicationStopping);
    }

    /// <summary>
    /// D-6: the hold after the <paramref name="failures"/>-th consecutive failure:
    /// base * 2^(n-1), capped.
    /// </summary>
    public static TimeSpan Backoff(int failures, DelegationSettings settings)
    {
        var baseSeconds = Math.Max(1, settings.RemotePrepBackoffBaseSeconds);
        var maxSeconds = Math.Max(baseSeconds, settings.RemotePrepBackoffMaxSeconds);
        var exponent = Math.Clamp(failures - 1, 0, 30);
        var seconds = Math.Min((double)baseSeconds * Math.Pow(2, exponent), maxSeconds);
        return TimeSpan.FromSeconds(seconds);
    }

    /// <summary>
    /// Starts preparing <paramref name="taskId"/> unless an operation for it is already running in
    /// this process. Never awaits the operation. Returns whether a new operation started.
    /// </summary>
    public bool TryBegin(Guid taskId, string? runnerId = null)
    {
        if (_stopping.IsCancellationRequested)
            return false;
        var entry = new InFlight(runnerId, _clock.GetUtcNow().UtcDateTime,
            new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
        if (!_inFlight.TryAdd(taskId, entry))
            return false;
        _ = Task.Run(() => RunAsync(taskId, entry), CancellationToken.None);
        return true;
    }

    /// <summary>CARD-0653: mirrors already asked of this runner, excluding the task being gated.</summary>
    public int InFlightCount(string runnerId, Guid exceptTaskId) =>
        _inFlight.Count(pair => pair.Key != exceptTaskId
            && string.Equals(pair.Value.RunnerId, runnerId, StringComparison.Ordinal));

    public bool IsInFlight(Guid taskId, out DateTime since)
    {
        if (_inFlight.TryGetValue(taskId, out var entry))
        {
            since = entry.Since;
            return true;
        }

        since = default;
        return false;
    }

    /// <summary>
    /// CARD-1076 D-4. Null when <paramref name="taskId"/> has no preparation in flight.
    /// <see cref="RemotePrepProgress.BehindTaskId"/> is the enqueue-time snapshot (CARD-1093):
    /// with three or more tasks waiting on one repository it can name a pusher that has already
    /// finished, or be null. It is not the exact current holder.
    /// </summary>
    public RemotePrepProgress? Progress(Guid taskId)
    {
        if (!_inFlight.TryGetValue(taskId, out var entry))
            return null;
        return new RemotePrepProgress(entry.Since, entry.Phase, entry.BehindTaskId);
    }

    /// <summary>Test seam: completes once no operation is in flight.</summary>
    public async Task WhenIdleAsync(CancellationToken ct = default)
    {
        while (true)
        {
            var pending = _inFlight.Values.Select(e => e.Done.Task).ToArray();
            if (pending.Length == 0)
                return;
            await Task.WhenAll(pending).WaitAsync(ct);
        }
    }

    private async Task RunAsync(Guid taskId, InFlight entry)
    {
        var ct = _stopping.Token;
        try
        {
            await using var scope = _scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var remote = scope.ServiceProvider.GetRequiredService<RemoteWorkspaceService>();
            var task = await db.AgentTasks.AsNoTracking().FirstOrDefaultAsync(t => t.Id == taskId, ct);
            if (task is null)
                return;

            string? failure;
            try
            {
                var gateKey = PushGateKey(task.RepoPath);
                var gate = gateKey is null ? null : _pushGates.GetOrAdd(gateKey, static _ => new PushGate());
                if (gate is not null)
                    await EnterPushTurnAsync(gate, entry, taskId, ct);

                RemotePushResult push;
                try
                {
                    push = await remote.PushBranchAsync(task, ct);
                }
                finally
                {
                    if (gate is not null)
                        ExitPushTurn(gate, taskId);
                }

                if (!push.Pushed || push.Sha is null)
                {
                    failure = $"The task branch could not be pushed to origin ({push.Warning}); the task stays Queued.";
                }
                else
                {
                    entry.Phase = RemotePrepPhase.Mirroring;
                    entry.BehindTaskId = null;
                    var path = await remote.MirrorAsync(task, push.Sha, push.Repository!, ct);
                    // D-8: keyed by id, not by status, so a mirror that succeeds after a cancel is
                    // still known to retirement and removed rather than left as runner residue.
                    // The runner id is part of the key: a drain that moved the task while this
                    // mirror was in flight must not record a path that exists only on the old runner.
                    var runnerId = entry.RunnerId;
                    var recorded = await db.AgentTasks
                        .Where(t => t.Id == taskId && t.RunnerId == runnerId)
                        .ExecuteUpdateAsync(s => s
                            .SetProperty(t => t.RemoteWorktreePath, path)
                            .SetProperty(t => t.RemotePrepFailures, 0)
                            .SetProperty(t => t.DispatchNotBeforeAt, (DateTime?)null), CancellationToken.None);
                    if (recorded == 0)
                    {
                        _logger.LogWarning(
                            "Remote workspace for task {ShortId} was mirrored on runner {RunnerId} after the task left that runner; the path was not recorded",
                            DelegationReportFormatter.Short(taskId), entry.RunnerId);
                        return;
                    }

                    _logger.LogInformation(
                        "Remote workspace for task {ShortId} is ready at {Path}",
                        DelegationReportFormatter.Short(taskId), path);
                    return;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                // D-10: name the transport code (phone_home_request_timeout, ...) when there is one.
                var reason = ex is Antiphon.SessionRunner.Contracts.PhoneHomeTransportException transport
                    ? $"{transport.Code}: {ex.Message}"
                    : ex.Message;
                failure = $"The runner could not mirror the task branch ({reason}); the task stays Queued.";
                if (!await remote.SupportsRepositoryMirrorsAsync(task.RunnerId, ct))
                    failure += $" Runner '{task.RunnerId}' does not advertise workspaceRepositoryV1 and mirrors only its primary repository; upgrade it (CARD-0727) or pin another runner.";
            }

            var attempt = task.RemotePrepFailures + 1;
            var now = _clock.GetUtcNow().UtcDateTime;
            var notBefore = now + Backoff(attempt, _settings);
            db.AgentTaskEvents.Add(new AgentTaskEvent
            {
                Id = Guid.NewGuid(),
                AgentTaskId = taskId,
                Type = AgentTaskEventType.Warning,
                Detail = failure,
                At = now,
            });
            await db.SaveChangesAsync(CancellationToken.None);
            await db.AgentTasks.Where(t => t.Id == taskId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(t => t.RemotePrepFailures, t => t.RemotePrepFailures + 1)
                    .SetProperty(t => t.DispatchNotBeforeAt, notBefore), CancellationToken.None);
            _logger.LogWarning(
                "Remote workspace preparation for task {ShortId} failed (attempt {Attempt}); retry not before {NotBefore:O}: {Detail}",
                DelegationReportFormatter.Short(taskId), attempt, notBefore, failure);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Shutdown: write nothing; the next process re-arms from the row.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Remote workspace preparation for task {TaskId} could not record its outcome", taskId);
        }
        finally
        {
            _inFlight.TryRemove(new KeyValuePair<Guid, InFlight>(taskId, entry));
            entry.Done.TrySetResult();
        }
    }

    private static string? PushGateKey(string? repoPath)
    {
        if (string.IsNullOrWhiteSpace(repoPath))
            return null;
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(repoPath));
    }

    /// <summary>
    /// CARD-1076 D-4. Waits for this repository's push turn. The wait honours
    /// <paramref name="ct"/> and is outside the push budget. Returns holding the turn.
    /// </summary>
    private async Task EnterPushTurnAsync(PushGate gate, InFlight entry, Guid taskId, CancellationToken ct)
    {
        lock (gate.Sync)
        {
            if (gate.Holder == Guid.Empty && gate.Turn.CurrentCount > 0 && gate.Turn.Wait(0))
            {
                gate.Holder = taskId;
                entry.Phase = RemotePrepPhase.Pushing;
                entry.BehindTaskId = null;
                return;
            }

            entry.Phase = RemotePrepPhase.WaitingForPushTurn;
            entry.BehindTaskId = gate.Holder == Guid.Empty ? null : gate.Holder;
        }

        await gate.Turn.WaitAsync(ct);
        lock (gate.Sync)
        {
            gate.Holder = taskId;
            entry.Phase = RemotePrepPhase.Pushing;
            entry.BehindTaskId = null;
        }
    }

    private static void ExitPushTurn(PushGate gate, Guid taskId)
    {
        lock (gate.Sync)
        {
            if (gate.Holder == taskId)
                gate.Holder = Guid.Empty;
            gate.Turn.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        try { await _stopping.CancelAsync(); } catch (ObjectDisposedException) { }
        try { await WhenIdleAsync().WaitAsync(TimeSpan.FromSeconds(10)); }
        catch (TimeoutException) { }
        _stopping.Dispose();
    }
}
