using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

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
    private readonly AgentTuiLaunchResolver? _launchResolver;
    private readonly AgentSessionLaunchQueue _launchQueue;
    private readonly IEventBus _eventBus;
    private readonly TimeProvider _timeProvider;
    private readonly DelegationSettings _delegationSettings;
    private readonly ILogger<AgentControlService> _logger;

    public AgentControlService(
        AppDbContext db,
        AgentService agentService,
        CardService cardService,
        AgentSessionService agentSessionService,
        AgentRegistry agentRegistry,
        AgentSessionLaunchQueue launchQueue,
        IEventBus eventBus,
        TimeProvider timeProvider,
        IOptions<DelegationSettings> delegationSettings,
        ILogger<AgentControlService> logger,
        AgentTuiLaunchResolver? launchResolver = null)
    {
        _db = db;
        _agentService = agentService;
        _cardService = cardService;
        _agentSessionService = agentSessionService;
        _agentRegistry = agentRegistry;
        _launchResolver = launchResolver;
        _launchQueue = launchQueue;
        _eventBus = eventBus;
        _timeProvider = timeProvider;
        _delegationSettings = delegationSettings.Value;
        _logger = logger;
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

        // Any Start (human, bridge, supervisor) lifts the supervision suspend latch and cancels a
        // pending scheduled restart — a start IS the intent supervision waits for. The failure
        // counter is deliberately NOT reset here (only sustained healthy uptime resets it), so a
        // manual retry of a still-broken agent doesn't collapse the backoff ladder back to 5s.
        await ClearSupervisionLatchAsync(agent, ct);

        // Already running — leave the existing process (and its remote-control state) untouched.
        if (await HasLiveSessionAsync(agent, ct))
            return await _agentService.GetByIdAsync(agent.Id, ct);

        var remoteControl = request.RemoteControl ?? agent.RemoteControlEnabled;
        var remoteControlName = remoteControl ? agent.Name : null;
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
        agent.Status = AgentStatus.Running;
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

        // Session-scoped delegation credential — the same env contract the task dispatcher gives
        // delegates, so scripts/delegate.ps1 works unchanged from inside this session. The token
        // authenticates the session to POST /api/agent-tasks as ITSELF: its delegates inherit ITS
        // working directory (not the server process's cwd) and their reports return into THIS
        // session (AgentTaskService.AuthenticateAsync, session fallback). Re-minted every launch;
        // only the hash is stored.
        var (delegationToken, delegationTokenHash) = AgentTaskService.NewToken();
        var extraEnv = new Dictionary<string, string>
        {
            ["ANTIPHON_API"] = _delegationSettings.ApiBaseUrl,
            ["ANTIPHON_AGENT_ID"] = agent.Id.ToString("D"),
            ["ANTIPHON_TASK_TOKEN"] = delegationToken,
        };

        var profileKind = await PeekProfileKindAsync(agent, ct);
        var isClaudeCode = profileKind == AgentKind.ClaudeCode;
        var extraArgs = new List<string>();
        if (isClaudeCode)
        {
            var sessionName = agent.Name.Trim();
            if (sessionName.Length > 0)
                extraArgs.AddRange(["--name", sessionName]);

            // Prefer an exact selected model; fall back to the legacy ModelLevel alias only when
            // the agent has no exact ModelId (migration compatibility).
            if (string.IsNullOrWhiteSpace(agent.ModelId))
                extraArgs.AddRange(["--model", ModelLevelAliases.ForClaude(agent.ModelLevel)]);

            if (!string.IsNullOrWhiteSpace(agent.SystemPromptAppend))
            {
                var boundChannels = await _db.ChatChannels
                    .Where(c => c.AgentId == agent.Id && c.Enabled)
                    .Select(c => new { c.Provider, c.Title, c.ExternalId })
                    .ToListAsync(ct);
                var rendered = ChannelPreamble.Render(
                    agent.SystemPromptAppend,
                    agent.Name,
                    boundChannels.Select(c => (c.Provider, c.Title ?? c.ExternalId)).ToList());
                extraArgs.AddRange(["--append-system-prompt", rendered]);
            }
        }

        var resolved = await AgentLaunchResolution.ResolveForAgentAsync(
            agent,
            _agentRegistry,
            _launchResolver,
            new AgentLaunchOptions(
                Cols: 120,
                Rows: 30,
                ExtraArgs: extraArgs.Count > 0 ? extraArgs : null,
                ExtraEnv: extraEnv),
            ct);
        var spec = resolved.Spec;
        var definitionName = spec.DefinitionName;

        var notes = isClaudeCode && !string.IsNullOrWhiteSpace(agent.SystemPromptAppend)
            ? new LaunchNotes(ChannelPreamble.BootstrapBody, ChannelPreamble.RestartResumeBody)
            : null;

        AgentExecutableResolver.Default.EnsureSpawnable(spec.Exe);

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
                previous.DelegationTokenHash = delegationTokenHash;
                previous.TuiProfileRevisionId = resolved.ProfileRevisionId;
                previous.EffectiveModelId = resolved.EffectiveModelId;
                await _db.SaveChangesAsync(ct);

                _launchQueue.EnqueueInteractiveSession(
                    previous.Id, agent.Id, spec, remoteControlName, resume: true, notes: notes);
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
            LastSeenAt = now,
            DelegationTokenHash = delegationTokenHash,
            TuiProfileRevisionId = resolved.ProfileRevisionId,
            EffectiveModelId = resolved.EffectiveModelId
        };
        _db.AgentSessions.Add(session);
        await _db.SaveChangesAsync(ct);

        // A NEW session id strands any messages still queued on the previous conversation's session
        // (fresh fallback after repeated failures, or a non-resumable previous session). Carry the
        // pending ones over so they deliver into the new conversation instead of vanishing.
        if (Guid.TryParse(agent.PersistentSessionId, out var previousSessionId)
            && previousSessionId != session.Id)
        {
            var moved = await _db.SessionQueuedMessages
                .Where(m => m.AgentSessionId == previousSessionId && m.Status == QueuedMessageStatus.Pending)
                .ExecuteUpdateAsync(s => s.SetProperty(m => m.AgentSessionId, session.Id), ct);
            if (moved > 0)
                _logger.LogInformation(
                    "Agent {AgentName}: moved {Count} pending queued message(s) from session {Previous} to new session {New}",
                    agent.Name, moved, previousSessionId, session.Id);
        }

        _launchQueue.EnqueueInteractiveSession(session.Id, agent.Id, spec, remoteControlName, notes: notes);
        return session.Id;
    }

    private async Task<AgentKind?> PeekProfileKindAsync(Agent agent, CancellationToken ct)
    {
        if (agent.TuiProfileId is { } profileId)
        {
            return await _db.AgentTuiProfiles.AsNoTracking()
                .Where(profile => profile.Id == profileId)
                .Select(profile => (AgentKind?)profile.Kind)
                .FirstOrDefaultAsync(ct);
        }

        if (_launchResolver is not null)
        {
            var defaultProfileKind = await _db.AgentTuiProfiles.AsNoTracking()
                .Where(profile => profile.IsDefault)
                .Select(profile => (AgentKind?)profile.Kind)
                .FirstOrDefaultAsync(ct);
            if (defaultProfileKind is not null)
                return defaultProfileKind;
        }

        return Enum.TryParse<AgentKind>(
            _agentRegistry.LookupByName(_agentRegistry.Settings.DefaultDefinition).Kind,
            ignoreCase: true,
            out var legacyKind)
            ? legacyKind
            : null;
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

        // Deliberate stop of an always-on agent suspends supervision until a manual Start —
        // supervision must never fight a human's explicit intent.
        if (agent.AlwaysOn)
        {
            var state = await GetOrCreateSupervisionStateAsync(agent.Id, ct);
            if (!state.Suspended)
            {
                state.Suspended = true;
                state.NextRestartAt = null;
                state.UpdatedAt = UtcNow();
                _db.AgentIncidents.Add(new AgentIncident
                {
                    Id = Guid.NewGuid(),
                    AgentId = agent.Id,
                    Kind = AgentIncidentKind.SuspendedByUser,
                    Severity = AlertSeverity.Info,
                    Message = "Stopped by user; always-on supervision suspended until the next manual start.",
                    CreatedAt = UtcNow(),
                });
            }
        }

        await _db.SaveChangesAsync(ct);
        await _eventBus.PublishToAllAsync("AgentChanged", new AgentChangedEventDto(agent.Id), ct);

        return await _agentService.GetByIdAsync(agent.Id, ct);
    }

    private async Task ClearSupervisionLatchAsync(Agent agent, CancellationToken ct)
    {
        var state = await _db.AgentSupervisionStates.FirstOrDefaultAsync(s => s.AgentId == agent.Id, ct);
        if (state is null || (!state.Suspended && state.NextRestartAt is null))
            return;

        var wasSuspended = state.Suspended;
        state.Suspended = false;
        state.NextRestartAt = null;
        state.UpdatedAt = UtcNow();
        if (wasSuspended)
        {
            _db.AgentIncidents.Add(new AgentIncident
            {
                Id = Guid.NewGuid(),
                AgentId = agent.Id,
                Kind = AgentIncidentKind.ResumedByUser,
                Severity = AlertSeverity.Info,
                Message = "Started; always-on supervision resumed.",
                CreatedAt = UtcNow(),
            });
        }

        await _db.SaveChangesAsync(ct);
    }

    private async Task<AgentSupervisionState> GetOrCreateSupervisionStateAsync(Guid agentId, CancellationToken ct)
    {
        var state = await _db.AgentSupervisionStates.FirstOrDefaultAsync(s => s.AgentId == agentId, ct);
        if (state is null)
        {
            state = new AgentSupervisionState { AgentId = agentId, UpdatedAt = UtcNow() };
            _db.AgentSupervisionStates.Add(state);
        }

        return state;
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
    // Cards whose work is finished (Review/Done/Canceled) are never spawnable: the queue policy
    // dequeues them on transition, but rows written before that policy existed — or raced past
    // it — must not re-trigger the restart respawn loop (CARD-0001: five sessions, one per
    // agent restart, onto a card sitting in Review).
    private async Task<Card?> ResolveStartCardAsync(Agent agent, CancellationToken ct)
    {
        if (agent.CurrentCardId is Guid currentId)
        {
            var current = await _db.Cards
                .Include(c => c.BoardColumn)
                .FirstOrDefaultAsync(c => c.Id == currentId, ct);
            if (current is not null && !current.BoardColumn.IsTerminal)
            {
                if (IsSpawnable(current))
                    return current;

                _logger.LogWarning(
                    "Agent {AgentName} ({AgentId}): current card {CardIdentifier} ({CardId}) is in status {Status} — work is finished, not respawning on it",
                    agent.Name, agent.Id, current.Identifier, current.Id, current.Status);
            }
        }

        var queued = await _db.Cards
            .Include(c => c.BoardColumn)
            .Where(c => c.AssignedAgentId == agent.Id && c.AgentQueuePosition != null)
            .OrderBy(c => c.AgentQueuePosition)
            .ThenBy(c => c.CreatedAt)
            .ToListAsync(ct);
        foreach (var candidate in queued)
        {
            if (IsSpawnable(candidate))
                return candidate;

            _logger.LogWarning(
                "Agent {AgentName} ({AgentId}): skipping queued card {CardIdentifier} ({CardId}) in status {Status} — finished cards should have been dequeued (stale queue row)",
                agent.Name, agent.Id, candidate.Identifier, candidate.Id, candidate.Status);
        }

        return null;
    }

    private static bool IsSpawnable(Card card) =>
        !card.BoardColumn.IsTerminal
        && card.Status is not (CardStatus.Review or CardStatus.Done or CardStatus.Canceled)
        // An archived card is off the board. Left spawnable, one sitting at an agent's queue head
        // would be respawned on at every agent start — the CARD-0001 loop, on a card someone had
        // just taken out of play.
        && card.ArchivedAt is null;

    private async Task<Agent> LockAgentAsync(Guid agentId, CancellationToken ct) =>
        await _db.Agents
            .FromSqlInterpolated($"""SELECT * FROM "Agents" WHERE "Id" = {agentId} FOR UPDATE""")
            .FirstOrDefaultAsync(ct)
        ?? throw new NotFoundException(nameof(Agent), agentId);

    private DateTime UtcNow() => _timeProvider.GetUtcNow().UtcDateTime;
}
