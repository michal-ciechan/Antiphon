using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// Agent-facing lifecycle layer: starts/stops the persistent process for an agent.
/// Selects the agent's current (or queue-head) card and delegates the actual process
/// work to <see cref="CardService"/> / <see cref="AgentSessionService"/>. When started
/// in remote-control mode the booted agent is renamed and put into /remote-control before
/// its work prompt, so the user can monitor it from elsewhere.
/// </summary>
public sealed class AgentControlService
{
    private static readonly SessionStatus[] LiveSessionStatuses =
        [SessionStatus.Starting, SessionStatus.Running, SessionStatus.Stopping];

    private readonly AppDbContext _db;
    private readonly AgentService _agentService;
    private readonly CardService _cardService;
    private readonly AgentSessionService _agentSessionService;
    private readonly AgentRegistry _agentRegistry;
    private readonly AgentSessionLaunchQueue _launchQueue;
    private readonly IEventBus _eventBus;
    private readonly TimeProvider _timeProvider;

    public AgentControlService(
        AppDbContext db,
        AgentService agentService,
        CardService cardService,
        AgentSessionService agentSessionService,
        AgentRegistry agentRegistry,
        AgentSessionLaunchQueue launchQueue,
        IEventBus eventBus,
        TimeProvider timeProvider)
    {
        _db = db;
        _agentService = agentService;
        _cardService = cardService;
        _agentSessionService = agentSessionService;
        _agentRegistry = agentRegistry;
        _launchQueue = launchQueue;
        _eventBus = eventBus;
        _timeProvider = timeProvider;
    }

    /// <summary>
    /// Boots the agent's process if it isn't already running. Idempotent: if the agent already
    /// has a live session this is a no-op (it does NOT re-rename / re-enable remote control).
    /// With a queued/current card it spawns work on that card; with no card it starts a cardless,
    /// human-driven interactive session in the agent's working directory.
    /// </summary>
    public async Task<AgentDetailDto> StartAsync(Guid agentId, StartAgentRequest request, CancellationToken ct)
    {
        var agent = await LockAgentAsync(agentId, ct);

        // Already running — leave the existing process (and its remote-control state) untouched.
        if (await HasLiveSessionAsync(agent, ct))
            return await _agentService.GetByIdAsync(agent.Id, ct);

        var remoteControlName = request.RemoteControl ? agent.Name : null;
        var card = await ResolveStartCardAsync(agent, ct);

        Guid sessionId;
        if (card is not null)
        {
            var spawn = await _cardService.SpawnAsync(
                card.Id,
                new SpawnCardRequest(RemoteControlName: remoteControlName),
                ct);
            sessionId = spawn.SessionId;
            agent.CurrentCardId = card.Id;
        }
        else
        {
            sessionId = await StartInteractiveSessionAsync(agent, remoteControlName, request.Fresh, ct);
            agent.CurrentCardId = null;
        }

        agent.PersistentSessionId = sessionId.ToString("D");
        agent.Status = AgentStatus.Working;
        agent.UpdatedAt = UtcNow();
        await _db.SaveChangesAsync(ct);
        await _eventBus.PublishToAllAsync("AgentChanged", new AgentChangedEventDto(agent.Id), ct);

        return await _agentService.GetByIdAsync(agent.Id, ct);
    }

    // Pre-creates a cardless session row (Starting) in the agent's working directory and hands the
    // actual process launch to the background queue, mirroring how card spawns return immediately.
    // By default the agent's previous Claude session is resumed (same id, `claude --resume`) so the
    // terminal picks up where it left off; `fresh` forces a brand-new conversation.
    private async Task<Guid> StartInteractiveSessionAsync(
        Agent agent, string? remoteControlName, bool fresh, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(agent.WorkingDirectory))
            throw new ConflictException($"Agent '{agent.Name}' has no working directory to start a session in.");

        // Canonicalise to a native OS path. Working directories are often stored with forward slashes
        // (e.g. "C:/src/foo"); ConPTY resolves a bare exe (cl.bat) against the cwd, and a non-native
        // path breaks that lookup ("cannot find the file specified"). The card flow dodges this by
        // running in a worktree path that's already backslashed.
        var cwd = Path.GetFullPath(agent.WorkingDirectory);
        if (!Directory.Exists(cwd))
            throw new ConflictException($"Agent '{agent.Name}' working directory does not exist: {cwd}");

        var definitionName = _agentRegistry.Settings.DefaultDefinition;
        var spec = _agentRegistry.Resolve(definitionName, new AgentLaunchOptions(Cols: 120, Rows: 30));

