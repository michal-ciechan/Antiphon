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
    private readonly PolicyRefreshService? _policyRefresh;
    private readonly OrchestratorWorkspaceWarningService? _workspaceWarning;
    private readonly HerdrSupervisionStateService _herdrSupervision;
    private readonly global::Antiphon.SessionRunner.Contracts.GrokRulesSettings _grokRulesSettings;

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
        ModelAvailability? modelAvailability = null,
        PolicyRefreshService? policyRefresh = null,
        OrchestratorWorkspaceWarningService? workspaceWarning = null,
        HerdrSupervisionStateService? herdrSupervision = null,
        IOptions<SupervisionSettings>? supervision = null,
        IOptions<global::Antiphon.SessionRunner.Contracts.GrokRulesSettings>? grokRulesSettings = null)
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
        _policyRefresh = policyRefresh;
        _workspaceWarning = workspaceWarning;
        _grokRulesSettings = grokRulesSettings?.Value ?? new();
        _herdrSupervision = herdrSupervision ?? new HerdrSupervisionStateService(
            db, supervision ?? Options.Create(new SupervisionSettings()), timeProvider, launchQueue, eventBus);
    }

    /// <summary>
    /// CARD-0334 S3. Idle-gated like the policy-refresh sweep: kill+resume, or a WhenIdle
    /// notify, without suspending supervision. <paramref name="force"/> skips only the
    /// idle-minutes floor and the cooldown; a session that reads working is always 409
    /// <c>session_working</c>.
    /// </summary>
    public async Task<RefreshPolicyResultDto> RefreshPolicyAsync(
        Guid agentId, bool force, CancellationToken ct)
    {
        if (_policyRefresh is null)
            throw new InvalidOperationException("PolicyRefreshService is not registered.");

        var exists = await _db.Agents.AsNoTracking().AnyAsync(a => a.Id == agentId, ct);
        if (!exists)
            throw new NotFoundException(nameof(Agent), agentId);

        var outcome = await _policyRefresh.RefreshAgentAsync(agentId, force, ct);
        var detail = await _agentService.GetByIdAsync(agentId, ct);
        return new RefreshPolicyResultDto(outcome.Refreshed, outcome.Notified, detail);
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
    public async Task<AgentDetailDto> StartAsync(Guid agentId, StartAgentRequest request, CancellationToken ct, bool automatic = false)
    {
        if ((request.Fresh ? 1 : 0) + (request.ResumeSessionId is not null ? 1 : 0) + (request.RetryContinuity ? 1 : 0) > 1)
            throw new ValidationException("start", "fresh, resumeSessionId and retryContinuity are mutually exclusive.");
        if ((automatic || request.CapacityRecovery) && (request.Fresh || request.ResumeSessionId is not null || request.RetryContinuity))
            throw new ConflictException("Automatic recovery cannot select or discard history.", "standing_recovery_operator_required");
        var agent = await LockAgentAsync(agentId, ct);

        if (await StandingSpecialistSeatPolicy.StartRefusalAsync(_db, agent, _delegationSettings, automatic || request.CapacityRecovery, ct) is { } specialistRefusal)
            throw new ConflictException(specialistRefusal, "specialist_start_refused");
        if (StandingSpecialistSeatPolicy.IsCheck(agent, _delegationSettings)
            && (request.RemoteControl == true || !string.IsNullOrWhiteSpace(request.Prompt)))
            throw new ConflictException("The Check seat accepts correlated specialist tasks only.", "specialist_start_prompt_refused");

        var herdrState = await _herdrSupervision.ObserveAsync(agentId, request.ResetHerdrFailureHold, false, ct);
        await _db.Entry(agent).ReloadAsync(ct);
        var alreadyLive = await HasLiveSessionAsync(agent, ct);
        if (herdrState.ContinuityHeldAt is not null && !request.Fresh && request.ResumeSessionId is null && !request.RetryContinuity)
            throw new ConflictException(herdrState.ContinuityEvidence ?? "Conversation recovery needs a decision.", StandingContinuityState.HeldCode);
        if ((request.ResumeSessionId is { } selected && agent.PersistentSessionId != selected.ToString("D") || request.Fresh)
            && (alreadyLive || Guid.TryParse(agent.PersistentSessionId, out var activeId) && _launchQueue.Owns(activeId)))
            throw new ConflictException("Stop the current conversation before selecting another.", "standing_resume_current_active");
        if (!alreadyLive && herdrState.HerdrFailureHeldAt is not null)
            throw new ConflictException(_herdrSupervision.HeldMessage(agent.Name, herdrState), HerdrSupervisionStateService.HeldCode);
        if (!alreadyLive && Guid.TryParse(agent.PersistentSessionId, out var pendingId) && _launchQueue.Owns(pendingId))
            return await _agentService.GetByIdAsync(agent.Id, ct);

        if (request.CapacityRecovery)
        {
            if (!agent.AlwaysOn)
            {
                throw new ConflictException(
                    "Capacity recovery cannot start an unowned or ephemeral session.",
                    "capacity_recovery_not_standing");
            }

            if (herdrState.Suspended)
            {
                throw new ConflictException(
                    "Capacity recovery cannot start a suspended agent.",
                    "capacity_recovery_suspended");
            }

            if (herdrState.LivenessLatchedAt is not null)
            {
                throw new ConflictException(
                    "Capacity recovery cannot start a liveness-latched agent.",
                    "capacity_recovery_liveness_latched");
            }
        }
        // Already running — leave the existing process (and its remote-control state) untouched.
        if (await HasLiveSessionAsync(agent, ct))
        {
            if (!automatic && !request.CapacityRecovery && herdrState.ContinuityHeldAt is null)
                await ClearSupervisionLatchAsync(agent, ct);
            return await _agentService.GetByIdAsync(agent.Id, ct);
        }

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
        if (card is not null && (request.ResumeSessionId is not null || request.RetryContinuity))
            throw new ConflictException("Settle queued card work before conversation recovery.", "standing_resume_card_work_pending");
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
                agent, remoteControlName, request.Fresh, launchEnvOverride, initialPrompt,
                request.PolicyRefreshDelta, request.ResumeSessionId, request.RetryContinuity, automatic || request.CapacityRecovery, ct);
            agent.CurrentCardId = null;
        }

        if (card is not null)
        {
            agent.PersistentSessionId = sessionId.ToString("D");
            agent.Status = AgentStatus.Running;
            agent.UpdatedAt = UtcNow();
            if (!automatic && !request.CapacityRecovery) await ClearSupervisionLatchAsync(agent, ct);
            await _db.SaveChangesAsync(ct);
        }
        await _eventBus.PublishToAllAsync("AgentChanged", new AgentChangedEventDto(agent.Id), ct);
        if (_workspaceWarning is not null)
            await _workspaceWarning.MaybeRaiseForStandingAgentAsync(agent, sessionId, ct);

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
        string? policyRefreshDelta,
        Guid? resumeSessionId,
        bool retryContinuity,
        bool automatic,
        CancellationToken ct)
    {
        var expectedAgentVersion = agent.UpdatedAt;
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
                GrokRulesPayload: composition.GrokRulesPayload,
                CommandLineBudgetChars: composition.CommandLineBudgetChars,
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
        var isStandingSpecialist = StandingSpecialistSeatPolicy.IsCheck(agent, _delegationSettings);
        // The hard launch policy follows the TYPED relation only, never the configured slug.
        // The slug is a compatibility discovery path (CARD-0415: keep logical ownership typed,
        // do not detect seats by name), so an ordinary agent that merely carries the interpreters
        // name is a lookalike: it stays ordinary instead of being refused a start for not holding
        // a contract it was never provisioned with. A renamed real seat keeps its typed relation
        // and stays protected. The softer specialist behaviours below still follow the slug,
        // exactly as they did before this policy existed.
        var isProvisionedSeat = StandingSpecialistSeatPolicy.IsCheck(agent);
        if (isProvisionedSeat)
            spec = CheckSpecialistLaunchPolicy.Apply(spec,
                CheckInterpreterProvisioner.Spec(_delegationSettings) with { WorkingDirectory = cwd }, agent.SessionBackend);
        var specialistLaunchEvidence = isProvisionedSeat ? SpecialistExecutionEvidenceReader.CaptureLaunch(spec) : null;
        GrokLaunchArgs.EnsureWindowsRulesArgv(spec.Args, spec.Kind, agent.SessionBackend, spec.Env, $"Agent '{agent.Name}'");

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
        var notes = isClaudeCode
                && !isStandingSpecialist
                && !string.IsNullOrWhiteSpace(agent.SystemPromptAppend)
            ? new LaunchNotes(ChannelPreamble.BootstrapBody, ChannelPreamble.RestartResumeBody)
            : null;
        if (!isStandingSpecialist && !string.IsNullOrWhiteSpace(policyRefreshDelta))
        {
            // CARD-0334: an orchestrator seat without a channel preamble must still be told
            // why it was relaunched. Resume body is the policy note regardless of preamble;
            // a resume→fresh fallback keeps BootstrapBody when one was already composed.
            var policyBody = ChannelPreamble.PolicyRefreshResumeBody(policyRefreshDelta);
            notes = new LaunchNotes(
                FreshBody: notes?.FreshBody ?? ChannelPreamble.BootstrapBody,
                ResumeBody: policyBody);
        }

        AgentExecutableResolver.Default.EnsureSpawnable(spec.Exe);

        if (agent.SessionBackend == SessionBackend.Herdr)
        {
            var probe = new AgentSession
            {
                CardId = null,
                DefinitionName = definitionName,
                AgentKind = spec.Kind,
                SpecialistLaunchEvidenceJson = specialistLaunchEvidence,
            };
            var paneTitle = HerdrLaunchContextResolver.PaneTitleFor(agent, probe);
            var resolver = _herdrContext ?? new HerdrLaunchContextResolver(_db);
            spec = spec with { Herdr = await resolver.ResolveAsync(probe, agent, paneTitle, ct) };
        }

        AgentSession? previous = null;
        if (!fresh)
            previous = await FindResumableSessionAsync(agent, spec.Kind, cwd, resumeSessionId, ct);
        var chosenSessionId = previous?.Id ?? Guid.NewGuid();
        await PreflightNamedPlacementAsync(agent, spec, chosenSessionId, ct);

        var expectedPointer = agent.PersistentSessionId;
        var expectedGeneration = previous?.StartedAt;
        var sourceId = Guid.TryParse(expectedPointer, out var source) ? source : (Guid?)null;
        var involved = new[] { sourceId, previous?.Id }.OfType<Guid>().Distinct().Order().ToArray();
        if (resumeSessionId is not null && _sessionRunner is null)
            throw new ServiceUnavailableException("Runner liveness cannot be verified.", "standing_resume_runner_unavailable");
        if (_sessionRunner is not null && involved.Length > 0)
        {
            var processes = await _sessionRunner.ListAsync(ct);
            if (processes.Any(p => involved.Contains(p.SessionId) && p.Status != "Exited" && p.ExitCode is null))
                throw new ConflictException("An involved conversation still has a live runner process.", "standing_resume_target_active");
        }
        // Coordinate with delivery before taking the short database reservation. These are the
        // same singleton queue locks that guard stamping and typing, in deterministic order.
        var queueLocks = involved.Select(id => _agentSessionService.MessageQueue.GetLock(id)).ToArray();
        var acquired = 0;
        try
        {
            foreach (var queueLock in queueLocks) { await queueLock.WaitAsync(ct); acquired++; }
            await using var reservation = await _db.Database.BeginTransactionAsync(ct);
            await LockAgentAsync(agent.Id, ct);
            await _db.Entry(agent).ReloadAsync(ct);
            foreach (var id in involved)
                await _db.AgentSessions.FromSqlInterpolated($"SELECT * FROM \"AgentSessions\" WHERE \"Id\" = {id} FOR UPDATE")
                    .AsNoTracking().SingleOrDefaultAsync(ct);
            if (agent.PersistentSessionId != expectedPointer)
                throw new ConflictException("The current conversation changed; refresh and retry.", "standing_resume_current_changed");
            if (await HasLiveSessionAsync(agent, ct) || involved.Any(id => _launchQueue.Owns(id)))
            {
                if (!fresh && previous?.Id == sourceId) return previous!.Id;
                throw new ConflictException("A conversation launch already owns this agent.", "standing_resume_current_active");
            }
            if (agent.UpdatedAt != expectedAgentVersion)
                throw new ConflictException("Agent settings or intent changed during preflight.", "standing_resume_current_changed");
            var intent = await GetOrCreateSupervisionStateAsync(agent.Id, ct);
            if (_db.Entry(intent).State != EntityState.Added) await _db.Entry(intent).ReloadAsync(ct);
            if (automatic && (intent.Suspended || intent.LivenessLatchedAt is not null))
                throw new ConflictException("Start was superseded by operator intent.", "standing_start_intent_revoked");
            if (intent.HerdrFailureHeldAt is not null)
                throw new ConflictException("Herdr recovery requires its explicit reset.", HerdrSupervisionStateService.HeldCode);
            if (intent.ContinuityHeldAt is not null && !fresh && resumeSessionId is null && !retryContinuity)
                throw new ConflictException("Conversation recovery needs a decision.", StandingContinuityState.HeldCode);
            if (await ResolveStartCardAsync(agent, ct) is not null)
                throw new ConflictException("Card work became pending.", "standing_resume_card_work_pending");
            if (previous is not null)
            {
                await _db.Entry(previous).ReloadAsync(ct);
                if (previous.StartedAt != expectedGeneration)
                    throw new ConflictException("The target generation changed.", "standing_resume_target_active");
                await new StandingSessionOwnership(_db).RequireAsync(agent, previous, ct);
                if (new StandingSessionOwnership(_db).Refusal(agent, previous, spec.Kind, cwd) is { } refusal)
                    throw new ConflictException("The selected conversation cannot be resumed.", refusal);
            }
            var switching = sourceId is not null && sourceId != chosenSessionId;
            var pending = new List<SessionQueuedMessage>();
            if (switching)
            {
                if (resumeSessionId is not null && await _db.AgentTasks.AnyAsync(t => t.AgentSessionId != null
                    && involved.Contains(t.AgentSessionId.Value)
                    && (t.Status == AgentTaskStatus.Dispatched || t.Status == AgentTaskStatus.Working || t.Status == AgentTaskStatus.Blocked), ct))
                    throw new ConflictException("Settle execution assignments before selecting history.", "standing_resume_work_in_flight");
                pending = await _db.SessionQueuedMessages.Where(m => m.AgentSessionId == sourceId
                    && m.Status == QueuedMessageStatus.Pending && m.RulesRefreshKey == null).OrderBy(m => m.Sequence).ThenBy(m => m.Id).ToListAsync(ct);
                if (pending.Any(m => !StandingQueueSwitchPolicy.NeverAttempted(m)))
                    throw new ConflictException($"Resolve attempted pending input in /sessions/{sourceId}/queue before switching.", "standing_resume_delivery_pending");
            }

        if (previous is not null)
        {
                GrokRulesLaunchValidation.Validate(spec with { Backend = agent.SessionBackend }, _grokRulesSettings);
                GrokRulesRefreshService.PreflightResume(previous, spec.GrokRulesPayload);
                var resumeNow = UtcNow();
                previous.DefinitionName = definitionName;
                previous.Status = SessionStatus.Starting;
                previous.StartedAt = resumeNow;
                previous.LastSeenAt = resumeNow;
                previous.EndedAt = null;
                previous.ExitCode = null;
                previous.FailureReason = null;
                previous.HerdrSupervisionFailureKind = null;
                previous.RestartFailureKind = null;
                previous.InteractiveLaunchCompletedAt = null;
                previous.StandingAgentId ??= agent.Id;
                // A resume is a new life of this row. Leaving the kill's PolicyRefresh (or a
                // crash's ProcessExit) stamped would make SessionTermination.Record a no-op on
                // the next close — first-writer-wins would never record the real closer.
                previous.TerminationSource = SessionTerminationSource.Unknown;
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

            await AcceptAsync(previous);
            await reservation.CommitAsync(ct);
            await _db.Entry(previous).ReloadAsync(ct);
            _launchQueue.EnqueueInteractiveSession(
                previous.Id, agent.Id, spec, remoteControlName, resume: true, notes: notes,
                initialPrompt: initialPrompt);
            return previous.Id;
        }

        var now = UtcNow();
        var session = new AgentSession
        {
            Id = chosenSessionId,
            StandingAgentId = agent.IsPoolDelegate ? null : agent.Id,
            CardId = null,
            WorktreeId = null,
            DefinitionName = definitionName,
            AgentKind = spec.Kind,
            SpecialistLaunchEvidenceJson = specialistLaunchEvidence,
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
            // Same follow-through for in-flight tasks: OnTurnEndAsync looks up the open task by
            // AgentSessionId of the session that just ended the turn. Leaving Dispatched/Working
            // rows on the previous id is how CARD-0079's check interpreter answered on the new
            // session and never settled (the occupancy lock then blocked every later check).
            var remapped = !StandingSpecialistSeatPolicy.IsCheck(agent, _delegationSettings) ? 0 : await _db.AgentTasks
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
                    Herdr = spec.Herdr is { } existing
                        ? existing with { ReusePaneOfSessionId = previousSessionId }
                        : new HerdrLaunchOptions(
                            WorkspaceKey: "none",
                            WorkspaceLabel: "Antiphon",
                            WorkspaceCwd: null,
                            PaneTitle: "agent",
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

        await AcceptAsync(session);
        await reservation.CommitAsync(ct);
        _launchQueue.EnqueueInteractiveSession(
            session.Id, agent.Id, spec, remoteControlName, notes: notes, initialPrompt: initialPrompt);
        return session.Id;

        async Task AcceptAsync(AgentSession accepted)
        {
            if (pending.Count > 0)
            {
                var sequence = await _db.SessionQueuedMessages.Where(m => m.AgentSessionId == accepted.Id)
                    .MaxAsync(m => (long?)m.Sequence, ct) ?? 0;
                foreach (var message in pending) { message.AgentSessionId = accepted.Id; message.Sequence = ++sequence; }
            }
            agent.PersistentSessionId = accepted.Id.ToString("D");
            agent.Status = AgentStatus.Running;
            agent.CurrentCardId = null;
            agent.UpdatedAt = UtcNow();
            new StandingContinuityState(_db, _timeProvider).Clear(intent);
            if (!automatic) await ClearSupervisionLatchAsync(agent, ct);
            if (fresh || resumeSessionId is not null || retryContinuity || spec.Kind is not (AgentKind.ClaudeCode or AgentKind.Grok))
                _db.AgentIncidents.Add(new AgentIncident
                {
                    Id = Guid.NewGuid(), AgentId = agent.Id, SessionId = accepted.Id,
                    Kind = fresh ? AgentIncidentKind.StandingFreshSelected
                        : resumeSessionId is not null || retryContinuity ? AgentIncidentKind.StandingResumeSelected : AgentIncidentKind.ResumeUnsupported,
                    Severity = AlertSeverity.Info, CreatedAt = UtcNow(),
                    Message = $"Accepted {(fresh ? "explicit fresh conversation" : "resume selection")}: {expectedPointer ?? "none"} -> {accepted.Id:D}; launch queued.",
                });
            await _db.SaveChangesAsync(ct);
        }
        }
        finally { for (var i = acquired - 1; i >= 0; i--) queueLocks[i].Release(); }
    }

    public async Task<StandingSessionHistoryDto> GetSessionsAsync(Guid agentId, int take, Guid? before, CancellationToken ct)
    {
        var agent = await _db.Agents.AsNoTracking().SingleOrDefaultAsync(a => a.Id == agentId, ct)
            ?? throw new NotFoundException(nameof(Agent), agentId);
        if (agent.IsPoolDelegate) return new([], null);
        var ownership = new StandingSessionOwnership(_db);
        var kind = await _launchComposer.PeekProfileKindAsync(agent, ct) ?? agent.Kind;
        var candidates = _db.AgentSessions.AsNoTracking().Where(s => s.CardId == null && s.WorktreeId == null
            && (s.StandingAgentId == agentId || s.StandingAgentId == null
                && (agent.PersistentSessionId == s.Id.ToString()
                    || _db.AgentTasks.Any(t => t.AgentId == agentId && t.AgentSessionId == s.Id)
                    || _db.AgentIncidents.Any(i => i.AgentId == agentId && i.SessionId == s.Id
                        && (i.Kind == AgentIncidentKind.Crash || i.Kind == AgentIncidentKind.RestartScheduled || i.Kind == AgentIncidentKind.Recovered)))));
        if (before is { } cursor)
        {
            var boundary = await candidates.SingleOrDefaultAsync(s => s.Id == cursor, ct)
                ?? throw new ValidationException("before", "Unknown history cursor.");
            candidates = candidates.Where(s => s.CreatedAt < boundary.CreatedAt
                || s.CreatedAt == boundary.CreatedAt && s.Id.CompareTo(boundary.Id) < 0);
        }
        var page = new List<StandingSessionHistoryItemDto>();
        var limit = Math.Clamp(take, 1, 100);
        Guid? next = null;
        // Scan bounded batches: contradictory legacy evidence must never leak another owner's history.
        var rows = await candidates.OrderByDescending(s => s.CreatedAt).ThenByDescending(s => s.Id).Take(limit * 4 + 1).ToListAsync(ct);
        foreach (var session in rows)
        {
            var proof = await ownership.ResolveAsync(session, ct);
            if (proof.Owner != agentId) continue;
            if (page.Count == limit) { next = page[^1].Id; break; }
            var refusal = ownership.Refusal(agent, session, kind, agent.WorkingDirectory);
            if (_launchQueue.Owns(session.Id)) refusal = "standing_resume_target_active";
            page.Add(new(session.Id, session.CreatedAt, session.EndedAt, session.AgentKind, session.Cwd,
                session.Status, proof.Evidence, refusal is null, refusal));
        }
        if (next is null && rows.Count == limit * 4 + 1) next = rows[^1].Id;
        return new(page, next);
    }

    private async Task PreflightNamedPlacementAsync(
        Agent agent, AgentLaunchSpec spec, Guid sessionId, CancellationToken ct)
    {
        if (agent.SessionBackend != SessionBackend.Herdr)
            return;
        if (string.IsNullOrWhiteSpace(spec.Herdr?.TabLabel))
            return;
        if (_sessionRunner is null)
            return;

        var caps = await _sessionRunner.GetCapabilitiesAsync(ct);
        if (caps?.Features is not { } features
            || !features.Contains(RunnerCapabilityFeatures.HerdrNamedTabPlacement, StringComparer.OrdinalIgnoreCase))
        {
            throw new ConflictException(
                $"The session runner does not advertise {RunnerCapabilityFeatures.HerdrNamedTabPlacement}. Rebuild and restart it: pwsh -File scripts/restart-session-runner.ps1.",
                HerdrProblemTypes.Refused);
        }

        await _sessionRunner.CheckHerdrPlacementAsync(
            new HerdrPlacementCheckRequest(sessionId, spec.Herdr), ct);
    }

    // The agent's last interactive session is resumable when it is the same session-identity kind
    // (Claude or Grok), ended (Stopped/Failed), and ran in the same working directory — both
    // runners scope conversations per directory, so resuming an id from a different cwd would fail.
    // Codex/OpenCode/Raw always start fresh.
    private async Task<AgentSession?> FindResumableSessionAsync(
        Agent agent, AgentKind kind, string cwd, Guid? selected, CancellationToken ct)
    {
        if (selected is null && string.IsNullOrWhiteSpace(agent.PersistentSessionId)) return null;
        var previousId = selected ?? (Guid.TryParse(agent.PersistentSessionId, out var parsed) ? parsed : Guid.Empty);
        var previous = await _db.AgentSessions.FirstOrDefaultAsync(s => s.Id == previousId, ct);
        if (selected is null && kind is not (AgentKind.ClaudeCode or AgentKind.Grok)
            && previous?.AgentKind is not (AgentKind.ClaudeCode or AgentKind.Grok)) return null;
        if (previous is null)
        {
            if (selected is not null) throw new NotFoundException(nameof(AgentSession), previousId);
            await HoldAsync(StandingContinuityReason.TargetMissing);
        }
        try
        {
            await new StandingSessionOwnership(_db).RequireAsync(agent, previous!, ct);
            if (new StandingSessionOwnership(_db).Refusal(agent, previous!, kind, cwd) is { } refusal)
                throw new ConflictException("The selected conversation is incompatible or active.", refusal);
        }
        catch (ConflictException ex) when (selected is null)
        {
            await HoldAsync(ex.Code is "standing_resume_owner_unproven" or "standing_resume_not_owned"
                ? StandingContinuityReason.OwnershipUnproven : StandingContinuityReason.TargetIncompatible);
        }
        return previous;

        async Task HoldAsync(StandingContinuityReason reason)
        {
            await new StandingContinuityState(_db, _timeProvider).HoldAsync(agent.Id,
                previousId == Guid.Empty ? null : previousId, reason, ct);
            throw new ConflictException("Inspect the existing conversation before retrying or starting fresh.", StandingContinuityState.HeldCode);
        }
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
            var ours = (existing.StandingAgentId is null || existing.StandingAgentId == agent.Id)
                && owner is not null
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
            session.StandingAgentId ??= agent.IsPoolDelegate ? null : agent.Id;
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
                StandingAgentId = agent.IsPoolDelegate ? null : agent.Id,
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
        if (_workspaceWarning is not null)
            await _workspaceWarning.MaybeRaiseForStandingAgentAsync(agent, session.Id, ct);

        return await _agentService.GetByIdAsync(agent.Id, ct);
    }

    /// <summary>Stops the agent's persistent session (if live) and marks the agent stopped.</summary>
    public async Task<AgentDetailDto> StopAsync(Guid agentId, CancellationToken ct)
    {
        var agent = await LockAgentAsync(agentId, ct);

        if (!agent.IsPoolDelegate)
        {
            // Record human intent before the runner RPC. A launch already queued but not
            // yet started must see the stop even when there is no process to kill yet.
            await using var transaction = await _db.Database.BeginTransactionAsync(ct);
            var ownerId = agent.StandingSpecialistOwnerId ?? agent.Id;
            await _db.Agents.FromSqlInterpolated($"SELECT * FROM \"Agents\" WHERE \"Id\" = {ownerId} FOR UPDATE")
                .AsNoTracking().SingleOrDefaultAsync(ct);
            await _db.Entry(agent).ReloadAsync(ct);
            var intent = await GetOrCreateSupervisionStateAsync(agent.Id, ct);
            if (!intent.Suspended)
                _db.AgentIncidents.Add(new AgentIncident { Id = Guid.NewGuid(), AgentId = agent.Id,
                    Kind = AgentIncidentKind.SuspendedByUser, Severity = AlertSeverity.Info,
                    Message = "Stopped by user; always-on supervision suspended until the next manual start.", CreatedAt = UtcNow() });
            intent.Suspended = true;
            intent.NextRestartAt = null;
            intent.UpdatedAt = UtcNow();
            agent.Status = AgentStatus.Stopped;
            agent.UpdatedAt = UtcNow();
            if (Guid.TryParse(agent.PersistentSessionId, out var stoppingId))
                await _db.AgentSessions.Where(s => s.Id == stoppingId)
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.TerminationSource, SessionTerminationSource.OperatorRequest), ct);
            await _db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }

        if (Guid.TryParse(agent.PersistentSessionId, out var sessionId)
            && await _db.AgentSessions.AnyAsync(s => s.Id == sessionId && LiveSessionStatuses.Contains(s.Status), ct))
        {
            await _agentSessionService.KillAsync(sessionId, SessionTerminationSource.OperatorRequest, ct);
        }

        if (agent.IsPoolDelegate)
        {
            agent.Status = AgentStatus.Stopped;
            agent.UpdatedAt = UtcNow();
        }

        // Deliberate stop of an always-on agent suspends supervision until a manual Start —
        // supervision must never fight a human's explicit intent.
        if (agent.AlwaysOn && agent.IsPoolDelegate)
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
