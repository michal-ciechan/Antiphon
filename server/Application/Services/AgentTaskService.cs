using System.Security.Cryptography;
using System.Text;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// Creates and queries delegated tasks. Everything that decides WHAT a delegate will be — its tier,
/// its directory, whether it may itself delegate — happens here, at creation, so the dispatcher only
/// has to execute an already-authorised decision.
/// </summary>
public sealed class AgentTaskService
{
    private readonly AppDbContext _db;
    private readonly DelegationWorkspaceResolver _workspace;
    private readonly DelegationSettings _settings;
    private readonly IEventBus _eventBus;
    private readonly IDelegateSessionStopper _sessions;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<AgentTaskService> _logger;

    public AgentTaskService(
        AppDbContext db,
        DelegationWorkspaceResolver workspace,
        IOptions<DelegationSettings> settings,
        IEventBus eventBus,
        IDelegateSessionStopper sessions,
        TimeProvider timeProvider,
        ILogger<AgentTaskService> logger)
    {
        _db = db;
        _workspace = workspace;
        _settings = settings.Value;
        _eventBus = eventBus;
        _sessions = sessions;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <summary>
    /// Who is calling. Resolved from the bearer token by <see cref="AuthenticateAsync"/> — a manual
    /// (UI) caller has no task and no parent session.
    /// </summary>
    public sealed record Caller(AgentTask? Task, Guid? SessionId, string WorkingDirectory)
    {
        /// <summary>Only an orchestrator (or the UI) may create tasks. A worker gets 403.</summary>
        public bool MayDelegate => Task is null || Task.Kind == AgentTaskKind.Orchestrator;
    }

    /// <summary>
    /// Resolve a delegate's bearer token to its task. The token is hashed at rest, so a leaked
    /// database row can't be replayed as a credential.
    /// </summary>
    public async Task<Caller> AuthenticateAsync(string? token, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(token))
            throw new ForbiddenException("A delegation token is required (ANTIPHON_TASK_TOKEN).");

        var hash = HashToken(token);
        var task = await _db.AgentTasks.FirstOrDefaultAsync(t => t.TokenHash == hash, ct);
        if (task is not null)
        {
            // A worktree caller's directory IS its worktree — children it spawns without -Dir must
            // land where it actually works, or their edits bypass its branch entirely.
            return new Caller(task, task.AgentSessionId, task.WorktreePath ?? task.WorkingDirectory);
        }

        // Session-scoped token: a standing agent session (an always-on orchestrator) delegating on
        // its own behalf. No parent task — Caller.MayDelegate is true via Task is null — and the
        // session id makes ReplyTo=Session routing work, so reports return to the calling session
        // instead of landing silently on the board.
        var session = await _db.AgentSessions.AsNoTracking()
            .FirstOrDefaultAsync(s => s.DelegationTokenHash == hash, ct)
            ?? throw new ForbiddenException("Delegation token is not recognised.");
        return new Caller(null, session.Id, session.Cwd);
    }

