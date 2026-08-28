using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
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
    private readonly AgentSessionLaunchComposer _launchComposer;
    private readonly AgentTuiLaunchResolver? _launchResolver;
    private readonly AgentSessionLaunchQueue _launchQueue;
    private readonly IEventBus _eventBus;
    private readonly TimeProvider _timeProvider;
    private readonly DelegationSettings _delegationSettings;
    private readonly ILogger<AgentControlService> _logger;
    private readonly AgentWorkspaceProvisioner? _workspace;
    // CARD-0106 S2. Optional like the launch resolver beside it: absent, placeholders go
    // unresolved and the launch tripwire refuses them by name. Production always registers it.
    private readonly ApiKeyEnvResolver? _apiKeyEnvResolver;
    // CARD-0136. Optional so the existing integration harness keeps constructing this
    // unchanged; tests that want the gate wire it explicitly. Production always registers it.
    private readonly SubscriptionQuotaGate? _quotaGate;

    public AgentControlService(
        AppDbContext db,
        AgentService agentService,
        CardService cardService,
        AgentSessionService agentSessionService,
        AgentRegistry agentRegistry,
        AgentSessionLaunchComposer launchComposer,
        AgentSessionLaunchQueue launchQueue,
        IEventBus eventBus,
        TimeProvider timeProvider,
        IOptions<DelegationSettings> delegationSettings,
        ILogger<AgentControlService> logger,
        AgentTuiLaunchResolver? launchResolver = null,
        // Optional for the same reason as everywhere else here: a harness that wires no provisioner
        // still starts agents, it just starts them without the CLAUDE.md floor.
        AgentWorkspaceProvisioner? workspace = null,
        ApiKeyEnvResolver? apiKeyEnvResolver = null,
        SubscriptionQuotaGate? quotaGate = null)
    {
        _db = db;
        _agentService = agentService;
        _cardService = cardService;
        _agentSessionService = agentSessionService;
        _agentRegistry = agentRegistry;
        _launchComposer = launchComposer;
        _launchResolver = launchResolver;
        _launchQueue = launchQueue;
        _eventBus = eventBus;
        _timeProvider = timeProvider;
        _delegationSettings = delegationSettings.Value;
        _logger = logger;
        _workspace = workspace;
        _apiKeyEnvResolver = apiKeyEnvResolver;
        _quotaGate = quotaGate;
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

        var kind = await _launchComposer.PeekProfileKindAsync(agent, ct);
        if (kind is AgentKind k && _quotaGate is not null)
        {
            var overridden = await _quotaGate.EnforceAsync(
                k,
                SubscriptionUsageKey.For(agent, k),
                request.IgnoreSubscriptionQuota,
                $"start of agent '{agent.Name}'",
                ct);
            if (overridden is not null)
                RecordQuotaOverrideIncident(agent, overridden);
        }

        // A launch is the reconcile point for the CLAUDE.md floor (CARD-0059): Claude reads the file
        // from cwd at every process start, so writing it here means a floor improved in a PR reaches
        // every agent at its next launch with nothing stored to drift. Deliberately BEFORE the card
        // branch, so a card spawn gets it too. Never clobbers an unmarked file and never throws.
        _workspace?.Provision(agent);

        var launchKind = kind ?? agent.Kind;
        RemoteControlPolicy.Require(launchKind, request.RemoteControl == true, $"start of agent '{agent.Name}'");
        var remoteControl = request.RemoteControl ?? agent.RemoteControlEnabled;
        if (remoteControl && !RemoteControlPolicy.Permits(launchKind))
        {
            // Inherited stale flag: ignore, never refuse (D3 / CARD-0212).
            _logger.LogWarning("{Message}", RemoteControlPolicy.IgnoredMessage(launchKind, $"start of agent '{agent.Name}'"));
            remoteControl = false;
        }
        var remoteControlName = remoteControl ? agent.Name : null;
        var card = await ResolveStartCardAsync(agent, ct);
        var launchEnvOverride = AgentLaunchEnv.ValidateOverride(
            request.LaunchEnvOverride, "launchEnvOverride");

        Guid sessionId;
        if (card is not null)
        {
            var spawn = await _cardService.SpawnAsync(
                card.Id,
                new SpawnCardRequest(
                    RemoteControlName: remoteControlName,
                    LaunchEnvOverride: launchEnvOverride.Count == 0 ? null : launchEnvOverride),
                ct);
            sessionId = spawn.SessionId;
            agent.CurrentCardId = card.Id;
        }
        else
        {
            sessionId = await StartInteractiveSessionAsync(
                agent, remoteControlName, request.Fresh, launchEnvOverride, ct);
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
        Agent agent,
        string? remoteControlName,
        bool fresh,
        IReadOnlyDictionary<string, string>? launchEnvOverride,
        CancellationToken ct)
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

        var composition = await _launchComposer.ComposeForAgentAsync(agent, ct);
        var profileKind = await _launchComposer.PeekProfileKindAsync(agent, ct);
        var isClaudeCode = profileKind == AgentKind.ClaudeCode;
        var resolved = await AgentLaunchResolution.ResolveForAgentAsync(
            agent,
            _agentRegistry,
            _launchResolver,
            new AgentLaunchOptions(
                Cols: 120,
                Rows: 30,
                ExtraArgs: composition.ExtraArgs,
                ExtraEnv: composition.ExtraEnv,
                LaunchEnvOverride: launchEnvOverride,
                ModelTier: agent.ModelLevel),
            ct,
            _apiKeyEnvResolver);
        var spec = resolved.Spec;
        var definitionName = spec.DefinitionName;

        // Bootstrap/restart notes ride on every launch of a preamble-configured agent; the launch
        // path picks FreshBody vs ResumeBody where the fresh/resume/fallback truth lives.
        //
        // Except the standing check interpreter. The gate here is "has a SystemPromptAppend", which
        // when these notes were written meant "has a channel preamble" — CARD-0047 then started
        // using the same field for a standing CONTRACT. Both note bodies order a workspace ritual
        // (read CLAUDE.md, SOUL.md, MEMORY.md, today's memory log), and the specialist has no
        // CLAUDE.md in its scratch directory and a deny-all PreToolUse hook that would refuse the
        // reads anyway. It is an impossible instruction, and obeying it costs a turn of the agent
        // explaining that. Its whole contract already rides --append-system-prompt.
        var isStandingSpecialist = string.Equals(
            agent.Slug,
            CheckInterpreterProvisioner.Slug(_delegationSettings),
            StringComparison.OrdinalIgnoreCase);
        var notes = isClaudeCode
                && !isStandingSpecialist
                && !string.IsNullOrWhiteSpace(agent.SystemPromptAppend)
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
                previous.DelegationTokenHash = composition.DelegationTokenHash;
                previous.TuiProfileRevisionId = resolved.ProfileRevisionId;
                previous.EffectiveModelId = resolved.EffectiveModelId;
                // A resume is a LAUNCH — the args are rebuilt per invocation, so the resumed process
                // carries whatever the repo says today. Restamping is what keeps the badge honest:
                // leaving the old stamp would keep flagging drift the resume just resolved.
                previous.ComposedBundleStamp = composition.ComposedStamp;
                // CARD-0186: a PATCH that changed the agent's lane takes effect on the next
                // crash-restart rather than being silently ignored for the life of this row.
                previous.SessionBackend = agent.SessionBackend;
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
            // CARD-0160: snapshot the agent's backend at creation — a later PATCH must not rewrite
            // how THIS session was launched.
            SessionBackend = agent.SessionBackend,
            Status = SessionStatus.Starting,
            Cwd = cwd,
            Cols = 120,
            Rows = 30,
            CreatedAt = now,
            StartedAt = now,
            LastSeenAt = now,
            DelegationTokenHash = composition.DelegationTokenHash,
            TuiProfileRevisionId = resolved.ProfileRevisionId,
            EffectiveModelId = resolved.EffectiveModelId,
            ComposedBundleStamp = composition.ComposedStamp,
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

            // Same follow-through for in-flight tasks: OnTurnEndAsync looks up the open task by
            // AgentSessionId of the session that just ended the turn. Leaving Dispatched/Working
            // rows on the previous id is how CARD-0079's check interpreter answered on the new
            // session and never settled (the occupancy lock then blocked every later check).
            var remapped = await _db.AgentTasks
                .Where(t => t.AgentId == agent.Id
                    && t.AgentSessionId == previousSessionId
                    && (t.Status == AgentTaskStatus.Dispatched || t.Status == AgentTaskStatus.Working))
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.AgentSessionId, session.Id), ct);
            if (remapped > 0)
                _logger.LogInformation(
                    "Agent {AgentName}: re-pointed {Count} in-flight task(s) from session {Previous} to new session {New}",
                    agent.Name, remapped, previousSessionId, session.Id);

            // CARD-0224 D3: a Fresh (or FreshAfterResumeFailures) new-row launch still targets
            // the agent's last pane. Capture the previous id NOW — PersistentSessionId is
            // overwritten to the new row after this method returns, before the queued launch
            // runs. Never set on the resume arm (same id) or on card spawns.
            if (agent.SessionBackend == SessionBackend.Herdr)
            {
                spec = spec with
                {
                    Herdr = new HerdrLaunchOptions(
                        WorkspaceKey: spec.Herdr?.WorkspaceKey ?? "none",
                        WorkspaceLabel: spec.Herdr?.WorkspaceLabel ?? "Antiphon",
                        WorkspaceCwd: spec.Herdr?.WorkspaceCwd,
                        PaneTitle: spec.Herdr?.PaneTitle ?? "agent",
                        AgentKind: spec.Herdr?.AgentKind,
                        AgentSlug: spec.Herdr?.AgentSlug,
                        ReusePaneOfSessionId: previousSessionId),
                };
            }
        }

        _launchQueue.EnqueueInteractiveSession(session.Id, agent.Id, spec, remoteControlName, notes: notes);
        return session.Id;
    }

    // The agent's last interactive session is resumable when it is the same session-identity kind
    // (Claude or Grok), ended (Stopped/Failed), and ran in the same working directory — both
    // runners scope conversations per directory, so resuming an id from a different cwd would fail.
    // Codex/OpenCode/Raw always start fresh.
    private async Task<AgentSession?> FindResumableSessionAsync(
        Agent agent, AgentKind kind, string cwd, CancellationToken ct)
    {
        if (kind is not (AgentKind.ClaudeCode or AgentKind.Grok)
            || !Guid.TryParse(agent.PersistentSessionId, out var previousId))
            return null;

        var previous = await _db.AgentSessions.FirstOrDefaultAsync(s => s.Id == previousId, ct);
        if (previous is null
            || previous.CardId is not null
            || previous.AgentKind != kind
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

    private void RecordQuotaOverrideIncident(Agent agent, SubscriptionQuotaVerdict verdict)
    {
        _db.AgentIncidents.Add(new AgentIncident
        {
            Id = Guid.NewGuid(),
            AgentId = agent.Id,
            Kind = AgentIncidentKind.SubscriptionQuotaOverridden,
            Severity = AlertSeverity.Warning,
            Message = SubscriptionQuotaPolicy.FormatSentence(verdict),
            CreatedAt = UtcNow(),
        });
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

                if (current.Status == CardStatus.NeedsDecision)
                    _logger.LogDebug(
                        "Agent {AgentName} ({AgentId}): current card {CardIdentifier} ({CardId}) is waiting on a human decision, not respawning on it",
                        agent.Name, agent.Id, current.Identifier, current.Id);
                else
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

            if (candidate.Status == CardStatus.NeedsDecision)
                _logger.LogDebug(
                    "Agent {AgentName} ({AgentId}): skipping queued card {CardIdentifier} ({CardId}) — waiting on a human decision",
                    agent.Name, agent.Id, candidate.Identifier, candidate.Id);
            else
                _logger.LogWarning(
                    "Agent {AgentName} ({AgentId}): skipping queued card {CardIdentifier} ({CardId}) in status {Status} — finished cards should have been dequeued (stale queue row)",
                    agent.Name, agent.Id, candidate.Identifier, candidate.Id, candidate.Status);
        }

        return null;
    }

    private static bool IsSpawnable(Card card) =>
        !card.BoardColumn.IsTerminal
        && card.Status is not (CardStatus.Review or CardStatus.NeedsDecision or CardStatus.Done or CardStatus.Canceled)
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