        if (!fresh)
        {
            var previous = await FindResumableSessionAsync(agent, spec.Kind, cwd, ct);
            if (previous is not null)
            {
                var resumeNow = UtcNow();
                previous.DefinitionName = definitionName;
                previous.Status = SessionStatus.Starting;
                previous.StartedAt = resumeNow;
                previous.LastSeenAt = resumeNow;
                previous.EndedAt = null;
                previous.ExitCode = null;
                previous.FailureReason = null;
                await _db.SaveChangesAsync(ct);

                _launchQueue.EnqueueInteractiveSession(
                    previous.Id, agent.Id, spec, remoteControlName, resume: true);
                return previous.Id;
            }
        }

        var now = UtcNow();
        var session = new AgentSession
        {
            Id = Guid.NewGuid(),
            CardId = null,
            WorktreeId = null,
            DefinitionName = definitionName,
            AgentKind = spec.Kind,
            Status = SessionStatus.Starting,
            Cwd = cwd,
            Cols = 120,
            Rows = 30,
            CreatedAt = now,
            StartedAt = now,
            LastSeenAt = now
        };
        _db.AgentSessions.Add(session);
        await _db.SaveChangesAsync(ct);

        _launchQueue.EnqueueInteractiveSession(session.Id, agent.Id, spec, remoteControlName);
        return session.Id;
    }

    // The agent's last interactive session is resumable when it is the same Claude-kind definition,
    // ended (Stopped/Failed), and ran in the same working directory — Claude scopes its transcripts
    // per directory, so resuming an id from a different cwd would fail. Only Claude Code has a
    // resumable conversation; Codex/Raw always start fresh.
    private async Task<AgentSession?> FindResumableSessionAsync(
        Agent agent, AgentKind kind, string cwd, CancellationToken ct)
    {
        if (kind != AgentKind.ClaudeCode || !Guid.TryParse(agent.PersistentSessionId, out var previousId))
            return null;

        var previous = await _db.AgentSessions.FirstOrDefaultAsync(s => s.Id == previousId, ct);
        if (previous is null
            || previous.CardId is not null
            || previous.AgentKind != AgentKind.ClaudeCode
            || previous.Status is not (SessionStatus.Stopped or SessionStatus.Failed)
            || !string.Equals(
                Path.GetFullPath(previous.Cwd), cwd,
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
        {
            return null;
        }

        return previous;
    }

    /// <summary>Stops the agent's persistent session (if live) and marks the agent stopped.</summary>
    public async Task<AgentDetailDto> StopAsync(Guid agentId, CancellationToken ct)
    {
        var agent = await LockAgentAsync(agentId, ct);

        if (Guid.TryParse(agent.PersistentSessionId, out var sessionId)
            && await _db.AgentSessions.AnyAsync(s => s.Id == sessionId && LiveSessionStatuses.Contains(s.Status), ct))
        {
            await _agentSessionService.KillAsync(sessionId, ct);
        }

        agent.Status = AgentStatus.Stopped;
        agent.UpdatedAt = UtcNow();
        await _db.SaveChangesAsync(ct);
        await _eventBus.PublishToAllAsync("AgentChanged", new AgentChangedEventDto(agent.Id), ct);

        return await _agentService.GetByIdAsync(agent.Id, ct);
    }

    private async Task<bool> HasLiveSessionAsync(Agent agent, CancellationToken ct)
    {
        if (!Guid.TryParse(agent.PersistentSessionId, out var sessionId))
            return false;

        return await _db.AgentSessions.AnyAsync(
            s => s.Id == sessionId && LiveSessionStatuses.Contains(s.Status),
            ct);
    }

    // Prefer the agent's current card while it's still runnable, otherwise the head of its queue.
    private async Task<Card?> ResolveStartCardAsync(Agent agent, CancellationToken ct)
    {
        if (agent.CurrentCardId is Guid currentId)
        {
            var current = await _db.Cards
                .Include(c => c.BoardColumn)
                .FirstOrDefaultAsync(c => c.Id == currentId, ct);
            if (current is not null && !current.BoardColumn.IsTerminal)
                return current;
        }

        return await _db.Cards
            .Include(c => c.BoardColumn)
            .Where(c => c.AssignedAgentId == agent.Id && c.AgentQueuePosition != null)
            .OrderBy(c => c.AgentQueuePosition)
            .ThenBy(c => c.CreatedAt)
            .FirstOrDefaultAsync(ct);
    }

    private async Task<Agent> LockAgentAsync(Guid agentId, CancellationToken ct) =>
        await _db.Agents
            .FromSqlInterpolated($"""SELECT * FROM "Agents" WHERE "Id" = {agentId} FOR UPDATE""")
            .FirstOrDefaultAsync(ct)
        ?? throw new NotFoundException(nameof(Agent), agentId);

    private DateTime UtcNow() => _timeProvider.GetUtcNow().UtcDateTime;
}
