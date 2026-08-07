using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// Turns queued tasks into running delegates. Mirrors <see cref="OrchestratorService"/>'s two proven
/// patterns — count-then-skip against a concurrency cap, and a transactional claim so two ticks can
/// never dispatch the same task.
///
/// Everything about WHAT the delegate will be (tier, directory, whether it may delegate) was decided
/// and authorised at creation; this only executes it.
/// </summary>
public sealed class AgentTaskDispatcher
{
    private readonly AppDbContext _db;
    private readonly AgentRegistry _agentRegistry;
    private readonly AgentSessionLaunchQueue _launchQueue;
    private readonly SessionMessageQueueService _queue;
    private readonly DelegationSettings _settings;
    private readonly IEventBus _eventBus;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<AgentTaskDispatcher> _logger;

    public AgentTaskDispatcher(
        AppDbContext db,
        AgentRegistry agentRegistry,
        AgentSessionLaunchQueue launchQueue,
        SessionMessageQueueService queue,
        IOptions<DelegationSettings> settings,
        IEventBus eventBus,
        TimeProvider timeProvider,
        ILogger<AgentTaskDispatcher> logger)
    {
        _db = db;
        _agentRegistry = agentRegistry;
        _launchQueue = launchQueue;
        _queue = queue;
        _settings = settings.Value;
        _eventBus = eventBus;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public sealed record TickResult(int Eligible, int Dispatched, int SkippedConcurrency, int SkippedScope, int Failures);

    public async Task<TickResult> TickAsync(CancellationToken ct)
    {
        if (!_settings.Enabled)
            return new TickResult(0, 0, 0, 0, 0);

        var active = await _db.AgentTasks.CountAsync(
            t => t.Status == AgentTaskStatus.Dispatched || t.Status == AgentTaskStatus.Working, ct);

        var queued = await _db.AgentTasks
            .Where(t => t.Status == AgentTaskStatus.Queued)
            .OrderBy(t => t.CreatedAt)
            .ToListAsync(ct);
        if (queued.Count == 0)
            return new TickResult(0, 0, 0, 0, 0);

        // Shared tasks that declare overlapping file scopes must not run concurrently — the second
        // waits rather than racing on read-modify-write. This is the cost of Shared being the
        // default, and the mitigation for it.
        var busyScopes = await _db.AgentTasks
            .Where(t => (t.Status == AgentTaskStatus.Dispatched || t.Status == AgentTaskStatus.Working)
                && t.ScopeGlob != null)
            .Select(t => new { t.WorkingDirectory, t.ScopeGlob })
            .ToListAsync(ct);
        var heldScopes = busyScopes
            .Select(s => (s.WorkingDirectory, Glob: s.ScopeGlob!))
            .ToList();

        var dispatched = 0;
        var skippedConcurrency = 0;
        var skippedScope = 0;
        var failures = 0;

        foreach (var task in queued)
        {
            ct.ThrowIfCancellationRequested();

            if (active + dispatched >= _settings.MaxConcurrentTasks)
            {
                skippedConcurrency++;
                continue;
            }

            if (task.ScopeGlob is { } glob && heldScopes.Any(h => ScopesIntersect(h, (task.WorkingDirectory, glob))))
            {
                skippedScope++;
                continue;
            }

            // A root that has burned through its budget stops dispatching. Work already in flight is
            // left alone — killing it would lose reports that have already been paid for.
            if (await RootIsOverBudgetAsync(task.RootTaskId, ct))
            {
                await BlockAsync(task, $"Run cost ceiling reached (${_settings.MaxCostUsdPerRoot:0.00}).", ct);
                continue;
            }

            try
            {
                if (await DispatchOneAsync(task, ct))
                {
                    dispatched++;
                    if (task.ScopeGlob is { } held)
                        heldScopes.Add((task.WorkingDirectory, held));
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Failed to dispatch task {ShortId}",
                    DelegationReportFormatter.Short(task.Id));
                await FailAsync(task, ex.Message, ct);
                failures++;
            }
        }

        return new TickResult(queued.Count, dispatched, skippedConcurrency, skippedScope, failures);
    }

    private async Task<bool> DispatchOneAsync(AgentTask task, CancellationToken ct)
    {
        // Transactional claim: re-read under the concurrency token so a second tick (or another
        // server instance) racing this one loses cleanly instead of double-launching a delegate.
        await using var transaction = await _db.Database.BeginTransactionAsync(ct);
        var claimed = await _db.AgentTasks.FirstOrDefaultAsync(
            t => t.Id == task.Id
                && t.Status == AgentTaskStatus.Queued
                && t.ConcurrencyToken == task.ConcurrencyToken, ct);
        if (claimed is null)
        {
            await transaction.RollbackAsync(ct);
            return false;
        }

        var now = UtcNow();
        var agent = await ResolveAgentAsync(claimed, now, ct);
        var session = new AgentSession
        {
            Id = Guid.NewGuid(),
            CardId = null,
            WorktreeId = null,
            DefinitionName = _agentRegistry.Settings.DefaultDefinition,
            AgentKind = AgentKind.ClaudeCode,
            Status = SessionStatus.Starting,
            Cwd = claimed.WorkingDirectory,
            Cols = _settings.DefaultCols,
            Rows = _settings.DefaultRows,
            CreatedAt = now,
            StartedAt = now,
            LastSeenAt = now,
        };
        _db.AgentSessions.Add(session);

        claimed.AgentId = agent.Id;
        claimed.AgentSessionId = session.Id;
        claimed.Status = AgentTaskStatus.Dispatched;
        claimed.DispatchedAt = now;
        claimed.ConcurrencyToken = Guid.NewGuid();

        agent.PersistentSessionId = session.Id.ToString("D");
        agent.Status = AgentStatus.Running;
        agent.UpdatedAt = now;

        _db.AgentTaskEvents.Add(new AgentTaskEvent
        {
            Id = Guid.NewGuid(),
            AgentTaskId = claimed.Id,
            Type = AgentTaskEventType.Dispatched,
            ModelLevel = claimed.ModelLevel,
            Detail = $"Dispatched to agent '{agent.Name}' ({ModelLevelAliases.ForClaude(claimed.ModelLevel)}) in {claimed.WorkingDirectory}",
            At = now,
        });

        await _db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        var spec = BuildLaunchSpec(claimed, agent, session);
        _launchQueue.EnqueueInteractiveSession(session.Id, agent.Id, spec, remoteControlName: null, notes: null);

        // The brief goes through the message QUEUE, never straight to the pty: that is the only path
        // that normalises line endings, wraps in a bracketed paste, and submits with a separate CR.
        // A raw multi-line write fragments into several turns (documented live miss).
        var brief = DelegationReportFormatter.BuildBrief(claimed, _settings);
        try
        {
            await _queue.EnqueueAsync(
                session.Id, brief, MessageSendMode.WhenIdle, ct, QueuedMessageOrigin.Delegation);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The message row is persisted BEFORE the queue probes the runner for liveness, so a
            // runner that is briefly unreachable has not lost the brief — it delivers when the
            // session comes up. Failing the task here would turn a transient transport blip into a
            // permanent death for every task dispatched during it.
            _logger.LogWarning(
                ex, "Task {ShortId}: the brief is queued but could not be delivered yet",
                DelegationReportFormatter.Short(claimed.Id));
        }

        await _eventBus.PublishToAllAsync(
            "AgentTaskChanged", new { taskId = claimed.Id, rootId = claimed.RootTaskId }, ct);
        _logger.LogInformation(
            "Dispatched task {ShortId} ({Kind}/{Role} at {Alias}) to session {SessionId} in {Dir}",
            DelegationReportFormatter.Short(claimed.Id), claimed.Kind, claimed.Role,
            ModelLevelAliases.ForClaude(claimed.ModelLevel), session.Id, claimed.WorkingDirectory);
        return true;
    }

    /// <summary>
    /// Launch args and environment for a delegate. Three things make delegation work here: the tier
    /// as <c>--model &lt;alias&gt;</c>, the contracts as <c>--append-system-prompt</c>, and the
    /// ANTIPHON_* env block that lets the delegate call back without being told who it is.
    /// </summary>
    internal AgentLaunchSpec BuildLaunchSpec(AgentTask task, Agent agent, AgentSession session)
    {
        var extraArgs = new List<string>
        {
            "--name", agent.Name,
            // Family alias, never a pinned version — every launch picks up the family's current model.
            "--model", ModelLevelAliases.ForClaude(task.ModelLevel),
        };

        // A sub-orchestrator gets the orchestrator contract at LAUNCH (survives compaction, applies
        // to every turn); the reporting contract rides the brief. A worker needs neither at launch.
        if (task.Kind == AgentTaskKind.Orchestrator)
            extraArgs.AddRange(["--append-system-prompt", DelegationReportFormatter.OrchestratorContract]);

        return _agentRegistry.Resolve(
            _agentRegistry.Settings.DefaultDefinition,
            new AgentLaunchOptions(
                Cwd: task.WorkingDirectory,
                Cols: session.Cols,
                Rows: session.Rows,
                ExtraArgs: extraArgs,
                ExtraEnv: BuildEnv(task, agent, session)));
    }

    /// <summary>
    /// The env contract. Because the caller's identity lives here, the delegate never has to know or
    /// pass it — parent linkage, depth accounting, fan-out caps and reply routing all follow from
    /// ANTIPHON_SESSION_ID and the token.
    /// </summary>
    internal Dictionary<string, string> BuildEnv(AgentTask task, Agent agent, AgentSession session)
    {
        var env = new Dictionary<string, string>
        {
            ["ANTIPHON_API"] = _settings.ApiBaseUrl,
            ["ANTIPHON_SESSION_ID"] = session.Id.ToString("D"),
            ["ANTIPHON_AGENT_ID"] = agent.Id.ToString("D"),
            ["ANTIPHON_TASK_ID"] = task.Id.ToString("D"),
        };

        // The raw token exists only between creation and this injection — it is stored hashed, so
        // this is the one and only chance to hand it to the delegate.
        if (AgentTaskService.RawTokens.TryRemove(task.Id, out var token))
            env["ANTIPHON_TASK_TOKEN"] = token;

        return env;
    }

    private async Task<Agent> ResolveAgentAsync(AgentTask task, DateTime now, CancellationToken ct)
    {
        if (task.AgentId is Guid pinned)
        {
            var existing = await _db.Agents.FirstOrDefaultAsync(a => a.Id == pinned, ct);
            if (existing is not null)
                return existing;
        }

        // Ephemeral is the right default for delegated work: clean context is the entire economy of
        // the design, and --model is a launch arg, so a fresh process is the only way to pick a tier
        // per task.
        var shortId = DelegationReportFormatter.Short(task.Id);
        var name = $"task-{shortId}";
        var agent = new Agent
        {
            Id = Guid.NewGuid(),
            Name = name,
            Slug = name,
            WorkingDirectory = task.WorkingDirectory,
            Details = $"Ephemeral delegate for {task.Kind}/{task.Role} task {shortId}.",
            Status = AgentStatus.Idle,
            ModelLevel = task.ModelLevel,
            AlwaysOn = false,
            RemoteControlEnabled = false,
            CreatedAt = now,
            UpdatedAt = now,
        };
        _db.Agents.Add(agent);
        return agent;
    }

    private async Task<bool> RootIsOverBudgetAsync(Guid rootId, CancellationToken ct)
    {
        var spent = await _db.AgentTasks
            .Where(t => t.RootTaskId == rootId)
            .SumAsync(t => (decimal?)t.CostUsd, ct) ?? 0m;
        return spent >= _settings.MaxCostUsdPerRoot;
    }

    private async Task BlockAsync(AgentTask task, string reason, CancellationToken ct)
    {
        var now = UtcNow();
        task.Status = AgentTaskStatus.Blocked;
        task.FailureReason = reason;
        task.ConcurrencyToken = Guid.NewGuid();
        _db.AgentTaskEvents.Add(new AgentTaskEvent
        {
            Id = Guid.NewGuid(),
            AgentTaskId = task.Id,
            Type = AgentTaskEventType.Blocked,
            Detail = reason,
            At = now,
        });
        await _db.SaveChangesAsync(ct);
    }

    private async Task FailAsync(AgentTask task, string reason, CancellationToken ct)
    {
        var now = UtcNow();
        task.Status = AgentTaskStatus.Failed;
        task.FailureReason = reason;
        task.CompletedAt = now;
        task.ConcurrencyToken = Guid.NewGuid();
        _db.AgentTaskEvents.Add(new AgentTaskEvent
        {
            Id = Guid.NewGuid(),
            AgentTaskId = task.Id,
            Type = AgentTaskEventType.Failed,
            Detail = reason.Length <= 4000 ? reason : reason[..4000],
            At = now,
        });
        await _db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Advisory lease check: same directory and overlapping globs. Deliberately coarse — a prefix
    /// match up to the first wildcard catches the real case (two tasks both owning "docs/**")
    /// without pretending to be a glob engine.
    /// </summary>
    internal static bool ScopesIntersect((string Dir, string Glob) a, (string Dir, string Glob) b)
    {
        if (!string.Equals(
                DelegationWorkspaceResolver.NormalizeSeparators(a.Dir),
                DelegationWorkspaceResolver.NormalizeSeparators(b.Dir),
                StringComparison.OrdinalIgnoreCase))
            return false;

        var left = LiteralPrefix(a.Glob);
        var right = LiteralPrefix(b.Glob);
        return left.StartsWith(right, StringComparison.OrdinalIgnoreCase)
            || right.StartsWith(left, StringComparison.OrdinalIgnoreCase);
    }

    private static string LiteralPrefix(string glob)
    {
        var normalized = glob.Replace('\\', '/').TrimStart('.', '/');
        var wildcard = normalized.IndexOfAny(['*', '?', '[']);
        return wildcard < 0 ? normalized : normalized[..wildcard];
    }

    private DateTime UtcNow() => _timeProvider.GetUtcNow().UtcDateTime;
}
