using System.Diagnostics;
using Antiphon.Server.Application.Interfaces;
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

/// <summary>CARD-0716 D-2. Drains launches, records what a shutdown interrupts, then stops the host.</summary>
public sealed class OperatorShutdownCoordinator
{
    private readonly ILaunchDrain _drain;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly IOptions<OperatorSettings> _settings;
    private readonly ILogger<OperatorShutdownCoordinator> _logger;
    private readonly IServiceScopeFactory _scopes;
    private readonly TimeProvider _clock;

    public OperatorShutdownCoordinator(
        ILaunchDrain drain,
        IHostApplicationLifetime lifetime,
        IOptions<OperatorSettings> settings,
        ILogger<OperatorShutdownCoordinator> logger,
        IServiceScopeFactory scopes,
        TimeProvider clock)
    {
        _drain = drain;
        _lifetime = lifetime;
        _settings = settings;
        _logger = logger;
        _scopes = scopes;
        _clock = clock;
    }

    public async Task StopAsync(string? reason, CancellationToken ct = default)
    {
        var seconds = _settings.Value.ShutdownDrainSeconds;
        var drained = true;
        var sw = Stopwatch.StartNew();
        if (seconds > 0)
        {
            try
            {
                await _drain.WaitForIdleAsync(TimeSpan.FromSeconds(seconds), ct);
            }
            catch (OperationCanceledException)
            {
                drained = false;
            }
        }

        IReadOnlyList<Guid> starting = [];
        try
        {
            await using var scope = _scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetService<AppDbContext>();
            if (db is not null)
                starting = await RecordInterruptedLaunchesAsync(db, reason, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Operator shutdown could not record interrupted launches");
        }

        _logger.LogInformation(
            "Operator shutdown: reason={Reason} drained={Drained} in {DrainMs}ms; starting launches {Count} ({SessionIds})",
            reason, drained, (int)sw.ElapsedMilliseconds, starting.Count, string.Join(',', starting));
        _lifetime.StopApplication();
    }

    public async Task<IReadOnlyList<Guid>> RecordInterruptedLaunchesAsync(
        AppDbContext db, string? reason, CancellationToken ct)
    {
        var sessions = await db.AgentSessions.AsNoTracking()
            .Where(s => s.Status == SessionStatus.Starting)
            .Select(s => new { s.Id, s.RunnerId })
            .ToListAsync(ct);
        if (sessions.Count == 0)
            return [];

        var ids = sessions.Select(s => s.Id).ToArray();
        var tasks = await db.AgentTasks
            .Where(t => t.AgentSessionId != null && ids.Contains(t.AgentSessionId.Value)
                && t.Status == AgentTaskStatus.Dispatched)
            .Select(t => new { t.Id, SessionId = t.AgentSessionId!.Value })
            .ToListAsync(ct);
        if (tasks.Count == 0)
            return [];

        var runnerBySession = sessions.ToDictionary(s => s.Id, s => s.RunnerId);
        var now = _clock.GetUtcNow().UtcDateTime;
        var recorded = new List<Guid>();
        foreach (var task in tasks)
        {
            runnerBySession.TryGetValue(task.SessionId, out var runnerId);
            var runner = string.IsNullOrWhiteSpace(runnerId) ? "local" : runnerId;
            db.AgentTaskEvents.Add(new AgentTaskEvent
            {
                Id = Guid.NewGuid(),
                AgentTaskId = task.Id,
                Type = AgentTaskEventType.Warning,
                Detail =
                    $"launch interrupted by an operator shutdown (reason={reason}) while Starting on runner '{runner}'; "
                    + "the restart reconciler re-attaches it if the runner holds it, or fails it with a restart reason",
                At = now,
            });
            if (!recorded.Contains(task.SessionId))
                recorded.Add(task.SessionId);
        }

        await db.SaveChangesAsync(ct);
        return recorded;
    }
}
