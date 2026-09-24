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

    private sealed record InFlight(string? RunnerId, DateTime Since, TaskCompletionSource Done);

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
                var push = await remote.PushBranchAsync(task, ct);
                if (!push.Pushed || push.Sha is null)
                {
                    failure = $"The task branch could not be pushed to origin ({push.Warning}); the task stays Queued.";
                }
                else
                {
                    var path = await remote.MirrorAsync(task, push.Sha, ct);
                    // D-8: keyed by id, not by status, so a mirror that succeeds after a cancel is
                    // still known to retirement and removed rather than left as runner residue.
                    await db.AgentTasks.Where(t => t.Id == taskId)
                        .ExecuteUpdateAsync(s => s
                            .SetProperty(t => t.RemoteWorktreePath, path)
                            .SetProperty(t => t.RemotePrepFailures, 0)
                            .SetProperty(t => t.DispatchNotBeforeAt, (DateTime?)null), CancellationToken.None);
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

    public async ValueTask DisposeAsync()
    {
        try { await _stopping.CancelAsync(); } catch (ObjectDisposedException) { }
        try { await WhenIdleAsync().WaitAsync(TimeSpan.FromSeconds(10)); }
        catch (TimeoutException) { }
        _stopping.Dispose();
    }
}
