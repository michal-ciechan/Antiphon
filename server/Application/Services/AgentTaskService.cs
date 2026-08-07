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
        var task = await _db.AgentTasks.FirstOrDefaultAsync(t => t.TokenHash == hash, ct)
            ?? throw new ForbiddenException("Delegation token is not recognised.");

        return new Caller(task, task.AgentSessionId, task.WorkingDirectory);
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

        var id = Guid.NewGuid();
        var level = ResolveLevel(request.Kind, request.Role, request.ModelLevel);
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
            ModelLevel = level,
            Workspace = request.Workspace,
            WorkingDirectory = resolved.WorkingDirectory,
            RepoPath = resolved.RepoPath,
            ScopeGlob = string.IsNullOrWhiteSpace(request.ScopeGlob) ? null : request.ScopeGlob.Trim(),
            // A worktree task merges into its parent's branch — but only when they share a repo.
            // Cross-repo "merge" is a release-coordination problem and deliberately out of scope.
            MergeTargetRef = request.MergeTargetRef
                ?? (SharesRepoWith(parent, resolved.RepoPath) ? parent?.MergeTargetRef : null),
            AgentId = request.AgentId,
            Ephemeral = request.AgentId is null,
            Status = AgentTaskStatus.Queued,
            ReplyTo = caller.SessionId is null ? AgentTaskReplyTo.None : AgentTaskReplyTo.Session,
            MaxAttempts = 2,
            CreatedAt = now,
            TokenHash = tokenHash,
        };

        _db.AgentTasks.Add(task);
        _db.AgentTaskEvents.Add(new AgentTaskEvent
        {
            Id = Guid.NewGuid(),
            AgentTaskId = id,
            Type = AgentTaskEventType.Created,
            ModelLevel = level,
            Detail = request.ModelLevel is { } explicitLevel
                ? $"{request.Kind}/{request.Role} at {explicitLevel} (explicit override) in {resolved.WorkingDirectory}"
                : $"{request.Kind}/{request.Role} at {level} (role policy) in {resolved.WorkingDirectory}",
            At = now,
        });
        await _db.SaveChangesAsync(ct);

        await _eventBus.PublishToAllAsync("AgentTaskChanged", new { taskId = id, rootId = task.RootTaskId }, ct);
        _logger.LogInformation(
            "Delegated task {ShortId} ({Kind}/{Role}, {Level}) created in {Dir} at depth {Depth}",
            DelegationReportFormatter.Short(id), task.Kind, task.Role, level, task.WorkingDirectory, depth);

        // The raw token is returned ONCE, to be injected into the delegate's environment. It is
        // never persisted and never readable again.
        RawTokens[id] = token;
        return new AgentTaskCreatedDto(id, DelegationReportFormatter.Short(id), task.Status, level);
    }

    /// <summary>
    /// Raw tokens held only until the dispatcher injects them into the delegate's environment.
    /// Static because the creating scope and the dispatching scope are different DI scopes.
    /// </summary>
    internal static readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, string> RawTokens = new();

    public async Task<IReadOnlyList<AgentTaskSummaryDto>> ListAsync(
        Guid? rootId, AgentTaskStatus? status, CancellationToken ct)
    {
        var query = _db.AgentTasks.AsNoTracking();
        if (rootId is { } root) query = query.Where(t => t.RootTaskId == root);
        if (status is { } s) query = query.Where(t => t.Status == s);

        var tasks = await query.OrderBy(t => t.CreatedAt).ToListAsync(ct);
        var names = await AgentNamesAsync(tasks, ct);
        return tasks.Select(t => ToSummary(t, tasks, names)).ToList();
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
            ToSummary(task, family, await AgentNamesAsync([task], ct)), task.Goal, task.Result,
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
    public async Task<AgentTaskSummaryDto> EscalateAsync(Guid id, AgentModelLevel? to, CancellationToken ct)
    {
        var task = await _db.AgentTasks.FirstOrDefaultAsync(t => t.Id == id, ct)
            ?? throw new NotFoundException(nameof(AgentTask), id);

        var from = task.ModelLevel;
        var target = ResolveEscalationTarget(task, to);
        if (target is null)
        {
            throw new ConflictException(
                $"Task {DelegationReportFormatter.Short(id)} is already at the top of the ladder "
                + $"({ModelLevelAliases.ForClaude(from)}).");
        }

        task.EscalatedFrom = from;
        task.ModelLevel = target.Value;
        var detail = $"Escalated {ModelLevelAliases.ForClaude(from)} -> {ModelLevelAliases.ForClaude(target.Value)}.";

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
        // discarded; a pinned agent is the caller's explicit choice and stays.
        if (task.Ephemeral)
            task.AgentId = null;
        task.DispatchedAt = null;
        task.CompletedAt = null;
        task.ConcurrencyToken = Guid.NewGuid();

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

    /// <summary>Project a loaded task to its DTO. <paramref name="family"/> is the whole run — it
    /// carries the subtree cost rollup, which a single row cannot answer.</summary>
    public async Task<AgentTaskSummaryDto> GetSummaryAsync(
        AgentTask task, IReadOnlyList<AgentTask> family, CancellationToken ct = default) =>
        ToSummary(task, family, await AgentNamesAsync([task], ct));

    /// <summary>The DTO for one task, re-reading its run for the cost rollup.</summary>
    private async Task<AgentTaskSummaryDto> SummaryOfAsync(AgentTask task, CancellationToken ct)
    {
        var family = await _db.AgentTasks.AsNoTracking()
            .Where(t => t.RootTaskId == task.RootTaskId).ToListAsync(ct);
        return ToSummary(task, family, await AgentNamesAsync([task], ct));
    }

    /// <summary>
    /// Agent names for the tasks that have one. The board chip names the delegate that ran the work
    /// ("doc-hand · sonnet · 4m12s"), and an id is not a name.
    /// </summary>
    private async Task<Dictionary<Guid, string>> AgentNamesAsync(
        IReadOnlyList<AgentTask> tasks, CancellationToken ct)
    {
        var ids = tasks.Where(t => t.AgentId is not null).Select(t => t.AgentId!.Value).Distinct().ToList();
        if (ids.Count == 0)
            return [];

        return await _db.Agents.AsNoTracking()
            .Where(a => ids.Contains(a.Id))
            .ToDictionaryAsync(a => a.Id, a => a.Name, ct);
    }

    internal static bool IsSettled(AgentTaskStatus status) =>
        status is AgentTaskStatus.Succeeded or AgentTaskStatus.Failed or AgentTaskStatus.Canceled;

    private static bool SharesRepoWith(AgentTask? parent, string? repoPath) =>
        parent?.RepoPath is not null
        && repoPath is not null
        && DelegationWorkspaceResolver.IsWithinRoot(repoPath, parent.RepoPath);

    private static AgentTaskSummaryDto ToSummary(
        AgentTask task, IReadOnlyList<AgentTask> family, IReadOnlyDictionary<Guid, string> agentNames)
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
            task.ModelLevel, task.EscalatedFrom, task.Status, task.Workspace, task.WorkingDirectory,
            task.RepoPath, task.ScopeGlob, task.AgentId,
            task.AgentId is { } agentId && agentNames.TryGetValue(agentId, out var name) ? name : null,
            task.AgentSessionId, task.Attempt,
            task.CreatedAt, task.DispatchedAt, task.CompletedAt,
            task.TokensIn, task.TokensOut, task.CostUsd, subtreeCost, childCount);
    }

    private static bool IsDescendantOf(AgentTask candidate, Guid ancestorId, IReadOnlyList<AgentTask> family)
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

    private static (string Token, string Hash) NewToken()
    {
        var raw = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        return (raw, HashToken(raw));
    }

    internal static string HashToken(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();

    private DateTime UtcNow() => _timeProvider.GetUtcNow().UtcDateTime;
}