    /// <summary>
    /// Create a task. <paramref name="caller"/> is the authenticated creator: an orchestrator
    /// delegating downward, or the UI acting on a human's behalf.
    /// </summary>
    public async Task<AgentTaskCreatedDto> CreateAsync(
        CreateAgentTaskRequest request,
        Caller caller,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Goal))
            throw new ValidationException(nameof(request.Goal), "A goal is required.");

        // Rejected rather than reinterpreted. 0 does NOT mean "never check" and a negative is not a
        // silent default: opting a single task out of checking is not offered until someone needs
        // it, and a caller who typed a nonsense number should hear about it (CARD-0047 §1.5).
        var expectedMinutes = request.ExpectedMinutes ?? Math.Clamp(_settings.DefaultExpectedMinutes, 1, 1440);
        if (expectedMinutes is < 1 or > 1440)
        {
            throw new ValidationException(
                nameof(request.ExpectedMinutes),
                $"Expected duration must be between 1 and 1440 minutes (got {expectedMinutes}). "
                + "It is a hint that schedules the first check-in, not a deadline.");
        }

        // Follow-up: run on the SAME agent that ran an earlier task, keeping its context. The
        // task inherits that agent's directory (that is where the context lives) and its TIER —
        // the model is already running; a role policy cannot change it mid-session.
        if (!string.IsNullOrWhiteSpace(request.FollowUpOnTask))
        {
            var priorId = await ResolveTaskIdAsync(request.FollowUpOnTask, ct);
            var prior = await _db.AgentTasks.AsNoTracking().FirstAsync(t => t.Id == priorId, ct);
            if (prior.AgentId is not Guid followAgentId)
                throw new ConflictException(
                    $"Task {DelegationReportFormatter.Short(priorId)} never ran on an agent — there is nothing to follow up on.");

            var followAgent = await _db.Agents.AsNoTracking().FirstOrDefaultAsync(a => a.Id == followAgentId, ct)
                ?? throw new ConflictException(
                    $"The agent that ran task {DelegationReportFormatter.Short(priorId)} has been retired "
                    + "from the pool — delegate normally instead; the report is still on the task.");

            // The agent is already running, as whatever program it was launched as. A follow-up
            // keeps that context, so the kind is not a choice any more: unset inherits the prior
            // task's, and an explicit mismatch is refused rather than silently reinterpreted.
            if (request.AgentKind is { } wantedKind && wantedKind != prior.AgentKind)
            {
                throw new ConflictException(
                    $"Task {DelegationReportFormatter.Short(priorId)} ran on {prior.AgentKind}, so a "
                    + $"follow-up on its agent cannot run on {wantedKind} — that agent's context lives "
                    + "in the session that is already running. Delegate normally to change kind.");
            }

            request = request with
            {
                WorkingDirectory = request.WorkingDirectory ?? followAgent.WorkingDirectory,
                Workspace = WorkspaceMode.Shared,
                ModelLevel = followAgent.ModelLevel,
                AgentKind = prior.AgentKind,
                AgentId = followAgent.Id,
            };
        }

        // THE recursion boundary. A worker's token carries no create scope, so a worker cannot start
        // a fan-out even if it decides it wants to — this is what keeps nesting bounded, not MaxDepth.
        if (!caller.MayDelegate)
        {
            await RecordRejectionAsync(caller.Task!, "A worker attempted to delegate.", ct);
            throw new ForbiddenException(
                "Workers cannot delegate. Do the work and report back, or ask the caller to send a "
                + "sub-orchestrator (-Orchestrator) for a chunk that needs decomposing.");
        }

        var parent = caller.Task;
        var depth = parent is null ? 0 : parent.Depth + 1;
        if (depth > _settings.MaxDepth)
            throw new ConflictException($"Delegation depth limit reached ({_settings.MaxDepth}).");

        var rootId = parent?.RootTaskId;
        if (rootId is { } root)
        {
            var siblings = await _db.AgentTasks.CountAsync(t => t.RootTaskId == root, ct);
            if (siblings >= _settings.MaxTasksPerRoot)
                throw new ConflictException($"This run has reached its task limit ({_settings.MaxTasksPerRoot}).");

            var spent = await _db.AgentTasks.Where(t => t.RootTaskId == root).SumAsync(t => (decimal?)t.CostUsd, ct) ?? 0m;
            if (spent >= _settings.MaxCostUsdPerRoot)
            {
                throw new ConflictException(
                    $"This run has reached its cost ceiling (${spent:0.00} of ${_settings.MaxCostUsdPerRoot:0.00}).");
            }
        }

        DelegationWorkspaceResolver.Resolution resolved;
        try
        {
            resolved = await _workspace.ResolveAsync(
                request.WorkingDirectory, caller.WorkingDirectory, _settings.AllowedRoots, ct);
        }
        catch (DelegationWorkspaceResolver.RejectedException ex)
        {
            if (parent is not null)
                await RecordRejectionAsync(parent, ex.Message, ct);
            throw new ValidationException(nameof(request.WorkingDirectory), ex.Message);
        }

        if (request.Workspace == WorkspaceMode.Worktree && resolved.RepoPath is null)
        {
            throw new ValidationException(
                nameof(request.Workspace),
                $"'{resolved.WorkingDirectory}' is not a git repository, so there is nothing to branch. "
                + "Use the default shared workspace instead.");
        }

        var (workspace, warning) = ResolveWorkspace(request, caller, resolved);

        var id = Guid.NewGuid();
        var level = ResolveLevel(request.Kind, request.Role, request.ModelLevel);
        var agentKind = ResolveAgentKind(request.Kind, request.Role, request.AgentKind);
        var now = UtcNow();
        var (token, tokenHash) = NewToken();

        var task = new AgentTask
        {
            Id = id,
            RootTaskId = parent?.RootTaskId ?? id,
            ParentTaskId = parent?.Id,
            // Where the report goes. The dispatcher fills the parent's session id at dispatch time
            // for a task created before its parent's session existed.
            ParentSessionId = caller.SessionId,
            Depth = depth,
            Title = BuildTitle(request),
            Goal = request.Goal.Trim(),
            Kind = request.Kind,
            Role = request.Role,
            AgentKind = agentKind,
            ModelLevel = level,
            Workspace = workspace,
            DenyDirectEdits = request.DenyDirectEdits,
            WorkingDirectory = resolved.WorkingDirectory,
            RepoPath = resolved.RepoPath,
            ScopeGlob = string.IsNullOrWhiteSpace(request.ScopeGlob) ? null : request.ScopeGlob.Trim(),
            // A worktree task merges into its parent's BRANCH — but only when they share a repo.
            // A worktree parent's children target its task branch (integration once per level);
            // a shared-workspace parent passes its own target down. Cross-repo "merge" is a
            // release-coordination problem and deliberately out of scope.
            MergeTargetRef = request.MergeTargetRef
                ?? (SharesRepoWith(parent, resolved.RepoPath)
                    ? parent?.WorktreeBranch ?? parent?.MergeTargetRef
                    : null),
            AgentId = request.AgentId,
            Ephemeral = request.AgentId is null,
            Status = AgentTaskStatus.Queued,
            ReplyTo = caller.SessionId is null ? AgentTaskReplyTo.None : AgentTaskReplyTo.Session,
            MaxAttempts = 2,
            CreatedAt = now,
            TokenHash = tokenHash,
            // Stored RESOLVED — the row always carries a number, so nothing downstream has to know
            // whether the caller declared one. NextCheckAt stays null until dispatch.
            ExpectedDurationMinutes = expectedMinutes,
        };

        _db.AgentTasks.Add(task);
        _db.AgentTaskEvents.Add(new AgentTaskEvent
        {
            Id = Guid.NewGuid(),
            AgentTaskId = id,
            Type = AgentTaskEventType.Created,
            ModelLevel = level,
            Detail = (request.ModelLevel is { } explicitLevel
                    ? $"{request.Kind}/{request.Role} at {explicitLevel} (explicit override) in {resolved.WorkingDirectory}"
                    : $"{request.Kind}/{request.Role} at {level} (role policy) in {resolved.WorkingDirectory}")
                // Only when it is NOT the default: an event line that says "on ClaudeCode" on every
                // task teaches nobody anything, and the one that says "on Grok" is the whole point.
                + (agentKind == AgentKind.ClaudeCode
                    ? string.Empty
                    : $" on {agentKind}{(request.AgentKind is null ? " (role policy)" : " (explicit)")}"),
            At = now,
        });
        if (warning is not null)
            AddEvent(id, AgentTaskEventType.Warning, null, warning, now);
        await _db.SaveChangesAsync(ct);

        await _eventBus.PublishToAllAsync("AgentTaskChanged", new { taskId = id, rootId = task.RootTaskId }, ct);
        _logger.LogInformation(
            "Delegated task {ShortId} ({Kind}/{Role}, {Level}, {AgentKind}) created in {Dir} at depth {Depth}",
            DelegationReportFormatter.Short(id), task.Kind, task.Role, level, agentKind,
            task.WorkingDirectory, depth);

        // The raw token is returned ONCE, to be injected into the delegate's environment. It is
        // never persisted and never readable again.
        RawTokens[id] = token;
        return new AgentTaskCreatedDto(
            id, DelegationReportFormatter.Short(id), task.Status, level, warning, agentKind);
    }

    /// <summary>
    /// The workspace an unspecified request gets, and the warning a risky explicit one earns.
    ///
    /// The principle: an orchestrator should always have something of its own — its own worktree,
    /// or its own location. It fans out writers; running it directly in its caller's directory
    /// means its delegates and its caller silently overwrite each other. So unspecified
    /// orchestrators isolate by default, and an explicit choice that shares anyway is honoured
    /// but WARNED — at creation, to the caller, not just in a timeline nobody reads in time.
    /// </summary>
    internal (WorkspaceMode Workspace, string? Warning) ResolveWorkspace(
        CreateAgentTaskRequest request,
        Caller caller,
        DelegationWorkspaceResolver.Resolution resolved)
    {
        var sharesCallersDirectory = PathsEqual(resolved.WorkingDirectory, caller.WorkingDirectory);

        if (request.Workspace is { } explicitMode)
        {
            var warned = request.Kind == AgentTaskKind.Orchestrator
                && explicitMode == WorkspaceMode.Shared
                && sharesCallersDirectory
                    ? "This orchestrator runs directly in its caller's directory. Its delegates and "
                      + "its caller can overwrite each other's files; prefer the default worktree, "
                      + "or give it its own -Dir."
                    : null;
            return (explicitMode, warned);
        }

        if (request.Kind != AgentTaskKind.Orchestrator)
            return (WorkspaceMode.Shared, null);

        // Its own location IS isolation — a second worktree on top would be pure overhead.
        if (!sharesCallersDirectory)
            return (WorkspaceMode.Shared, null);

        if (resolved.RepoPath is not null)
            return (WorkspaceMode.Worktree, null);

        return (WorkspaceMode.Shared,
            $"'{resolved.WorkingDirectory}' is not a git repository, so this orchestrator cannot "
            + "be isolated in a worktree and will share its caller's directory. Its delegates and "
            + "its caller can overwrite each other's files.");
    }

    private static bool PathsEqual(string a, string b)
    {
        if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b))
            return false;
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        return string.Equals(
            DelegationWorkspaceResolver.NormalizeSeparators(Path.GetFullPath(a)),
            DelegationWorkspaceResolver.NormalizeSeparators(Path.GetFullPath(b)),
            comparison);
    }

    /// <summary>
    /// Raw tokens held only until the dispatcher injects them into the delegate's environment.
    /// Static because the creating scope and the dispatching scope are different DI scopes.
    /// </summary>
    internal static readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, string> RawTokens = new();

    /// <param name="includeChecks">
    /// Show <see cref="AgentTaskRole.Check"/> rows — the interpretation tasks the check worker
    /// creates, one per interpreted check-in (CARD-0047 §1.1.1). Off by default, and the default is
    /// SERVER-side on purpose: none of them is anybody's delegated work, and a busy fleet would
    /// otherwise bury the board under them. Correlation survives the hiding both ways — the
    /// interpretation's title names the checked task, and the checked task's <c>Check</c> event
    /// names the interpretation's short id and cost.
    /// </param>
    public async Task<IReadOnlyList<AgentTaskSummaryDto>> ListAsync(
        Guid? rootId, AgentTaskStatus? status, bool includeChecks, CancellationToken ct)
    {
        var query = _db.AgentTasks.AsNoTracking();
        if (rootId is { } root) query = query.Where(t => t.RootTaskId == root);
        if (status is { } s) query = query.Where(t => t.Status == s);
        if (!includeChecks) query = query.Where(t => t.Role != AgentTaskRole.Check);

        var tasks = await query.OrderBy(t => t.CreatedAt).ToListAsync(ct);
        return tasks.Select(t => ToSummary(t, tasks)).ToList();
    }

    public async Task<AgentTaskDetailDto> GetAsync(Guid id, CancellationToken ct)
    {
        var task = await _db.AgentTasks.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id, ct)
            ?? throw new NotFoundException(nameof(AgentTask), id);

        // Subtree cost needs the whole run, not just this row.
        var family = await _db.AgentTasks.AsNoTracking()
            .Where(t => t.RootTaskId == task.RootTaskId)
            .ToListAsync(ct);

        var events = await _db.AgentTaskEvents.AsNoTracking()
            .Where(e => e.AgentTaskId == id)
            .OrderBy(e => e.At)
            .Select(e => new AgentTaskEventDto(e.Type, e.ModelLevel, e.Detail, e.At))
            .ToListAsync(ct);

        return new AgentTaskDetailDto(
            ToSummary(task, family), task.Goal, task.Result,
            task.ResultFilePath, task.FailureReason, task.MergeTargetRef, events);
    }

    public async Task<AgentTaskSummaryDto> CancelAsync(Guid id, CancellationToken ct)
    {
        var task = await _db.AgentTasks.FirstOrDefaultAsync(t => t.Id == id, ct)
            ?? throw new NotFoundException(nameof(AgentTask), id);

        if (IsSettled(task.Status))
            throw new ConflictException($"Task {DelegationReportFormatter.Short(id)} has already finished.");

        // Stop the delegate BEFORE relabelling the row. A cancel that only changes a status leaves
        // a Claude running against the run's cost ceiling while the board says the work stopped.
        await StopDelegateAsync(task, ct);
        await RemoveEphemeralAgentAsync(task, task.AgentId, ct);

        var now = UtcNow();
        task.Status = AgentTaskStatus.Canceled;
        task.CompletedAt = now;
        task.ConcurrencyToken = Guid.NewGuid();
        AddEvent(task.Id, AgentTaskEventType.Canceled, null, "Canceled.", now);
        await _db.SaveChangesAsync(ct);
        await _eventBus.PublishToAllAsync("AgentTaskChanged", new { taskId = id, rootId = task.RootTaskId }, ct);

        return await SummaryOfAsync(task, ct);
    }

    /// <summary>
    /// Run a task again, at the same tier. For a task that stalled, failed, or came back with an
    /// answer the caller rejected — the goal is unchanged, so what changes is the attempt.
    /// </summary>
    public async Task<AgentTaskSummaryDto> RetryAsync(Guid id, CancellationToken ct)
    {
        var task = await _db.AgentTasks.FirstOrDefaultAsync(t => t.Id == id, ct)
            ?? throw new NotFoundException(nameof(AgentTask), id);

        if (task.Status == AgentTaskStatus.Queued)
        {
            throw new ConflictException(
                $"Task {DelegationReportFormatter.Short(id)} has not run yet — it is already queued.");
        }

        await RequeueAsync(
            task, AgentTaskEventType.Retried, task.ModelLevel,
            $"Retried at {ModelLevelAliases.ForClaude(task.ModelLevel)}.", ct);
        return await SummaryOfAsync(task, ct);
    }

    /// <summary>
    /// Move a task up the ladder and run it again. The tier bump is applied IN PLACE (one chip per
    /// task, <see cref="AgentTask.EscalatedFrom"/> set, the ladder readable in the events) rather
    /// than forking a second row — and the next attempt carries a handoff block built from what the
    /// last one found, because escalation that restarts cold just pays more for the same dead end.
    /// </summary>
    public async Task<AgentTaskSummaryDto> EscalateAsync(
        Guid id, AgentModelLevel? to, CancellationToken ct, string? reason = null)
    {
        var task = await _db.AgentTasks.FirstOrDefaultAsync(t => t.Id == id, ct)
            ?? throw new NotFoundException(nameof(AgentTask), id);

        var from = task.ModelLevel;
        var target = ResolveEscalationTarget(task, to);
        if (target is null)
        {
            throw new ConflictException(
                $"Task {DelegationReportFormatter.Short(id)} is already at the top of the ladder "
                + $"({EscalationAlias(task, from)}).");
        }

        task.EscalatedFrom = from;
        task.ModelLevel = target.Value;
        var detail = $"Escalated {EscalationAlias(task, from)} -> {EscalationAlias(task, target.Value)}."
            + SameModelEscalationNote(task, from, target.Value)
            + (reason is null ? string.Empty : $" {reason}");

        // A task that has not started yet only needs the new tier — there is nothing to requeue.
        if (task.Status == AgentTaskStatus.Queued)
        {
            var now = UtcNow();
            task.ConcurrencyToken = Guid.NewGuid();
            AddEvent(task.Id, AgentTaskEventType.Escalated, target.Value, detail, now);
            await _db.SaveChangesAsync(ct);
            await _eventBus.PublishToAllAsync("AgentTaskChanged", new { taskId = id, rootId = task.RootTaskId }, ct);
            return await SummaryOfAsync(task, ct);
        }

        await RequeueAsync(task, AgentTaskEventType.Escalated, target.Value, detail, ct);
        return await SummaryOfAsync(task, ct);
    }

    /// <summary>
    /// The model-family alias to NAME in an escalation text, for the program the task actually runs
    /// on (CARD-0084 S3). Deliberately private and confined to the two escalation messages rather
    /// than a general <c>For(kind, level)</c> helper: replacing the ~12 display-only
    /// <see cref="ModelLevelAliases.ForClaude"/> call sites is S4's mechanical sweep, and doing it
    /// piecemeal here would leave the codebase with two half-migrated conventions. An escalation
    /// text is in scope now because it is the one that would otherwise state something FALSE — it
    /// promises a bigger model by name.
    /// </summary>
    private static string EscalationAlias(AgentTask task, AgentModelLevel level) =>
        task.AgentKind == AgentKind.Grok
            ? ModelLevelAliases.ForGrok(level)
            : ModelLevelAliases.ForClaude(level);

    /// <summary>
    /// What an escalation buys when both rungs map to the SAME model. Grok's ladder is shorter than
    /// the generic one — Frontier and High are both grok-4.6 — so a Debug escalation on Grok moves
    /// no model at all. That is still worth doing (a fresh context is most of what escalation buys
    /// in practice: the stalled session is killed and the next attempt starts from the handoff
    /// block rather than the dead end), but the event must SAY so. Silence here would read as a
    /// promise of a larger model that xAI does not currently offer, and the operator would spend
    /// the escalation expecting something the ladder cannot deliver.
    /// </summary>
    private static string SameModelEscalationNote(AgentTask task, AgentModelLevel from, AgentModelLevel to)
    {
        if (task.AgentKind != AgentKind.Grok)
            return string.Empty;

        var alias = ModelLevelAliases.ForGrok(to);
        if (!string.Equals(ModelLevelAliases.ForGrok(from), alias, StringComparison.Ordinal))
            return string.Empty;

        return $" On {AgentKind.Grok} the {from} and {to} tiers both map to {alias}, so this is a"
            + " FRESH CONTEXT at the same model, not a larger one.";
    }

    /// <summary>
    /// One rung up, unless the role policy names a specific target — the ladder is config, so a
    /// configured <c>EscalateTo</c> wins over counting rungs. Null means there is nowhere to go.
    /// </summary>
    private AgentModelLevel? ResolveEscalationTarget(AgentTask task, AgentModelLevel? requested)
    {
        // Frontier = 0, so "higher tier" is a LOWER enum value.
        if (requested is { } explicitTarget)
            return (int)explicitTarget < (int)task.ModelLevel ? explicitTarget : null;

        if (_settings.RolePolicy.TryGetValue(task.Role.ToString(), out var policy)
            && policy.EscalateTo is { } configured
            && (int)configured < (int)task.ModelLevel)
        {
            return configured;
        }

        return task.ModelLevel == AgentModelLevel.Frontier ? null : task.ModelLevel - 1;
    }

    /// <summary>
    /// Put a task back on the queue for another attempt. Shared by retry and escalation because the
    /// mechanics are identical — only the reason differs.
    /// </summary>
    private async Task RequeueAsync(
        AgentTask task, AgentTaskEventType type, AgentModelLevel level, string detail, CancellationToken ct)
    {
        await StopDelegateAsync(task, ct);

        var now = UtcNow();
        task.Attempt++;
        // A human asking for another go outranks the automatic attempt cap.
        if (task.Attempt > task.MaxAttempts)
            task.MaxAttempts = task.Attempt;
        task.Status = AgentTaskStatus.Queued;
        task.AgentSessionId = null;
        // --model is a LAUNCH argument, so a new tier needs a new process. An ephemeral delegate is
        // discarded — row included, or every retry leaks a dead agent; a pinned agent is the
        // caller's explicit choice and stays.
        if (task.Ephemeral)
        {
            await RemoveEphemeralAgentAsync(task, task.AgentId, ct);
            task.AgentId = null;
        }
        task.DispatchedAt = null;
        task.CompletedAt = null;
        task.ConcurrencyToken = Guid.NewGuid();
        // A new attempt gets a new check schedule: the old NextCheckAt was measured from a dispatch
        // that no longer exists, and the previous attempt's checks are not this one's budget.
        task.NextCheckAt = null;
        task.CheckCount = 0;

        // Result and FailureReason are deliberately KEPT: they are the handoff the next attempt gets
        // (DelegationReportFormatter.BuildBrief), and the drawer still shows what the last try said.
        var (token, hash) = NewToken();
        task.TokenHash = hash;
        RawTokens[task.Id] = token;

        AddEvent(task.Id, type, level, detail, now);
        await _db.SaveChangesAsync(ct);
        await _eventBus.PublishToAllAsync(
            "AgentTaskChanged", new { taskId = task.Id, rootId = task.RootTaskId }, ct);
        _logger.LogInformation(
            "Task {ShortId} requeued as attempt {Attempt} at {Alias}: {Detail}",
            DelegationReportFormatter.Short(task.Id), task.Attempt,
            ModelLevelAliases.ForClaude(task.ModelLevel), detail);
    }

    /// <summary>
    /// Delete a pool delegate's Agent row (dependents cascade). Keyed off the AGENT's
    /// IsPoolDelegate, not the task's Ephemeral flag: a follow-up task pins a pool agent (so it is
    /// not "ephemeral"), but cancelling it must still retire that agent — while a user's standing
    /// agent must never be deleted by any task action. The task's snapshotted
    /// <see cref="AgentTask.AgentName"/> keeps the board naming who ran the work.
    /// </summary>
    internal async Task RemoveEphemeralAgentAsync(AgentTask task, Guid? agentId, CancellationToken ct)
    {
        if (agentId is not Guid id)
            return;

        var agent = await _db.Agents.FirstOrDefaultAsync(a => a.Id == id, ct);
        if (agent is { IsPoolDelegate: true })
            _db.Agents.Remove(agent);
    }

    /// <summary>
    /// A task id as the caller knows it: the full guid, or the 8-char short id every report and
    /// board chip shows. The completion note says "[task 7f3a2b91 done]" — telling the caller to
    /// then produce a full guid it has never seen would make -Reply and -OnAgent unusable.
    /// </summary>
    public async Task<Guid> ResolveTaskIdAsync(string idOrShortId, CancellationToken ct)
    {
        var value = idOrShortId.Trim();
        if (Guid.TryParse(value, out var full))
        {
            if (!await _db.AgentTasks.AsNoTracking().AnyAsync(t => t.Id == full, ct))
                throw new NotFoundException(nameof(AgentTask), full);
            return full;
        }

        if (value.Length != 8 || !value.All(Uri.IsHexDigit))
            throw new ValidationException(
                nameof(idOrShortId), $"'{value}' is neither a task id nor an 8-character short id.");

        // The short id is the first 8 hex digits — which are also the first 8 chars of the guid's
        // canonical text (the first dash falls at index 8), so a text prefix match finds it.
        var prefix = value.ToLowerInvariant();
        var matches = await _db.AgentTasks.AsNoTracking()
            .Where(t => t.Id.ToString().StartsWith(prefix))
            .Select(t => t.Id)
            .Take(2)
            .ToListAsync(ct);

        return matches.Count switch
        {
            0 => throw new NotFoundException(nameof(AgentTask), prefix),
            1 => matches[0],
            _ => throw new ConflictException(
                $"Short id '{prefix}' matches more than one task — use the full id."),
        };
    }

    /// <summary>
    /// Spawn the Merge-role delegate that resolves a Worktree task's rebase conflict. SYSTEM
    /// spawned — the server decides this, so it bypasses the caller checks — but the run's task
    /// cap still applies: a run at its limit gets the Blocked task and no fixer, stated in events.
    /// It is a CHILD of the conflicted task (the tree records that this work needed a merge hand)
    /// and reports to the same parent session, working directly in the conflicted worktree.
    /// </summary>
    internal async Task<AgentTask?> CreateMergeTaskAsync(
        AgentTask conflicted, IReadOnlyList<string> conflictFiles, CancellationToken ct)
    {
        var siblings = await _db.AgentTasks.CountAsync(t => t.RootTaskId == conflicted.RootTaskId, ct);
        if (siblings >= _settings.MaxTasksPerRoot || conflicted.Depth + 1 > _settings.MaxDepth)
        {
            _logger.LogWarning(
                "Task {ShortId}: merge conflict but the run is at its task/depth cap — leaving Blocked",
                DelegationReportFormatter.Short(conflicted.Id));
            return null;
        }

        if (conflicted.WorktreePath is not { } worktree || conflicted.WorktreeBranch is not { } branch
            || conflicted.MergeTargetRef is not { } target)
        {
            return null;
        }

        var id = Guid.NewGuid();
        var now = UtcNow();
        var (token, tokenHash) = NewToken();
        var files = string.Join("\n", conflictFiles.Select(f => $"- {f}"));

        var task = new AgentTask
        {
            Id = id,
            RootTaskId = conflicted.RootTaskId,
            ParentTaskId = conflicted.Id,
            ParentSessionId = conflicted.ParentSessionId,
            Depth = conflicted.Depth + 1,
            Title = $"Resolve merge conflict: {Clamp(conflicted.Title, 250)}",
            Goal = $"""
                Task {DelegationReportFormatter.Short(conflicted.Id)} finished its work on branch
                {branch}, but rebasing onto {target} hit conflicts in:
                {files}

                You are in its worktree. Complete the merge:
                1. git rebase {target} — resolve each conflict the way the TASK intended; its work
                   is the newer change. Read the conflicted task's goal in your context if unsure.
                2. git rebase --continue until clean.
                3. Fast-forward the target: git fetch . {branch}:{target} — if git refuses because
                   {target} is checked out elsewhere, run git merge --ff-only {branch} in that
                   checkout instead.
                Report which files conflicted and how you resolved each. Do NOT redo or review the
                task's work — only integrate it.
                """,
            Kind = AgentTaskKind.Worker,
            Role = AgentTaskRole.Merge,
            ModelLevel = ResolveLevel(AgentTaskKind.Worker, AgentTaskRole.Merge, null),
            Workspace = WorkspaceMode.Shared,
            WorkingDirectory = worktree,
            RepoPath = conflicted.RepoPath,
            MergeTargetRef = target,
            Ephemeral = true,
            Status = AgentTaskStatus.Queued,
            ReplyTo = conflicted.ReplyTo,
            MaxAttempts = 2,
            CreatedAt = now,
            TokenHash = tokenHash,
            ExpectedDurationMinutes = Math.Clamp(_settings.DefaultExpectedMinutes, 1, 1440),
        };

        _db.AgentTasks.Add(task);
        _db.AgentTaskEvents.Add(new AgentTaskEvent
        {
            Id = Guid.NewGuid(),
            AgentTaskId = id,
            Type = AgentTaskEventType.Created,
            ModelLevel = task.ModelLevel,
            Detail = $"Spawned by the server to resolve {conflictFiles.Count} conflicted file(s) from "
                + $"task {DelegationReportFormatter.Short(conflicted.Id)}.",
            At = now,
        });
        RawTokens[id] = token;
        return task;
    }

    /// <summary>
    /// End the delegate's session if it has one. Best-effort: a session the runner has already lost
    /// must not stop the caller from cancelling or requeueing the task.
    /// </summary>
    private async Task StopDelegateAsync(AgentTask task, CancellationToken ct)
    {
        if (task.AgentSessionId is not Guid sessionId)
            return;

        try
        {
            await _sessions.KillAsync(sessionId, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(
                ex, "Could not stop session {SessionId} for task {ShortId}",
                sessionId, DelegationReportFormatter.Short(task.Id));
        }
    }

    /// <summary>
    /// Resolve the tier. The role policy is the mechanism; an explicit override wins but is recorded.
    /// A sub-orchestrator never runs below <see cref="DelegationSettings.MinOrchestratorLevel"/> —
    /// decomposition is the expensive kind of thinking, and a cheap one produces a bad tree.
    /// </summary>
    public AgentModelLevel ResolveLevel(AgentTaskKind kind, AgentTaskRole role, AgentModelLevel? explicitLevel)
    {
        var level = explicitLevel
            ?? (_settings.RolePolicy.TryGetValue(role.ToString(), out var policy) ? policy.Level : _settings.DefaultLevel);

        // Frontier = 0 and Low = 3, so "at least" is a numeric MINIMUM on the enum value.
        if (kind == AgentTaskKind.Orchestrator && (int)level > (int)_settings.MinOrchestratorLevel)
            level = _settings.MinOrchestratorLevel;

        return level;
    }

    /// <summary>
    /// Kinds a delegated task may run on TODAY (CARD-0084 S2). Deliberately an allowlist and not a
    /// capability query: what a delegate needs of its program — a model argument, permission bypass,
    /// structured activity to compute working/idle from, and a channel for the instruction bundle —
    /// is exactly the contract CARD-0083 is designing. This one method is what CARD-0083 replaces;
    /// until then a kind is on the list because it has been measured, not because it exists.
    /// </summary>
    public static readonly IReadOnlyList<AgentKind> DelegatableKinds = [AgentKind.ClaudeCode, AgentKind.Grok];

    /// <summary>
    /// Resolve WHICH AGENT PROGRAM runs the task: an explicit request wins, else the role policy's
    /// <c>Kind</c> (unset everywhere as shipped), else ClaudeCode. The mirror of
    /// <see cref="ResolveLevel"/>, and the same shape of decision.
    ///
    /// <para>Two refusals, both loud. A kind outside <see cref="DelegatableKinds"/> is rejected with
    /// its reason — nothing has been exercised on Codex/OpenCode/Raw as a DELEGATE, and quietly
    /// substituting Claude for what the caller asked for is worse than failing. And an orchestrator
    /// is ClaudeCode only: its contract (the PreToolUse deny hook, delegate.ps1 usage, the check
    /// interpreter) has only ever run on Claude, so Grok starts as a worker kind. Unlike the tier
    /// floor, which silently clamps, an EXPLICIT orchestrator kind is rejected rather than
    /// reinterpreted — a caller who typed it deserves to know it did not happen.</para>
    /// </summary>
    public AgentKind ResolveAgentKind(AgentTaskKind kind, AgentTaskRole role, AgentKind? explicitKind)
    {
        var fromPolicy = _settings.RolePolicy.TryGetValue(role.ToString(), out var policy) ? policy.Kind : null;
        var resolved = explicitKind ?? fromPolicy ?? AgentKind.ClaudeCode;
        var explicitlyAsked = explicitKind is not null;

        if (!DelegatableKinds.Contains(resolved))
        {
            var source = explicitlyAsked
                ? "is not a delegate kind"
                : $"is configured as the '{role}' role's kind, but is not a delegate kind";
            throw new ValidationException(
                nameof(CreateAgentTaskRequest.AgentKind),
                $"{resolved} {source}. Delegated work runs on "
                + $"{string.Join(" or ", DelegatableKinds)} (CARD-0084); the others have never been "
                + "exercised as delegates and CARD-0083 replaces this allowlist with a capability "
                + "contract that can answer for them.");
        }

        if (kind == AgentTaskKind.Orchestrator && resolved != AgentKind.ClaudeCode)
        {
            if (explicitlyAsked)
            {
                throw new ValidationException(
                    nameof(CreateAgentTaskRequest.AgentKind),
                    $"An orchestrator cannot run on {resolved}. Its contract — the PreToolUse deny "
                    + "hook, delegate.ps1, the check interpreter — has only ever been exercised on "
                    + $"{AgentKind.ClaudeCode}, so {resolved} is a WORKER kind for now (CARD-0084). "
                    + "Delegate the workers on it and keep the orchestrator on Claude.");
            }

            // Policy-derived: promoting a role in config must not silently make orchestrators
            // unrunnable, so it clamps the way the tier floor does.
            resolved = AgentKind.ClaudeCode;
        }

        return resolved;
    }

    /// <summary>Project a loaded task to its DTO. <paramref name="family"/> is the whole run — it
    /// carries the subtree cost rollup, which a single row cannot answer.</summary>
    public Task<AgentTaskSummaryDto> GetSummaryAsync(
        AgentTask task, IReadOnlyList<AgentTask> family, CancellationToken ct = default) =>
        Task.FromResult(ToSummary(task, family));

    /// <summary>The DTO for one task, re-reading its run for the cost rollup.</summary>
    private async Task<AgentTaskSummaryDto> SummaryOfAsync(AgentTask task, CancellationToken ct)
    {
        var family = await _db.AgentTasks.AsNoTracking()
            .Where(t => t.RootTaskId == task.RootTaskId).ToListAsync(ct);
        return ToSummary(task, family);
    }

    internal static bool IsSettled(AgentTaskStatus status) =>
        status is AgentTaskStatus.Succeeded or AgentTaskStatus.Failed or AgentTaskStatus.Canceled;

    private static bool SharesRepoWith(AgentTask? parent, string? repoPath) =>
        parent is not null
        && repoPath is not null
        // The parent's WORKTREE counts as its repo: a child created inside it resolves the
        // worktree path as its toplevel, and worktrees share refs with the main checkout — so
        // targeting the parent's branch from there works exactly as it does from the repo.
        && ((parent.RepoPath is not null && DelegationWorkspaceResolver.IsWithinRoot(repoPath, parent.RepoPath))
            || (parent.WorktreePath is not null && DelegationWorkspaceResolver.IsWithinRoot(repoPath, parent.WorktreePath)));

    private static AgentTaskSummaryDto ToSummary(AgentTask task, IReadOnlyList<AgentTask> family)
    {
        // Walk the parent chain rather than recursing children — the same O(n) pass answers both
        // "my subtree's cost" and "my child count" for every row in a run.
        var subtreeCost = task.CostUsd;
        var childCount = 0;
        foreach (var other in family)
        {
            if (other.Id == task.Id) continue;
            if (other.ParentTaskId == task.Id) childCount++;
            if (IsDescendantOf(other, task.Id, family)) subtreeCost += other.CostUsd;
        }

        return new AgentTaskSummaryDto(
            task.Id, task.RootTaskId, task.ParentTaskId, task.Depth, task.Title, task.Kind, task.Role,
            task.AgentKind,
            task.ModelLevel, task.EscalatedFrom, task.Status, task.Workspace, task.WorkingDirectory,
            task.RepoPath, task.WorktreePath, task.WorktreeBranch, task.ScopeGlob, task.AgentId,
            // Snapshotted at dispatch — survives the ephemeral agent row's deletion on settle.
            task.AgentName,
            task.AgentSessionId, task.Attempt,
            task.CreatedAt, task.DispatchedAt, task.CompletedAt,
            task.TokensIn, task.CacheReadTokens, task.CacheCreationTokens, task.TokensOut,
            task.CostUsd, task.CostPricingVersion, subtreeCost, childCount,
            task.ExpectedDurationMinutes, task.NextCheckAt, task.CheckCount);
    }

    /// <summary>Internal so the attention projection rolls up subtree spend the SAME way the board
    /// does — two answers to "what has this run cost" would eventually differ.</summary>
    internal static bool IsDescendantOf(AgentTask candidate, Guid ancestorId, IReadOnlyList<AgentTask> family)
    {
        var seen = 0;
        var current = candidate;
        while (current.ParentTaskId is { } parentId)
        {
            if (parentId == ancestorId) return true;
            // A cycle can't happen through the API, but a hand-edited row shouldn't hang the server.
            if (++seen > family.Count) return false;
            var next = family.FirstOrDefault(t => t.Id == parentId);
            if (next is null) return false;
            current = next;
        }
        return false;
    }

    private static string BuildTitle(CreateAgentTaskRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.Title))
            return Clamp(request.Title.Trim(), 300);

        // Fall back to the goal's first line — a board chip needs something readable.
        var firstLine = request.Goal.ReplaceLineEndings("\n").Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault()?.Trim() ?? "Delegated task";
        return Clamp(firstLine, 300);
    }

    private static string Clamp(string value, int max) =>
        value.Length <= max ? value : value[..(max - 1)] + "…";

    private async Task RecordRejectionAsync(AgentTask task, string detail, CancellationToken ct)
    {
        AddEvent(task.Id, AgentTaskEventType.Rejected, null, detail, UtcNow());
        await _db.SaveChangesAsync(ct);
        _logger.LogWarning("Delegation rejected for task {ShortId}: {Detail}",
            DelegationReportFormatter.Short(task.Id), detail);
    }

    private void AddEvent(Guid taskId, AgentTaskEventType type, AgentModelLevel? level, string detail, DateTime at) =>
        _db.AgentTaskEvents.Add(new AgentTaskEvent
        {
            Id = Guid.NewGuid(),
            AgentTaskId = taskId,
            Type = type,
            ModelLevel = level,
            Detail = Clamp(detail, 4000),
            At = at,
        });

    internal static (string Token, string Hash) NewToken()
    {
        var raw = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        return (raw, HashToken(raw));
    }

    internal static string HashToken(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();

    private DateTime UtcNow() => _timeProvider.GetUtcNow().UtcDateTime;
}
