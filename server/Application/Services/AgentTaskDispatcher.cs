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
    private readonly DelegationWorktreeService _worktrees;
    private readonly AgentTaskService _tasks;
    private readonly IDelegateSessionStopper _sessions;
    private readonly DelegationSettings _settings;
    private readonly IEventBus _eventBus;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<AgentTaskDispatcher> _logger;

    public AgentTaskDispatcher(
        AppDbContext db,
        AgentRegistry agentRegistry,
        AgentSessionLaunchQueue launchQueue,
        SessionMessageQueueService queue,
        DelegationWorktreeService worktrees,
        AgentTaskService tasks,
        IDelegateSessionStopper sessions,
        IOptions<DelegationSettings> settings,
        IEventBus eventBus,
        TimeProvider timeProvider,
        ILogger<AgentTaskDispatcher> logger)
    {
        _db = db;
        _agentRegistry = agentRegistry;
        _launchQueue = launchQueue;
        _queue = queue;
        _worktrees = worktrees;
        _tasks = tasks;
        _sessions = sessions;
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

        // Before dispatching new work, deal with running work that has gone quiet — a stalled
        // opus Debug task escalating to fable IS the tier ladder working, not an error path.
        await AutoEscalateStalledAsync(ct);

        // And with work that never STARTED — zero transcript entries long after dispatch means the
        // boot prompt was lost, which is categorically different from slow progress and must fail
        // loudly, never escalate (a bigger model can't fix an undelivered brief).
        await FailNeverStartedAsync(ct);

        // And with warm delegates that have sat idle too long — the pool trades memory for
        // startup latency, and the janitor is what keeps that trade bounded.
        await RetireIdleWarmAgentsAsync(ct);

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

    /// <summary>
    /// The automatic rung of the ladder: a task whose role policy names an EscalateTo, still
    /// running past EscalateAfterMinutes with NO transcript progress in that window, is stopped
    /// and requeued one tier up. Progress resets the clock — a task that keeps producing output
    /// is thinking, not stalled, however long it takes.
    /// </summary>
    internal async Task<int> AutoEscalateStalledAsync(CancellationToken ct)
    {
        // Which roles can escalate at all is config; don't scan tasks that have nowhere to go.
        var escalatable = _settings.RolePolicy
            .Where(p => p.Value.EscalateTo is not null && p.Value.EscalateAfterMinutes is not null)
            .ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
        if (escalatable.Count == 0)
            return 0;

        var running = await _db.AgentTasks.AsNoTracking()
            .Where(t => (t.Status == AgentTaskStatus.Dispatched || t.Status == AgentTaskStatus.Working)
                && t.DispatchedAt != null && t.AgentSessionId != null)
            .ToListAsync(ct);

        var escalated = 0;
        var now = UtcNow();
        foreach (var task in running)
        {
            if (!escalatable.TryGetValue(task.Role.ToString(), out var policy))
                continue;
            // Already at (or above) the configured target — repeating the bump would be a no-op
            // that still kills the session. Frontier = 0, so "at or above" is <=.
            if ((int)task.ModelLevel <= (int)policy.EscalateTo!.Value)
                continue;

            var window = TimeSpan.FromMinutes(policy.EscalateAfterMinutes!.Value);
            var lastProgress = await _db.TranscriptEntries.AsNoTracking()
                .Where(e => e.AgentSessionId == task.AgentSessionId)
                .MaxAsync(e => (DateTime?)e.CreatedAt, ct);
            var stalledSince = lastProgress is { } p && p > task.DispatchedAt!.Value
                ? p
                : task.DispatchedAt!.Value;
            if (now - stalledSince < window)
                continue;

            try
            {
                // The handoff (BuildBrief) carries FailureReason into the next attempt — say WHY
                // this escalated so the bigger model starts from the stall, not from zero.
                var tracked = await _db.AgentTasks.FirstAsync(t => t.Id == task.Id, ct);
                tracked.FailureReason =
                    $"Stalled: no transcript progress for {(int)(now - stalledSince).TotalMinutes} minutes "
                    + $"at {ModelLevelAliases.ForClaude(task.ModelLevel)}.";
                await _tasks.EscalateAsync(
                    task.Id, policy.EscalateTo, ct,
                    reason: $"Auto: no progress for {(int)window.TotalMinutes}+ minutes.");
                escalated++;
                _logger.LogInformation(
                    "Task {ShortId} auto-escalated to {Level} after stalling",
                    DelegationReportFormatter.Short(task.Id), policy.EscalateTo);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // A concurrent settle or cancel beat us to the row — that IS progress; move on.
                _logger.LogDebug(ex, "Auto-escalation of task {ShortId} skipped",
                    DelegationReportFormatter.Short(task.Id));
            }
        }

        return escalated;
    }

    /// <summary>
    /// The delivery backstop (CARD-0003/CARD-0020) for a task still Dispatched
    /// <see cref="DelegationSettings.DeliveryFailTimeoutMinutes"/> after dispatch. Two ways that
    /// happens, and it reports which:
    ///
    /// <list type="bullet">
    /// <item>The brief never arrived — ZERO transcript entries. Four tasks sat like that for up to
    /// 26 minutes on 2026-08-09 while every surface reported Running.</item>
    /// <item>The brief arrived, the delegate worked and REPORTED, but no turn could be matched to
    /// the task, so nothing settled it (2026-08-11: three tasks stranded overnight after the pty
    /// ate the head of the brief and the marker with it).</item>
    /// </list>
    ///
    /// Deliberately separate from the stall scan above: escalation re-runs work on a bigger model,
    /// which would launder a lost prompt into a billed upgrade. The stranded-queue watchdog gets
    /// the whole window to redeliver a reverted brief first.
    /// </summary>
    internal async Task<int> FailNeverStartedAsync(CancellationToken ct)
    {
        var timeout = TimeSpan.FromMinutes(_settings.DeliveryFailTimeoutMinutes);
        if (timeout <= TimeSpan.Zero)
            return 0;

        var cutoff = UtcNow() - timeout;
        var suspects = await _db.AgentTasks
            .Where(t => t.Status == AgentTaskStatus.Dispatched
                && t.DispatchedAt != null && t.DispatchedAt < cutoff
                && t.AgentSessionId != null)
            .ToListAsync(ct);

        var failed = 0;
        foreach (var task in suspects)
        {
            ct.ThrowIfCancellationRequested();
            var sessionId = task.AgentSessionId!.Value;

            // Any transcript entry at all means the session started — slow work belongs to the
            // stall scan, not here.
            var started = await _db.TranscriptEntries.AnyAsync(t => t.AgentSessionId == sessionId, ct);

            string reason;
            if (!started)
            {
                var briefStatus = await _db.SessionQueuedMessages
                    .AsNoTracking()
                    .Where(m => m.AgentSessionId == sessionId && m.Origin == QueuedMessageOrigin.Delegation)
                    .OrderBy(m => m.Sequence)
                    .Select(m => (QueuedMessageStatus?)m.Status)
                    .FirstOrDefaultAsync(ct);
                var evidence = briefStatus switch
                {
                    QueuedMessageStatus.Pending => "the brief is still queued Pending, so every delivery attempt failed",
                    QueuedMessageStatus.Sent => "the brief is marked Sent, but the session never wrote a transcript",
                    null => "no brief was ever queued for the session",
                    _ => $"brief status: {briefStatus}",
                };
                reason =
                    $"Boot prompt was never delivered: {(int)timeout.TotalMinutes} minutes after dispatch "
                    + $"the session has zero transcript entries ({evidence}). "
                    + "See the agent's incidents for the delivery errors.";
            }
            else if (await _db.AgentIncidents.AnyAsync(
                i => i.SessionId == sessionId
                    && i.Kind == AgentIncidentKind.DelegateReportUncorrelated, ct))
            {
                // The opposite failure to the one above, and the one that actually stranded three
                // tasks overnight (2026-08-11): the session ran, worked and REPORTED, but no turn
                // could be matched to the task, so nothing ever settled it. Starting is not the
                // test of a healthy task — settling is. Without this branch the check above waves
                // it through forever on the strength of a transcript it cannot use.
                reason =
                    $"Delegate reported but the result could not be attributed: {(int)timeout.TotalMinutes} "
                    + "minutes after dispatch the session has ended a turn with a report whose prompt "
                    + "carries no task marker (most likely the brief was mangled in delivery). The work "
                    + $"may be real — read session {sessionId} before re-running this task.";
            }
            else
            {
                continue;
            }

            await FailAsync(task, reason, ct);

            try
            {
                await _sessions.KillAsync(sessionId, CancellationToken.None);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(
                    ex, "Could not stop never-started session {SessionId} for task {ShortId}",
                    sessionId, DelegationReportFormatter.Short(task.Id));
            }

            await _tasks.RemoveEphemeralAgentAsync(task, task.AgentId, ct);
            await _db.SaveChangesAsync(ct);

            // The caller must HEAR about the death, not discover it: same note path as a normal
            // completion, and the board sees the status flip.
            if (task.ReplyTo == AgentTaskReplyTo.Session && task.ParentSessionId is Guid parentSession)
            {
                var note = DelegationReportFormatter.BuildCompletionNote(task, _settings, reason);
                try
                {
                    await _queue.EnqueueAsync(
                        parentSession, note.Body, MessageSendMode.WhenIdle, ct,
                        QueuedMessageOrigin.Delegation, $"task:{task.RootTaskId:N}");
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning(
                        ex, "Could not deliver never-started failure of task {ShortId} to parent session {SessionId}",
                        DelegationReportFormatter.Short(task.Id), parentSession);
                }
            }

            await _eventBus.PublishToAllAsync(
                "AgentTaskChanged", new { taskId = task.Id, rootId = task.RootTaskId }, ct);
            _logger.LogWarning(
                "Task {ShortId} failed by the delivery watchdog (session {SessionId}): {Reason}",
                DelegationReportFormatter.Short(task.Id), sessionId, reason);
            failed++;
        }

        return failed;
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

        // Reuse before spawn: a warm delegate already sitting in this directory takes the task
        // without a cold start. Shared tasks only — a worktree task's directory doesn't exist yet.
        if (claimed.Workspace == WorkspaceMode.Shared)
        {
            switch (await TryReuseWarmAgentAsync(claimed, now, ct))
            {
                case ReuseOutcome.Reused:
                    await _db.SaveChangesAsync(ct);
                    await transaction.CommitAsync(ct);
                    await DeliverReuseMessagesAsync(claimed, ct);
                    await _eventBus.PublishToAllAsync(
                        "AgentTaskChanged", new { taskId = claimed.Id, rootId = claimed.RootTaskId }, ct);
                    return true;

                case ReuseOutcome.WaitForAgent:
                    // The pinned agent is mid-task. Delivering the follow-up now would land it
                    // BETWEEN the running task's turns and corrupt both correlations — wait for
                    // the settle → pool handshake instead; the task stays queued.
                    await transaction.RollbackAsync(ct);
                    return false;
            }
        }

        // Isolation is real, not declarative: a Worktree task gets its own `git worktree add`
        // BEFORE the session exists, and the delegate runs inside it. Branching from the merge
        // target keeps the eventual rebase-back linear.
        if (claimed.Workspace == WorkspaceMode.Worktree && claimed.WorktreePath is null)
        {
            await _worktrees.CreateForTaskAsync(claimed, ct);

            // The hard version of the orchestrator contract: a PreToolUse hook that refuses
            // Edit/Write with "delegate this instead". Only ever written into the task's OWN
            // worktree — a settings file in a shared directory changes every session there.
            var armed = ShouldArmDenyHook(claimed, _settings)
                && await _worktrees.ArmDenyHookAsync(claimed, ct);

            _db.AgentTaskEvents.Add(new AgentTaskEvent
            {
                Id = Guid.NewGuid(),
                AgentTaskId = claimed.Id,
                Type = AgentTaskEventType.Dispatched,
                Detail = $"Worktree created at {claimed.WorktreePath} on {claimed.WorktreeBranch}"
                    + (claimed.MergeTargetRef is { } t ? $" (merges into {t})" : " (no merge target — branch left for review)")
                    + (armed ? "; PreToolUse deny hook armed — direct edits are refused" : string.Empty),
                At = now,
            });
        }

        var agent = await ResolveAgentAsync(claimed, now, ct);
        var cwd = claimed.WorktreePath ?? claimed.WorkingDirectory;
        var session = new AgentSession
        {
            Id = Guid.NewGuid(),
            CardId = null,
            WorktreeId = null,
            DefinitionName = _agentRegistry.Settings.DefaultDefinition,
            AgentKind = AgentKind.ClaudeCode,
            Status = SessionStatus.Starting,
            Cwd = cwd,
            Cols = _settings.DefaultCols,
            Rows = _settings.DefaultRows,
            CreatedAt = now,
            StartedAt = now,
            LastSeenAt = now,
        };
        _db.AgentSessions.Add(session);

        claimed.AgentId = agent.Id;
        // Snapshotted, not joined: the ephemeral agent row is deleted when the task settles, and
        // the board must keep naming who ran the work.
        claimed.AgentName = agent.Name;
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
        var brief = FitBriefForTyping(claimed);
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
    /// The brief as it will actually be TYPED. A brief past
    /// <see cref="DelegationSettings.BriefInlineMaxChars"/> is written to a file and replaced by a
    /// pointer, because handing a body of any size to a pty is how the 2026-08-10 and 2026-08-11
    /// live misses happened: a 5 203-character brief arrived spliced mid-word, and four briefs of
    /// 1 366-2 320 characters arrived as their last chunk alone, losing the head that carried the
    /// task — so the delegate could not tell what it had been asked to do.
    ///
    /// Note this gate is deliberately NOT <see cref="DelegationSettings.ReplyInlineMaxChars"/>: it
    /// was, and every one of those four briefs sat under it. A brief is not a deliverable, so the
    /// ceiling that governs reports is the wrong one to reuse here.
    ///
    /// The full text is on the task row either way, so if the file cannot be written the pointer
    /// names the API instead; what we never do is type a body big enough to be silently mangled.
    /// </summary>
    private string FitBriefForTyping(AgentTask task)
    {
        var brief = DelegationReportFormatter.BuildBrief(task, _settings);
        if (brief.Length <= _settings.BriefInlineMaxChars)
            return brief;

        string? spillPath = null;
        try
        {
            var absolute = Path.Combine(
                task.WorkingDirectory,
                ".antiphon",
                $"task-{DelegationReportFormatter.Short(task.Id)}-brief.md");
            Directory.CreateDirectory(Path.GetDirectoryName(absolute)!);
            File.WriteAllText(absolute, brief);
            spillPath = absolute;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Not fatal: the API pointer needs no filesystem at all.
            _logger.LogWarning(
                ex, "Task {ShortId}: could not write the brief spill file; pointing at the API instead",
                DelegationReportFormatter.Short(task.Id));
        }

        _logger.LogInformation(
            "Task {ShortId}: brief is {Chars:N0} chars (> {Ceiling:N0}); delivering a pointer to {Where}",
            DelegationReportFormatter.Short(task.Id), brief.Length, _settings.BriefInlineMaxChars,
            spillPath ?? "the API");

        return DelegationReportFormatter.BuildBriefPointer(task, _settings, spillPath, brief.Length);
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
                // A Worktree task lives in its worktree — launching in the shared directory would
                // silently defeat the isolation the caller opted into.
                Cwd: task.WorktreePath ?? task.WorkingDirectory,
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

        // A fresh delegate when no warm one fits: clean context, and --model is a launch arg so a
        // new process is the only way to START a tier. It is born pool-eligible — when its task
        // settles it goes warm instead of dying, until the janitor retires it.
        var shortId = DelegationReportFormatter.Short(task.Id);
        var name = $"task-{shortId}";
        var agent = new Agent
        {
            Id = Guid.NewGuid(),
            Name = name,
            Slug = name,
            WorkingDirectory = task.WorktreePath ?? task.WorkingDirectory,
            Details = $"Pool delegate for {task.Kind}/{task.Role} task {shortId}.",
            Status = AgentStatus.Idle,
            ModelLevel = task.ModelLevel,
            AlwaysOn = false,
            RemoteControlEnabled = false,
            IsPoolDelegate = true,
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

    internal enum ReuseOutcome
    {
        /// <summary>No warm agent fits — spawn a fresh delegate (the pre-pool path).</summary>
        SpawnFresh = 0,

        /// <summary>A warm delegate took the task; its session gets the brief, no launch.</summary>
        Reused = 1,

        /// <summary>The pinned agent is mid-task; leave the task queued until it goes warm.</summary>
        WaitForAgent = 2,
    }

    /// <summary>
    /// Try to place the task on an agent that already exists. Pinned first (a follow-up, or a
    /// retried pin): a warm pool delegate is taken over, a busy one is waited for. Unpinned Shared
    /// tasks then shop the pool: same directory, same tier, live session — honouring each warm
    /// agent's reservation window, during which it answers only to the run that just used it.
    /// </summary>
    internal async Task<ReuseOutcome> TryReuseWarmAgentAsync(AgentTask claimed, DateTime now, CancellationToken ct)
    {
        Agent? agent = null;

        if (claimed.AgentId is Guid pinnedId)
        {
            var pinned = await _db.Agents.FirstOrDefaultAsync(a => a.Id == pinnedId, ct);
            // Retired between create and dispatch, or a user's standing agent — the spawn path
            // owns both cases (it falls back to a fresh delegate / a fresh session respectively).
            if (pinned is null || !pinned.IsPoolDelegate)
                return ReuseOutcome.SpawnFresh;

            if (await LiveSessionIdOfAsync(pinned, ct) is null)
                return ReuseOutcome.SpawnFresh;

            if (pinned.Status != AgentStatus.Idle || pinned.PoolIdleSince is null)
                return ReuseOutcome.WaitForAgent;

            agent = pinned;
        }
        else if (_settings.PoolEnabled)
        {
            var reservationCutoff = now.AddMinutes(-Math.Max(0, _settings.PoolReservedForCallerMinutes));
            var warm = await _db.Agents
                .Where(a => a.IsPoolDelegate
                    && a.Status == AgentStatus.Idle
                    && a.PoolIdleSince != null
                    && a.ModelLevel == claimed.ModelLevel)
                .ToListAsync(ct);

            agent = warm
                .Where(a => SameDirectory(a.WorkingDirectory, claimed.WorkingDirectory))
                .Where(a => a.PoolReservedForRootTaskId == claimed.RootTaskId
                    || a.PoolIdleSince <= reservationCutoff)
                // Same-run context first — that agent has just read the code this run cares
                // about; then the freshest context.
                .OrderByDescending(a => a.PoolReservedForRootTaskId == claimed.RootTaskId)
                .ThenByDescending(a => a.PoolIdleSince)
                .FirstOrDefault();
        }

        if (agent is null)
            return ReuseOutcome.SpawnFresh;

        var sessionId = await LiveSessionIdOfAsync(agent, ct);
        if (sessionId is not Guid session)
            return ReuseOutcome.SpawnFresh;

        agent.Status = AgentStatus.Running;
        agent.PoolIdleSince = null;
        agent.PoolReservedForRootTaskId = null;
        agent.UpdatedAt = now;

        claimed.AgentId = agent.Id;
        claimed.AgentName = agent.Name;
        claimed.AgentSessionId = session;
        claimed.Status = AgentTaskStatus.Dispatched;
        claimed.DispatchedAt = now;
        claimed.ConcurrencyToken = Guid.NewGuid();

        // The session's environment still holds the PREVIOUS task's raw token — env can't change
        // on a live process. So the previous task's hash moves to THIS task: the delegate keeps
        // presenting the same bearer, and the server now resolves it to the work it is actually
        // doing. The task's own unused token is discarded.
        var previous = await _db.AgentTasks
            .Where(t => t.AgentSessionId == session && t.Id != claimed.Id && t.TokenHash != null)
            .OrderByDescending(t => t.DispatchedAt)
            .FirstOrDefaultAsync(ct);
        if (previous is not null)
        {
            claimed.TokenHash = previous.TokenHash;
            previous.TokenHash = null;
        }
        AgentTaskService.RawTokens.TryRemove(claimed.Id, out _);

        _db.AgentTaskEvents.Add(new AgentTaskEvent
        {
            Id = Guid.NewGuid(),
            AgentTaskId = claimed.Id,
            Type = AgentTaskEventType.Dispatched,
            ModelLevel = claimed.ModelLevel,
            Detail = $"Reused warm delegate '{agent.Name}' ({ModelLevelAliases.ForClaude(claimed.ModelLevel)}) "
                + $"in {claimed.WorkingDirectory} — no cold start"
                + (previous is not null && previous.RootTaskId != claimed.RootTaskId
                    ? "; unrelated to its last task, focused /compact first"
                    : string.Empty),
            At = now,
        });

        _logger.LogInformation(
            "Task {ShortId} reusing warm delegate '{Agent}' (session {SessionId})",
            DelegationReportFormatter.Short(claimed.Id), agent.Name, session);
        return ReuseOutcome.Reused;
    }

    /// <summary>
    /// The brief for a reused session — preceded, when the new work is UNRELATED to what the
    /// session last did, by a focused /compact: shrink the old context down to whatever could help
    /// the new task before the task arrives. Same-run follow-ups skip it — their old context is
    /// exactly the value being reused.
    /// </summary>
    private async Task DeliverReuseMessagesAsync(AgentTask task, CancellationToken ct)
    {
        if (task.AgentSessionId is not Guid session)
            return;

        try
        {
            var previousRoot = await _db.AgentTasks.AsNoTracking()
                .Where(t => t.AgentSessionId == session
                    && t.Id != task.Id
                    && t.DispatchedAt != null
                    && t.DispatchedAt < task.DispatchedAt)
                .OrderByDescending(t => t.DispatchedAt)
                .Select(t => (Guid?)t.RootTaskId)
                .FirstOrDefaultAsync(ct);

            if (previousRoot is not null && previousRoot != task.RootTaskId)
            {
                // One line: a slash command is parsed from the submitted composer text, and the
                // focus argument tells the summariser what the surviving context must serve.
                var focus = task.Goal.ReplaceLineEndings(" ").Trim();
                if (focus.Length > 300) focus = focus[..300];
                await _queue.EnqueueAsync(
                    session,
                    $"/compact This session is being handed NEW, unrelated work. Keep only context useful for: {focus}",
                    MessageSendMode.WhenIdle, ct, QueuedMessageOrigin.Delegation);
            }

            var brief = FitBriefForTyping(task);
            await _queue.EnqueueAsync(
                session, brief, MessageSendMode.WhenIdle, ct, QueuedMessageOrigin.Delegation);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Same contract as the spawn path: rows are persisted before the runner is probed, so
            // a transient transport failure delays delivery rather than losing the brief.
            _logger.LogWarning(
                ex, "Task {ShortId}: reuse messages are queued but could not be delivered yet",
                DelegationReportFormatter.Short(task.Id));
        }
    }

    /// <summary>
    /// Retire warm delegates that outstayed their welcome: idle past the TTL, or beyond the
    /// per-directory cap (oldest first). This bound is what makes "keep Claudes warm" a trade
    /// instead of a leak.
    /// </summary>
    internal async Task<int> RetireIdleWarmAgentsAsync(CancellationToken ct)
    {
        var warm = await _db.Agents
            .Where(a => a.IsPoolDelegate && a.Status == AgentStatus.Idle && a.PoolIdleSince != null)
            .ToListAsync(ct);
        if (warm.Count == 0)
            return 0;

        var now = UtcNow();
        var cutoff = now.AddMinutes(-Math.Max(1, _settings.PoolIdleRetireMinutes));
        var retire = new HashSet<Agent>(warm.Where(a => !_settings.PoolEnabled || a.PoolIdleSince <= cutoff));

        foreach (var surplus in warm
            .GroupBy(a => DelegationWorkspaceResolver.NormalizeSeparators(a.WorkingDirectory).ToUpperInvariant())
            .SelectMany(g => g.OrderByDescending(a => a.PoolIdleSince)
                .Skip(Math.Max(0, _settings.PoolMaxIdlePerDirectory))))
        {
            retire.Add(surplus);
        }

        foreach (var agent in retire)
        {
            if (Guid.TryParse(agent.PersistentSessionId, out var sessionId))
            {
                try
                {
                    await _sessions.KillAsync(sessionId, ct);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning(ex, "Could not stop pooled session {SessionId}", sessionId);
                }
            }
            _db.Agents.Remove(agent);
            _logger.LogInformation(
                "Retired warm delegate '{Name}' from {Dir} (idle since {Since:O})",
                agent.Name, agent.WorkingDirectory, agent.PoolIdleSince);
        }

        if (retire.Count > 0)
            await _db.SaveChangesAsync(ct);
        return retire.Count;
    }

    private async Task<Guid?> LiveSessionIdOfAsync(Agent agent, CancellationToken ct)
    {
        if (!Guid.TryParse(agent.PersistentSessionId, out var sessionId))
            return null;

        var alive = await _db.AgentSessions.AsNoTracking().AnyAsync(
            s => s.Id == sessionId
                && (s.Status == SessionStatus.Starting || s.Status == SessionStatus.Running), ct);
        return alive ? sessionId : null;
    }

    private static bool SameDirectory(string a, string b)
    {
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        return string.Equals(
            DelegationWorkspaceResolver.NormalizeSeparators(a),
            DelegationWorkspaceResolver.NormalizeSeparators(b),
            comparison);
    }

    /// <summary>
    /// Whether an orchestrator's worktree gets the PreToolUse deny hook. The per-task choice wins;
    /// otherwise config. Workers never get it — their whole job is to edit.
    /// </summary>
    internal static bool ShouldArmDenyHook(AgentTask task, DelegationSettings settings) =>
        task.Kind == AgentTaskKind.Orchestrator
        && task.Workspace == WorkspaceMode.Worktree
        && (task.DenyDirectEdits ?? settings.OrchestratorDenyHookEnabled);

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
