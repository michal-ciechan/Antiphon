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
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<AgentTaskService> _logger;

    public AgentTaskService(
        AppDbContext db,
        DelegationWorkspaceResolver workspace,
        IOptions<DelegationSettings> settings,
        IEventBus eventBus,
        TimeProvider timeProvider,
        ILogger<AgentTaskService> logger)
    {
        _db = db;
        _workspace = workspace;
        _settings = settings.Value;
        _eventBus = eventBus;
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
            ToSummary(task, family), task.Goal, task.Result, task.ResultFilePath,
            task.FailureReason, task.MergeTargetRef, events);
    }

    public async Task<AgentTaskSummaryDto> CancelAsync(Guid id, CancellationToken ct)
    {
        var task = await _db.AgentTasks.FirstOrDefaultAsync(t => t.Id == id, ct)
            ?? throw new NotFoundException(nameof(AgentTask), id);

        if (IsSettled(task.Status))
            throw new ConflictException($"Task {DelegationReportFormatter.Short(id)} has already finished.");

        var now = UtcNow();
        task.Status = AgentTaskStatus.Canceled;
        task.CompletedAt = now;
        task.ConcurrencyToken = Guid.NewGuid();
        AddEvent(task.Id, AgentTaskEventType.Canceled, null, "Canceled.", now);
        await _db.SaveChangesAsync(ct);
        await _eventBus.PublishToAllAsync("AgentTaskChanged", new { taskId = id, rootId = task.RootTaskId }, ct);

        var family = await _db.AgentTasks.AsNoTracking().Where(t => t.RootTaskId == task.RootTaskId).ToListAsync(ct);
        return ToSummary(task, family);
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
    public Task<AgentTaskSummaryDto> GetSummaryAsync(AgentTask task, IReadOnlyList<AgentTask> family) =>
        Task.FromResult(ToSummary(task, family));

    internal static bool IsSettled(AgentTaskStatus status) =>
        status is AgentTaskStatus.Succeeded or AgentTaskStatus.Failed or AgentTaskStatus.Canceled;

    private static bool SharesRepoWith(AgentTask? parent, string? repoPath) =>
        parent?.RepoPath is not null
        && repoPath is not null
        && DelegationWorkspaceResolver.IsWithinRoot(repoPath, parent.RepoPath);

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
            task.ModelLevel, task.EscalatedFrom, task.Status, task.Workspace, task.WorkingDirectory,
            task.RepoPath, task.ScopeGlob, task.AgentId, task.AgentSessionId, task.Attempt,
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
