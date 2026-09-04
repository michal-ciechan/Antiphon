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
    // CARD-0022. Optional so the existing integration harness keeps constructing this
    // unchanged; production always registers it. A Fable AlwaysOn restart is refused —
    // do not silently reroute.
    private readonly ModelAvailability? _modelAvailability;
    private readonly ISessionRunnerClient? _sessionRunner;
    private readonly HerdrLaunchContextResolver? _herdrContext;

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
        SubscriptionQuotaGate? quotaGate = null,
        ISessionRunnerClient? sessionRunner = null,
        HerdrLaunchContextResolver? herdrContext = null,
        ModelAvailability? modelAvailability = null)
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
        _sessionRunner = sessionRunner;
        _herdrContext = herdrContext;
        _modelAvailability = modelAvailability;
    }

    /// <summary>
    /// Boots the agent's process if it isn't already running. Idempotent: if the agent already
    /// has a live session this is a no-op (it does NOT re-rename / re-enable remote control).
    /// With a queued/current card it spawns work on that card (card description is the first
    /// prompt). With no card it starts a cardless, human-driven interactive session in the
    /// agent's working directory — idle at the composer unless <see cref="StartAgentRequest.Prompt"/>
    /// is supplied. <see cref="Agent.Details"/> is standing-job metadata (CLAUDE.md) and is never
    /// typed as that prompt (CARD-0283).
    /// </summary>
    public async Task<AgentDetailDto> StartAsync(Guid agentId, StartAgentRequest request, CancellationToken ct)
    {
        var agent = await LockAgentAsync(agentId, ct);

        // Any Start (human, bridge, supervisor) lifts the supervision suspend latch, cancels a
        // pending scheduled restart, and clears CARD-0312's LivenessLatchedAt — a start IS the
        // intent supervision waits for. The failure counter is deliberately NOT reset here (only
        // sustained healthy uptime resets it), so a manual retry of a still-broken agent doesn't
        // collapse the backoff ladder back to 5s.
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

        if (kind is AgentKind startKind && _modelAvailability is not null)
        {
            var alias = ModelAlias.Normalize(startKind, agent.ModelId)
                ?? ModelLevelAliases.For(startKind, agent.ModelLevel);
            await _modelAvailability.RequireAsync(startKind, alias, ct);
        }

        // A launch is the reconcile point for the CLAUDE.md floor (CARD-0059): Claude reads the file
        // from cwd at every process start, so writing it here means a floor improved in a PR reaches
        // every agent at its next launch with nothing stored to drift. Deliberately BEFORE the card
        // branch, so a card spawn gets it too. Never clobbers an unmarked file and never throws.
        // CARD-0250: pass current channel bindings so a bound agent's floor names the follow-up
        // attach rule. Bindings change without relaunch; the content hash moves at the next Start.
        IReadOnlyList<(string Provider, string Title)> boundChannels = [];
        if (_workspace is not null)
        {
            var rows = await _db.ChatChannels.AsNoTracking()
                .Where(c => c.AgentId == agent.Id)
                .OrderBy(c => c.Provider).ThenBy(c => c.Title)
                .Select(c => new { c.Provider, Title = c.Title ?? c.ExternalId })
                .ToListAsync(ct);
            boundChannels = rows.Select(c => (c.Provider, c.Title)).ToList();
        }
        _workspace?.Provision(agent, boundChannels);

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
        var initialPrompt = string.IsNullOrWhiteSpace(request.Prompt) ? null : request.Prompt.Trim();

        Guid sessionId;
        if (card is not null)
        {
            if (initialPrompt is not null)
            {
                throw new ValidationException(
                    nameof(request.Prompt),
                    "prompt is only valid on a cardless start; this agent has queued or current card work. "
                    + "The card description is delivered as the first prompt.");
            }

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
                agent, remoteControlName, request.Fresh, launchEnvOverride, initialPrompt, ct);
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
        string? initialPrompt,
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
                LaunchEnvOverride: launchEnvOverride),
            // ModelTier deliberately omitted (CARD-0246): ResolveForAgentAsync itself fills it from
            // agent.ModelLevel ONLY when agent.ModelId is blank (AgentTuiLaunchResolver.cs:51-55) -
            // passing agent.ModelLevel here unconditionally short-circuited that null-coalesce and
            // made a pinned exact ModelId launch on its tier alias instead.
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
                previous.InstructionFileStamp = composition.InstructionFileStamp;
                // CARD-0186: a PATCH that changed the agent's lane takes effect on the next
                // crash-restart rather than being silently ignored for the life of this row.
                previous.SessionBackend = agent.SessionBackend;
                await _db.SaveChangesAsync(ct);

                _launchQueue.EnqueueInteractiveSession(
                    previous.Id, agent.Id, spec, remoteControlName, resume: true, notes: notes,
                    initialPrompt: initialPrompt);
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
            InstructionFileStamp = composition.InstructionFileStamp,
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

        if (initialPrompt is null && !string.IsNullOrWhiteSpace(agent.Details))
        {
            // CARD-0283: Details is standing-job metadata (CLAUDE.md), not a first prompt. A caller
            // that stuffed a task into Details and then started has done the gym-stat-weightsteps
            // shape — Running with an empty transcript, no error. Say so in the log rather than
            // silently matching a healthy idle AlwaysOn / UI Start.
            _logger.LogInformation(
                "Cardless start of {AgentName} ({AgentId}): Details is not delivered as a prompt. "
                + "Session {SessionId} stays idle until POST /api/sessions/{{id}}/messages or StartAgentRequest.Prompt",
                agent.Name, agent.Id, session.Id);
        }

        _launchQueue.EnqueueInteractiveSession(
            session.Id, agent.Id, spec, remoteControlName, notes: notes, initialPrompt: initialPrompt);
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

    /// <summary>
    /// CARD-0213: bind a standing Herdr agent to an operator pane Antiphon did not launch.
    /// Inspect is read-only; the DB row is written Starting before the runner binds anything.
    /// Nothing is typed (no remote-control, no launch note, no queue flush).
    /// </summary>
    public async Task<AgentDetailDto> AttachHerdrAsync(Guid agentId, AttachHerdrPaneRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.PaneId))
            throw new ConflictException("paneId is required.", HerdrProblemTypes.Refused);

        var agent = await LockAgentAsync(agentId, ct);
        if (agent.SessionBackend != SessionBackend.Herdr)
        {
            throw new ConflictException(
                $"Agent '{agent.Name}' is not on the Herdr session backend.",
                HerdrProblemTypes.Refused);
        }

        AgentService.ValidateSessionBackendPairing(SessionBackend.Herdr, agent.Kind);

        if (await HasLiveSessionAsync(agent, ct))
            throw new ConflictException($"Agent '{agent.Name}' already has a live session.", HerdrProblemTypes.SessionActive);

        if (_sessionRunner is null)
            throw new ServiceUnavailableException("Session runner is not configured.", HerdrProblemTypes.Unreachable);

        if (await _sessionRunner.GetSessionBackendCapabilityMismatchAsync(ct) is { } herdrMismatch)
            throw new ConflictException(herdrMismatch, HerdrProblemTypes.Refused);

        var caps = await _sessionRunner.GetCapabilitiesAsync(ct);
        if (caps?.Features is not { } features
            || !features.Contains(RunnerCapabilityFeatures.HerdrAttach, StringComparer.OrdinalIgnoreCase))
        {
            throw new ConflictException(
                "The session runner does not advertise herdr-attach. Rebuild and restart it: pwsh -File scripts/restart-session-runner.ps1.",
                HerdrProblemTypes.Refused);
        }

        var inspect = await _sessionRunner.InspectHerdrPaneAsync(request.PaneId, ct);
        if (!HerdrAgentKindMap.TryMap(agent.Kind, out var expectedKind)
            || !string.Equals(inspect.Agent, expectedKind, StringComparison.Ordinal))
        {
            throw new ConflictException(
                $"pane {request.PaneId} is '{inspect.Agent ?? "none"}' where '{expectedKind}' was expected",
                HerdrProblemTypes.KindMismatch);
        }

        if (inspect.BoundToSessionId is Guid bound)
        {
            throw new ConflictException(
                $"pane {request.PaneId} is bound to session {bound:D} ({inspect.BoundOrigin ?? "unknown"})",
                HerdrProblemTypes.PaneBound);
        }

        if (inspect.Foreground.Count != 1)
        {
            throw new ConflictException(
                $"pane {request.PaneId} foreground is not a single {expectedKind} process",
                HerdrProblemTypes.PaneForeign);
        }

        var occupant = inspect.Foreground[0];

        var cwd = occupant.Cwd is { Length: > 0 } processCwd
            ? Path.GetFullPath(processCwd)
            : Path.GetFullPath(agent.WorkingDirectory);
        var sessionId = inspect.NativeSessionId ?? Guid.NewGuid();
        var existing = await _db.AgentSessions.FirstOrDefaultAsync(s => s.Id == sessionId, ct);
        AgentSession session;
        if (existing is not null)
        {
            var owner = await _db.Agents.FirstOrDefaultAsync(
                a => a.PersistentSessionId == sessionId.ToString("D"), ct);
            var ours = owner is not null
                && owner.Id == agent.Id
                && existing.CardId is null
                && existing.Status is SessionStatus.Stopped or SessionStatus.Failed
                && string.Equals(
                    Path.GetFullPath(existing.Cwd), cwd,
                    OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
            if (!ours)
            {
                var ownerName = existing.CardId is not null
                    ? "a card session"
                    : owner?.Name ?? "another agent";
                throw new ConflictException(
                    $"session {sessionId:D} is owned by {ownerName}",
                    HerdrProblemTypes.SessionIdTaken);
            }

            session = existing;
            var resumeNow = UtcNow();
            session.Status = SessionStatus.Starting;
            session.StartedAt = occupant.StartTimeUtc ?? resumeNow;
            session.LastSeenAt = resumeNow;
            session.EndedAt = null;
            session.ExitCode = null;
            session.FailureReason = null;
            session.SessionBackend = SessionBackend.Herdr;
            session.AgentKind = agent.Kind;
            session.Cwd = cwd;
            session.TuiProfileRevisionId = null;
            session.EffectiveModelId = null;
            session.ComposedBundleStamp = null;
            session.InstructionFileStamp = null;
        }
        else
        {
            var definitionName = _agentRegistry.Settings.DefaultDefinition;
            var now = UtcNow();
            session = new AgentSession
            {
                Id = sessionId,
                CardId = null,
                WorktreeId = null,
                DefinitionName = definitionName,
                AgentKind = agent.Kind,
                SessionBackend = SessionBackend.Herdr,
                Status = SessionStatus.Starting,
                Cwd = cwd,
                Cols = 120,
                Rows = 30,
                CreatedAt = now,
                StartedAt = occupant.StartTimeUtc ?? now,
                LastSeenAt = now,
                TuiProfileRevisionId = null,
                EffectiveModelId = null,
                ComposedBundleStamp = null,
                InstructionFileStamp = null,
            };
            _db.AgentSessions.Add(session);
        }

        await _db.SaveChangesAsync(ct);

        var workspaceKey = "none";
        if (_herdrContext is not null)
        {
            var opts = await _herdrContext.ResolveAsync(session, agent, agent.Name, ct);
            workspaceKey = opts.WorkspaceKey;
        }

        var transcriptFormat = agent.Kind switch
        {
            AgentKind.Grok => TranscriptFormats.Grok,
            AgentKind.Codex => TranscriptFormats.Codex,
            _ => TranscriptFormats.Claude,
        };
        try
        {
            await _sessionRunner.AttachHerdrAsync(
                new HerdrAttachRequest(
                    sessionId,
                    request.PaneId,
                    expectedKind,
                    transcriptFormat,
                    occupant.Pid,
                    workspaceKey,
                    inspect.NativeSessionId),
                ct);
        }
        catch (Exception ex)
        {
            session.Status = SessionStatus.Failed;
            session.FailureReason = ex is HttpException http && http.Code is { } code ? code : ex.Message;
            session.EndedAt = UtcNow();
            session.LastSeenAt = session.EndedAt.Value;
            SessionTermination.Record(session, SessionTerminationSource.SystemRequest);
            await _db.SaveChangesAsync(CancellationToken.None);
            throw;
        }

        session.Status = SessionStatus.Running;
        session.LastSeenAt = UtcNow();
        agent.PersistentSessionId = sessionId.ToString("D");
        agent.Status = AgentStatus.Running;
        agent.UpdatedAt = UtcNow();
        await _db.SaveChangesAsync(ct);

        await ClearSupervisionLatchAsync(agent, ct);
        await _eventBus.PublishToGroupAsync(
            AgentSessionGroups.Session(session.Id),
            "SessionStarted",
            new { sessionId = session.Id, cardId = (Guid?)null },
            ct);
        await _eventBus.PublishToAllAsync("AgentChanged", new AgentChangedEventDto(agent.Id), ct);

        return await _agentService.GetByIdAsync(agent.Id, ct);
    }

    /// <summary>Stops the agent's persistent session (if live) and marks the agent stopped.</summary>
    public async Task<AgentDetailDto> StopAsync(Guid agentId, CancellationToken ct)
    {
        var agent = await LockAgentAsync(agentId, ct);

        if (Guid.TryParse(agent.PersistentSessionId, out var sessionId)
            && await _db.AgentSessions.AnyAsync(s => s.Id == sessionId && LiveSessionStatuses.Contains(s.Status), ct))
        {
            await _agentSessionService.KillAsync(sessionId, SessionTerminationSource.OperatorRequest, ct);
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
        if (state is null
            || (!state.Suspended && state.NextRestartAt is null && state.LivenessLatchedAt is null))
            return;

        var wasSuspended = state.Suspended;
        state.Suspended = false;
        state.NextRestartAt = null;
        state.LivenessLatchedAt = null;
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
