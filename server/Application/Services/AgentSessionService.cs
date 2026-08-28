using Antiphon.Agents.Pty;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Domain.StateMachine;
using Antiphon.Server.Domain.ValueObjects;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Application.Services;

public sealed class AgentSessionService : IDelegateSessionStopper
{
    private const string MemoryKilledFailureReason = "MemoryKilled: agent exceeded the configured memory limit.";
    public const string ClaudeSessionNotFoundFailureReason =
        "Claude resume session was not found. Continue from last context in this worktree or start a new Claude session.";
    private const string ClaudeSessionNotFoundNeedle = "No conversation found with session ID:";

    private readonly AppDbContext _db;
    private readonly IWorktreeManager _worktreeManager;
    private readonly WorkspaceHookService _hookService;
    private readonly IAgentProtocolAdapterFactory _adapterFactory;
    private readonly AgentSessionRuntime _runtime;
    private readonly IEventBus _eventBus;
    private readonly SessionMessageQueueService _messageQueue;
    // Incidents are recorded through AgentSupervisorService, which reaches back to this service via
    // AgentControlService — so it can only be resolved from a scope of its own, not injected.
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly AgentSessionSettings _settings;
    // Boot-prompt retry + transcript late-confirm knobs live with the queue's delivery verification
    // (CARD-0056 slice 3): both answer "did the body we typed actually reach the composer", and the
    // late-confirm reuses the queue's matcher outright.
    private readonly DeliveryVerificationSettings _verification;
    private readonly DelegationSettings _delegationSettings;
    private readonly PtyDeliveryProfile? _ptyProfile;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<AgentSessionService> _logger;

    public AgentSessionService(
        AppDbContext db,
        IWorktreeManager worktreeManager,
        WorkspaceHookService hookService,
        IAgentProtocolAdapterFactory adapterFactory,
        AgentSessionRuntime runtime,
        IEventBus eventBus,
        SessionMessageQueueService messageQueue,
        IServiceScopeFactory scopeFactory,
        IOptions<AgentSessionSettings> settings,
        IOptions<SupervisionSettings> supervision,
        TimeProvider timeProvider,
        ILogger<AgentSessionService> logger,
        IOptions<DelegationSettings>? delegationSettings = null,
        PtyDeliveryProfile? ptyProfile = null)
    {
        _db = db;
        _worktreeManager = worktreeManager;
        _hookService = hookService;
        _adapterFactory = adapterFactory;
        _runtime = runtime;
        _eventBus = eventBus;
        _messageQueue = messageQueue;
        _scopeFactory = scopeFactory;
        _settings = settings.Value;
        _verification = supervision.Value.DeliveryVerification;
        _timeProvider = timeProvider;
        _logger = logger;
        _delegationSettings = delegationSettings?.Value ?? new DelegationSettings();
        _ptyProfile = ptyProfile;
    }

    /// <summary>
    /// Same conservative default as <see cref="SessionMessageQueueService"/>: tests (and any
    /// construction that forgets the profile) get the inbox conhost ceilings.
    /// </summary>
    private PtyDeliveryCeilings Ceilings =>
        _ptyProfile?.Ceilings
        ?? _delegationSettings.CeilingsFor(PtyBackend.InboxConhost, "no pty profile — assuming the default backend");

    public async Task<AgentSessionStartResult> StartAsync(
        StartAgentSessionRequest request,
        AgentLaunchSpec launchSpec,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Prompt))
            throw new ValidationException(nameof(request.Prompt), "Prompt must not be empty.");

        var card = await LoadCardAsync(request.CardId, ct);
        var hasActiveSession = await _db.AgentSessions.AnyAsync(
            s => s.CardId == card.Id
                && (s.Status == SessionStatus.Starting
                    || s.Status == SessionStatus.Running
                    || s.Status == SessionStatus.Stopping)
                && (request.PreclaimedSessionId == null || s.Id != request.PreclaimedSessionId.Value),
            ct);
        if (hasActiveSession)
            throw new ConflictException($"Card '{card.Identifier}' already has an active agent session.");

        var now = UtcNow();
        var activeDefinition = card.Board.WorkflowDefinitions
            .FirstOrDefault(d => request.BoardWorkflowDefinitionId is not null && d.Id == request.BoardWorkflowDefinitionId)
            ?? card.Board.WorkflowDefinitions
                .Where(d => d.IsActive)
                .OrderByDescending(d => d.Version)
                .FirstOrDefault();
        if (request.BoardWorkflowDefinitionId is not null && activeDefinition?.Id != request.BoardWorkflowDefinitionId)
        {
            throw new ValidationException(
                nameof(request.BoardWorkflowDefinitionId),
                "Pinned board workflow definition does not belong to this card's board.");
        }
        var hooks = ParseHooks(activeDefinition);
        var prompt = request.Prompt;

        var attempt = new RunAttempt
        {
            Id = Guid.NewGuid(),
            CardId = card.Id,
            BoardWorkflowDefinitionId = activeDefinition?.Id,
            AttemptNumber = await NextAttemptNumberAsync(card.Id, ct),
            Phase = RunPhase.PreparingWorkspace,
            CreatedAt = now,
            StartedAt = now,
            LastEventAt = now,
            PhaseStartedAt = now,
            Prompt = prompt,
            Card = card
        };
        _db.RunAttempts.Add(attempt);
        await _db.SaveChangesAsync(ct);

        AgentSession? session = null;
        IAgentProtocolAdapter? adapter = null;

        try
        {
            var (worktree, createdWorktree) = await ResolveOrCreateWorktreeAsync(card, attempt, ct);

            var hookContext = new WorkspaceHookContext(
                worktree.Path,
                CardId: card.Identifier,
                WorktreePath: worktree.Path);
            if (createdWorktree)
                await _hookService.RunAfterCreateAsync(hookContext, hooks, ct);

            RunAttemptStateMachine.Transition(attempt, RunPhase.BuildingPrompt, UtcNow());
            prompt = BuildLaunchPrompt(request, card, worktree, activeDefinition);
            attempt.Prompt = prompt;
            await _db.SaveChangesAsync(ct);

            await _hookService.RunBeforeRunAsync(hookContext, hooks, ct);

            RunAttemptStateMachine.Transition(attempt, RunPhase.LaunchingAgent, UtcNow());
            await _db.SaveChangesAsync(ct);

            session = await ResolveSessionAsync(request, card, worktree, ct);
            card.OwnerSessionId = session.Id;
            attempt.AgentSessionId = session.Id;
            attempt.AgentSession = session;
            await _db.SaveChangesAsync(ct);

            adapter = _adapterFactory.Create(request.AgentKind);
            var spec = await BuildRuntimeLaunchSpecAsync(launchSpec, session, worktree.Path, resumeMode: null, ct);
            EnsureHerdrLaunchAllowed(session, spec);
            await adapter.StartAsync(spec, ct);

            RunAttemptStateMachine.Transition(attempt, RunPhase.InitializingSession, UtcNow());
            await _db.SaveChangesAsync(ct);

            await WaitForReadyOrThrowAsync(adapter, ct);
            session.Status = SessionStatus.Running;
            session.LastSeenAt = UtcNow();
            await _db.SaveChangesAsync(ct);

            RunAttemptStateMachine.Transition(attempt, RunPhase.StreamingTurn, UtcNow());
            await _db.SaveChangesAsync(ct);

            await _eventBus.PublishToGroupAsync(
                AgentSessionGroups.Session(session.Id),
                "SessionStarted",
                new { sessionId = session.Id, cardId = card.Id, runAttemptId = attempt.Id },
                ct);
            await _eventBus.PublishToAllAsync(
                "CardChanged",
                new { boardId = card.BoardId, cardId = card.Id },
                ct);

            // Best-effort (it is monitoring here too); the WORK prompt below stays fatal on failure
            // — that prompt is the session's whole purpose.
            await SendRemoteControlCommandsAsync(adapter, request.RemoteControlName, session, agentId: null, ct);

            await SendBootPromptWithRetryAsync(adapter, prompt, session.Id, ct);
            var firstDeltaReceived = await adapter.WaitForFirstPromptOutputAsync(
                TimeSpan.FromMilliseconds(Math.Max(100, _settings.FirstDeltaTimeoutMs)),
                ct);

            attempt.LastEventAt = UtcNow();
            session.LastSeenAt = UtcNow();
            if (!firstDeltaReceived)
            {
                if (!await TryMarkMemoryKilledAsync(session, attempt, adapter))
                {
                    RunAttemptStateMachine.Transition(attempt, RunPhase.TimedOut, UtcNow());
                    session.Status = SessionStatus.Failed;
                    session.FailureReason = "Timed out waiting for first agent output.";
                    session.EndedAt = UtcNow();
                    await adapter.KillAsync(TimeSpan.FromMilliseconds(Math.Max(100, _settings.KillGraceMs)), ct);
                    await adapter.DisposeAsync();
                }
            }
            else
            {
                var turn = await adapter.WaitForTurnCompleteAsync(ct);
                if (await TryMarkMemoryKilledAsync(session, attempt, adapter))
                {
                    // Persisted below.
                }
                else if (turn.TurnCompleted)
                {
                    RunAttemptStateMachine.Transition(attempt, RunPhase.Finishing, UtcNow());
                    await _db.SaveChangesAsync(ct);

                    await _hookService.RunAfterRunAsync(hookContext, hooks, ct);

                    RunAttemptStateMachine.Transition(attempt, RunPhase.Succeeded, UtcNow());
                }
                else
                {
                    RunAttemptStateMachine.Transition(attempt, RunPhase.TimedOut, UtcNow());
                    session.Status = SessionStatus.Failed;
                    session.FailureReason = "Timed out waiting for the agent turn to complete.";
                    session.EndedAt = UtcNow();
                    await adapter.KillAsync(TimeSpan.FromMilliseconds(Math.Max(100, _settings.KillGraceMs)), ct);
                    await adapter.DisposeAsync();
                }
            }

            await _db.SaveChangesAsync(ct);

            return new AgentSessionStartResult(session.Id, attempt.Id, worktree.Id, firstDeltaReceived);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Failed to start agent session for card {CardId}", card.Id);

            if (ex is RunnerCapabilityMismatchException && session is not null)
                await RecordRunnerBuildStaleAsync(session.Id, ex.Message, CancellationToken.None);

            // FIRST, before any bookkeeping: kill what this launch started. Anything thrown outside
            // the two timeout branches above (WaitForReadyOrThrowAsync, the remote-control commands,
            // a SaveChanges) used to reach only DisposeAsync — which leaks the process (see
            // KillAndDisposeAsync). Teardown must not depend on the DB write below succeeding.
            if (adapter is not null)
                await KillAndDisposeAsync(adapter);

            attempt.ErrorDetails = ex.Message;
            if (!RunAttemptStateMachine.IsTerminal(attempt.Phase))
                RunAttemptStateMachine.Transition(attempt, RunPhase.Failed, UtcNow());

            if (session is not null)
            {
                session.Status = SessionStatus.Failed;
                session.FailureReason = ex.Message;
                session.EndedAt = UtcNow();
            }

            await _db.SaveChangesAsync(CancellationToken.None);

            throw;
        }
    }

    /// <summary>
    /// Launches a cardless, human-driven interactive session that was pre-created in Starting state.
    /// Unlike <see cref="StartAsync"/> there is no card, worktree, workflow or run attempt: the agent
    /// process is spawned in the session's Cwd and left Running for the user to drive via the web
    /// terminal. No work prompt is sent (optionally the remote-control commands are, if requested).
    /// With <paramref name="resume"/> the agent's previous Claude conversation (same session id) is
    /// resumed; if Claude reports the conversation no longer exists, a fresh conversation is started
    /// under the same id so the terminal still opens.
    /// </summary>
    public async Task LaunchInteractiveAsync(
        Guid sessionId,
        Guid agentId,
        AgentLaunchSpec launchSpec,
        string? remoteControlName,
        bool resume,
        LaunchNotes? notes,
        CancellationToken ct)
    {
        var session = await _db.AgentSessions.FirstOrDefaultAsync(s => s.Id == sessionId, ct)
            ?? throw new NotFoundException(nameof(AgentSession), sessionId);

        try
        {
            try
            {
                await LaunchInteractiveProcessAsync(
                    session, agentId, launchSpec, remoteControlName,
                    resume ? AgentSessionResumeMode.Resume : null, notes, ct);
            }
            catch (ClaudeSessionNotFoundException)
            {
                _logger.LogInformation(
                    "Claude conversation for session {SessionId} was not found; starting fresh with the same id",
                    sessionId);
                // Effective fresh: same session row, brand-new conversation — it must BOOTSTRAP,
                // not get the restart note, so the fallback keeps the full notes and the process
                // launcher's resumeMode=null branch selects FreshBody.
                await LaunchInteractiveProcessAsync(
                    session, agentId, launchSpec, remoteControlName, resumeMode: null, notes, ct);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Failed to start interactive agent session {SessionId}", sessionId);
            session.Status = SessionStatus.Failed;
            session.FailureReason = ex.Message;
            session.EndedAt = UtcNow();
            session.LastSeenAt = session.EndedAt.Value;

            // The Start API already flipped the agent to Running before this background launch ran.
            // Without rolling that back the UI shows a phantom "Running" agent with no live session
            // and no error. Failed makes the outcome visible and re-enables the Start button.
            var agent = await _db.Agents.FirstOrDefaultAsync(a => a.Id == agentId, CancellationToken.None);
            if (agent is not null
                && agent.Status == AgentStatus.Running
                && string.Equals(agent.PersistentSessionId, sessionId.ToString("D"), StringComparison.OrdinalIgnoreCase))
            {
                agent.Status = AgentStatus.Failed;
                agent.UpdatedAt = UtcNow();
            }

            await _db.SaveChangesAsync(CancellationToken.None);
            // Let the UI refetch: the now-Failed session is no longer "live", so the agent card returns
            // to offering a fresh start instead of a dead terminal.
            await _eventBus.PublishToAllAsync("AgentChanged", new AgentChangedEventDto(agentId), CancellationToken.None);

            throw;
        }
    }

    private async Task LaunchInteractiveProcessAsync(
        AgentSession session,
        Guid agentId,
        AgentLaunchSpec launchSpec,
        string? remoteControlName,
        AgentSessionResumeMode? resumeMode,
        LaunchNotes? notes,
        CancellationToken ct)
    {
        IAgentProtocolAdapter? adapter = null;
        try
        {
            adapter = _adapterFactory.Create(session.AgentKind);
            var spec = await BuildRuntimeLaunchSpecAsync(launchSpec, session, session.Cwd, resumeMode, ct);
            EnsureHerdrLaunchAllowed(session, spec);
            await adapter.StartAsync(spec, ct);

            await WaitForReadyOrThrowAsync(adapter, ct);
            session.Status = SessionStatus.Running;
            session.LastSeenAt = UtcNow();
            await _db.SaveChangesAsync(ct);

            // Before anything types into the session: if the reused transcript still reads
            // mid-turn, the old process died before its TurnEnd — state the truth (boundary
            // record) so working/idle, the queue and the cards all read idle, not "Working"
            // forever (live miss 2026-08-08).
            var interruptedTurn = await WriteRestartBoundaryIfInterruptedAsync(session.Id, ct);

            await _eventBus.PublishToGroupAsync(
                AgentSessionGroups.Session(session.Id),
                "SessionStarted",
                new { sessionId = session.Id, cardId = (Guid?)null },
                ct);
            // Nudge the agent UI to refetch so its live session flips Starting -> Running (enables input).
            await _eventBus.PublishToAllAsync("AgentChanged", new AgentChangedEventDto(agentId), ct);

            // Interactive: no work prompt — the human drives the agent via the terminal. We only push
            // the agent into remote-control mode if asked, so it can also be monitored from elsewhere.
            // Best-effort: this session has no purpose that a monitoring command's failure invalidates.
            await SendRemoteControlCommandsAsync(adapter, remoteControlName, session, agentId, ct);

            // Channel-facing agents get a launch note: bootstrap on a fresh conversation (including
            // the resume-not-found fallback, which re-enters here with resumeMode=null), the cheaper
            // restart note on a successful resume. This branch point is where the truth lives —
            // AgentControlService cannot know whether a resume will fall back.
            await DeliverLaunchNoteAsync(session.Id, resumeMode, notes, ct);

            // LAST, and only on a genuine --resume (a fresh conversation has nothing to continue):
            // queue the auto-continue for the interrupted turn. WhenIdle deliberately serialises it
            // AFTER the launch note's turn — enqueued any earlier it would race the remote-control
            // commands and the note into one garbled composer.
            if (interruptedTurn && resumeMode == AgentSessionResumeMode.Resume)
                await EnqueueResumeContinueAsync(session.Id, ct);

            // Boot is complete — deliver anything queued while the session was Starting. The
            // enqueue path refuses to type into a booting TUI (the write would race the ready
            // probe, which then kills a healthy delegate — live miss 2026-08-09), so a delegation
            // brief enqueued at dispatch time is sitting Pending right now. If the launch note
            // above started a turn, this no-ops and the turn-end flush takes over.
            await _messageQueue.FlushSessionAsync(session.Id, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Read the adapter's output BEFORE tearing it down — that is where the "No conversation
            // found with session ID:" evidence lives.
            var sessionNotFound = resumeMode == AgentSessionResumeMode.Resume
                && IsClaudeSessionNotFound(adapter, ex);
            if (adapter is not null)
                await KillAndDisposeAsync(adapter);

            if (sessionNotFound)
                throw new ClaudeSessionNotFoundException();

            throw;
        }
    }

    /// <summary>
    /// Tears down the process a failed launch started, in the only order that actually ends it:
    /// kill, THEN dispose. <see cref="IAsyncDisposable.DisposeAsync"/> is not teardown — on the
    /// production adapter it is literally <c>=&gt; ValueTask.CompletedTask</c>, because the agent
    /// lives in a detached pty-host that deliberately outlives this server (the pty-host split). So
    /// a catch that only disposed left a real, billable agent running while its row read Failed and
    /// the always-on supervisor started a replacement: two such sessions were found live on
    /// 2026-08-16, one of them three days old (CARD-0056).
    ///
    /// This also makes the resume-not-found fallback correct by construction: that fallback
    /// relaunches under the SAME session id, which until now only worked if the first process
    /// happened to have died on its own.
    ///
    /// <see cref="CancellationToken.None"/> matches the cleanup posture of the callers' catches
    /// (their own token may already be cancelled). A kill failure is swallowed: it must never
    /// replace the launch failure the caller is about to rethrow, and an HttpClient timeout arrives
    /// here as a TaskCanceledException with nothing cancelled, so the catch is deliberately broad.
    /// Killing an already-killed session is harmless — the runner answers false for a session it no
    /// longer knows.
    /// </summary>
    private async Task KillAndDisposeAsync(IAgentProtocolAdapter adapter)
    {
        try
        {
            await adapter.KillAsync(
                TimeSpan.FromMilliseconds(Math.Max(100, _settings.KillGraceMs)),
                CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Killing the agent process after a failed launch threw; disposing anyway");
        }

        await adapter.DisposeAsync();
    }

    /// <summary>
    /// The runner client knows the technical mismatch but not which delegate it was about to
    /// launch. Resolve that ownership here and make the refusal a Critical supervisor incident.
    /// This runs in a separate scope because the supervisor reaches the session-control graph.
    /// </summary>
    private async Task RecordRunnerBuildStaleAsync(Guid sessionId, string message, CancellationToken ct)
    {
        try
        {
            var agentId = await _db.AgentTasks
                .Where(t => t.AgentSessionId == sessionId && t.AgentId != null)
                .OrderByDescending(t => t.DispatchedAt)
                .Select(t => t.AgentId)
                .FirstOrDefaultAsync(ct);
            if (agentId is not Guid id)
            {
                // Cardless/interactive sessions may not have an Agent row to alert through. The
                // exception still preserves the actionable refusal for their caller.
                return;
            }

            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            if (await db.AgentIncidents.AnyAsync(
                    i => i.SessionId == sessionId && i.Kind == AgentIncidentKind.RunnerBuildStale, ct))
                return;

            var supervisor = scope.ServiceProvider.GetRequiredService<AgentSupervisorService>();
            await supervisor.RecordIncidentAsync(
                id, sessionId, AgentIncidentKind.RunnerBuildStale, AlertSeverity.Critical,
                message, failureReason: message, ct: ct);
            await db.SaveChangesAsync(ct);
        }
        catch (Exception incidentEx) when (incidentEx is not OperationCanceledException)
        {
            _logger.LogWarning(incidentEx,
                "Could not record stale-runner incident for failed launch of session {SessionId}", sessionId);
        }
    }

    // Internal control-flow marker: a Claude --resume launch failed because the conversation is gone.
    private sealed class ClaudeSessionNotFoundException : Exception
    {
        public ClaudeSessionNotFoundException() : base(ClaudeSessionNotFoundFailureReason) { }
    }

    /// <summary>
    /// How stale a <c>UserPrompt</c> record's own timestamp may be and still count as evidence that
    /// OUR boot prompt landed. This is the guard that keeps a resumed conversation's copied history
    /// from confirming us: <c>--resume</c> legitimately re-ingests records that predate the relaunch
    /// (which is exactly why CARD-0006's rule C3 waives its age check on resume), and that history
    /// contains the PREVIOUS boot's own <c>/remote-control</c> wrapper. Sequence alone cannot tell
    /// the two apart — backfill rebases unseen entries past the session max — but the record's own
    /// timestamp can, because the copy keeps the original's. The tolerance covers clock granularity
    /// between Claude's write and our baseline read, nothing more; it is minutes short of any
    /// history. A record with no timestamp at all is not evidence (falling back to the degraded
    /// path costs an incident, and a false "delivered" costs a silently unmonitored session).
    /// </summary>
    private static readonly TimeSpan BootConfirmClockTolerance = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Sends one BOOT prompt — a launch-time write that happens before the message queue exists —
    /// and retries the WHOLE verified submit when the terminal never showed evidence of it.
    ///
    /// <para>Why re-typing is safe here and nowhere else: the only failure this retries is
    /// <see cref="PromptDeliveryException"/>, which <c>VerifiedPromptSubmitter</c> throws when no
    /// composer evidence ever appeared. That is the same check that would gate the submitting
    /// Enter, so the exception is positive evidence that the composer does NOT hold the body and a
    /// second typing cannot double-submit. CARD-0055's Enter-only rule governs the phase after
    /// evidence; this is the phase before it.</para>
    ///
    /// <para><b>That justification does not hold for Codex</b> (CARD-0108 S1): its measured failure
    /// mode is a body that arrived in the composer perfectly and a CR that folded into a newline
    /// instead of submitting, so the composer may still be HOLDING the prompt when the exception is
    /// thrown. <c>CodexSubmitConfirmation</c> therefore LOOKS at the screen before it throws and
    /// sets <see cref="PromptDeliveryException.ComposerMayHoldBody"/>; when it is set this loop
    /// skips the re-type and goes straight to the late-confirm, because appending a second copy is
    /// how a body arrives spliced onto itself. Narrow on purpose — the general look-then-clear
    /// before any re-type is CARD-0103's, not this card's.</para>
    ///
    /// <para>Before declaring failure, one last look at ground truth: if this session already has an
    /// observable transcript, a <c>UserPrompt</c> record carrying this body proves the submit
    /// actually happened while the screen reads were blind. That gate is why this is not a general
    /// replacement for composer evidence — a FRESH boot has no transcript file at all (the first
    /// submit creates it), so CARD-0055's boot scope-out still stands there. The case it does cover
    /// is the one that produced CARD-0056: a resume-mode relaunch of a session with a full day of
    /// ingestion behind it.</para>
    /// </summary>
    private async Task SendBootPromptWithRetryAsync(
        IAgentProtocolAdapter adapter, string body, Guid sessionId, CancellationToken ct)
    {
        var attempts = Math.Max(1, _verification.BootPromptAttempts);
        var delay = TimeSpan.FromSeconds(Math.Max(0, _verification.BootPromptRetryDelaySeconds));
        var toType = await SpillBootPromptAsync(sessionId, body, ct);

        // Captured ONCE, before the first keystroke: every attempt is confirmed against the same
        // floor, so a record that landed during attempt 1 still counts after attempt 3.
        var baseline = await CaptureBootConfirmBaselineAsync(sessionId, ct);
        var confirmFrom = UtcNow() - BootConfirmClockTolerance;

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await adapter.SendPromptAsync(toType, ct);
                return;
            }
            catch (PromptDeliveryException ex)
            {
                if (attempt < attempts && !ex.ComposerMayHoldBody)
                {
                    _logger.LogWarning(
                        "Boot prompt to session {SessionId} showed no composer evidence on attempt {Attempt} "
                        + "of {Attempts}: {Reason} Re-typing in {Delay}s — the missing evidence is itself proof "
                        + "the composer is not holding the body",
                        sessionId, attempt, attempts, ex.Message, delay.TotalSeconds);
                    if (delay > TimeSpan.Zero)
                        await Task.Delay(delay, _timeProvider, ct);
                    continue;
                }

                if (ex.ComposerMayHoldBody && attempt < attempts)
                {
                    _logger.LogWarning(
                        "Boot prompt to session {SessionId} failed on attempt {Attempt} of {Attempts} with "
                        + "the body STILL VISIBLE in the composer: {Reason} Skipping the remaining "
                        + "re-types — appending a second copy would splice the body onto itself "
                        + "(CARD-0108 S1)", sessionId, attempt, attempts, ex.Message);
                }

                if (await TryLateConfirmBootPromptAsync(sessionId, toType, baseline, confirmFrom, ct))
                    return;

                _logger.LogWarning(ex,
                    "Boot prompt to session {SessionId} failed after {Attempt} of {Attempts} attempt(s) "
                    + "and no transcript record confirms it", sessionId, attempt, attempts);
                throw;
            }
        }
    }

    /// <summary>
    /// CARD-0025 / CARD-0019 Delta 2: a spawn work prompt over the brief ceiling is written to
    /// <c>{cwd}/.antiphon/inbox/spawn-{sessionId:N8}.md</c> and typed as a pointer. File-write
    /// failure (or empty cwd) types the original so a filesystem problem cannot block the launch;
    /// if that original is past the single-write envelope the existing oversize incident still
    /// fires. <c>RunAttempt.Prompt</c> is the caller's body and is not rewritten.
    /// </summary>
    private async Task<string> SpillBootPromptAsync(Guid sessionId, string body, CancellationToken ct)
    {
        var ceilings = Ceilings;
        var bytes = System.Text.Encoding.UTF8.GetByteCount(body);
        if (bytes <= ceilings.BriefInlineMaxBytes)
            return body;

        var session = await _db.AgentSessions.AsNoTracking()
            .Where(s => s.Id == sessionId)
            .Select(s => new { s.Cwd, s.AgentKind })
            .FirstOrDefaultAsync(ct);

        var fileStem = $"spawn-{sessionId.ToString("N")[..8]}";
        string? absolute = null;
        if (!string.IsNullOrWhiteSpace(session?.Cwd))
            absolute = TypedBodySpill.InboxAbsolutePath(session.Cwd, fileStem);

        var fit = TypedBodySpill.Fit(new TypedBodySpill.Request(
            Body: body,
            CeilingBytes: ceilings.BriefInlineMaxBytes,
            AbsoluteSpillPath: absolute,
            RelativeSpillPath: TypedBodySpill.InboxRelativePath(fileStem),
            AgentKind: session?.AgentKind ?? AgentKind.ClaudeCode,
            Logger: _logger));

        if (!fit.Spilled && bytes > ceilings.SingleWriteMaxBytes)
            await RecordBootOversizeAsync(sessionId, bytes, ceilings, ct);

        return fit.ToType;
    }

    private async Task RecordBootOversizeAsync(
        Guid sessionId, int length, PtyDeliveryCeilings ceilings, CancellationToken ct)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var agent = await db.Agents.FirstOrDefaultAsync(
                a => a.PersistentSessionId == sessionId.ToString("D"), ct);
            if (agent is null)
                return;

            var supervisor = scope.ServiceProvider.GetRequiredService<AgentSupervisorService>();
            await supervisor.RecordIncidentAsync(
                agent.Id, sessionId, AgentIncidentKind.OversizedTerminalDelivery, AlertSeverity.Warning,
                $"A {length:N0}-byte spawn prompt was written into this terminal, past the "
                + $"{ceilings.SingleWriteMaxBytes:N0} bytes measured to arrive whole on {ceilings.Backend}. "
                + "The spill file could not be written, so the body was typed anyway. Treat what the "
                + "agent read as unverified.",
                ct: ct);
            await db.SaveChangesAsync(ct);
            await _eventBus.PublishToAllAsync("AgentChanged", new AgentChangedEventDto(agent.Id), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Failed to record oversized spawn prompt for session {SessionId}", sessionId);
        }
    }

    /// <summary>
    /// The transcript floor a boot-prompt confirmation is measured against, or null when this
    /// session has no observable transcript — a fresh conversation, whose JSONL file does not exist
    /// until the first submit creates it. Null means "no ground truth exists", not "nothing
    /// matched": without a floor, any record found would prove nothing at all.
    /// </summary>
    private async Task<long?> CaptureBootConfirmBaselineAsync(Guid sessionId, CancellationToken ct)
    {
        if (!_verification.TranscriptConfirmEnabled)
            return null;

        return await _db.TranscriptEntries
            .AsNoTracking()
            .Where(t => t.AgentSessionId == sessionId)
            .MaxAsync(t => (long?)t.Sequence, ct);
    }

    /// <summary>
    /// Did the boot prompt reach Claude after all? Pulls the runner's own transcript first — the
    /// live event stream is not a reliable clock, and a decision made on "the transcript does not
    /// contain X" without asking the runner is the mistake CARD-0055 slice 6 was filed for — then
    /// looks for a <c>UserPrompt</c> record past the baseline that carries this body.
    ///
    /// <para>Two arms, both positive evidence: the record's <c>&lt;command-name&gt;</c> wrapper
    /// names the slash command we typed, or <see cref="PromptSubmissionMatch.IsConfirmedBy"/>
    /// matches the body's head window (which the wrapper also satisfies — <c>/remote-control</c>
    /// normalizes to 15 chars, past <see cref="PromptSubmissionMatch.MinMatchChars"/>). A body too
    /// short to identify by text takes NO weak arm here: unlike the queue's 30-second confirm
    /// window, a boot runs while a resumed conversation's history is still being ingested, so "some
    /// UserPrompt showed up" is not evidence of anything.</para>
    /// </summary>
    private async Task<bool> TryLateConfirmBootPromptAsync(
        Guid sessionId, string body, long? baseline, DateTime confirmFrom, CancellationToken ct)
    {
        if (baseline is not long floor)
            return false;
        if (!PromptSubmissionMatch.RequiresTextMatch(body))
            return false;

        await _runtime.CatchUpTranscriptAsync(sessionId, ct);

        var candidates = await _db.TranscriptEntries
            .AsNoTracking()
            .Where(t => t.AgentSessionId == sessionId
                && t.Kind == TranscriptKinds.UserPrompt
                && t.Sequence > floor
                && t.Timestamp != null
                && t.Timestamp >= confirmFrom)
            .OrderBy(t => t.Sequence)
            .Select(t => t.Text)
            .ToListAsync(ct);

        var command = ReadSlashCommandName(body);
        var confirmed = candidates.Any(text =>
            (command is not null && string.Equals(
                TranscriptKinds.TryReadLocalCommandName(TranscriptKinds.UserPrompt, text),
                command,
                StringComparison.OrdinalIgnoreCase))
            || PromptSubmissionMatch.IsConfirmedBy(body, text));
        if (!confirmed)
            return false;

        _logger.LogInformation(
            "Boot prompt to session {SessionId} is late-confirmed: it became a UserPrompt record past "
            + "sequence {Baseline} even though no composer evidence was ever seen, so it is treated as "
            + "delivered", sessionId, floor);
        return true;
    }

    /// <summary>
    /// The command a boot prompt invokes ("/remote-control", "/rename"), or null when the body is
    /// an ordinary prompt. Only the NAME is available as evidence: Claude's wrapper records the
    /// arguments in a separate tag, so a head-window text match cannot see "/rename &lt;name&gt;" as
    /// one string. The name alone is still positive, timestamped evidence that this command ran.
    /// </summary>
    private static string? ReadSlashCommandName(string body)
    {
        var trimmed = body.TrimStart();
        if (trimmed.Length < 2 || trimmed[0] != '/')
            return null;

        var end = trimmed.IndexOfAny([' ', '\t', '\r', '\n']);
        return end < 0 ? trimmed : trimmed[..end];
    }

    public async Task KillAsync(Guid sessionId, CancellationToken ct)
    {
        var session = await _db.AgentSessions.FirstOrDefaultAsync(s => s.Id == sessionId, ct)
            ?? throw new NotFoundException(nameof(AgentSession), sessionId);

        session.Status = SessionStatus.Stopping;
        session.LastSeenAt = UtcNow();
        await _db.SaveChangesAsync(ct);

        var killed = await _runtime.KillAsync(
            sessionId,
            TimeSpan.FromMilliseconds(Math.Max(100, _settings.KillGraceMs)),
            ct);

        var runnerSession = await _runtime.GetSessionAsync(sessionId, ct);
        var exitReason = runnerSession.ExitReason;
        session.ExitCode = runnerSession.ExitCode;
        await _runtime.DisposeSessionAsync(sessionId);

        var memoryKilled = exitReason == AgentExitReason.MemoryKilled;
        session.Status = memoryKilled
            ? SessionStatus.Failed
            : killed ? SessionStatus.Stopped : SessionStatus.Failed;

        if (exitReason == AgentExitReason.HerdrPaneLeftOpen)
            await RaiseHerdrPaneLeftOpenAsync(sessionId, ct);
        session.EndedAt = UtcNow();
        session.LastSeenAt = session.EndedAt.Value;
        session.FailureReason = memoryKilled
            ? MemoryKilledFailureReason
            : killed ? null : "Agent process did not exit within the configured grace period.";

        var attempt = await _db.RunAttempts
            .Where(a => a.AgentSessionId == sessionId && a.CompletedAt == null)
            .OrderByDescending(a => a.AttemptNumber)
            .FirstOrDefaultAsync(ct);
        if (attempt is not null && !RunAttemptStateMachine.IsTerminal(attempt.Phase))
        {
            RunAttemptStateMachine.Transition(
                attempt,
                memoryKilled ? RunPhase.Failed : killed ? RunPhase.Canceled : RunPhase.Failed,
                UtcNow());
            attempt.ExitCode = session.ExitCode;
            attempt.ErrorDetails = memoryKilled
                ? MemoryKilledFailureReason
                : killed ? "Agent session was killed by request."
                : session.FailureReason;
        }

        await _db.SaveChangesAsync(ct);
        await _eventBus.PublishToGroupAsync(
            AgentSessionGroups.Session(sessionId),
            "SessionExited",
            new { sessionId, status = session.Status.ToString(), session.ExitCode },
            ct);
        // Cardless (interactive) sessions have nothing to notify a board about.
        if (session.CardId is Guid cardId)
        {
            var card = await _db.Cards
                .AsNoTracking()
                .Where(c => c.Id == cardId)
                .Select(c => new { c.BoardId, c.Id })
                .FirstOrDefaultAsync(ct);
            if (card is not null)
            {
                await _eventBus.PublishToAllAsync(
                    "CardChanged",
                    new { boardId = card.BoardId, cardId = card.Id },
                    ct);
            }
        }
    }

    public async Task<AgentSessionResumeResult> ResumeAsync(
        Guid sessionId,
        AgentLaunchSpec launchSpec,
        AgentSessionResumeMode resumeMode,
        CancellationToken ct)
    {
        if (!Enum.IsDefined(resumeMode))
            throw new ValidationException(nameof(resumeMode), "Resume mode is not supported.");

        var session = await _db.AgentSessions
            .Include(s => s.Card)
            .Include(s => s.Worktree)
            .FirstOrDefaultAsync(s => s.Id == sessionId, ct)
            ?? throw new NotFoundException(nameof(AgentSession), sessionId);

        if (ProviderContractCatalog.For(session.AgentKind).SessionResume.State
            != AgentTuiCapabilityState.Supported)
            throw new ConflictException("Only Claude Code and Grok sessions can be resumed.");
        // Resume rebuilds a card's worktree-bound session; a cardless interactive session has neither.
        if (session.CardId is not Guid cardId)
            throw new ConflictException($"Agent session '{sessionId}' has no card and cannot be resumed.");
        if (session.Status is SessionStatus.Starting or SessionStatus.Running or SessionStatus.Stopping)
            throw new ConflictException($"Agent session '{sessionId}' is already active.");
        if (_runtime.ListLiveSessions().Contains(sessionId))
            throw new ConflictException($"Agent session '{sessionId}' is already running.");

        var activeOtherSession = await _db.AgentSessions.AnyAsync(
            s => s.CardId == session.CardId
                && s.Id != session.Id
                && (s.Status == SessionStatus.Starting
                    || s.Status == SessionStatus.Running
                    || s.Status == SessionStatus.Stopping),
            ct);
        if (activeOtherSession)
            throw new ConflictException($"Card '{session.Card.Identifier}' already has an active agent session.");

        var cwd = !string.IsNullOrWhiteSpace(session.Worktree?.Path)
            ? session.Worktree.Path
            : session.Cwd;
        if (string.IsNullOrWhiteSpace(cwd) || !Directory.Exists(cwd))
            throw new ConflictException($"Agent session '{sessionId}' has no usable worktree path to resume.");

        var now = UtcNow();
        session.Status = SessionStatus.Starting;
        session.StartedAt = now;
        session.LastSeenAt = now;
        session.EndedAt = null;
        session.ExitCode = null;
        session.FailureReason = null;
        session.Cwd = cwd;
        session.Card.OwnerSessionId = session.Id;
        session.Card.ConcurrencyToken = Guid.NewGuid();
        session.Card.UpdatedAt = now;
        await _db.SaveChangesAsync(ct);

        IAgentProtocolAdapter? adapter = null;
        try
        {
            adapter = _adapterFactory.Create(session.AgentKind);
            var spec = await BuildRuntimeLaunchSpecAsync(launchSpec, session, cwd, resumeMode, ct);
            EnsureHerdrLaunchAllowed(session, spec);
            await adapter.StartAsync(spec, ct);
            await WaitForReadyOrThrowAsync(adapter, ct);

            session.Status = SessionStatus.Running;
            session.LastSeenAt = UtcNow();
            await _db.SaveChangesAsync(ct);

            // Same truth-telling as the interactive relaunch path: a mid-turn transcript on a
            // freshly resumed process means the old turn died — record its end. No auto-continue
            // here: a human resumed this card session deliberately and will say what they want.
            await WriteRestartBoundaryIfInterruptedAsync(session.Id, ct);

            // Deliver anything queued while the session was Starting (the enqueue path refuses
            // to type into a booting TUI — see LaunchInteractiveProcessAsync).
            await _messageQueue.FlushSessionAsync(session.Id, ct);

            await _eventBus.PublishToGroupAsync(
                AgentSessionGroups.Session(session.Id),
                "SessionResumed",
                new { sessionId = session.Id, cardId = session.CardId },
                ct);
            await _eventBus.PublishToAllAsync(
                "CardChanged",
                new { boardId = session.Card.BoardId, cardId = session.CardId },
                ct);

            return new AgentSessionResumeResult(session.Id, cardId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Failed to resume agent session {SessionId}", session.Id);
            // Same evidence-then-teardown order as the interactive launch: read the adapter's output
            // for the not-found needle, then kill what this resume started (KillAndDisposeAsync —
            // disposing alone leaks the process).
            var sessionNotFound = IsClaudeSessionNotFound(adapter, ex);
            if (adapter is not null)
                await KillAndDisposeAsync(adapter);

            var failureReason = sessionNotFound
                ? ClaudeSessionNotFoundFailureReason
                : ex.Message;
            session.Status = SessionStatus.Failed;
            session.FailureReason = failureReason;
            session.EndedAt = UtcNow();
            session.LastSeenAt = session.EndedAt.Value;
            session.Card.OwnerSessionId = null;
            session.Card.ConcurrencyToken = Guid.NewGuid();
            session.Card.UpdatedAt = session.EndedAt.Value;
            await _db.SaveChangesAsync(CancellationToken.None);

            if (sessionNotFound)
                throw new ConflictException(ClaudeSessionNotFoundFailureReason);

            throw;
        }
    }

    public Task SendInputAsync(Guid sessionId, string input, CancellationToken ct) =>
        _runtime.SendInputAsync(sessionId, input, ct);

    public Task ResizeAsync(Guid sessionId, int cols, int rows, CancellationToken ct)
    {
        if (cols <= 0 || rows <= 0)
            throw new ValidationException("size", "Terminal cols and rows must be positive.");

        return ResizeAndPersistAsync(sessionId, cols, rows, ct);
    }

    public string GetBuffer(Guid sessionId) => _runtime.GetBufferSnapshot(sessionId).Buffer;

    public async Task<AgentSessionBufferDto> GetBufferAsync(Guid sessionId, CancellationToken ct)
    {
        var exists = await _db.AgentSessions.AnyAsync(s => s.Id == sessionId, ct);
        if (!exists)
            throw new NotFoundException(nameof(AgentSession), sessionId);

        var snapshot = _runtime.GetBufferSnapshot(sessionId);
        return new AgentSessionBufferDto(sessionId, snapshot.Buffer, snapshot.LastSequence);
    }

    public async Task<SessionTranscriptDto> GetTranscriptAsync(Guid sessionId, long since, CancellationToken ct)
    {
        var exists = await _db.AgentSessions.AnyAsync(s => s.Id == sessionId, ct);
        if (!exists)
            throw new NotFoundException(nameof(AgentSession), sessionId);

        // Best-effort: fill any gaps from the live runner (no-op if the session isn't live) before reading.
        await _runtime.SyncTranscriptAsync(sessionId, ct);

        var entries = await _db.TranscriptEntries
            .AsNoTracking()
            .Where(t => t.AgentSessionId == sessionId && t.Sequence > since)
            .OrderBy(t => t.Sequence)
            .Select(t => new TranscriptEntryDto(
                t.Sequence, t.Kind, t.Uuid, t.ParentUuid, t.Timestamp, t.Role, t.Text,
                t.ToolName, t.ToolInput, t.ToolUseId, t.ToolIsError, t.StopReason,
                t.ApiCallId, t.InputTokens, t.OutputTokens, t.CacheReadTokens, t.CacheCreationTokens,
                t.IsApiError, t.ApiErrorClass, t.ApiErrorStatus, t.Model))
            .ToListAsync(ct);

        var last = entries.Count > 0 ? entries[^1].Sequence : since;
        return new SessionTranscriptDto(sessionId, entries, last);
    }

    private async Task<Card> LoadCardAsync(Guid cardId, CancellationToken ct)
    {
        return await _db.Cards
            .Include(c => c.Board).ThenInclude(b => b.Project)
            .Include(c => c.Board).ThenInclude(b => b.WorkflowDefinitions)
            .Include(c => c.CurrentWorktree)
            .Include(c => c.ExternalIssueRef)
            .FirstOrDefaultAsync(c => c.Id == cardId, ct)
            ?? throw new NotFoundException(nameof(Card), cardId);
    }

    private async Task<AgentSession> ResolveSessionAsync(
        StartAgentSessionRequest request,
        Card card,
        Worktree worktree,
        CancellationToken ct)
    {
        var now = UtcNow();
        if (request.PreclaimedSessionId is Guid preclaimedSessionId)
        {
            var preclaimed = await _db.AgentSessions
                .FirstOrDefaultAsync(s => s.Id == preclaimedSessionId && s.CardId == card.Id, ct)
                ?? throw new NotFoundException(nameof(AgentSession), preclaimedSessionId);
            if (preclaimed.Status != SessionStatus.Starting)
                throw new ConflictException($"Preclaimed agent session '{preclaimedSessionId}' is not in Starting state.");

            preclaimed.WorktreeId = worktree.Id;
            preclaimed.DefinitionName = request.DefinitionName;
            preclaimed.AgentKind = request.AgentKind;
            preclaimed.Cwd = worktree.Path;
            preclaimed.Cols = request.Cols;
            preclaimed.Rows = request.Rows;
            preclaimed.LastSeenAt = now;
            preclaimed.Worktree = worktree;
            return preclaimed;
        }

        // CARD-0160: stamp the assigned agent's SessionBackend when one exists; otherwise PtyHost
        // (card spawn with no assigned agent has nowhere else to read it from).
        var sessionBackend = SessionBackend.PtyHost;
        if (card.AssignedAgentId is Guid assignedAgentId)
        {
            var assignedBackend = await _db.Agents.AsNoTracking()
                .Where(a => a.Id == assignedAgentId)
                .Select(a => (SessionBackend?)a.SessionBackend)
                .FirstOrDefaultAsync(ct);
            if (assignedBackend is { } backend)
                sessionBackend = backend;
        }

        var session = new AgentSession
        {
            Id = Guid.NewGuid(),
            CardId = card.Id,
            WorktreeId = worktree.Id,
            DefinitionName = request.DefinitionName,
            AgentKind = request.AgentKind,
            SessionBackend = sessionBackend,
            Status = SessionStatus.Starting,
            Cwd = worktree.Path,
            Cols = request.Cols,
            Rows = request.Rows,
            CreatedAt = now,
            StartedAt = now,
            LastSeenAt = now,
            Card = card,
            Worktree = worktree
        };
        _db.AgentSessions.Add(session);
        return session;
    }

    private async Task<AgentLaunchSpec> BuildRuntimeLaunchSpecAsync(
        AgentLaunchSpec launchSpec,
        AgentSession session,
        string cwd,
        AgentSessionResumeMode? resumeMode,
        CancellationToken ct)
    {
        var args = UsesSessionIdentityArgs(session.AgentKind)
            ? BuildSessionIdentityArgs(launchSpec.Args, session.Id, resumeMode)
            : launchSpec.Args;

        // Read the SESSION snapshot, never the agent's live value — ceilings follow the lane
        // this process was actually launched on. A resume restamps the snapshot from the agent
        // first (CARD-0186), so a PATCH takes effect on the next crash-restart.
        var backend = session.SessionBackend;
        HerdrLaunchOptions? herdr = null;
        if (backend == SessionBackend.Herdr)
        {
            var agent = await ResolveOwningAgentAsync(session, ct);
            var paneTitle = string.IsNullOrWhiteSpace(session.DefinitionName)
                ? (agent?.Name ?? "agent")
                : session.DefinitionName;
            herdr = await new HerdrLaunchContextResolver(_db)
                .ResolveAsync(session, agent, paneTitle, ct);
            // Session-row kind, the same value the tailer format is derived from — the two cannot
            // disagree. Null AgentKind on the wire still means Claude (old-server compat).
            if (HerdrAgentKindMap.TryMap(session.AgentKind, out var herdrKind))
                herdr = herdr with { AgentKind = herdrKind };
        }

        var spec = launchSpec with
        {
            Args = args,
            Cwd = cwd,
            Cols = session.Cols,
            Rows = session.Rows,
            MemoryLimitMb = _settings.MemoryLimitMb,
            SessionId = session.Id,
            Backend = backend,
            Herdr = herdr,
        };

        // THE API KEY TRIPWIRE (CARD-0106 S2). Every launch from every path — interactive, card,
        // resume — passes through this method on its way to one of the three adapter.StartAsync
        // sites, and nothing else in the server calls StartAsync at all. So a future path that
        // builds an Env and forgets to resolve it fails its FIRST launch naming the surviving
        // token, instead of exporting the literal {{key:...}} string into a real process where the
        // only symptom is an agent that authenticates as nobody — or, worse, being "fixed" later by
        // somebody deleting the placeholder. It refuses; it does not resolve (there is no database
        // here on purpose — a tripwire that could paper over the gap would stop being evidence of
        // it). It also refuses a placeholder in ARGUMENTS, which is the enforcement half of
        // "env values only": args are process-listing-visible and quoted into logs and failure
        // reasons, and --append-system-prompt text additionally lands in transcripts.
        ApiKeyPlaceholder.EnsureResolved(spec, session.Id);
        return spec;
    }

    /// <summary>
    /// CARD-0160 / CARD-0187 launch-time backstop: Herdr AND Kind not in
    /// {ClaudeCode, Grok, Codex} → ConflictException. Defense in depth for drift between PATCH
    /// and launch. AlwaysOn / channel-bound arms lifted (CARD-0186).
    /// </summary>
    private void EnsureHerdrLaunchAllowed(AgentSession session, AgentLaunchSpec spec)
    {
        if (spec.Backend != SessionBackend.Herdr)
            return;

        var agentId = session.Card?.AssignedAgentId;
        Agent? agent = null;
        if (agentId is Guid id)
            agent = _db.Agents.AsNoTracking().FirstOrDefault(a => a.Id == id);

        agent ??= _db.Agents.AsNoTracking()
            .FirstOrDefault(a => a.PersistentSessionId == session.Id.ToString("D"));

        var kind = agent?.Kind ?? session.AgentKind;

        try
        {
            AgentService.ValidateSessionBackendPairing(SessionBackend.Herdr, kind);
        }
        catch (ConflictException)
        {
            session.Status = SessionStatus.Failed;
            session.FailureReason = $"Herdr launch refused: {kind} is not supported on herdr.";
            session.EndedAt = UtcNow();
            throw;
        }

        if (spec.Herdr is null)
            throw new ConflictException(
                "Herdr launch requires HerdrLaunchOptions (server failed to resolve workspace context).",
                "herdr_refused");
    }

    private async Task<Agent?> ResolveOwningAgentAsync(AgentSession session, CancellationToken ct)
    {
        if (session.CardId is Guid cardId)
        {
            var assignedId = await _db.Cards.AsNoTracking()
                .Where(c => c.Id == cardId)
                .Select(c => c.AssignedAgentId)
                .FirstOrDefaultAsync(ct);
            if (assignedId is Guid id)
                return await _db.Agents.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id, ct);
        }

        var sessionKey = session.Id.ToString("D");
        return await _db.Agents.AsNoTracking()
            .FirstOrDefaultAsync(a => a.PersistentSessionId == sessionKey, ct);
    }

    /// <summary>
    /// Whether this kind's launch carries <c>--session-id</c>/<c>--resume</c> at all. Internal so
    /// <c>DelegateLaunchArgvIntegrityTests</c> can ask the real rule which kinds it must find that
    /// argument on, rather than encoding a list of kinds that would drift.
    /// </summary>
    internal static bool UsesSessionIdentityArgs(AgentKind kind) =>
        ProviderContractCatalog.For(kind).SessionResume.State == AgentTuiCapabilityState.Supported;

    /// <summary>
    /// Internal, not private, for CARD-0101's coverage gap (P0-1): <c>--session-id</c> is appended
    /// HERE, after <c>AgentTaskDispatcher.BuildLaunchSpec</c> has composed the bundles, so an argv
    /// integrity test that stopped at the spec would be testing a command line production never
    /// builds — and losing this argument is precisely what made the shred survive three days (an
    /// unbound transcript reads as a transcript-layer bug).
    /// </summary>
    internal static IReadOnlyList<string> BuildSessionIdentityArgs(
        IReadOnlyList<string> args,
        Guid sessionId,
        AgentSessionResumeMode? resumeMode)
    {
        var filtered = new List<string>();
        for (var i = 0; i < args.Count; i++)
        {
            var arg = args[i];
            if (IsClaudeSessionArg(arg))
            {
                if (ClaudeSessionArgConsumesValue(arg)
                    && i + 1 < args.Count
                    && !args[i + 1].StartsWith("-", StringComparison.Ordinal))
                {
                    i++;
                }
                continue;
            }

            filtered.Add(arg);
        }

        if (resumeMode == AgentSessionResumeMode.Continue)
        {
            filtered.Add("--continue");
            return filtered.AsReadOnly();
        }

        filtered.Add(resumeMode == AgentSessionResumeMode.Resume ? "--resume" : "--session-id");
        filtered.Add(sessionId.ToString("D"));
        return filtered.AsReadOnly();
    }

    private static bool ClaudeSessionArgConsumesValue(string arg) =>
        arg == "--session-id"
        || arg == "-s"
        || arg == "--resume"
        || arg == "-r";

    private static bool IsClaudeSessionArg(string arg) =>
        arg == "--session-id"
        || arg == "-s"
        || arg == "--resume"
        || arg == "-r"
        || arg == "--continue"
        || arg == "-c"
        || arg.StartsWith("--session-id=", StringComparison.Ordinal)
        || arg.StartsWith("--resume=", StringComparison.Ordinal)
        || arg.StartsWith("--continue=", StringComparison.Ordinal);

    private static async Task WaitForReadyOrThrowAsync(IAgentProtocolAdapter adapter, CancellationToken ct)
    {
        if (!await adapter.WaitForReadyAsync(ct))
            throw new InvalidOperationException("Agent process did not become ready.");
    }

    // Cap on how long we wait for each remote-control slash command to echo output before moving on.
    private static readonly TimeSpan RemoteControlCommandTimeout = TimeSpan.FromSeconds(3);

    // What the TUI prints once the remote-control bridge is genuinely connected.
    private const string RemoteControlArmedMarker = "remote-control is active";

    // When an agent is booted for remote monitoring, flip it into remote-control mode and THEN
    // rename it, before the work prompt lands. The order matters: claude.ai's session list only
    // picks up titles from /rename events that fire while the bridge is armed — titles set before
    // arming (--name at launch, or a pre-arm /rename) never sync, and the entry falls back to the
    // first message's text (verified live 2026-07-23: a post-arm /rename updated the claude.ai
    // entry immediately). The rename waits for the TUI's "remote-control is active" line, not
    // just for prompt echo: a 3s echo wait proved too short live — the rename fired before the
    // bridge finished connecting (title lost) and typing into the still-busy resume composer
    // jammed "/remote-control /rename <name>" into one submission that armed the bridge twice.
    //
    // BEST-EFFORT (CARD-0056). Remote control is MONITORING, not the session's purpose, so nothing
    // in here may fail a launch. On 2026-08-16 a 15-character "/remote-control" was typed into a
    // healthy, resuming orchestrator and never surfaced in its composer while the TUI re-rendered a
    // large resume history; VerifiedPromptSubmitter correctly reported no evidence, the launch was
    // declared failed, the process was left running (slice 1) and the supervisor started a second
    // orchestrator against that false signal. Every decision after the false signal was correct —
    // the signal was the defect. A delivery failure now costs an RcDegraded incident and the
    // session keeps running, un-monitored but alive.
    //
    // The /rename is skipped by construction when /remote-control fails: the exception leaves the
    // try block, so nothing appends to a composer that may still be holding the first body.
    //
    // Both commands go through SendBootPromptWithRetryAsync (CARD-0056 slice 3), so "fails" here
    // now means all three typings showed no composer evidence AND no transcript record confirms
    // the command ran. Degrading is what is left after retrying and after asking ground truth.
    private async Task SendRemoteControlCommandsAsync(
        IAgentProtocolAdapter adapter,
        string? remoteControlName,
        AgentSession session,
        Guid? agentId,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(remoteControlName))
            return;

        var sessionId = session.Id;
        if (!RemoteControlPolicy.Permits(session.AgentKind))
        {
            _logger.LogWarning(
                "{Message}",
                RemoteControlPolicy.IgnoredMessage(session.AgentKind, $"session {sessionId}"));
            return;
        }

        try
        {
            // Baseline BEFORE arming: a resumed TUI can redraw a previous run's "remote-control is
            // active" line, which must not satisfy the wait.
            var baseline = adapter.SnapshotRawOutput().Length;
            await SendBootPromptWithRetryAsync(adapter, "/remote-control", sessionId, ct);
            await adapter.WaitForFirstPromptOutputAsync(RemoteControlCommandTimeout, ct);
            if (!await WaitForRemoteControlArmedAsync(adapter, baseline, ct))
            {
                await RaiseRemoteControlDegradedAsync(
                    sessionId,
                    agentId,
                    $"Remote control did not report itself armed within {_settings.RemoteControlArmTimeoutMs}ms. "
                    + "The session is running; it may not be reachable from claude.ai, and its entry there "
                    + "may keep its first-message title. /rename was sent anyway.",
                    "RemoteControlNotArmed",
                    ct);
            }

            await SendBootPromptWithRetryAsync(adapter, $"/rename {remoteControlName.Trim()}", sessionId, ct);
            await adapter.WaitForFirstPromptOutputAsync(RemoteControlCommandTimeout, ct);
        }
        catch (PromptDeliveryException ex)
        {
            _logger.LogWarning(ex,
                "Remote-control setup could not be delivered to session {SessionId}; continuing the launch "
                + "without it", sessionId);
            await RaiseRemoteControlDegradedAsync(
                sessionId,
                agentId,
                $"Remote control could not be set up: {ex.Message} The session is running and usable, but it "
                + "is not reachable from claude.ai. A monitoring command's delivery must never fail a healthy "
                + "session (CARD-0056), so the launch continued.",
                "RemoteControlNotDelivered",
                ct);
        }
    }

    // Polls the raw output (append-only — the rendered screen can scroll the line away) for the
    // armed marker appearing AFTER the baseline. Returns whether it arrived. On timeout the boot
    // proceeds anyway: the local rename is still worth sending, and holding the launch hostage to a
    // claude.ai outage isn't — but the caller now records the miss as an incident rather than one
    // log line, so a silently un-monitored session is always visible (CARD-0056).
    private async Task<bool> WaitForRemoteControlArmedAsync(
        IAgentProtocolAdapter adapter, int baseline, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromMilliseconds(_settings.RemoteControlArmTimeoutMs);
        var searchFrom = Math.Max(0, baseline - RemoteControlArmedMarker.Length);
        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            var raw = adapter.SnapshotRawOutput();
            if (raw.IndexOf(RemoteControlArmedMarker, Math.Min(searchFrom, raw.Length), StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            await Task.Delay(250, ct);
        }

        _logger.LogWarning(
            "Remote-control armed marker did not appear within {TimeoutMs}ms; sending /rename anyway "
            + "(the claude.ai entry may keep its first-message title)", _settings.RemoteControlArmTimeoutMs);
        return false;
    }

    /// <summary>
    /// Records the degraded remote control as an <see cref="AgentIncidentKind.RcDegraded"/> incident
    /// (Warning, alert included — incidents are the supervisor's alerts 1:1). Best-effort in every
    /// direction: it runs in a scope of its own because <see cref="AgentSupervisorService"/> reaches
    /// back into this service through <see cref="AgentControlService"/>, and a failure to RECORD a
    /// degradation must never do what the degradation itself is no longer allowed to do — fail the
    /// launch. An incident needs an owning agent; a session nothing claims gets the log line only.
    /// </summary>
    /// <summary>
    /// CARD-0186 S2: KillAsync refused pane.close because a foreign process was in the pane; our
    /// child is gone and the pane needs tidying. Warning, never Critical.
    /// </summary>
    private async Task RaiseHerdrPaneLeftOpenAsync(Guid sessionId, CancellationToken ct)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            if (await db.AgentIncidents.AnyAsync(
                    i => i.SessionId == sessionId && i.Kind == AgentIncidentKind.HerdrPaneLeftOpen, ct))
                return;

            var owner = await db.Agents
                .Where(a => a.PersistentSessionId == sessionId.ToString("D"))
                .Select(a => (Guid?)a.Id)
                .FirstOrDefaultAsync(ct);
            if (owner is not Guid ownerId)
            {
                _logger.LogWarning(
                    "Herdr pane left open after kill for session {SessionId} but no agent claims it",
                    sessionId);
                return;
            }

            var supervisor = scope.ServiceProvider.GetRequiredService<AgentSupervisorService>();
            await supervisor.RecordIncidentAsync(
                ownerId,
                sessionId,
                AgentIncidentKind.HerdrPaneLeftOpen,
                AlertSeverity.Warning,
                "Herdr kill left the pane open: a foreign process was in the foreground. Our child was killed by pid; tidy the pane by hand.",
                failureReason: AgentExitReason.HerdrPaneLeftOpen.ToString(),
                ct: ct);
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex,
                "Recording HerdrPaneLeftOpen for session {SessionId} failed", sessionId);
        }
    }

    private async Task RaiseRemoteControlDegradedAsync(
        Guid sessionId, Guid? agentId, string message, string failureReason, CancellationToken ct)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var owner = agentId ?? await db.Agents
                .Where(a => a.PersistentSessionId == sessionId.ToString("D"))
                .Select(a => (Guid?)a.Id)
                .FirstOrDefaultAsync(ct);
            if (owner is not Guid ownerId)
            {
                _logger.LogWarning(
                    "Remote control is degraded for session {SessionId} ({FailureReason}) but no agent claims "
                    + "it, so there is nothing to hang an incident on: {Message}",
                    sessionId, failureReason, message);
                return;
            }

            // RecordIncidentAsync adds the row without saving; this scope's SaveChanges commits it.
            var supervisor = scope.ServiceProvider.GetRequiredService<AgentSupervisorService>();
            await supervisor.RecordIncidentAsync(
                ownerId,
                sessionId,
                AgentIncidentKind.RcDegraded,
                AlertSeverity.Warning,
                message,
                failureReason: failureReason,
                ct: ct);
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex,
                "Recording the degraded remote control for session {SessionId} failed", sessionId);
        }
    }

    // Delivers the launch note (bootstrap on fresh/effective-fresh, restart note on resume) through
    // the queue's verified path. Now-mode, NOT WhenIdle: the session just reached Running and is
    // idle by construction, but on the resume/fallback paths the reused session row can carry a
    // stale mid-turn transcript that makes IsWorkingAsync read true — a WhenIdle enqueue would skip
    // the idle fast-path, no turn-end is coming, and the stranded watchdog only covers always-on
    // agents. Failure falls back to a WhenIdle enqueue (watchdog / next turn-end recovery) and must
    // never fail the launch. No reply correlation is tracked — notes never route to a chat.
    private async Task DeliverLaunchNoteAsync(
        Guid sessionId, AgentSessionResumeMode? resumeMode, LaunchNotes? notes, CancellationToken ct)
    {
        if (notes is null)
            return;
        var body = resumeMode is null ? notes.FreshBody : notes.ResumeBody;
        if (string.IsNullOrWhiteSpace(body))
            return;

        try
        {
            await _messageQueue.EnqueueAsync(sessionId, body, MessageSendMode.Now, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex,
                "Launch note delivery failed for session {SessionId}; queueing for idle instead", sessionId);
            try
            {
                await _messageQueue.EnqueueAsync(
                    sessionId, body, MessageSendMode.WhenIdle, ct, origin: QueuedMessageOrigin.System);
            }
            catch (Exception fallbackEx) when (fallbackEx is not OperationCanceledException)
            {
                _logger.LogWarning(fallbackEx,
                    "Launch note fallback enqueue failed for session {SessionId}; giving up (note lost)", sessionId);
            }
        }
    }

    /// <summary>
    /// A relaunched session row can carry a transcript whose last turn never ended — the previous
    /// process died mid-turn (reboot, crash, kill) and no TurnEnd (nor interrupt marker) was ever
    /// written. Without intervention IsWorkingAsync reads true forever: the agent card badges
    /// "Working", WhenIdle deliveries strand, and the interrupted work silently never resumes
    /// (live miss 2026-08-08). The relaunch itself is proof nothing is running, so persist a
    /// SessionRestartBoundary — a turn END for the working rule — stating exactly that.
    /// Returns whether a boundary was written. Failures are logged, never fatal to the launch.
    /// </summary>
    private async Task<bool> WriteRestartBoundaryIfInterruptedAsync(Guid sessionId, CancellationToken ct)
    {
        try
        {
            if (!await SessionMessageQueueService.IsWorkingAsync(_db, sessionId, ct))
                return false;

            var now = UtcNow();
            var maxSeq = await _db.TranscriptEntries
                .Where(t => t.AgentSessionId == sessionId)
                .MaxAsync(t => (long?)t.Sequence, ct) ?? 0;
            _db.TranscriptEntries.Add(new TranscriptEntry
            {
                Id = Guid.NewGuid(),
                AgentSessionId = sessionId,
                Sequence = maxSeq + 1,
                Kind = TranscriptKinds.SessionRestartBoundary,
                // Synthetic uuid so the (uuid, kind) dedup in PersistTranscriptAsync can never
                // collide it with a real JSONL line.
                Uuid = Guid.NewGuid().ToString("D"),
                Timestamp = now,
                Role = "system",
                Text = "Session relaunched; the previous turn had been interrupted mid-flight.",
                CreatedAt = now,
            });
            await _db.SaveChangesAsync(ct);

            _logger.LogInformation(
                "Session {SessionId} relaunched with a mid-turn transcript; wrote a restart boundary so it reads idle",
                sessionId);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Restart-boundary check failed for session {SessionId}", sessionId);
            return false;
        }
    }

    // Queues the interrupted-turn continue prompt (see AgentSessionSettings.ResumeAutoContinue).
    // WhenIdle: the restart boundary already makes the session read idle, so this flushes
    // immediately when nothing else is talking — and waits its turn when a launch note is.
    private async Task EnqueueResumeContinueAsync(Guid sessionId, CancellationToken ct)
    {
        if (!_settings.ResumeAutoContinue || string.IsNullOrWhiteSpace(_settings.ResumeContinuePrompt))
            return;

        try
        {
            await _messageQueue.EnqueueAsync(
                sessionId, _settings.ResumeContinuePrompt, MessageSendMode.WhenIdle, ct,
                origin: QueuedMessageOrigin.System);
            _logger.LogInformation(
                "Session {SessionId} resumed an interrupted turn; queued the auto-continue prompt", sessionId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Auto-continue enqueue failed for session {SessionId}", sessionId);
        }
    }

    private static bool IsClaudeSessionNotFound(IAgentProtocolAdapter? adapter, Exception ex)
    {
        if (ex.Message.Contains(ClaudeSessionNotFoundNeedle, StringComparison.OrdinalIgnoreCase))
            return true;

        if (adapter is null)
            return false;

        try
        {
            return adapter.SnapshotRawOutput().Contains(ClaudeSessionNotFoundNeedle, StringComparison.OrdinalIgnoreCase);
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private async Task<(Worktree Worktree, bool Created)> ResolveOrCreateWorktreeAsync(
        Card card,
        RunAttempt attempt,
        CancellationToken ct)
    {
        var now = UtcNow();
        if (card.CurrentWorktree is { Status: WorktreeStatus.Active } existing
            && Directory.Exists(existing.Path))
        {
            existing.LastTouchedAt = now;
            attempt.WorktreeId = existing.Id;
            attempt.Worktree = existing;
            await _db.SaveChangesAsync(ct);
            try
            {
                await _worktreeManager.TouchAsync(existing.Path, ct);
            }
            catch (Exception ex) when (ex is NotFoundException or ValidationException)
            {
                // Older rows may predate sidecar metadata or the current worktree root. DB timestamp is still updated.
                _logger.LogDebug(ex, "Skipping optional touch for existing worktree {WorktreePath}", existing.Path);
            }
            return (existing, Created: false);
        }

        var repoPath = await ResolveRepoPathAsync(card.Board.Project, ct);
        WorktreeInfo worktreeInfo;
        try
        {
            worktreeInfo = await _worktreeManager.CreateAsync(
                repoPath,
                card.Identifier,
                card.Board.Project.BaseBranch,
                ct);
        }
        catch (ConflictException)
        {
            var orphanedWorktree = (await _worktreeManager.ListAsync(repoPath, ct))
                .FirstOrDefault(w => w.CardId == card.Identifier && Directory.Exists(w.Path));
            if (orphanedWorktree is null)
                throw;

            worktreeInfo = orphanedWorktree;
        }

        var worktree = new Worktree
        {
            Id = Guid.NewGuid(),
            CardId = card.Id,
            RepoPath = worktreeInfo.RepoPath,
            Path = worktreeInfo.Path,
            Branch = worktreeInfo.Branch,
            BaseRef = worktreeInfo.BaseRef,
            Status = WorktreeStatus.Active,
            CreatedAt = worktreeInfo.CreatedAt.UtcDateTime,
            LastTouchedAt = worktreeInfo.LastTouchedAt.UtcDateTime,
            Card = card
        };
        _db.Worktrees.Add(worktree);
        card.CurrentWorktreeId = worktree.Id;
        card.ConcurrencyToken = Guid.NewGuid();
        card.UpdatedAt = now;
        attempt.WorktreeId = worktree.Id;
        attempt.Worktree = worktree;
        await _db.SaveChangesAsync(ct);
        return (worktree, Created: true);
    }

    private async Task<int> NextAttemptNumberAsync(Guid cardId, CancellationToken ct)
    {
        var last = await _db.RunAttempts
            .Where(a => a.CardId == cardId)
            .MaxAsync(a => (int?)a.AttemptNumber, ct);
        return (last ?? 0) + 1;
    }

    private static Task<string> ResolveRepoPathAsync(Project project, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(project.LocalRepositoryPath))
            throw new ValidationException(nameof(Project.LocalRepositoryPath), "Project local repository path is required to start an agent session.");

        return Task.FromResult(project.LocalRepositoryPath);
    }

    private static string BuildLaunchPrompt(
        StartAgentSessionRequest request,
        Card card,
        Worktree worktree,
        BoardWorkflowDefinition? activeDefinition)
    {
        if (!request.UseWorkflowPrompt
            || activeDefinition is null
            || !WorkflowDefinitionLoader.TryParseContent(activeDefinition.Content, out var definition, out _)
            || definition is null)
        {
            return request.Prompt;
        }

        return WorkflowDefinitionLoader.RenderPrompt(
            definition.PromptMarkdown,
            WorkflowDefinitionLoader.BuildPromptVariables(card, worktree));
    }

    private static WorkflowHooks ParseHooks(BoardWorkflowDefinition? activeDefinition)
    {
        if (activeDefinition is null
            || string.IsNullOrWhiteSpace(activeDefinition.Content)
            || !activeDefinition.Content.Contains("hooks:", StringComparison.Ordinal))
        {
            return WorkflowHooks.Empty;
        }

        if (WorkflowDefinitionLoader.TryParseContent(activeDefinition.Content, out var definition, out _) && definition is not null)
            return definition.Hooks;

        if (activeDefinition.Content.Contains("stages:", StringComparison.Ordinal))
            return WorkflowDefinitionParser.ParseYamlDefinition(activeDefinition.Content).Hooks;

        return WorkflowDefinitionParser.ParseYamlHooks(activeDefinition.Content);
    }

    private DateTime UtcNow() => _timeProvider.GetUtcNow().UtcDateTime;

    private async Task<bool> TryMarkMemoryKilledAsync(
        AgentSession session,
        RunAttempt attempt,
        IAgentProtocolAdapter adapter)
    {
        if (adapter.ExitReason != AgentExitReason.MemoryKilled)
            return false;

        if (!RunAttemptStateMachine.IsTerminal(attempt.Phase))
            RunAttemptStateMachine.Transition(attempt, RunPhase.Failed, UtcNow());

        session.Status = SessionStatus.Failed;
        session.EndedAt = UtcNow();
        session.LastSeenAt = session.EndedAt.Value;
        session.FailureReason = MemoryKilledFailureReason;
        if (adapter.Exited.IsCompletedSuccessfully)
            session.ExitCode = adapter.Exited.Result;
        attempt.ExitCode = session.ExitCode;
        attempt.ErrorDetails = MemoryKilledFailureReason;
        await adapter.DisposeAsync();
        return true;
    }

    private async Task ResizeAndPersistAsync(Guid sessionId, int cols, int rows, CancellationToken ct)
    {
        await _runtime.ResizeAsync(sessionId, cols, rows, ct);

        var session = await _db.AgentSessions.FirstOrDefaultAsync(s => s.Id == sessionId, ct)
            ?? throw new NotFoundException(nameof(AgentSession), sessionId);
        session.Cols = cols;
        session.Rows = rows;
        session.LastSeenAt = UtcNow();
        await _db.SaveChangesAsync(ct);
    }
}
