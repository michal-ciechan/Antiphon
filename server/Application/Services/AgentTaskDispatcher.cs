using Antiphon.Agents.Pty;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
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
    private readonly PtyDeliveryProfile? _ptyProfile;
    private readonly AgentTaskReplyService? _replies;
    private readonly AgentTaskCheckQueue? _checkQueue;
    private readonly ISessionRunnerClient? _runnerClient;
    private readonly DeadSessionFirstSeenState? _deadSessions;
    private readonly BootWedgeRelaunchState? _bootWedgePending;
    private readonly DelegateBindRefusalRecovery? _bindRefusalRecovery;
    private readonly AgentSessionRuntime? _runtime;
    // CARD-0106 S2. Optional like everything else here; absent, a delegate whose agent env carries a
    // placeholder fails at the launch tripwire by name rather than exporting the literal token.
    private readonly ApiKeyEnvResolver? _apiKeyEnvResolver;
    private readonly IServiceScopeFactory? _scopeFactory;
    // CARD-0140 S2. Optional so every harness that predates this card stays on the registry path.
    // Production wires it (S3); a missing registration here is not a launch of the wrong program,
    // it is today's registry resolve.
    private readonly AgentTuiLaunchResolver? _launchResolver;
    // CARD-0117 D7: parked-at-MaxDeliveryAttempts wording. Optional so predating harnesses keep
    // the shipped default of 3.
    private readonly int _maxDeliveryAttempts;
    // CARD-0153 S2. Optional so S1 harnesses keep constructing this; absent, the transcript arm
    // stands alone.
    private readonly AgentFilesService? _files;
    // CARD-0136. EvaluateAsync only — never EnforceAsync. A refusal here would silently
    // fail a task with no caller present.
    private readonly SubscriptionQuotaGate? _quotaGate;
    // CARD-0063 S2: the repo's named areas. Optional so every harness that predates the map keeps
    // constructing this; absent, a scope name compares as an opaque label, which is exactly S1.
    private readonly AreaMapLoader? _areas;
    // CARD-0248: in-memory sweep watermark. Optional so predating harnesses re-hand every tick
    // (equivalent to ReportSweepRehandSeconds = 0). Production registers the singleton.
    private readonly DeferredReportSweepMarks? _sweepMarks;
    // CARD-0022. Optional so predating harnesses keep constructing this; absent, no model
    // hold is consulted and queued work dispatches the way it does today.
    private readonly ModelAvailability? _modelAvailability;
    // CARD-0305. Same optional contract. Only the NotBefore clock is read here: the kind and tier
    // were snapshotted onto the row at create, and re-resolving a pin at spawn would silently
    // rewrite work that was already authorised.
    private readonly RoutingPinService? _routingPins;

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
        ILogger<AgentTaskDispatcher> logger,
        PtyDeliveryProfile? ptyProfile = null,
        // Optional so every harness that predates CARD-0046 keeps constructing this; where it is
        // absent the deferred-settlement sweep is simply not armed.
        AgentTaskReplyService? replies = null,
        // Same contract for CARD-0047: no queue registered means no check sweep, so a harness that
        // predates it keeps working and never claims a check it has no worker to run.
        AgentTaskCheckQueue? checkQueue = null,
        // And for CARD-0021's dead-session sweep, which needs BOTH: the runner is the evidence that
        // a session really is gone, and the state is the grace clock. Either missing leaves the
        // sweep unarmed — it must never fail a task on the DB row alone (see
        // FailDeadSessionTasksAsync), so an absent runner client disarms rather than degrades.
        ISessionRunnerClient? runnerClient = null,
        DeadSessionFirstSeenState? deadSessions = null,
        // CARD-0085: optional so predating harnesses keep today's Failed on an empty transcript
        // table. Recovery also needs _replies (it owns settlement); either missing skips the gate.
        DelegateBindRefusalRecovery? bindRefusalRecovery = null,
        // CARD-0020: the fetch-and-persist half of the transcript sync. Every sweep here that is
        // about to make an IRREVERSIBLE decision on "the transcript does not contain X" pulls the
        // runner's own view first — the live stream is not a reliable clock, and the kill that
        // proved it wrong is what produced the records (CARD-0055, session e809ce65). Optional for
        // the same reason as the rest: a harness without it falls back to whatever streamed, which
        // is exactly today's behaviour, and the pull swallows its own failures anyway.
        AgentSessionRuntime? runtime = null,
        ApiKeyEnvResolver? apiKeyEnvResolver = null,
        IServiceScopeFactory? scopeFactory = null,
        AgentTuiLaunchResolver? launchResolver = null,
        IOptions<SupervisionSettings>? supervision = null,
        AgentFilesService? files = null,
        SubscriptionQuotaGate? quotaGate = null,
        AreaMapLoader? areas = null,
        DeferredReportSweepMarks? sweepMarks = null,
        ModelAvailability? modelAvailability = null,
        BootWedgeRelaunchState? bootWedgePending = null,
        RoutingPinService? routingPins = null)
    {
        _routingPins = routingPins;
        _bootWedgePending = bootWedgePending;
        _sweepMarks = sweepMarks;
        _areas = areas;
        _modelAvailability = modelAvailability;
        _apiKeyEnvResolver = apiKeyEnvResolver;
        _scopeFactory = scopeFactory;
        _launchResolver = launchResolver;
        _maxDeliveryAttempts = Math.Max(1, supervision?.Value.DeliveryVerification.MaxDeliveryAttempts ?? 3);
        _files = files;
        _runtime = runtime;
        _runnerClient = runnerClient;
        _deadSessions = deadSessions;
        _checkQueue = checkQueue;
        _replies = replies;
        _bindRefusalRecovery = bindRefusalRecovery;
        _ptyProfile = ptyProfile;
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
        _quotaGate = quotaGate;
    }

    /// <summary>
    /// Whether the CARD-0055 pull-before-you-judge round trip is actually available (CARD-0020).
    /// The runtime is an OPTIONAL constructor dependency so that every harness predating it keeps
    /// constructing this — which means a missing DI registration in production would not fail, it
    /// would silently leave two irreversible sweeps judging the streamed rows alone, forever, with
    /// no symptom. Exists so that state is assertable rather than invisible.
    /// </summary>
    internal bool TranscriptPullArmed => _runtime is not null;

    /// <param name="SweepFailures">
    /// How many of the tick's clocks threw. Non-zero means the tick ran DEGRADED — the failed
    /// sweep did nothing this time round — and each failure is logged at Error by
    /// <see cref="RunSweepAsync"/> naming which one it was.
    /// </param>
    public sealed record TickResult(
        int Eligible, int Dispatched, int SkippedConcurrency, int SkippedScope, int Failures,
        int SweepFailures = 0,
        int SkippedModelAvailability = 0,
        /// <summary>
        /// CARD-0305: queued rows whose routing pin is not due yet. A separate counter from
        /// <see cref="SkippedModelAvailability"/> on purpose — a dated pin and a fleet hold are
        /// two different clocks and conflating them hides which one the work is waiting on.
        /// </summary>
        int SkippedRoutingPin = 0);

    public async Task<TickResult> TickAsync(CancellationToken ct)
    {
        if (!_settings.Enabled)
            return new TickResult(0, 0, 0, 0, 0);

        // The clocks below are INDEPENDENT and each runs isolated (see RunSweepAsync). They
        // used to be five bare awaits, which quietly made every one of them a single point of
        // failure for all the others AND for dispatching: one poisoned session in the settlement
        // sweep would abort the tick before the check sweep and the dispatch loop had run, on every
        // tick, and the only trace was one "Delegation dispatch tick failed" line that named
        // neither which clock had died nor what had stopped as a result.
        var sweepFailures = 0;

        // CARD-0302: Check-role Blocked rows with a reading are stale evidence, not questions.
        // Remap them before anything else so the attention feed and notifier see Succeeded.
        sweepFailures += await RunSweepAsync(
            "remap blocked check interpretations",
            ct2 => AgentTaskCheckService.RemapBlockedInterpretationsAsync(_db, _timeProvider, ct2),
            ct);

        // Before dispatching new work, deal with running work that has gone quiet — IF any role
        // still carries both EscalateTo and EscalateAfterMinutes. Shipped defaults disarm the
        // auto-trigger (CARD-0158); the sweep short-circuits to zero queries when nothing is armed.
        // Manual /escalate still uses EscalateTo alone via AgentTaskService.ResolveEscalationTarget.
        sweepFailures += await RunSweepAsync("auto-escalate stalled", AutoEscalateStalledAsync, ct);

        // And with work that never STARTED — no turn prompt after dispatch means the boot prompt
        // was lost, which is categorically different from slow progress and must fail loudly,
        // never escalate (a bigger model can't fix an undelivered brief).
        sweepFailures += await RunSweepAsync("delivery watchdog", FailNeverStartedAsync, ct);

        // And with work whose SESSION died under it (CARD-0021). Distinct from the watchdog above,
        // which only ever asks whether a Dispatched task started: a Working task is outside its
        // query altogether, and a task whose session wrote a transcript and then died passes its
        // "did it start" test forever. Three zombies sat open for hours on 2026-08-09 that way.
        sweepFailures += await RunSweepAsync("dead-session reconciler", FailDeadSessionTasksAsync, ct);

        // And with work that started, never stopped, and has now run past a deadline (CARD-0020).
        // The three clocks above all ask a question about DELIVERY or LIVENESS; a task whose brief
        // landed, whose session is alive and which is simply never going to finish answered every
        // one of them for as long as it ran. RolePolicyEntry.TimeoutMinutes was declared for this
        // and read nowhere, so until now there was no deadline on a working task at all.
        sweepFailures += await RunSweepAsync("overdue-task deadline", FailOverdueTasksAsync, ct);

        // And with work that is still writing rows but none of them is new (CARD-0153). After the
        // overdue deadline on purpose: a task that clock is about to fail does not need a stall
        // row on top. Detection only — this sweep never fails, kills, or escalates.
        sweepFailures += await RunSweepAsync("progress stall", DetectStalledProgressAsync, ct);

        // And with warm delegates that have sat idle too long — the pool trades memory for
        // startup latency, and the janitor is what keeps that trade bounded.
        sweepFailures += await RunSweepAsync("retire idle warm agents", RetireIdleWarmAgentsAsync, ct);

        // And with settlements deferred waiting for a turn-ending response's own text (CARD-0046).
        // Nothing re-triggers a response that never writes text, so the grace needs a clock, and
        // this is the one that already runs on a 5 s cadence before the early return below.
        sweepFailures += await RunSweepAsync("settle deferred reports", SettleDeferredReportsAsync, ct);

        // And with running work that is DUE A LOOK (CARD-0047). This is where every other
        // "running-work-gone-quiet" clock already lives, and check-due times have minute
        // granularity, so a 5 s cadence is two orders of magnitude finer than needed. It CLAIMS AND
        // HANDS OFF only — see RunScheduledChecksAsync for why the tick must never run a check.
        sweepFailures += await RunSweepAsync("scheduled checks", RunScheduledChecksAsync, ct);

        // And with a task that Failed before it was ever dispatched, whose caller has not yet
        // heard (CARD-0231). Same NextCheckAt ramp as a check, but it only re-sends the failure
        // note — nothing to probe, no interpreter, no session.
        sweepFailures += await RunSweepAsync("unacknowledged failure reminders", RemindUnacknowledgedFailuresAsync, ct);

        // And with check notes still Pending whose task has since settled (CARD-0074). The
        // interpreter window is closed in RunCheckAsync; this is the queue window — WhenIdle
        // notes that sat while the task finished. Mark, never suppress; the amend goes through
        // the queue's per-session lock so it cannot race a flush mid-type.
        sweepFailures += await RunSweepAsync("superseded check notes", ReconcileSupersededChecksAsync, ct);

        // The cap bounds concurrent Claude PROCESSES, so interpretation tasks are outside it both
        // ways — they neither consume a slot nor wait for one. A Check task is pinned to the
        // standing interpreter and delivered into a session that is already running, so it spawns
        // nothing; counting it would let a system at the cap starve every interpretation and
        // silently degrade all checks exactly when the operator most wants eyes on the fleet.
        // Their own backlog is bounded separately, on the interpreter (CARD-0047 §1.3).
        var active = await _db.AgentTasks.CountAsync(
            t => t.Role != AgentTaskRole.Check
                && (t.Status == AgentTaskStatus.Dispatched || t.Status == AgentTaskStatus.Working), ct);

        var queued = await _db.AgentTasks
            .Where(t => t.Status == AgentTaskStatus.Queued)
            .OrderBy(t => t.CreatedAt)
            .ToListAsync(ct);
        if (queued.Count == 0)
            return new TickResult(0, 0, 0, 0, 0, sweepFailures);

        // Tasks that declare overlapping file scopes must not run concurrently — the second waits
        // rather than racing on read-modify-write. This is the cost of Shared being the default,
        // and the mitigation for it.
        //
        // ReadOnly is outside the lease in BOTH directions (CARD-0063 §2.3): it writes nothing, so
        // it can neither corrupt a writer nor be corrupted by one, and holding it only ever cost a
        // wait for no reason. The key is the REPO, not the declared working directory — a task
        // dispatched with `-Dir <repo>/client` used to compare with nothing.
        //
        // Unscoped rows are loaded too, because D3's SerialiseSharedWriters says a second
        // write-capable task in one shared checkout is a collision REGARDLESS of scope. Check-role
        // tasks are outside the lease entirely: they are pinned to the standing interpreter, spawn
        // nothing, and write nothing.
        var busyScopes = await _db.AgentTasks
            .Where(t => (t.Status == AgentTaskStatus.Dispatched || t.Status == AgentTaskStatus.Working)
                && t.Workspace != WorkspaceMode.ReadOnly
                && t.Role != AgentTaskRole.Check)
            .Select(t => new
            {
                t.Id, t.Title, t.WorkingDirectory, t.RepoPath, t.Scope, t.Workspace, t.WorktreeBranch,
            })
            .ToListAsync(ct);
        var heldScopes = busyScopes
            .Select(s => SharedWriterLeaseProjection.Holder.From(
                s.Id,
                s.Title,
                s.RepoPath,
                s.WorkingDirectory,
                s.Scope,
                s.Workspace,
                s.WorktreeBranch,
                // Resolved against the HOLDER's own repo map: two tasks only ever compare when
                // they share a repo, so both sides always read the same antiphon.areas.json.
                AreasFor(s.RepoPath)))
            .ToList();

        // A hold leaves a trace exactly once. Before CARD-0063 it left none at all: the single
        // 579-second hold in 623 tasks was indistinguishable, from the board, from a tick that had
        // not yet reached the task. Loaded once per tick so a re-hold on the next tick is silent.
        var queuedIds = queued.Select(t => t.Id).ToList();
        var everHeld = (await _db.AgentTaskEvents
                .Where(e => queuedIds.Contains(e.AgentTaskId) && e.Type == AgentTaskEventType.Held)
                .Select(e => e.AgentTaskId)
                .Distinct()
                .ToListAsync(ct))
            .ToHashSet();

        var dispatched = 0;
        // Only process-spawning dispatches count against the cap — see the `active` query above.
        var dispatchedAgainstCap = 0;
        var skippedConcurrency = 0;
        var skippedScope = 0;
        var skippedModelAvailability = 0;
        var skippedRoutingPin = 0;
        var failures = 0;

        foreach (var task in queued)
        {
            ct.ThrowIfCancellationRequested();

            if (task.Role != AgentTaskRole.Check
                && active + dispatchedAgainstCap >= _settings.MaxConcurrentTasks)
            {
                skippedConcurrency++;
                continue;
            }

            var taskKey = ScopeResolver.KeyFor(task.RepoPath, task.WorkingDirectory);
            var inLease = SharedWriterLeaseProjection.Participates(task.Workspace, task.Role);
            var taskScope = inLease
                ? ScopeResolver.Resolve(task.Scope, AreasFor(task.RepoPath))
                : ResolvedScope.Empty;

            // The pair decides, not the area (2.3). Only Shared-to-Shared waits; a worktree on
            // either side dispatches and the caller is TOLD, because a worktree collision costs a
            // rebase at merge and blocking it throws away the parallelism worktrees exist to give.
            SharedWriterLeaseProjection.Overlap? blocking = null;
            IReadOnlyList<SharedWriterLeaseProjection.Overlap>? warned = null;
            if (inLease)
            {
                var decision = SharedWriterLeaseProjection.Decide(
                    heldScopes, taskKey, task.Workspace, taskScope, _settings.SerialiseSharedWriters);
                blocking = decision.Blocking;
                warned = decision.Warnings.Count == 0 ? null : decision.Warnings;
            }

            if (blocking is { } held)
            {
                skippedScope++;
                if (everHeld.Add(task.Id))
                {
                    _db.AgentTaskEvents.Add(new AgentTaskEvent
                    {
                        Id = Guid.NewGuid(),
                        AgentTaskId = task.Id,
                        Type = AgentTaskEventType.Held,
                        Detail = held.Describe(task),
                        At = UtcNow(),
                    });
                    await _db.SaveChangesAsync(ct);
                    // Information, and only on the transition into held - a per-tick line would
                    // write twelve an hour per waiting task and say nothing new after the first.
                    _logger.LogInformation(
                        "Task {ShortId} held behind running task {HolderShortId}: {Detail}",
                        DelegationReportFormatter.Short(task.Id),
                        DelegationReportFormatter.Short(held.Holder.TaskId), held.Describe(task));
                }

                continue;
            }

            // CARD-0305: a DATED pin holds its work until the operator's instant. Create returned
            // 200 Queued for exactly this — the pin is why the work exists — so the wait belongs
            // here, ahead of the fleet-hold skip below. Two clocks, two counters, two sentences:
            // "not before" is this card's, "is held" is CARD-0022/0309's.
            if (_routingPins is not null && task.Role != AgentTaskRole.Check)
            {
                var pin = await _routingPins.FindActiveAsync(task.CardId, task.Role, ct)
                    ?? await _routingPins.FindActiveAsync(null, task.Role, ct);
                if (pin?.NotBefore is { } notBefore && notBefore > UtcNow())
                {
                    skippedRoutingPin++;
                    if (everHeld.Add(task.Id))
                    {
                        _db.AgentTaskEvents.Add(new AgentTaskEvent
                        {
                            Id = Guid.NewGuid(),
                            AgentTaskId = task.Id,
                            Type = AgentTaskEventType.Held,
                            Detail =
                                $"routing pin not before {notBefore:yyyy-MM-ddTHH:mm:ssZ}; dispatch "
                                + $"paused ({pin.Reason}).",
                            At = UtcNow(),
                        });
                        await _db.SaveChangesAsync(ct);
                        _logger.LogInformation(
                            "Task {ShortId} held: routing pin not before {NotBefore}",
                            DelegationReportFormatter.Short(task.Id), notBefore);
                    }

                    continue;
                }
            }

            // CARD-0022: a held model must not spawn. Check-role tasks skip only if the
            // interpreter's own alias is held — a fable hold does not starve haiku checks.
            if (_modelAvailability is not null)
            {
                var alias = await ResolveDispatchAliasAsync(task, ct);
                if (await _modelAvailability.IsHeldAsync(task.AgentKind, alias, ct))
                {
                    skippedModelAvailability++;
                    if (everHeld.Add(task.Id))
                    {
                        _db.AgentTaskEvents.Add(new AgentTaskEvent
                        {
                            Id = Guid.NewGuid(),
                            AgentTaskId = task.Id,
                            Type = AgentTaskEventType.Held,
                            Detail = $"{alias} is held; dispatch paused for that model.",
                            At = UtcNow(),
                        });
                        await _db.SaveChangesAsync(ct);
                        _logger.LogInformation(
                            "Task {ShortId} held: model {Alias} is unavailable",
                            DelegationReportFormatter.Short(task.Id), alias);
                    }

                    continue;
                }
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
                    if (task.Role != AgentTaskRole.Check)
                        dispatchedAgainstCap++;
                    if (inLease)
                    {
                        heldScopes.Add(new SharedWriterLeaseProjection.Holder(
                            task.Id, task.Title, taskKey, taskScope, task.Workspace, task.WorktreeBranch));
                    }

                    // Dispatched anyway, but the caller owes somebody a rebase - say which task and
                    // which branch. Written after the dispatch so a launch that throws leaves no
                    // warning about work that never started.
                    if (warned is not null)
                    {
                        var warnedAt = UtcNow();
                        foreach (var overlap in warned)
                        {
                            _db.AgentTaskEvents.Add(new AgentTaskEvent
                            {
                                Id = Guid.NewGuid(),
                                AgentTaskId = task.Id,
                                Type = AgentTaskEventType.Warning,
                                Detail = overlap.Describe(task),
                                At = warnedAt,
                            });
                        }

                        await _db.SaveChangesAsync(ct);
                    }

                    // The other half of the transition: a task that was held and now runs.
                    if (everHeld.Contains(task.Id))
                    {
                        _logger.LogInformation(
                            "Task {ShortId} released: its scope '{Scope}' no longer intersects a running task",
                            DelegationReportFormatter.Short(task.Id), task.Scope);
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                _logger.LogWarning(ex, "Failed to dispatch task {ShortId}",
                    DelegationReportFormatter.Short(task.Id));
                await FailAndNotifyAsync(
                    task,
                    $"Dispatch failed before a session existed: {ex.Message}",
                    "dispatch",
                    ct);
                failures++;
            }
        }

        return new TickResult(
            queued.Count, dispatched, skippedConcurrency, skippedScope, failures, sweepFailures,
            skippedModelAvailability, skippedRoutingPin);
    }

    /// <summary>
    /// Run one of the tick's clocks so that its failure costs ONLY itself.
    ///
    /// <para>Every sweep here scans the whole table, so any one of them can meet a row, a session or
    /// a downstream service that throws — and the condition is usually persistent, so it repeats on
    /// every 5 s tick. Bare-awaited, that made the tick a chain: a throw in the second sweep meant
    /// the third, fourth and fifth never ran and nothing was dispatched, indefinitely, and the only
    /// evidence was a "Delegation dispatch tick failed" warning that named neither the dead clock
    /// nor its casualties. A check-in that silently stopped firing because the settlement sweep met
    /// a bad session is exactly the failure this shape prevents.</para>
    ///
    /// <para>Error, not Warning: a clock that is not running is not a transient hiccup, and it is
    /// also counted into <see cref="TickResult.SweepFailures"/> so the caller can see the tick ran
    /// degraded without reading the log.</para>
    ///
    /// <para>A cancellation that is OUR cancellation is shutdown and rethrown. Every other
    /// <see cref="OperationCanceledException"/> is a timeout wearing the same type (an HttpClient
    /// timeout is a TaskCanceledException) and is a transient failure like any other.</para>
    /// </summary>
    private async Task<int> RunSweepAsync(
        string name, Func<CancellationToken, Task<int>> sweep, CancellationToken ct)
    {
        try
        {
            await sweep(ct);
            return 0;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogError(
                ex, "Delegation sweep '{Sweep}' failed and did nothing this tick; "
                + "the remaining sweeps and dispatching continue", name);
            return 1;
        }
    }

    /// <summary>
    /// The automatic rung of the ladder: a task whose role policy names BOTH EscalateTo and
    /// EscalateAfterMinutes, still running past that window with NO transcript progress, is
    /// stopped and requeued one tier up. Progress resets the clock — a task that keeps producing
    /// output is thinking, not stalled, however long it takes.
    ///
    /// <para><b>Ships disarmed (CARD-0158).</b> No role's default carries both knobs, so this
    /// method returns 0 before its first query. Debug keeps <c>EscalateTo = Frontier</c> for the
    /// manual ladder; an operator who wants the old 25-minute kill-and-requeue re-arms it with
    /// one appsettings key. Do not re-arm lightly: the only two historical firings (2026-08-11)
    /// were idle-after-done, now owned earlier by the delivery watchdog's uncorrelated arm, and
    /// 25 minutes sits inside the measured healthy local-execution window.</para>
    /// </summary>
    internal async Task<int> AutoEscalateStalledAsync(CancellationToken ct)
    {
        // Which roles can auto-escalate at all is config; don't scan tasks that have nowhere to go.
        // EscalateTo alone (Debug, Test) is the manual ladder — both knobs required to arm the clock.
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
                    + $"at {ModelLevelAliases.For(task.AgentKind, task.ModelLevel)}.";
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
    /// <item>The brief never arrived — no non-housekeeping typed or queued prompt after this
    /// task's <c>DispatchedAt</c>. On a fresh session that is "zero transcript entries" (four tasks sat
    /// like that for up to 26 minutes on 2026-08-09). On a reused warm-pool session the inherited
    /// history made the old "any entry at all" test always true, so this branch was unreachable
    /// even when the NEW brief never landed (CARD-0077). The predicate is
    /// <see cref="TranscriptPromptSpan"/>, the same one settlement already uses.</item>
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

            // PULL BEFORE YOU JUDGE (CARD-0055, plan section 6.1). Everything below decides on "the
            // transcript does not contain X" and then FAILS the task and KILLS the session — the
            // most irreversible pair in this file — and the live stream is not a reliable clock:
            // on session e809ce65 six records landed in one burst at the instant of the kill,
            // because the flush is triggered by the session ENDING, so the kill produced the
            // evidence that the kill was wrong. This costs one runner round trip per suspect, ten
            // minutes after dispatch, on a query that returns nothing on a healthy day.
            await CatchUpTranscriptAsync(sessionId, ct);

            // A non-housekeeping typed or queued prompt after THIS task's dispatch is the proof
            // the brief landed. "Any transcript entry at all" is the wrong test on a reused
            // session: it inherited the previous task's history, so this branch was unreachable
            // (CARD-0077). Slow work with a real prompt still belongs to the stall scan, not here.
            // HasTurnPromptSinceAsync stays for any other caller; this arm takes the Result so
            // a queued-only hit can be named in the log (CARD-0135 D6).
            var span = await TranscriptPromptSpan.LoadAsync(
                _db, sessionId, task.DispatchedAt, ct);
            var started = span.TurnPrompts.Count > 0;
            if (started
                && span.TurnPrompts.All(p => p.Kind == TranscriptKinds.QueuedUserPrompt))
            {
                _logger.LogInformation(
                    "Task {ShortId}: delivery watchdog sees queued-only turn evidence on session {SessionId} "
                    + "({Count} QueuedUserPrompt row(s) after dispatch); withholding the never-started failure",
                    DelegationReportFormatter.Short(task.Id), sessionId, span.TurnPrompts.Count);
            }

            // CARD-0117 D7: the brief's own queue row outranks the transcript. A Pending brief is
            // direct, positive evidence about THIS task's own message; `started` is evidence about
            // some prompt, and the uncorrelated incident is evidence about some turn.
            var marker = DelegationReportFormatter.TaskMarker(task.Id);
            var briefRow = await _db.SessionQueuedMessages
                .AsNoTracking()
                .Where(m => m.AgentSessionId == sessionId
                    && m.Origin == QueuedMessageOrigin.Delegation
                    && (task.DispatchedAt == null || m.CreatedAt >= task.DispatchedAt)
                    && m.Body.Contains(marker))
                .OrderBy(m => m.Sequence)
                .Select(m => new { m.Status, m.DeliveryAttempts })
                .FirstOrDefaultAsync(ct);
            var briefNeverTyped = briefRow is { Status: QueuedMessageStatus.Pending };

            // CARD-0117 D8: decline to judge a provably-untyped brief on a provably-working session.
            // The queue row is a different subsystem from the transcript, so this is not the pair
            // CARD-0055 forbids. Every later tick re-evaluates: Sent → the normal arms; idle with
            // the brief still Pending → arm 1 below; never-stops → TaskDeadlinePolicy (no kill).
            if (briefNeverTyped && await SessionMessageQueueService.IsWorkingAsync(_db, sessionId, ct))
            {
                _logger.LogInformation(
                    "Task {ShortId}: delivery watchdog deferring — the brief is still queued Pending "
                    + "and session {SessionId} is working; the clock passes to TaskDeadlinePolicy",
                    DelegationReportFormatter.Short(task.Id), sessionId);
                continue;
            }

            string reason;
            var withholdKill = false;
            if (!started || briefNeverTyped)
            {
                // CARD-0085: an empty TranscriptEntries table is not evidence the work did not
                // happen. Pull git / later-JSONL evidence before writing Failed (and killing).
                // After CARD-0077 this also covers a reused session whose only new records are
                // the compact's housekeeping — the work may still have landed unbound.
                // Only when `started` is false: a Pending brief with real prompts since dispatch
                // is a delivery miss, not a bind refusal.
                if (!started && await TryRecoverBindRefusalAsync(task, sessionId, ct))
                    continue;

                // CARD-0112: an explicit capability omission is positive evidence that this runner
                // cannot observe the task's transcript. Fail the uncorrelatable task, but do not
                // kill a session that may have been doing the requested work all along.
                if (!started
                    && await TryGetRunnerCapabilityMismatchAsync(task, sessionId, ct) is { } mismatch)
                {
                    reason = mismatch.Message;
                    withholdKill = true;
                }
                else
                {
                    var evidence = DescribeBriefQueueEvidence(
                        briefRow?.Status, briefRow?.DeliveryAttempts ?? 0);
                    reason = started && briefNeverTyped
                        ? $"Boot prompt was never delivered: {(int)timeout.TotalMinutes} minutes after dispatch "
                          + "the session has written prompts since dispatch, but none of them was this "
                          + $"task's brief — it is still queued Pending ({evidence}). "
                          + "See the agent's incidents for the delivery errors — and if one of them is a "
                          + "TranscriptBindFailed, the delegate may have been WORKING all along with no "
                          + "transcript bound to read (CARD-0064), so check the session before re-running."
                        : $"Boot prompt was never delivered: {(int)timeout.TotalMinutes} minutes after dispatch "
                          + $"the session wrote no turn prompt of either kind for this task ({evidence}). "
                          + "See the agent's incidents for the delivery errors — and if one of them is a "
                          + "TranscriptBindFailed, the delegate may have been WORKING all along with no "
                          + "transcript bound to read (CARD-0064), so check the session before re-running.";
                }
            }
            else
            {
                var uncorrelated = await _db.AgentIncidents.AsNoTracking()
                    .Where(i => i.SessionId == sessionId
                        && i.Kind == AgentIncidentKind.DelegateReportUncorrelated)
                    .Select(i => new { i.SessionId, i.CreatedAt })
                    .ToListAsync(ct);
                if (!uncorrelated.Any(i =>
                    UncorrelatedReportEvidence.IsEvidenceFor(task, i.SessionId, i.CreatedAt)))
                    continue;

                // The opposite failure to the one above, and the one that actually stranded three
                // tasks overnight (2026-08-11): the session ran, worked and REPORTED, but no turn
                // could be matched to the task, so nothing ever settled it. Starting is not the
                // test of a healthy task — settling is. Without this branch the check above waves
                // it through forever on the strength of a transcript it cannot use.
                // CARD-0117 S1: scoped to THIS task via UncorrelatedReportEvidence, not the session.
                reason =
                    $"Delegate reported but the result could not be attributed: {(int)timeout.TotalMinutes} "
                    + "minutes after dispatch the session has ended a turn with a report whose prompt "
                    + "carries no task marker (most likely the brief was mangled in delivery). The work "
                    + $"may be real — read session {sessionId} before re-running this task.";
            }

            // CARD-0117 D9: negative transcript evidence about a mid-turn session is not grounds
            // to destroy the work on screen. The task still Fails; the session lives (CARD-0056
            // "unclaimed never implies kill" — RemoveEphemeralAgentAsync may leave it unclaimed).
            if (!withholdKill
                && await SessionMessageQueueService.IsWorkingAsync(_db, sessionId, ct))
            {
                withholdKill = true;
            }

            await FailAsync(task, reason, ct);

            if (!withholdKill)
            {
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
                        QueuedMessageOrigin.Delegation, $"task:{task.RootTaskId:N}",
                        task.Id, DelegationNoteDigest.Compute(reason), note.Header);
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

    /// <summary>
    /// CARD-0117 D7: the Pending-arm evidence used to read "every delivery attempt failed" when
    /// <c>DeliveryAttempts</c> was 0 — the queue never tried, because the session was mid-turn
    /// at every flush. Project the attempt count and say which it was.
    /// </summary>
    private string DescribeBriefQueueEvidence(QueuedMessageStatus? status, int deliveryAttempts) =>
        status switch
        {
            QueuedMessageStatus.Pending when deliveryAttempts <= 0
                => "the brief is still queued Pending and was never attempted",
            QueuedMessageStatus.Pending when deliveryAttempts >= _maxDeliveryAttempts
                => "the brief is still queued Pending, parked at MaxDeliveryAttempts",
            QueuedMessageStatus.Pending
                => $"the brief is still queued Pending after {deliveryAttempts} delivery attempt(s) failed",
            QueuedMessageStatus.Sent
                => "the brief is marked Sent, but the session never wrote a turn prompt for this task",
            null => "no brief was queued for this task after dispatch",
            _ => $"brief status: {status}",
        };

    private async Task<RunnerCapabilityMismatch?> TryGetRunnerCapabilityMismatchAsync(
        AgentTask task, Guid sessionId, CancellationToken ct)
    {
        if (_runnerClient is null)
            return null;

        RunnerCapabilityMismatch? mismatch;
        try
        {
            mismatch = await _runnerClient.GetTranscriptCapabilityMismatchAsync(task.AgentKind, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A failed probe is explicitly no evidence. The normal watchdog verdict remains the
            // conservative behaviour until a runner positively enumerates an omission.
            _logger.LogDebug(ex, "Could not probe session-runner transcript capabilities for task {ShortId}",
                DelegationReportFormatter.Short(task.Id));
            return null;
        }

        if (mismatch is null)
            return null;

        await RecordRunnerBuildStaleIncidentAsync(task.AgentId, sessionId, mismatch.Message, ct);
        return mismatch;
    }

    private async Task RecordRunnerBuildStaleIncidentAsync(
        Guid? agentId, Guid sessionId, string message, CancellationToken ct)
    {
        if (agentId is not Guid id || _scopeFactory is null)
            return;

        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            // A watchdog runs every tick. One incident per session is the useful diagnostic; a
            // second identical row would only obscure that the runner was never restarted.
            if (await db.AgentIncidents.AnyAsync(
                    i => i.SessionId == sessionId && i.Kind == AgentIncidentKind.RunnerBuildStale, ct))
                return;

            var supervisor = scope.ServiceProvider.GetRequiredService<AgentSupervisorService>();
            await supervisor.RecordIncidentAsync(
                id, sessionId, AgentIncidentKind.RunnerBuildStale, AlertSeverity.Critical,
                message, failureReason: message, ct: ct);
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Could not record stale-runner incident for session {SessionId}", sessionId);
        }
    }

    /// <summary>
    /// CARD-0021: an open task whose SESSION is dead is failed, with the session's own reason, and
    /// the caller is told. Detection of this state already existed (the attention projection's
    /// DeadSession row, CARD-0035) — what did not was anything that ACTED on it, so three tasks sat
    /// Dispatched for hours on 2026-08-09 behind sessions that had been gone the whole time.
    ///
    /// <para><b>Fail, not retry.</b> A dead session with an unsettled task is unambiguous: no report
    /// is coming, and <c>Failed</c> with the real reason is the truthful state. Re-running is the
    /// caller's decision — the completion note reaches the parent session, and the attention row's
    /// Retry action is there for the human.</para>
    ///
    /// <para><b>It never kills anything, and that is the point.</b> CARD-0056 exists because a row
    /// reading Failed was once wrong about a perfectly healthy session — the operator's own — and a
    /// pass that resolved the mismatch by killing would have killed it mid-sentence. Everything
    /// destructive that <see cref="FailNeverStartedAsync"/> does on its way out stays there; this
    /// sweep is its tail MINUS the kill. Two evidence gates guard even the DB write, both required:
    /// </para>
    ///
    /// <list type="number">
    /// <item>The runner must ANSWER. An unreachable runner is no evidence of anything (the doctrine
    /// <c>SessionReconciliationService</c> already runs on), and the task has waited minutes
    /// already — another 5 s tick costs nothing.</item>
    /// <item>The runner must NOT list the session Running. That combination — row dead, process
    /// alive — is precisely the false-Failed shape reconciliation's third pass re-adopts, and
    /// re-adoption flips the row back to Running, which takes the task out of this predicate
    /// altogether. It also covers the flap-cap state, where a human has already been escalated to.</item>
    /// </list>
    ///
    /// <para>Plus <see cref="DelegationSettings.DeadSessionFailGraceMinutes"/> from the FIRST sweep
    /// that saw the task dead, so re-adoption and a late settlement both win the race.</para>
    ///
    /// <para><see cref="AgentTaskRole.Check"/> tasks are included: a zombie Dispatched check on a
    /// dead previous session is what occupied the standing interpreter for two days (CARD-0079).
    /// <c>ReplyTo = None</c> already suppresses a completion note, and
    /// <see cref="AgentTaskService.RemoveEphemeralAgentAsync"/> only deletes pool delegates, so
    /// the specialist is safe. The sweep still never kills a session.</para>
    /// </summary>
    internal async Task<int> FailDeadSessionTasksAsync(CancellationToken ct)
    {
        // Both are needed and neither degrades: without the runner there is no evidence gate, and
        // without the state the grace would restart on every tick (the dispatcher is scoped).
        if (_runnerClient is null || _deadSessions is null)
            return 0;

        var grace = TimeSpan.FromMinutes(_settings.DeadSessionFailGraceMinutes);
        if (grace <= TimeSpan.Zero)
            return 0;

        var open = await _db.AgentTasks
            .Where(t => t.Status == AgentTaskStatus.Dispatched || t.Status == AgentTaskStatus.Working)
            .ToListAsync(ct);
        if (open.Count == 0)
            return 0;

        var sessionIds = open
            .Where(t => t.AgentSessionId is not null)
            .Select(t => t.AgentSessionId!.Value)
            .Distinct()
            .ToList();
        var sessionById = (await _db.AgentSessions.AsNoTracking()
                .Where(s => sessionIds.Contains(s.Id))
                .Select(s => new { s.Id, s.Status, s.EndedAt, s.FailureReason, s.TerminationSource })
                .ToListAsync(ct))
            .ToDictionary(
                s => s.Id,
                s => new AgentTaskLiveness.SessionSnapshot(
                    s.Status, s.EndedAt, s.FailureReason, s.TerminationSource));

        var dead = new List<(AgentTask Task, AgentTaskLiveness.SessionSnapshot? Session)>();
        foreach (var task in open)
        {
            // CARD-0299 S2: the old session is Stopping while RelaunchWedgedAsync attaches a
            // fresh one. Skip this tick so we do not Fail a task that is being relaunched.
            if (_bootWedgePending is not null && _bootWedgePending.IsPending(task.Id))
                continue;

            AgentTaskLiveness.SessionSnapshot? session =
                task.AgentSessionId is Guid sid && sessionById.TryGetValue(sid, out var row) ? row : null;

            if (AgentTaskLiveness.IsDeadSession(task.AgentSessionId, session))
                dead.Add((task, session));
            else
                // It recovered (or never was). Drop the burned grace: a later, unrelated death must
                // start its own window rather than be acted on the instant it is first seen.
                _deadSessions.Forget(task.Id);
        }

        if (dead.Count == 0)
            return 0;

        IReadOnlyList<SessionRunnerSessionDto> runnerSessions;
        try
        {
            runnerSessions = await _runnerClient.ListAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            // Gate 1. Not an error and not degraded operation — the sweep simply has no evidence
            // this pass. Debug, because a runner restart makes this the ordinary case for a minute.
            _logger.LogDebug(
                ex, "Dead-session reconciliation skipped for {Count} task(s): session runner unreachable",
                dead.Count);
            return 0;
        }

        var runnerRunning = runnerSessions
            .Where(s => string.Equals(s.Status, "Running", StringComparison.OrdinalIgnoreCase))
            .Select(s => s.SessionId)
            .ToHashSet();

        var now = UtcNow();
        var failed = 0;
        foreach (var (task, session) in dead)
        {
            ct.ThrowIfCancellationRequested();

            // Gate 2. The row is wrong about a live process — CARD-0056's exact shape. Leave the
            // task alone and forget it: reconciliation re-adopts the session, which flips the row to
            // Running and takes this task out of the predicate.
            if (task.AgentSessionId is Guid live && runnerRunning.Contains(live))
            {
                _deadSessions.Forget(task.Id);
                _logger.LogDebug(
                    "Task {ShortId} reads dead-session but the runner still serves session {SessionId} — "
                    + "leaving it to reconciliation",
                    DelegationReportFormatter.Short(task.Id), live);
                continue;
            }

            var firstSeen = _deadSessions.FirstSeenAt(task.Id, now);
            if (now - firstSeen < grace)
                continue;

            // CARD-0085: same gate as FailNeverStartedAsync, and ONLY when this session also has
            // zero TranscriptEntries. A dead session that ingested turns is CARD-0021's "no report
            // is coming"; do not widen.
            var hasTranscript = false;
            if (task.AgentSessionId is Guid unbound)
            {
                hasTranscript = await _db.TranscriptEntries.AnyAsync(t => t.AgentSessionId == unbound, ct);
                if (!hasTranscript && await TryRecoverBindRefusalAsync(task, unbound, ct))
                {
                    _deadSessions.Forget(task.Id);
                    continue;
                }
            }

            // CARD-0288: a dead session with a sitting marked-done report is Succeeded, not
            // Failed. Kill is not a settlement path; this is the last chance on the Fail path.
            // CARD-0085's zero-transcript bind recovery above stays first and unchanged.
            if (hasTranscript && _replies is not null && task.AgentSessionId is Guid settleSession)
            {
                await _replies.OnTurnEndAsync(settleSession, ct);
                await _db.Entry(task).ReloadAsync(ct);
                if (task.Status is not AgentTaskStatus.Dispatched and not AgentTaskStatus.Working)
                {
                    _logger.LogInformation(
                        "dead-session reconciler settled task {ShortId} from the transcript rather than failing it",
                        DelegationReportFormatter.Short(task.Id));
                    _deadSessions.Forget(task.Id);
                    continue;
                }
            }

            var classified = AgentTaskLiveness.ClassifyFailure(task.AgentSessionId, session, hasTranscript);

            // The FailNeverStartedAsync tail, minus its KillAsync. Nothing here may be destructive:
            // the whole justification for acting is that the session is already gone, so if that
            // evidence is ever wrong a kill would be the CARD-0056 disaster rather than tidiness.
            await FailAndNotifyAsync(
                task, classified.Reason, "dead-session reconciler", ct, classified.FailureCode);

            _deadSessions.Forget(task.Id);
            failed++;
        }

        return failed;
    }

    /// <summary>
    /// The deadline on work that STARTED and never stops (CARD-0020 S2/S3). Two clocks, evaluated
    /// together by the shared <see cref="TaskDeadlinePolicy"/> and the tighter one wins:
    ///
    /// <list type="number">
    /// <item><b>The hard ceiling</b> — <c>RolePolicyEntry.TimeoutMinutes</c> (240) wall-clock from
    /// <c>DispatchedAt</c>, whatever the session is doing. This is the config the card is right
    /// about: it was declared, defaulted to 60, and read NOWHERE, so a task that got its brief and
    /// then ran forever had no deadline at all. 240 rather than the shipped 60 because 5 of 247
    /// successful tasks (2.0%) ran past 60 minutes and the longest ran 2 732 — enabling the old
    /// default would have killed real work on day one.</item>
    /// <item><b>The phase-aware deadline</b> — 20 minutes waiting on the model, 90 running a local
    /// tool, and only while the session is MID-TURN. A tightening of the ceiling, not a
    /// replacement.</item>
    /// </list>
    ///
    /// <para><b>It fails and reports; it never escalates, kills or retries.</b> Escalation would
    /// re-run the work on a bigger model, which is the wrong response to a task that will not end —
    /// the same argument <see cref="FailNeverStartedAsync"/> makes in its own comment. The session
    /// is deliberately left ALIVE: unlike a never-started session there may be real work in it, and
    /// CARD-0056 is the standing reminder of what a wrong kill costs. Retry stays a human click.</para>
    ///
    /// <para><b>Three gates before anything is written</b>, in this order:</para>
    /// <list type="number">
    /// <item><b>Stand down for CARD-0072.</b> An unresolved <c>ApiErrorRecovery</c> row for this
    /// session means the retry ladder owns it — it is the more specific mechanism, it schedules its
    /// own resumes (hourly for Transient/Wall) and it escalates to Critical on its own caps.
    /// Failing the task underneath it would settle work the ladder is still reviving.</item>
    /// <item><b>Pull, then judge</b> (CARD-0055). Only for a task that has ALREADY tripped a
    /// deadline on the stored rows, so the round trip is bounded by the tasks about to be failed
    /// rather than paid on every tick for every healthy long-running task. The pull can only ever
    /// move a phase clock backwards — a record that arrives is a record the phase read was missing
    /// — so it can only ever withhold a failure, never cause one.</item>
    /// <item><b>Bind-refusal recovery</b> (CARD-0085). An empty (or unbound) transcript is not
    /// evidence that the work did not happen; ask the working directory before writing Failed.</item>
    /// </list>
    ///
    /// <para>The attention projection previews this at 80% of whichever limit applies
    /// (<see cref="AttentionKind.Overdue"/>), the way <c>NeverStartedGrace</c> previews the delivery
    /// watchdog — so a human sees it while a reply, a check or a cancel are all still open.</para>
    /// </summary>
    internal async Task<int> FailOverdueTasksAsync(CancellationToken ct)
    {
        var open = await _db.AgentTasks
            .Where(t => (t.Status == AgentTaskStatus.Dispatched || t.Status == AgentTaskStatus.Working)
                && t.DispatchedAt != null && t.AgentSessionId != null)
            .ToListAsync(ct);
        if (open.Count == 0)
            return 0;

        var failed = 0;
        foreach (var task in open)
        {
            ct.ThrowIfCancellationRequested();

            // Isolated per task, which the sibling sweeps are not. They fail one suspect a tick at
            // most and retry the rest 5 s later; this one walks EVERY open task on every tick, so a
            // single row that throws every time (a poisoned session, a runner that 500s on it)
            // would be a permanent, silent deadline outage for all the others rather than a skipped
            // tick. RunSweepAsync stays the outer net for anything outside the loop.
            try
            {
                if (await TryFailOverdueAsync(task, ct))
                    failed++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                _logger.LogError(
                    ex, "Overdue-deadline evaluation of task {ShortId} failed; the sweep continues "
                    + "with the remaining {Count} task(s)",
                    DelegationReportFormatter.Short(task.Id), open.Count - 1);
            }
        }

        return failed;
    }

    /// <summary>
    /// One task's pass through <see cref="FailOverdueTasksAsync"/>'s three gates. Returns true only
    /// when the task was actually failed.
    /// </summary>
    private async Task<bool> TryFailOverdueAsync(AgentTask task, CancellationToken ct)
    {
        var sessionId = task.AgentSessionId!.Value;

        // First pass on the stored rows alone. TaskDeadlinePolicy returns null in one
        // comparison for anything not yet near a limit, so the ordinary tick costs nothing.
        var suspected = await TaskDeadlinePolicy.EvaluateAsync(_db, task, UtcNow(), _settings, ct);
        if (suspected is not { Breached: true })
            return false;

        // Gate 1 — CARD-0072 owns this session.
        if (await _db.ApiErrorRecoveries.AsNoTracking()
            .AnyAsync(r => r.AgentSessionId == sessionId && r.ResolvedAt == null, ct))
        {
            _logger.LogDebug(
                "Task {ShortId} is past its deadline but session {SessionId} has an unresolved "
                + "API-error recovery — leaving it to the retry ladder",
                DelegationReportFormatter.Short(task.Id), sessionId);
            return false;
        }

        // Gate 2 — pull the runner's own view, then re-read the clocks against it.
        await CatchUpTranscriptAsync(sessionId, ct);
        var verdict = await TaskDeadlinePolicy.EvaluateAsync(_db, task, UtcNow(), _settings, ct);
        if (verdict is not { Breached: true })
        {
            _logger.LogInformation(
                "Task {ShortId} was past its deadline on the stored transcript and is not on the "
                + "runner's — the pull is what saved it; leaving it alone",
                DelegationReportFormatter.Short(task.Id));
            return false;
        }

        // Gate 3 — CARD-0085. Same call, same contract, as the two sweeps above.
        if (await TryRecoverBindRefusalAsync(task, sessionId, ct))
            return false;

        // The Summary already names the clock, the phase and the last entry's age — the failure
        // reason and the attention row are deliberately the same sentence, so a human who saw the
        // Overdue row reads the failure it became in the words it was previewed in.
        var reason =
            $"{verdict.Summary} "
            + "The task is Failed, not escalated and not retried: a bigger model cannot finish a "
            + "task that never ends, and re-running it is your call. The session was NOT killed "
            + $"— read session {sessionId} for what it was actually doing before you decide.";

        await FailAndNotifyAsync(task, reason, "overdue-task deadline", ct);
        return true;
    }

    /// <summary>
    /// CARD-0153 test seam. Production is null and uses <see cref="CatchUpTranscriptAsync"/>'s
    /// runtime pull. Tests set this to land (or withhold) transcript rows at the moment the stall
    /// sweep is about to raise, without constructing a live runner.
    /// </summary>
    internal Func<Guid, CancellationToken, Task>? CatchUpOverride { get; set; }

    /// <summary>
    /// CARD-0153 test seam. Production is null. Tests set this so the ninth clock throws and
    /// <see cref="TickResult.SweepFailures"/> can be shown to count it without taking the other
    /// eight down.
    /// </summary>
    internal Exception? ProgressStallSweepFault { get; set; }

    /// <summary>
    /// Pull the runner's own transcript view and persist it, so the next read is judging current
    /// evidence (CARD-0055). Never throws and never touches the message queue —
    /// <see cref="AgentSessionRuntime.CatchUpTranscriptAsync"/> is the fetch-and-persist half of the
    /// sync precisely because the full one re-enters the per-session queue lock. A dispatcher built
    /// without the runtime skips the pull and falls back to whatever streamed, which is what every
    /// harness predating this did.
    /// </summary>
    private async Task CatchUpTranscriptAsync(Guid sessionId, CancellationToken ct)
    {
        if (CatchUpOverride is { } hook)
        {
            await hook(sessionId, ct);
            return;
        }

        if (_runtime is null)
            return;

        try
        {
            await _runtime.CatchUpTranscriptAsync(sessionId, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            // The runtime already swallows an unreachable runner; this covers everything else it
            // does on the way (persisting the snapshot). A pull that fails leaves the caller judging
            // the streamed rows, which is exactly what it did before this call existed — it must
            // never be the reason a watchdog stops running.
            _logger.LogDebug(ex, "Transcript catch-up failed for session {SessionId}", sessionId);
        }
    }

    /// <summary>
    /// The ninth clock (CARD-0153): a working task whose rows keep landing and none of them is
    /// new. Detection only — records a <see cref="AgentIncidentKind.TaskProgressStalled"/> incident
    /// (Warning, then Error at 90 minutes, Critical only when channel-bound) and never fails,
    /// kills, escalates, or retypes. Deduped per stall episode against the newest incident of this
    /// kind on the same session, so a restart cannot re-raise every open stall.
    ///
    /// <para><b>Pull before you raise</b> (CARD-0055): only for a task that already has a stall
    /// verdict on the stored rows, so the round trip is bounded by tasks about to be reported
    /// rather than paid on every tick. A pull that yields novel rows withholds the incident.</para>
    /// </summary>
    internal async Task<int> DetectStalledProgressAsync(CancellationToken ct)
    {
        if (ProgressStallSweepFault is { } fault)
            throw fault;

        var stall = _settings.StallDetection;
        if (!stall.Enabled || stall.StallMinutes <= 0)
            return 0;

        var open = await _db.AgentTasks
            .Where(t => (t.Status == AgentTaskStatus.Dispatched || t.Status == AgentTaskStatus.Working)
                && t.DispatchedAt != null && t.AgentSessionId != null)
            .ToListAsync(ct);
        if (open.Count == 0)
            return 0;

        var raised = 0;
        foreach (var task in open)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                if (await TryRaiseProgressStallAsync(task, ct))
                    raised++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                _logger.LogError(
                    ex, "Progress-stall evaluation of task {ShortId} failed; the sweep continues "
                    + "with the remaining task(s)",
                    DelegationReportFormatter.Short(task.Id));
            }
        }

        return raised;
    }

    private async Task<bool> TryRaiseProgressStallAsync(AgentTask task, CancellationToken ct)
    {
        var workspace = await ProbeWorkspaceAsync(task, ct);
        var suspected = await TaskProgressPolicy.EvaluateAsync(
            _db, task, UtcNow(), _settings, ct, workspace);
        if (suspected is null)
            return false;

        var sessionId = task.AgentSessionId!.Value;
        await CatchUpTranscriptAsync(sessionId, ct);
        workspace = await ProbeWorkspaceAsync(task, ct);
        var verdict = await TaskProgressPolicy.EvaluateAsync(
            _db, task, UtcNow(), _settings, ct, workspace);
        if (verdict is null)
        {
            _logger.LogInformation(
                "Task {ShortId} stall withheld: novel rows on pull",
                DelegationReportFormatter.Short(task.Id));
            return false;
        }

        var latest = await _db.AgentIncidents.AsNoTracking()
            .Where(i => i.SessionId == sessionId && i.Kind == AgentIncidentKind.TaskProgressStalled)
            .OrderByDescending(i => i.CreatedAt)
            .Select(i => new { i.CreatedAt, i.Severity })
            .FirstOrDefaultAsync(ct);

        var severity = StallSeverity(
            verdict, await IsChannelBoundAsync(sessionId, ct), alreadyRaised: latest is not null);

        if (latest is not null && latest.CreatedAt >= verdict.LastProgressAt)
        {
            if (latest.Severity >= severity)
                return false;
        }

        if (task.AgentId is not Guid agentId)
            return false;

        _db.AgentIncidents.Add(new AgentIncident
        {
            Id = Guid.NewGuid(),
            AgentId = agentId,
            SessionId = sessionId,
            Kind = AgentIncidentKind.TaskProgressStalled,
            Severity = severity,
            Message = verdict.Summary,
            FailureReason = verdict.FailureReason,
            CreatedAt = UtcNow(),
        });
        await _db.SaveChangesAsync(ct);
        _logger.LogWarning(
            "Task {ShortId} progress stalled ({Severity}): {Summary}",
            DelegationReportFormatter.Short(task.Id), severity, verdict.Summary);
        return true;
    }

    private AlertSeverity StallSeverity(
        TaskProgressPolicy.Verdict verdict, bool channelBound, bool alreadyRaised)
    {
        var errorAfter = _settings.StallDetection.EscalateToErrorAfterMinutes;
        var atError = alreadyRaised
            && errorAfter > 0
            && verdict.StalledFor >= TimeSpan.FromMinutes(errorAfter);
        if (!atError)
            return AlertSeverity.Warning;
        return channelBound ? AlertSeverity.Critical : AlertSeverity.Error;
    }

    /// <summary>
    private async Task<WorkspaceProgressArm?> ProbeWorkspaceAsync(
        AgentTask task, CancellationToken ct)
    {
        if (_files is null || task.DispatchedAt is not DateTime dispatched)
            return null;
        return await _files.ProbeProgressAsync(
            task.WorkingDirectory, dispatched, task.Workspace == WorkspaceMode.Shared, ct);
    }

    /// <summary>
    /// Same shape as <c>ApiErrorRecoveryService.IsChannelBoundAsync</c>: the agent whose
    /// persistent session is this one, bound to a chat channel. A wedged ordinary delegate is a
    /// task, not an outage; a wedged channel-bound agent is a human waiting on a dead line.
    /// </summary>
    private async Task<bool> IsChannelBoundAsync(Guid sessionId, CancellationToken ct)
    {
        var sessionIdText = sessionId.ToString("D");
        var agentId = await _db.Agents
            .Where(a => a.PersistentSessionId == sessionIdText)
            .Select(a => (Guid?)a.Id)
            .FirstOrDefaultAsync(ct);
        if (agentId is not Guid id)
            return false;
        return await _db.ChatChannels.AsNoTracking().AnyAsync(c => c.AgentId == id, ct);
    }

    /// <summary>
    /// The NON-DESTRUCTIVE failure tail: write Failed, retire an ephemeral delegate, tell the
    /// caller, publish the change. Shared by the two sweeps that fail a task without killing its
    /// session — everything destructive stays in <see cref="FailNeverStartedAsync"/>, which is the
    /// only one holding evidence that the session never did anything (CARD-0021, CARD-0056).
    ///
    /// <para>The completion note is best-effort by design: the caller must HEAR about the death
    /// rather than discover it, but a parent session that cannot take a message is not a reason to
    /// leave the task open.</para>
    /// </summary>
    private async Task FailAndNotifyAsync(
        AgentTask task, string reason, string sweep, CancellationToken ct,
        AgentTaskFailureCode? failureCode = null)
    {
        await FailAsync(task, reason, ct, failureCode);
        await _tasks.RemoveEphemeralAgentAsync(task, task.AgentId, ct);
        if (task.DispatchedAt is null)
            ArmFailureReminder(task, task.CompletedAt ?? UtcNow());
        await _db.SaveChangesAsync(ct);

        if (task.ReplyTo == AgentTaskReplyTo.Session && task.ParentSessionId is Guid parentSession)
        {
            var note = DelegationReportFormatter.BuildCompletionNote(task, _settings, reason);
            try
            {
                await _queue.EnqueueAsync(
                    parentSession, note.Body, MessageSendMode.WhenIdle, ct,
                    QueuedMessageOrigin.Delegation, $"task:{task.RootTaskId:N}",
                    task.Id, DelegationNoteDigest.Compute(reason), note.Header);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(
                    ex, "Could not deliver the {Sweep} failure of task {ShortId} to parent session {SessionId}",
                    sweep, DelegationReportFormatter.Short(task.Id), parentSession);
            }
        }

        await _eventBus.PublishToAllAsync(
            "AgentTaskChanged", new { taskId = task.Id, rootId = task.RootTaskId }, ct);
        _logger.LogWarning(
            "Task {ShortId} failed by the {Sweep}: {Reason}",
            DelegationReportFormatter.Short(task.Id), sweep, reason);
    }

    /// <summary>
    /// The clock behind CARD-0046's grace window. Settlement defers when the turn-ending response
    /// has not written its own text yet; the ordinary resolution is the text's own arrival
    /// re-triggering <see cref="AgentTaskReplyService.OnTurnEndAsync"/>. But a response that never
    /// writes text at all is real — 1 in 180 measured, a lone <c>end_turn</c> thinking record
    /// followed by "API Error: Connection lost mid-response" — and nothing would ever come back for
    /// it, so that task would sit Dispatched until the 10-minute delivery watchdog killed it.
    ///
    /// Deliberately NARROW: it re-invokes settlement only for a session whose LATEST TurnEnd is
    /// still missing its own response's text past the grace. Every other running task is left
    /// untouched rather than being re-settled on a cadence.
    ///
    /// <para>CARD-0288 adds arm 0 ahead of both clocks: a marked <c>[antiphon-report:…]</c> already
    /// in the transcript re-hands on this tick. It does not wait for
    /// <see cref="DelegationSettings.SubagentGraceMinutes"/> or
    /// <see cref="DelegationSettings.FinalMessageGraceSeconds"/>, and it stays armed when those
    /// hatches are zeroed.</para>
    ///
    /// <para>Slice 4 adds the second clock, for the same reason: a turn that launched BACKGROUND
    /// subagents defers until their notifications return, and a subagent can die without ever
    /// notifying. That one is measured from the session's last transcript entry — a notification
    /// arriving resets it — and it is self-limiting, because the settlement it triggers takes the
    /// task out of Dispatched and out of this scan.</para>
    ///
    /// <para>CARD-0248: an UNCHANGED boundary is re-handed at most once per
    /// <see cref="DelegationSettings.ReportSweepRehandSeconds"/> (default 60). The predicates are
    /// monotonic, so without that watermark the sweep re-entered settlement every 5 s and ate the
    /// CARD-0159 nudge. Correctness never depends on the watermark — <c>ClassifyReportAsync</c>'s
    /// gates make re-entry inert — it only bounds the load. A changed boundary always hands off
    /// immediately. Settlement itself now requires a later boundary, a delivered nudge, and (for
    /// text-less turns) the response window; this method just finds candidates.</para>
    /// </summary>
    internal async Task<int> SettleDeferredReportsAsync(CancellationToken ct)
    {
        if (_replies is null)
            return 0;
        // CARD-0288 arm 0 (marked report already in the transcript) is independent of the two
        // grace hatches. Tests zero those knobs to isolate other clocks; a sitting [antiphon-report]
        // must still re-hand on the 5 s tick.
        var finalMessageArmed = _settings.FinalMessageGraceSeconds > 0;
        var subagentsArmed = _settings.SubagentGraceMinutes > 0;

        var cutoff = UtcNow() - TimeSpan.FromSeconds(_settings.FinalMessageGraceSeconds);
        var subagentCutoff = UtcNow() - TimeSpan.FromMinutes(_settings.SubagentGraceMinutes);
        var openTasks = await _db.AgentTasks.AsNoTracking()
            .Where(t => (t.Status == AgentTaskStatus.Dispatched || t.Status == AgentTaskStatus.Working)
                && t.AgentSessionId != null)
            .Select(t => new
            {
                t.Id,
                SessionId = t.AgentSessionId!.Value,
                t.Role,
                t.ReportNudgedAt,
                t.ReportNudgedSequence,
            })
            .ToListAsync(ct);
        var sessions = openTasks.Select(t => t.SessionId).Distinct().ToList();
        var tasksBySession = openTasks
            .GroupBy(t => t.SessionId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var swept = 0;
        foreach (var sessionId in sessions)
        {
            ct.ThrowIfCancellationRequested();

            var end = await _db.TranscriptEntries.AsNoTracking()
                .Where(e => e.AgentSessionId == sessionId && e.Kind == TranscriptKinds.TurnEnd)
                .OrderByDescending(e => e.Sequence)
                .Select(e => new { e.Sequence, e.ApiCallId, e.CreatedAt, e.Kind, e.StopReason })
                .FirstOrDefaultAsync(ct);
            if (end is null)
                continue; // no boundary at all — nothing has been deferred here

            // CARD-0159: a cancelled TurnEnd is an idle boundary, never a report. Re-invoking
            // settlement on it after the grace would recreate the incident — Succeeded on the
            // interrupted turn's narration. Skip both arms; the task stays Working.
            if (!TranscriptKinds.IsReportBoundary(end.Kind, end.StopReason))
                continue;

            var now = UtcNow();
            var rehandSeconds = _settings.ReportSweepRehandSeconds;

            // (0) CARD-0288: a marked closing line is already in the transcript. The live observer
            // missed the settle (server-down catch-up is the usual cause). Re-hand on this tick,
            // not after SubagentGraceMinutes. Identity, not a prose heuristic.
            if (tasksBySession.TryGetValue(sessionId, out var sessionTasks))
            {
                var markedTexts = await _db.TranscriptEntries.AsNoTracking()
                    .Where(e => e.AgentSessionId == sessionId
                        && e.Kind == TranscriptKinds.AssistantText
                        && e.Text != null
                        && e.Text.Contains("[antiphon-report:"))
                    .Select(e => e.Text!)
                    .ToListAsync(ct);
                Guid? markedTaskId = null;
                foreach (var candidate in sessionTasks)
                {
                    foreach (var text in markedTexts)
                    {
                        if (!DelegationReportFormatter.TryFindReportToken(candidate.Id, text, out _))
                            continue;
                        markedTaskId = candidate.Id;
                        break;
                    }
                    if (markedTaskId is not null)
                        break;
                }

                if (markedTaskId is Guid tid)
                {
                    if (_sweepMarks is null
                        || _sweepMarks.ShouldHandOff(sessionId, end.Sequence, lastEntryAt: null, now, rehandSeconds))
                    {
                        _logger.LogWarning(
                            "Task {ShortId}: marked report already in transcript at boundary {Sequence} — "
                            + "re-invoking settlement",
                            DelegationReportFormatter.Short(tid), end.Sequence);
                        await _replies.OnTurnEndAsync(sessionId, ct);
                        _sweepMarks?.RecordHandOff(sessionId, end.Sequence, lastEntryAt: null, now);
                        swept++;
                    }
                    continue;
                }
            }

            // (1) The turn-ending response never wrote its own text. No id to wait on, or still
            // inside the grace, means nothing was deferred. CreatedAt, never the record's
            // Timestamp: that one is backdated up to 30 s (CARD-0046).
            if (finalMessageArmed && end.ApiCallId is string apiCallId && end.CreatedAt <= cutoff)
            {
                var landed = await _db.TranscriptEntries.AsNoTracking().AnyAsync(
                    e => e.AgentSessionId == sessionId
                        && e.Kind == TranscriptKinds.AssistantText
                        && e.ApiCallId == apiCallId, ct);
                if (!landed)
                {
                    if (_sweepMarks is null
                        || _sweepMarks.ShouldHandOff(sessionId, end.Sequence, lastEntryAt: null, now, rehandSeconds))
                    {
                        _logger.LogWarning(
                            "Session {SessionId}: no text from the turn-ending response after {Grace}s — "
                            + "settling on what the turn produced",
                            sessionId, _settings.FinalMessageGraceSeconds);
                        await _replies.OnTurnEndAsync(sessionId, ct);
                        _sweepMarks?.RecordHandOff(sessionId, end.Sequence, lastEntryAt: null, now);
                        swept++;
                    }
                    continue;
                }
            }

            // (S1) CARD-0294: nudged, still idle on that same unmarked boundary past the
            // waiting clock. Independent of the two grace hatches (tests zero those knobs).
            // Do not call OnTurnEndAsync — same-boundary would no-op, a later boundary
            // would settle-anyway Succeeded.
            if (_settings.UnmarkedWaitingMinutes > 0
                && tasksBySession.TryGetValue(sessionId, out var waitingTasks))
            {
                var blockedWaiting = false;
                foreach (var candidate in waitingTasks)
                {
                    if (candidate.Role == AgentTaskRole.Check)
                        continue;
                    if (candidate.ReportNudgedAt is not DateTime nudgedAt)
                        continue;
                    if (now - nudgedAt < TimeSpan.FromMinutes(_settings.UnmarkedWaitingMinutes))
                        continue;
                    if (candidate.ReportNudgedSequence is not long nudgedSeq
                        || end.Sequence != nudgedSeq)
                        continue;
                    if (await SessionMessageQueueService.IsWorkingAsync(_db, sessionId, ct))
                        continue;

                    _logger.LogWarning(
                        "Task {ShortId}: unmarked idle past {Minutes}m after the closing-line nudge "
                        + "at boundary {Sequence} — blocking as UnmarkedWaiting",
                        DelegationReportFormatter.Short(candidate.Id),
                        _settings.UnmarkedWaitingMinutes,
                        end.Sequence);
                    await _replies.BlockUnmarkedWaitingAsync(sessionId, ct);
                    swept++;
                    blockedWaiting = true;
                    break;
                }
                if (blockedWaiting)
                    continue;
            }

            // (2) Background subagents that never notified. Silence on the WHOLE session is the
            // signal — while they are working there is nothing else to write, and a notification
            // landing resets the clock. Settlement itself decides whether any launch is actually
            // outstanding; this is only the clock, and a no-op in every other case.
            if (!subagentsArmed)
                continue;
            var lastEntryAt = await _db.TranscriptEntries.AsNoTracking()
                .Where(e => e.AgentSessionId == sessionId)
                .MaxAsync(e => (DateTime?)e.CreatedAt, ct);
            if (lastEntryAt is DateTime quietSince && quietSince <= subagentCutoff)
            {
                if (_sweepMarks is null
                    || _sweepMarks.ShouldHandOff(sessionId, end.Sequence, quietSince, now, rehandSeconds))
                {
                    _logger.LogDebug(
                        "Session {SessionId}: silent for {Grace}+ minutes — re-checking settlement for "
                        + "background subagents that never reported",
                        sessionId, _settings.SubagentGraceMinutes);
                    await _replies.OnTurnEndAsync(sessionId, ct);
                    _sweepMarks?.RecordHandOff(sessionId, end.Sequence, quietSince, now);
                    swept++;
                }
            }
        }

        _sweepMarks?.Prune(sessions);
        return swept;
    }

    /// <summary>
    /// The check-in sweep (CARD-0047 §1.1, §1.5). Selects tasks whose <see cref="AgentTask.NextCheckAt"/>
    /// has come, advances the schedule, and hands the ids to <see cref="AgentTaskCheckQueue"/>.
    ///
    /// <para><b>It claims and hands off; it never runs a check.</b> <see cref="TickAsync"/> is serial
    /// and runs every 5 s; a check takes seconds and, from slice 4, a model call. Awaiting one here
    /// would stall dispatching for its duration, so the only thing this method does with a due task
    /// is write its next schedule and drop the id on a channel.</para>
    ///
    /// <para><b>Re-arm BEFORE run.</b> The schedule is advanced and COMMITTED before the id is
    /// handed off, so a crash (or a throwing check) costs one skipped check instead of a task that
    /// is due forever and re-claimed on every 5 s tick. The claim itself is one conditional UPDATE
    /// keyed on the values this sweep read, so two ticks — or two server instances — cannot both
    /// claim the same check.</para>
    ///
    /// <para>Checking stops on three conditions, all handled here rather than in the worker: the
    /// task leaving Dispatched/Working (the filter — settlement needs no bookkeeping), the caller's
    /// session being gone or exited (nobody is listening, so the schedule is cleared), and the
    /// check budget being spent (the last check still RUNS, and its note says the budget is spent).</para>
    /// </summary>
    internal async Task<int> RunScheduledChecksAsync(CancellationToken ct)
    {
        if (!_settings.CheckEnabled || _checkQueue is null)
            return 0;

        var now = UtcNow();
        var due = await _db.AgentTasks.AsNoTracking()
            .Where(t => (t.Status == AgentTaskStatus.Dispatched || t.Status == AgentTaskStatus.Working)
                && t.NextCheckAt != null
                && t.NextCheckAt <= now
                && t.ReplyTo == AgentTaskReplyTo.Session
                && t.ParentSessionId != null
                // RECURSION GUARD, the other half of the one in ArmFirstCheck: nothing arms
                // NextCheckAt on an interpretation task, and nothing selects one either. Checks
                // that checked checks would create an interpretation per interpretation.
                && t.Role != AgentTaskRole.Check)
            .OrderBy(t => t.NextCheckAt)
            .ToListAsync(ct);

        var claimed = 0;
        foreach (var task in due)
        {
            ct.ThrowIfCancellationRequested();

            if (!await CallerIsListeningAsync(task, ct))
            {
                // Disarm rather than keep gathering facts nobody will read. Not an error: a caller
                // whose session ended has usually just finished with the run.
                if (await ClaimCheckAsync(task, nextCheckAt: null, checkCount: task.CheckCount, ct))
                {
                    _logger.LogInformation(
                        "Checks on task {ShortId} stopped — its caller session {SessionId} is gone",
                        DelegationReportFormatter.Short(task.Id), task.ParentSessionId);
                }
                continue;
            }

            var checkNumber = task.CheckCount + 1;
            var budgetSpent = checkNumber >= Math.Max(1, _settings.CheckMaxCount);
            var nextCheckAt = budgetSpent
                ? (DateTime?)null
                : now + CheckSchedule.NextInterval(_settings, task.ExpectedDurationMinutes, checkNumber);

            if (!await ClaimCheckAsync(task, nextCheckAt, checkNumber, ct))
                continue; // another tick took this one

            _checkQueue.TryEnqueue(task.Id);
            claimed++;
        }

        return claimed;
    }

    /// <summary>
    /// CARD-0231: re-send the pre-dispatch failure note on the check ramp while nothing shows the
    /// caller has heard. Not a check — no probe, no interpreter, no session. Failed +
    /// <c>NextCheckAt</c> is now a legal state; <see cref="RunScheduledChecksAsync"/> must keep
    /// filtering it out.
    /// </summary>
    internal async Task<int> RemindUnacknowledgedFailuresAsync(CancellationToken ct)
    {
        if (!_settings.CheckEnabled)
            return 0;

        var now = UtcNow();
        var due = await _db.AgentTasks.AsNoTracking()
            .Where(t => t.Status == AgentTaskStatus.Failed
                && t.DispatchedAt == null
                && t.NextCheckAt != null
                && t.NextCheckAt <= now
                && t.ReplyTo == AgentTaskReplyTo.Session
                && t.ParentSessionId != null
                && t.Role != AgentTaskRole.Check)
            .OrderBy(t => t.NextCheckAt)
            .ToListAsync(ct);
        if (due.Count == 0)
            return 0;

        var ids = due.Select(t => t.Id).ToList();
        var notes = await _db.SessionQueuedMessages.AsNoTracking()
            .Where(m => m.Origin == QueuedMessageOrigin.Delegation
                && m.SourceTaskId != null
                && ids.Contains(m.SourceTaskId.Value))
            .Select(m => new { m.SourceTaskId, m.Status, m.DeliveryAttempts })
            .ToListAsync(ct);
        var notesByTask = notes
            .GroupBy(n => n.SourceTaskId!.Value)
            .ToDictionary(
                g => g.Key,
                g => g.Select(n => (n.Status, n.DeliveryAttempts)).ToList());

        var acted = 0;
        foreach (var task in due)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                if (await RemindOneUnacknowledgedFailureAsync(
                    task, notesByTask.GetValueOrDefault(task.Id), now, ct))
                    acted++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                _logger.LogError(
                    ex, "Failure reminder on task {ShortId} failed; the sweep continues",
                    DelegationReportFormatter.Short(task.Id));
            }
        }

        return acted;
    }

    private async Task<bool> RemindOneUnacknowledgedFailureAsync(
        AgentTask task,
        IReadOnlyList<(QueuedMessageStatus Status, int DeliveryAttempts)>? notes,
        DateTime now,
        CancellationToken ct)
    {
        // The batch query projects an anonymous type; normalize here so the arms stay readable.
        var rows = notes ?? [];
        var acknowledged = task.LastPolledResultAt is not null
            || task.ReadAt is not null
            || rows.Any(n => n.Status is QueuedMessageStatus.Sent or QueuedMessageStatus.Canceled);
        if (acknowledged)
        {
            if (await ClaimCheckAsync(task, nextCheckAt: null, checkCount: task.CheckCount, ct))
            {
                _logger.LogInformation(
                    "Failure reminder on task {ShortId} disarmed — the caller has acknowledged the failure",
                    DelegationReportFormatter.Short(task.Id));
                return true;
            }

            return false;
        }

        if (!await CallerIsListeningAsync(task, ct))
        {
            if (await ClaimCheckAsync(task, nextCheckAt: null, checkCount: task.CheckCount, ct))
            {
                _logger.LogInformation(
                    "Checks on task {ShortId} stopped — its caller session {SessionId} is gone",
                    DelegationReportFormatter.Short(task.Id), task.ParentSessionId);
                return true;
            }

            return false;
        }

        var inFlight = rows.Any(n =>
            n.Status == QueuedMessageStatus.Pending && n.DeliveryAttempts < _maxDeliveryAttempts);
        if (inFlight)
        {
            var next = now + CheckSchedule.NextInterval(
                _settings, task.ExpectedDurationMinutes, Math.Max(1, task.CheckCount + 1));
            return await ClaimCheckAsync(task, next, task.CheckCount, ct);
        }

        var checkNumber = task.CheckCount + 1;
        var budgetSpent = checkNumber >= Math.Max(1, _settings.CheckMaxCount);
        var nextCheckAt = budgetSpent
            ? (DateTime?)null
            : now + CheckSchedule.NextInterval(_settings, task.ExpectedDurationMinutes, checkNumber);
        if (!await ClaimCheckAsync(task, nextCheckAt, checkNumber, ct))
            return false;

        var firstNoteState = rows.Count == 0 ? "absent" : "parked";
        var max = Math.Max(1, _settings.CheckMaxCount);
        var workspaceNote =
            $"reminder {checkNumber}/{max} — the first failure note did not reach you";
        if (budgetSpent)
            workspaceNote += $"; final reminder — the {max}-reminder budget is spent";

        var reason = task.FailureReason ?? string.Empty;
        var note = DelegationReportFormatter.BuildCompletionNote(
            task, _settings, reason, workspaceNote: workspaceNote);
        try
        {
            await _queue.EnqueueAsync(
                task.ParentSessionId!.Value, note.Body, MessageSendMode.WhenIdle, ct,
                QueuedMessageOrigin.Delegation, $"task:{task.RootTaskId:N}",
                task.Id, DelegationNoteDigest.Compute(reason), note.Header);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(
                ex, "Could not deliver failure reminder #{CheckNumber} of task {ShortId} to parent session {SessionId}",
                checkNumber, DelegationReportFormatter.Short(task.Id), task.ParentSessionId);
            return true;
        }

        var detail = $"Failure reminder #{checkNumber} queued: first note {firstNoteState}";
        if (budgetSpent)
            detail += $"; final reminder — the {max}-reminder budget is spent";
        _db.AgentTaskEvents.Add(new AgentTaskEvent
        {
            Id = Guid.NewGuid(),
            AgentTaskId = task.Id,
            Type = AgentTaskEventType.Warning,
            Detail = detail,
            At = now,
        });
        await _db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>
    /// Queue window (CARD-0132): a still-Pending check note whose task settled after enqueue is
    /// canceled before delivery once the completion note exists. Its digest is retained as an event.
    ///
    /// <para>Only <c>DeliveryAttempts == 0</c> rows are touched. A note that has been typed once
    /// carries a baseline sequence; amending it would make late-confirm hunt for banner text
    /// that was never typed.</para>
    /// </summary>
    internal async Task<int> ReconcileSupersededChecksAsync(CancellationToken ct)
    {
        var pending = await _db.SessionQueuedMessages.AsNoTracking()
            .Where(m => m.Status == QueuedMessageStatus.Pending
                && m.Origin == QueuedMessageOrigin.Check
                && m.DeliveryAttempts == 0)
            .Select(m => new { m.Id, m.AgentSessionId, m.ConversationKey, m.CreatedAt, m.Body })
            .ToListAsync(ct);
        if (pending.Count == 0)
            return 0;

        var canceled = 0;

        foreach (var note in pending)
        {
            ct.ThrowIfCancellationRequested();

            if (!AgentTaskCheckService.TryParseCheckConversationKey(note.ConversationKey, out var taskId))
                continue;
            var supersession = await AgentTaskCheckService.EvaluateAsync(_db, taskId, ct);
            if (supersession is not { Settled: true } settled)
                continue;
            if (settled.SettledAt <= note.CreatedAt)
                continue;

            var task = await _db.AgentTasks.AsNoTracking().Where(t => t.Id == taskId)
                .Select(t => new { t.ParentSessionId, t.RootTaskId }).FirstOrDefaultAsync(ct);
            if (task?.ParentSessionId is not Guid parentSession
                || !await AgentTaskCheckService.HasCompletionNoteAsync(_db, parentSession, task.RootTaskId, ct))
                continue;

            var capturedAt = AgentTaskCheckService.TryReadCapturedAt(note.Body, out var parsed)
                ? parsed
                : note.CreatedAt;
            var banner = AgentTaskCheckService.SupersededBanner(
                settled.Status, settled.SettledAt, capturedAt);
            if (await _queue.CancelPendingIfUntypedAsync(note.AgentSessionId, note.Id, ct))
            {
                _db.AgentTaskEvents.Add(new AgentTaskEvent
                {
                    Id = Guid.NewGuid(),
                    AgentTaskId = taskId,
                    Type = AgentTaskEventType.Check,
                    Detail = $"{banner}\n\n{note.Body}",
                    At = UtcNow(),
                });
                await _db.SaveChangesAsync(ct);
                canceled++;
            }
        }

        return canceled;
    }

    /// <summary>
    /// Advance the schedule atomically. The WHERE carries the values this sweep READ, so the update
    /// applies exactly once even if two ticks race; a zero row count means someone else won.
    /// </summary>
    private async Task<bool> ClaimCheckAsync(
        AgentTask task, DateTime? nextCheckAt, int checkCount, CancellationToken ct)
    {
        var seenNextCheckAt = task.NextCheckAt;
        var seenCheckCount = task.CheckCount;
        var rows = await _db.AgentTasks
            .Where(t => t.Id == task.Id
                && t.NextCheckAt == seenNextCheckAt
                && t.CheckCount == seenCheckCount)
            .ExecuteUpdateAsync(
                s => s.SetProperty(t => t.NextCheckAt, nextCheckAt)
                      .SetProperty(t => t.CheckCount, checkCount),
                ct);
        return rows > 0;
    }

    /// <summary>Is there still a caller session to deliver a check note into?</summary>
    private async Task<bool> CallerIsListeningAsync(AgentTask task, CancellationToken ct)
    {
        if (task.ParentSessionId is not Guid parent)
            return false;
        return await _db.AgentSessions.AsNoTracking().AnyAsync(
            s => s.Id == parent
                && (s.Status == SessionStatus.Created
                    || s.Status == SessionStatus.Starting
                    || s.Status == SessionStatus.Running),
            ct);
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

        // Resolved BEFORE anything with a side effect — no worktree is cut and no session row is
        // written for a kind this installation cannot launch (CARD-0084 S3), or for a pinned
        // standing agent's disabled / unvalidated profile (CARD-0140 S2). The reuse paths above
        // are deliberately outside it: they launch nothing, so a definition they would never use
        // must not be able to fail them. The outer tick catches the throw and fails the task with
        // the configuration gap named.
        var program = await ResolveDelegateProgramAsync(claimed, ct);

        if (_quotaGate is not null)
        {
            Agent? owner = null;
            if (claimed.AgentId is Guid oid)
                owner = await _db.Agents.AsNoTracking().FirstOrDefaultAsync(a => a.Id == oid, ct);
            var quota = await _quotaGate.EvaluateAsync(
                program.Kind, SubscriptionUsageKey.For(owner, program.Kind), ct);
            if (quota is not null)
            {
                _db.AgentTaskEvents.Add(new AgentTaskEvent
                {
                    Id = Guid.NewGuid(),
                    AgentTaskId = claimed.Id,
                    Type = AgentTaskEventType.Warning,
                    Detail = SubscriptionQuotaPolicy.FormatDispatchWarning(quota),
                    At = now,
                });
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
        // A pool delegate's environment is fixed for the life of its process. Record the task
        // scope at every cold launch (including a deliberate relaunch of an existing pool row),
        // so the warm-pool predicate can never hand that process work from another scope.
        // Pool delegates have no board-derived scope: null is intentionally global-only.
        // CARD-0260: the inherited-env snapshot is the other half of that honesty — a warm
        // process retains whatever LLM routing it launched with.
        if (agent.IsPoolDelegate)
        {
            agent.PoolProjectId = claimed.ProjectId;
            agent.LaunchEnvJson = string.IsNullOrWhiteSpace(claimed.InheritedLaunchEnvJson)
                ? "{}"
                : claimed.InheritedLaunchEnvJson;
        }

        var cwd = claimed.WorktreePath ?? claimed.WorkingDirectory;
        var session = new AgentSession
        {
            Id = Guid.NewGuid(),
            CardId = null,
            WorktreeId = null,
            DefinitionName = program.DefinitionName,
            // The program the pre-flight resolved, not a constant. This one assignment is what
            // makes every downstream reader — the brief's spill gate, the launch args, delivery,
            // the tailer — agree about whose composer is on the other end (CARD-0084 S3).
            AgentKind = program.Kind,
            // CARD-0160: snapshot from the resolved agent (pool delegate or standing).
            SessionBackend = agent.SessionBackend,
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
        ArmFirstCheck(claimed, now);

        agent.PersistentSessionId = session.Id.ToString("D");
        agent.Status = AgentStatus.Running;
        agent.UpdatedAt = now;

        var shippedModel = ShippedModelDisplay(claimed, agent, program);
        _db.AgentTaskEvents.Add(new AgentTaskEvent
        {
            Id = Guid.NewGuid(),
            AgentTaskId = claimed.Id,
            Type = AgentTaskEventType.Dispatched,
            ModelLevel = claimed.ModelLevel,
            Detail = $"Dispatched to agent '{agent.Name}' "
                + $"({shippedModel}) in {claimed.WorkingDirectory}",
            At = now,
        });

        await _db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        // After the commit, so a pinned agent's attachments are read from a settled view. A fresh
        // pool delegate has none — its row was created moments ago in this same transaction — so
        // this is a lookup that costs nothing in the common case and is the whole point in the
        // pinned one (CARD-0058 slice 6).
        var attachedBundleKeys = await AgentBundleAttachments.LoadAsync(_db, agent.Id, _logger, ct);
        var spec = await BuildLaunchSpecAsync(claimed, agent, session, program, attachedBundleKeys, ct);
        // CARD-0115 S2 — this is the bottom-level path a pool delegate takes. The task's recorded
        // project scope wins; a task without one falls back to its pinned standing agent's board.
        // A pool delegate has neither a board nor a path-derived fallback: no trustworthy scope
        // means global-only resolution. The profile path already resolved keys inside
        // AgentLaunchResolution (CARD-0140 D5), so running this again would double-substitute.
        if (program.ProfileId is null && _apiKeyEnvResolver is not null)
        {
            var projectId = claimed.ProjectId
                ?? await _apiKeyEnvResolver.ResolveProjectIdAsync(agent.BoardId, ct);
            spec = await _apiKeyEnvResolver.ResolveSpecAsync(
                spec,
                projectId,
                $"task {DelegationReportFormatter.Short(claimed.Id)} on agent '{agent.Name}'",
                ct);
        }
        _launchQueue.EnqueueInteractiveSession(session.Id, agent.Id, spec, remoteControlName: null, notes: null);

        // The brief goes through the message QUEUE, never straight to the pty: that is the only path
        // that normalises line endings, wraps in a bracketed paste, and submits with a separate CR.
        // A raw multi-line write fragments into several turns (documented live miss).
        // session.AgentKind, not a constant: whose composer this brief is typed into decides whether
        // it may be typed at all (CARD-0084 S1). It is ClaudeCode on every spawn today; reading it
        // off the session is what makes a Grok delegate spill instead of arriving run-on.
        var brief = FitBriefForTyping(claimed, _settings, _ptyProfile?.Ceilings, _logger, session.AgentKind);
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
            shippedModel, session.Id, claimed.WorkingDirectory);
        return true;
    }

    internal const string BootWedgeFailedReason =
        "boot prompt could not be delivered; TUI stopped reading after the brief rendered, twice; "
        + "relaunched once and wedged again.";

    /// <summary>
    /// CARD-0299 S2. Spawn a fresh session for the same agent, re-enqueue the brief, restamp
    /// DispatchedAt so the 10-minute watchdog measures the relaunch. Does not Fail the task
    /// and does not delete the pool agent.
    /// </summary>
    internal async Task RelaunchWedgedAsync(Guid taskId, Guid oldSessionId, CancellationToken ct)
    {
        var task = await _db.AgentTasks.FirstOrDefaultAsync(t => t.Id == taskId, ct);
        if (task is null)
            return;

        if (task.AgentId is not Guid agentId)
        {
            _logger.LogWarning(
                "Task {ShortId}: boot-wedge relaunch skipped — no agent on the task",
                DelegationReportFormatter.Short(task.Id));
            return;
        }

        var agent = await _db.Agents.FirstOrDefaultAsync(a => a.Id == agentId, ct);
        if (agent is null)
        {
            _logger.LogWarning(
                "Task {ShortId}: boot-wedge relaunch skipped — agent {AgentId} is gone",
                DelegationReportFormatter.Short(task.Id), agentId);
            return;
        }

        var now = UtcNow();
        DelegateProgram? program = null;
        try
        {
            program = await ResolveDelegateProgramAsync(task, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(
                ex, "Task {ShortId}: boot-wedge relaunch could not resolve a launch program; "
                + "the new session is still attached so the brief can be re-queued",
                DelegationReportFormatter.Short(task.Id));
        }

        var cwd = task.WorktreePath ?? task.WorkingDirectory;
        var session = new AgentSession
        {
            Id = Guid.NewGuid(),
            CardId = null,
            WorktreeId = null,
            DefinitionName = program?.DefinitionName ?? "codex",
            AgentKind = program?.Kind ?? task.AgentKind,
            SessionBackend = agent.SessionBackend,
            Status = SessionStatus.Starting,
            Cwd = cwd,
            Cols = _settings.DefaultCols,
            Rows = _settings.DefaultRows,
            CreatedAt = now,
            StartedAt = now,
            LastSeenAt = now,
        };
        _db.AgentSessions.Add(session);

        task.AgentSessionId = session.Id;
        task.DispatchedAt = now;
        task.BootWedgeRelaunchCount++;
        task.ConcurrencyToken = Guid.NewGuid();
        agent.PersistentSessionId = session.Id.ToString("D");
        agent.Status = AgentStatus.Running;
        agent.UpdatedAt = now;

        var limit = Math.Max(1, _settings.BootWedgeRelaunchLimit);
        _db.AgentTaskEvents.Add(new AgentTaskEvent
        {
            Id = Guid.NewGuid(),
            AgentTaskId = task.Id,
            Type = AgentTaskEventType.Warning,
            Detail = $"boot-wedge relaunch {task.BootWedgeRelaunchCount}/{limit}",
            At = now,
        });

        await _db.SaveChangesAsync(ct);

        if (program is { } resolved)
        {
            try
            {
                var attachedBundleKeys = await AgentBundleAttachments.LoadAsync(_db, agent.Id, _logger, ct);
                var spec = await BuildLaunchSpecAsync(task, agent, session, resolved, attachedBundleKeys, ct);
                if (resolved.ProfileId is null && _apiKeyEnvResolver is not null)
                {
                    var projectId = task.ProjectId
                        ?? await _apiKeyEnvResolver.ResolveProjectIdAsync(agent.BoardId, ct);
                    spec = await _apiKeyEnvResolver.ResolveSpecAsync(
                        spec,
                        projectId,
                        $"task {DelegationReportFormatter.Short(task.Id)} boot-wedge relaunch on agent '{agent.Name}'",
                        ct);
                }

                _launchQueue.EnqueueInteractiveSession(session.Id, agent.Id, spec, remoteControlName: null, notes: null);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(
                    ex, "Task {ShortId}: boot-wedge relaunch could not start session {SessionId}",
                    DelegationReportFormatter.Short(task.Id), session.Id);
            }
        }

        var brief = FitBriefForTyping(task, _settings, _ptyProfile?.Ceilings, _logger, session.AgentKind);
        try
        {
            await _queue.EnqueueAsync(
                session.Id, brief, MessageSendMode.WhenIdle, ct, QueuedMessageOrigin.Delegation);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(
                ex, "Task {ShortId}: boot-wedge relaunch brief is queued but could not be delivered yet",
                DelegationReportFormatter.Short(task.Id));
        }

        await _eventBus.PublishToAllAsync(
            "AgentTaskChanged", new { taskId = task.Id, rootId = task.RootTaskId }, ct);
        _logger.LogWarning(
            "Task {ShortId}: boot-wedge relaunch {Count}/{Limit} onto session {SessionId} (old {OldSessionId})",
            DelegationReportFormatter.Short(task.Id), task.BootWedgeRelaunchCount, limit,
            session.Id, oldSessionId);
    }

    /// <summary>
    /// CARD-0299 S2 at the relaunch limit: Fail the task now (~40 s after dispatch on a double
    /// wedge) instead of waiting out the 10-minute watchdog.
    /// </summary>
    internal async Task FailWedgedAtLimitAsync(Guid taskId, Guid sessionId, CancellationToken ct)
    {
        var task = await _db.AgentTasks.FirstOrDefaultAsync(t => t.Id == taskId, ct);
        if (task is null)
            return;

        await FailAndNotifyAsync(task, BootWedgeFailedReason, "boot-wedge", ct);
        try
        {
            await _sessions.KillAsync(sessionId, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(
                ex, "Could not stop boot-wedged session {SessionId} for task {ShortId}",
                sessionId, DelegationReportFormatter.Short(task.Id));
        }
    }

    /// <summary>
    /// Schedule the first check-in on a delegate that has just started running (CARD-0047 §1.2):
    /// <c>NextCheckAt = DispatchedAt + ExpectedDurationMinutes</c>.
    ///
    /// <para>Only for <see cref="AgentTaskReplyTo.Session"/>. A task whose report lands on the board
    /// alone has nobody to deliver a check note to, so arming one would gather facts and throw them
    /// away — <see cref="AgentTask.NextCheckAt"/> stays null and the sweep never sees it.</para>
    ///
    /// <para>This is the ONLY thing <see cref="AgentTask.ExpectedDurationMinutes"/> does. It never
    /// feeds the stall clock, the delivery watchdog, or any status transition: past its expected
    /// duration a task is not late, it has merely reached the point where someone wanted a look.</para>
    /// </summary>
    /// <summary>
    /// CARD-0231: arm the check ramp on a task that Failed before a session existed. The first
    /// look is the ramp base (5 minutes), not <see cref="AgentTask.ExpectedDurationMinutes"/> —
    /// that number describes work that never started. Same guards as <see cref="ArmFirstCheck"/>.
    /// </summary>
    private void ArmFailureReminder(AgentTask task, DateTime now)
    {
        if (!_settings.CheckEnabled || task.ReplyTo != AgentTaskReplyTo.Session)
            return;
        if (task.Role == AgentTaskRole.Check)
            return;

        task.NextCheckAt = now + CheckSchedule.NextInterval(
            _settings, task.ExpectedDurationMinutes, checkNumber: 1);
        task.CheckCount = 0;
    }

    private void ArmFirstCheck(AgentTask claimed, DateTime dispatchedAt)
    {
        if (!_settings.CheckEnabled || claimed.ReplyTo != AgentTaskReplyTo.Session)
            return;

        // RECURSION GUARD. An interpretation task is created with ReplyTo=None, so the line above
        // already declines it — this one is structural rather than incidental, because a future
        // change that gave interpretations a reply route would otherwise silently arm checks ON
        // checks, and each of those would create another interpretation task, forever.
        if (claimed.Role == AgentTaskRole.Check)
            return;

        var expected = Math.Clamp(claimed.ExpectedDurationMinutes, 1, 1440);
        claimed.NextCheckAt = dispatchedAt.AddMinutes(expected);
        claimed.CheckCount = 0;
    }

    /// <summary>
    /// The brief as it will actually be TYPED. A brief past
    /// <see cref="DelegationSettings.BriefInlineMaxBytes"/> is written to a file and replaced by a
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
    ///
    /// <para>Static and internal so the gate itself — not a copy of its arithmetic — can be driven
    /// end to end through a real ConPTY into a fake that CLIPS like the real TUI
    /// (<c>DelegationBriefCeilingPtyTests</c>, CARD-0028). A ceiling nobody has watched survive the
    /// transport is a number in a comment.</para>
    ///
    /// <para><paramref name="ceilings"/> is which pty is on the other end (CARD-0037). Null — every
    /// caller that has no <see cref="PtyDeliveryProfile"/>, which is every test that predates it —
    /// means the inbox conhost and the ceilings that shipped with it. The gate is never widened by
    /// omission.</para>
    ///
    /// <para><paramref name="agentKind"/> is whose COMPOSER is on the other end (CARD-0084 S1), a
    /// separate axis from the pseudoconsole. A kind whose composer is not trusted to keep the lines
    /// we type has an inline ceiling of 0, so its brief ALWAYS takes the spill path below and
    /// reaches it as a file with its structure intact, rather than as one run-on line whose paths
    /// and commands have grown the next line's first word. The gate is <b>default-deny — every kind
    /// except ClaudeCode</b> (CARD-0099 S2): measured for Grok, assumed for Codex and anything else
    /// whose composer nobody has put a canary on. Every production call site passes the session's
    /// own kind; the default keeps every test that predates this rendering byte-identical.</para>
    /// </summary>
    internal static string FitBriefForTyping(
        AgentTask task,
        DelegationSettings settings,
        PtyDeliveryCeilings? ceilings = null,
        ILogger? logger = null,
        AgentKind agentKind = AgentKind.ClaudeCode,
        bool refocus = false)
    {
        var limits = (ceilings ?? settings.CeilingsFor(PtyBackend.InboxConhost, "no pty profile — assuming the default backend"))
            .ForAgentKind(agentKind);
        var brief = DelegationReportFormatter.BuildBrief(task, settings, limits.ReplyInlineMaxChars, refocus);
        // UTF-8 bytes, not string.Length: the read quantum the TUI drops whole is measured in bytes,
        // and an em-dash costs 3 of them (CARD-0027).
        var briefBytes = System.Text.Encoding.UTF8.GetByteCount(brief);
        if (briefBytes <= limits.BriefInlineMaxBytes)
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
            logger?.LogWarning(
                ex, "Task {ShortId}: could not write the brief spill file; pointing at the API instead",
                DelegationReportFormatter.Short(task.Id));
        }

        logger?.LogInformation(
            "Task {ShortId}: brief is {Bytes:N0} UTF-8 bytes (> {Ceiling:N0} — {Ceilings}); delivering a pointer to {Where}",
            DelegationReportFormatter.Short(task.Id), briefBytes, limits.BriefInlineMaxBytes,
            limits, spillPath ?? "the API");

        return DelegationReportFormatter.BuildBriefPointer(task, settings, spillPath, brief.Length, agentKind);
    }

    /// <summary>
    /// Which program a cold launch will start (CARD-0140 S2). For a pinned standing agent with a
    /// TUI profile this is a projection over that profile — and it throws here, before a worktree
    /// is cut or a session row is committed, so a disabled / unvalidated profile cannot leave a
    /// Starting session with no process (the CARD-0056 leak shape). Everything else is the registry
    /// path this method replaced: <c>(task.AgentKind, DefinitionNameForKind(task.AgentKind), null)</c>.
    /// </summary>
    internal readonly record struct DelegateProgram(
        AgentKind Kind,
        string DefinitionName,
        Guid? ProfileId,
        string? ModelArgumentName = null);

    internal async Task<DelegateProgram> ResolveDelegateProgramAsync(AgentTask task, CancellationToken ct)
    {
        if (task.AgentId is Guid pinnedId && _launchResolver is not null)
        {
            var agent = await _db.Agents.AsNoTracking()
                .FirstOrDefaultAsync(a => a.Id == pinnedId, ct);
            if (agent is { IsPoolDelegate: false, TuiProfileId: Guid profileId })
            {
                var profile = await _db.AgentTuiProfiles.AsNoTracking()
                    .Where(p => p.Id == profileId)
                    .Select(p => new
                    {
                        p.Kind,
                        p.IsEnabled,
                        p.ActiveRevisionId,
                        p.SourceDefinitionName,
                        p.DisplayName,
                        ModelArgumentName = p.ActiveRevision != null
                            ? p.ActiveRevision.ModelArgumentName
                            : null,
                    })
                    .FirstOrDefaultAsync(ct);
                if (profile is not null)
                {
                    if (!profile.IsEnabled)
                    {
                        throw new ConflictException(
                            "The selected runner profile is disabled.",
                            "profile_disabled");
                    }

                    if (profile.ActiveRevisionId is null)
                    {
                        throw new ConflictException(
                            "The selected runner profile has no active revision.",
                            "profile_not_validated");
                    }

                    var definitionName = profile.SourceDefinitionName ?? profile.DisplayName;
                    if (profile.Kind != task.AgentKind)
                    {
                        throw new ConflictException(
                            $"Task {DelegationReportFormatter.Short(task.Id)} is {task.AgentKind}, but "
                            + $"its pinned agent now runs {profile.Kind}. Recreate the task so it "
                            + "inherits the agent's current kind.");
                    }

                    return new DelegateProgram(
                        profile.Kind, definitionName, profileId, profile.ModelArgumentName);
                }
            }
        }

        return new DelegateProgram(
            task.AgentKind,
            _agentRegistry.DefinitionNameForKind(task.AgentKind),
            null);
    }

    /// <summary>
    /// Launch args and environment for a delegate. Three things make delegation work here: the tier
    /// as <c>--model &lt;alias&gt;</c>, the standing instructions as <c>--append-system-prompt</c>,
    /// and the ANTIPHON_* env block that lets the delegate call back without being told who it is.
    /// </summary>
    /// <param name="attachedBundleKeys">
    /// Bundles attached to the resolved agent (CARD-0058 slice 6), on top of what its role implies.
    /// A fresh pool delegate is born with none, so in the common case this is empty — it matters
    /// when a task is PINNED to a standing agent, which is how an agent that works the card API
    /// comes to carry <c>board-api</c> without widening the role map for every delegate of its role.
    /// Passed in rather than read off the entity: <c>agent.BundleAttachments</c> on an agent loaded
    /// without the include is empty, and empty is indistinguishable from "none attached".
    /// </param>
    internal AgentLaunchSpec BuildLaunchSpec(
        AgentTask task,
        Agent agent,
        AgentSession session,
        IReadOnlyList<string>? attachedBundleKeys = null,
        IReadOnlyDictionary<string, string>? projectDefaultEnv = null)
    {
        var extraArgs = ComposeDelegateArgs(task, agent, session, attachedBundleKeys);
        var kind = session.AgentKind;

        return _agentRegistry.Resolve(
            // By KIND, not the default definition: the default is only the right answer while every
            // delegate is a Claude, and a missing definition throws rather than substituting one.
            // A pinned standing agent with a TUI profile is resolved earlier
            // (ResolveDelegateProgramAsync) and, from CARD-0140 S3, launched through
            // BuildLaunchSpecAsync; this registry path is the pool and the profile-less standing
            // case. Do not "fix" a missing profile by falling through to the installation default
            // — that is how a Grok task would launch Claude (CARD-0140 D2).
            _agentRegistry.DefinitionNameForKind(kind),
            new AgentLaunchOptions(
                // A Worktree task lives in its worktree — launching in the shared directory would
                // silently defeat the isolation the caller opted into.
                Cwd: task.WorktreePath ?? task.WorkingDirectory,
                Cols: session.Cols,
                Rows: session.Rows,
                ExtraArgs: extraArgs,
                // The agent's own launch env, merged BEFORE ExtraEnv so the ANTIPHON_* block below
                // always wins (CARD-0106 S2). A pool delegate's row carries "{}" and contributes
                // nothing; a pinned standing agent contributes whatever its settings say.
                AgentEnv: AgentLaunchEnv.ParseForAgent(agent),
                ExtraEnv: BuildEnv(task, agent, session),
                LaunchEnvOverride: AgentLaunchEnv.Parse(task.LaunchEnvOverrideJson),
                ProjectDefaultEnv: projectDefaultEnv,
                InheritedEnv: AgentLaunchEnv.Parse(task.InheritedLaunchEnvJson),
                TierModelAlias: TierAliasFor(kind, task.ModelLevel)));
    }

    /// <summary>
    /// Profile-aware sibling of <see cref="BuildLaunchSpec"/> (CARD-0140 D6). The existing
    /// synchronous method keeps the unpinned pool-delegate contract byte-identical for the four
    /// suites that call it; this one takes the managed-profile funnel when
    /// <see cref="DelegateProgram.ProfileId"/> is set.
    /// </summary>
    internal async Task<AgentLaunchSpec> BuildLaunchSpecAsync(
        AgentTask task,
        Agent agent,
        AgentSession session,
        DelegateProgram program,
        IReadOnlyList<string>? attachedBundleKeys,
        CancellationToken ct)
    {
        if (program.ProfileId is null)
        {
            Guid? projectId = task.ProjectId;
            if (projectId is null && _apiKeyEnvResolver is not null)
                projectId = await _apiKeyEnvResolver.ResolveProjectIdAsync(agent.BoardId, ct);
            var defaults = _apiKeyEnvResolver is not null
                ? await _apiKeyEnvResolver.GetProjectDefaultEnvAsync(projectId, ct)
                : null;
            return BuildLaunchSpec(task, agent, session, attachedBundleKeys, defaults);
        }

        // CARD-0182 D2 / CARD-0140 D4: an exact ModelId wins, so the tier alias is omitted and
        // the resolver appends the catalogue-validated model. Two --model flags would otherwise
        // reach the process. The alias is offered through TierModelAlias, never ExtraArgs, so a
        // blank ModelArgumentName can drop it.
        var extraArgs = ComposeDelegateArgs(task, agent, session, attachedBundleKeys);
        var tierModelAlias = string.IsNullOrWhiteSpace(agent.ModelId)
            ? TierAliasFor(session.AgentKind, task.ModelLevel)
            : null;

        Guid? apiKeyProjectId = task.ProjectId;
        if (apiKeyProjectId is null && _apiKeyEnvResolver is not null)
            apiKeyProjectId = await _apiKeyEnvResolver.ResolveProjectIdAsync(agent.BoardId, ct);

        var resolved = await AgentLaunchResolution.ResolveForAgentAsync(
            agent,
            _agentRegistry,
            _launchResolver,
            new AgentLaunchOptions(
                Cwd: task.WorktreePath ?? task.WorkingDirectory,
                Cols: session.Cols,
                Rows: session.Rows,
                ExtraArgs: extraArgs,
                AgentEnv: AgentLaunchEnv.ParseForAgent(agent),
                ExtraEnv: BuildEnv(task, agent, session),
                ApiKeyProjectId: apiKeyProjectId,
                LaunchEnvOverride: AgentLaunchEnv.Parse(task.LaunchEnvOverrideJson),
                InheritedEnv: AgentLaunchEnv.Parse(task.InheritedLaunchEnvJson),
                TierModelAlias: tierModelAlias),
            ct,
            _apiKeyEnvResolver);

        // D7: the session entity is still tracked after the dispatch commit. Without these
        // stamps the drift badge and CARD-0136's usage reader cannot tell which revision a
        // delegate ran under.
        session.TuiProfileRevisionId = resolved.ProfileRevisionId;
        session.EffectiveModelId = resolved.EffectiveModelId;
        await _db.SaveChangesAsync(ct);
        return resolved.Spec;
    }

    private static string? TierAliasFor(AgentKind kind, AgentModelLevel level) =>
        kind == AgentKind.Codex
            ? ModelLevelAliases.ForCodex(level)
            : kind == AgentKind.Grok
                ? ModelLevelAliases.ForGrok(level)
                : kind == AgentKind.ClaudeCode
                    ? ModelLevelAliases.ForClaude(level)
                    : null;

    private List<string> ComposeDelegateArgs(
        AgentTask task,
        Agent agent,
        AgentSession session,
        IReadOnlyList<string>? attachedBundleKeys)
    {
        // WHICH PROGRAM is being launched, read off the session row the dispatch just wrote — the
        // same value BuildEnv, the brief's spill gate and the pool claim all key on, so there is one
        // answer to "what is on the other end of this pty" rather than four (CARD-0084 S3).
        var kind = session.AgentKind;
        var isGrok = kind == AgentKind.Grok;
        var isCodex = kind == AgentKind.Codex;

        var extraArgs = new List<string>();
        // --name is a Claude-only flag; grok.exe rejects it and Codex has no equivalent at all
        // (`codex --help`, cli 0.147.0), so those delegates are nameless on their command line and
        // identified the way everything else already identifies them — the task marker in the brief
        // and ANTIPHON_TASK_ID in the environment.
        if (!isGrok && !isCodex)
            extraArgs.AddRange(["--name", agent.Name]);
        // CARD-0182 D2: the tier alias is no longer appended here. Callers pass it as
        // TierModelAlias so only the resolver (profile path) or AgentRegistry.Resolve
        // (profile-less) can ever emit --model.
        if (isCodex)
        {
            // Explicit, because Codex's own default for the FRONTIER slug is `low` and the operator's
            // config.toml would otherwise decide the tier's depth (CARD-0099 S3).
            extraArgs.AddRange([
                CodexLaunchArgs.ConfigFlag,
                CodexLaunchArgs.ReasoningEffortOverride(task.ModelLevel),
                CodexLaunchArgs.ConfigFlag,
                CodexLaunchArgs.DisablePasteBurst,
            ]);
        }

        // The role's standing instructions, composed from the repo's bundle files at LAUNCH
        // (CARD-0058). Two properties make this the right channel and neither is about size: the
        // system prompt is re-sent on every API call, so the rules survive compaction with no
        // conversational re-injection; and it is composed fresh every launch, so a rule edited in a PR
        // reaches every future delegate with nothing to reconcile. It is an ARGUMENT, never typed, so
        // no pty ceiling applies — the bound is the command line, guarded below.
        //
        // The brief keeps what is EPHEMERAL (the goal, today's known-red tests, what is already
        // landed); a bundle carrying any of that would be wrong tomorrow, for every agent at once.
        // That split is also why a warm-pool reuse composes nothing: it delivers a brief with no
        // launch at all (see ReuseOutcome), so a warm delegate keeps the bundles it started with until
        // it retires — bounded by PoolIdleRetireMinutes, and deliberately not "fixed" by typing
        // bundles into a live session.
        var composed = InstructionBundleComposer.Compose(
            InstructionBundles.ForDelegate(task.Kind, task.Role, attachedBundleKeys));
        var subject = $"Task {DelegationReportFormatter.Short(task.Id)} ({task.Kind}/{task.Role})";
        // Guarded BEFORE anything is added: over-budget throws at compose time and the launch fails
        // loudly. Truncating would run the delegate under half a contract with nothing to show it.
        InstructionBundleComposer.EnsureWithinCommandLineBudget(
            composed, extraArgs, _settings.CommandLineBudgetChars, subject);
        if (!composed.IsEmpty)
        {
            // Grok's system-prompt channel is --rules and Codex's is a `-c developer_instructions=`
            // config override; the flag differs but the contract does not — it is an ARGUMENT in all
            // three cases, so it survives compaction and no pty ceiling applies. Same branch
            // AgentControlService makes for a named Grok or Codex agent.
            extraArgs.AddRange(isCodex
                ? [CodexLaunchArgs.ConfigFlag, CodexLaunchArgs.DeveloperInstructions(composed.Text)]
                : new[] { isGrok ? "--rules" : "--append-system-prompt", composed.Text });
            // Logged because a composition is otherwise invisible from everywhere else: the args are
            // not stored, and the agent row of a pool delegate is deleted when its task settles.
            _logger.LogInformation(
                "{Subject}: launching with instruction bundles {Bundles}",
                subject, string.Join(", ", composed.Stamps));
        }

        return extraArgs;
    }

    /// <summary>
    /// Canonical alias this queued task would launch. A standing agent's exact
    /// <see cref="Agent.ModelId"/> wins when it normalizes; otherwise the tier ladder.
    /// </summary>
    private async Task<string> ResolveDispatchAliasAsync(AgentTask task, CancellationToken ct)
    {
        if (task.AgentId is Guid agentId)
        {
            var agent = await _db.Agents.AsNoTracking()
                .Where(a => a.Id == agentId)
                .Select(a => new { a.ModelId, a.ModelLevel })
                .FirstOrDefaultAsync(ct);
            var fromId = ModelAlias.Normalize(task.AgentKind, agent?.ModelId);
            if (fromId is not null)
                return fromId;
        }

        return ModelLevelAliases.For(task.AgentKind, task.ModelLevel);
    }

    private static string ShippedModelDisplay(AgentTask task, Agent agent, DelegateProgram program)
    {
        if (program.ProfileId is not null && string.IsNullOrWhiteSpace(program.ModelArgumentName))
            return "none (profile owns the model)";
        if (program.ProfileId is not null && !string.IsNullOrWhiteSpace(agent.ModelId))
            return $"{agent.ModelId.Trim()} from agent ModelId";
        return ModelLevelAliases.For(program.Kind, task.ModelLevel);
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
            // CARD-0247 S2: the PreToolUse investigation hook arms on Orchestrator and
            // unarms on any other kind via ANTIPHON_TASK_ID (plan §3.1 rules 2 and 3).
            ["ANTIPHON_TASK_KIND"] = task.Kind.ToString(),
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
            {
                // Reaching here means the reuse path declined this pin and a NEW session is about
                // to be launched on the row — including the one case that lands here on purpose, a
                // pinned pool delegate of the wrong kind (see TryReuseWarmAgentAsync). A pool
                // row must follow the session it is about to own, or the pool would go on offering
                // it as the kind it used to be. A standing agent's Kind is its own (CARD-0138):
                // nothing reads it, and restamping it from the task would undo the profile sync.
                if (existing.IsPoolDelegate)
                    existing.Kind = task.AgentKind;
                return existing;
            }
        }

        // A fresh delegate when no warm one fits: clean context, and --model is a launch arg so a
        // new process is the only way to START a tier. It is born pool-eligible — when its task
        // settles it goes warm instead of dying, until the janitor retires it.
        var shortId = DelegationReportFormatter.Short(task.Id);
        var name = $"task-{shortId}";
        // BoardId is deliberately absent: pool delegates are boardless by design (CARD-0210).
        var agent = new Agent
        {
            Id = Guid.NewGuid(),
            Name = name,
            Slug = name,
            WorkingDirectory = task.WorktreePath ?? task.WorkingDirectory,
            Details = $"Pool delegate for {task.Kind}/{task.Role} task {shortId}.",
            Status = AgentStatus.Idle,
            ModelLevel = task.ModelLevel,
            // Which program this delegate IS, for as long as it lives in the pool. A warm row with
            // the wrong kind here is worse than no row: the next task of that kind would claim it,
            // skip the cold start, and type its brief into a program that cannot read it.
            Kind = task.AgentKind,
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

    /// <summary>
    /// CARD-0085: before a zero-transcript Failed is written, ask the working directory whether
    /// the work actually happened. Recovery needs both the helper (evidence) and the reply
    /// service (settlement it already owns); either missing leaves today's Failed in place so
    /// predating harnesses stay unchanged.
    /// </summary>
    private async Task<bool> TryRecoverBindRefusalAsync(
        AgentTask task, Guid sessionId, CancellationToken ct)
    {
        if (_replies is null || _bindRefusalRecovery is null)
            return false;

        var session = await _db.AgentSessions.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == sessionId, ct);
        var knownSessionIds = await _db.AgentSessions.AsNoTracking()
            .Select(s => s.Id)
            .ToHashSetAsync(ct);
        var evidence = await _bindRefusalRecovery.TryFindAsync(task, session, knownSessionIds, ct);
        if (evidence is null)
            return false;

        await _replies.RecoverFromBindRefusalAsync(task.Id, evidence, ct);
        // Settlement ran on a different scope/DbContext. This tracker still holds the pre-recovery
        // Dispatched entity; detach so a later SaveChanges in this tick cannot clobber Succeeded.
        _db.Entry(task).State = EntityState.Detached;
        _logger.LogWarning(
            "Task {ShortId} recovered from an unbound session ({Evidence}); C1–C4 were not changed. "
            + "Session {SessionId} was not killed.",
            DelegationReportFormatter.Short(task.Id), evidence.Describe(), sessionId);
        return true;
    }

    private async Task FailAsync(
        AgentTask task, string reason, CancellationToken ct, AgentTaskFailureCode? failureCode = null)
    {
        var now = UtcNow();
        task.Status = AgentTaskStatus.Failed;
        task.FailureReason = reason;
        task.FailureCode = failureCode;
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
        // A reused process cannot change its environment. Honouring an override by reusing
        // anyway would mark the task running with the operator's overlay silently ignored.
        if (AgentLaunchEnv.Parse(claimed.LaunchEnvOverrideJson).Count > 0)
        {
            _logger.LogInformation(
                "Task {ShortId} carries a launch-env override — declining warm reuse so a fresh process can apply it",
                DelegationReportFormatter.Short(claimed.Id));
            return ReuseOutcome.SpawnFresh;
        }

        Agent? agent = null;

        if (claimed.AgentId is Guid pinnedId)
        {
            var pinned = await _db.Agents.FirstOrDefaultAsync(a => a.Id == pinnedId, ct);
            // Retired between create and dispatch — the spawn path falls back to a fresh delegate.
            if (pinned is null)
                return ReuseOutcome.SpawnFresh;

            // A STANDING agent (a user's own, or a supervised specialist) is not pool furniture and
            // has its own rules — see PlaceOnStandingAgentAsync.
            if (!pinned.IsPoolDelegate)
                return await PlaceOnStandingAgentAsync(claimed, pinned, now, ct);

            if (await LiveSessionIdOfAsync(pinned, ct) is null)
                return ReuseOutcome.SpawnFresh;

            // CARD-0221: a recovered Worktree delegate is Idle so the janitor can see it, but it
            // must never be handed to a Shared task — even if the directories happened to match.
            if (claimed.Workspace == WorkspaceMode.Shared
                && IsWorktreeWorkingDirectory(pinned.WorkingDirectory))
            {
                _logger.LogInformation(
                    "Task {ShortId} is pinned to worktree delegate '{Agent}' — declining reuse so a Shared task cannot inherit it",
                    DelegationReportFormatter.Short(claimed.Id), pinned.Name);
                return ReuseOutcome.SpawnFresh;
            }

            // A warm delegate of the wrong PROGRAM cannot run this task at all, so waiting for it
            // to free up would wait forever. The spawn path takes it instead — which relaunches
            // this same row on a session of the right kind (ResolveAgentAsync restamps
            // Agent.Kind), rather than typing a Grok brief into a live Claude.
            if (pinned.Kind != claimed.AgentKind)
            {
                _logger.LogInformation(
                    "Task {ShortId} is pinned to '{Agent}' but wants {Wanted} and the agent is {Actual} — relaunching it",
                    DelegationReportFormatter.Short(claimed.Id), pinned.Name, claimed.AgentKind, pinned.Kind);
                return ReuseOutcome.SpawnFresh;
            }

            // This process was launched with the pool row's recorded scope. A live environment
            // cannot be re-resolved for a new brief, so a differently-scoped pin must take the
            // same cold-launch path as a kind mismatch. ResolveAgentAsync keeps the row but the
            // spawn path gives it a fresh session and restamps PoolProjectId.
            if (pinned.PoolProjectId != claimed.ProjectId)
            {
                _logger.LogInformation(
                    "Task {ShortId} is pinned to warm delegate '{Agent}' with pool scope {ActualScope}, but needs {WantedScope} — relaunching it",
                    DelegationReportFormatter.Short(claimed.Id), pinned.Name,
                    pinned.PoolProjectId, claimed.ProjectId);
                return ReuseOutcome.SpawnFresh;
            }

            if (!InheritedEnvMatchesPool(pinned, claimed))
            {
                _logger.LogInformation(
                    "Task {ShortId} is pinned to warm delegate '{Agent}' whose inherited env does not match this task — relaunching it",
                    DelegationReportFormatter.Short(claimed.Id), pinned.Name);
                return ReuseOutcome.SpawnFresh;
            }

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
                    && a.ModelLevel == claimed.ModelLevel
                    // A warm process retains its launch environment. Scope equality includes
                    // null == null: global-only delegates may serve global-only tasks, never a
                    // task that needs project credentials (CARD-0115 S3).
                    && a.PoolProjectId == claimed.ProjectId
                    // Kind is as hard a match as the tier, and for a stronger reason: a tier
                    // mismatch would merely run the work on the wrong model, a kind mismatch would
                    // deliver the brief to a program that is not the one the caller chose.
                    && a.Kind == claimed.AgentKind)
                .ToListAsync(ct);

            agent = warm
                .Where(a => SameDirectory(a.WorkingDirectory, claimed.WorkingDirectory))
                .Where(a => claimed.Workspace != WorkspaceMode.Shared
                    || !IsWorktreeWorkingDirectory(a.WorkingDirectory))
                .Where(a => InheritedEnvMatchesPool(a, claimed))
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
        ArmFirstCheck(claimed, now);

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
            Detail = $"Reused warm delegate '{agent.Name}' "
                + $"({ModelLevelAliases.For(claimed.AgentKind, claimed.ModelLevel)}) "
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
    /// Place a task pinned to a STANDING agent — a user's own agent, or a supervised specialist like
    /// the check interpreter (CARD-0047 slice 4A). The general capability behind "run this on my
    /// standing agent".
    ///
    /// <para>Until this existed, every pinned non-pool agent fell through to <c>SpawnFresh</c>, which
    /// creates a SECOND session and overwrites <see cref="Agent.PersistentSessionId"/>. For an
    /// AlwaysOn agent that is not merely wasteful — it points the row at a session the supervisor
    /// never started while the one it did start keeps running, so the two fight over the row for as
    /// long as both live.</para>
    ///
    /// <para>So: a live session takes the work, and NOTHING on the agent row is written.
    /// <c>PersistentSessionId</c> belongs to whoever launched the session, and
    /// <c>Status</c>/<c>PoolIdleSince</c>/<c>PoolReservedForRootTaskId</c> belong to the pool — a
    /// standing agent is in neither's gift.</para>
    ///
    /// <para>No live session and <see cref="Agent.AlwaysOn"/> means the supervisor is already on it
    /// (its sweep ensures every AlwaysOn agent that is not user-suspended has a session), so the task
    /// waits rather than racing it. A standing agent that nothing supervises keeps today's
    /// <c>SpawnFresh</c> behaviour — there is no one else to bring it up. From CARD-0140 the fresh
    /// session's program is the agent's own TUI profile (via <see cref="ResolveDelegateProgramAsync"/>),
    /// not the task-kind registry default that used to launch <c>claude.exe</c> under a Codex
    /// agent's name.</para>
    /// </summary>
    /// <summary>
    /// Warm-pool reuse may only keep a process whose inherit-list projection matches this
    /// task's snapshot (CARD-0260). Same names and Ordinal values; two empties match.
    /// </summary>
    private bool InheritedEnvMatchesPool(Agent agent, AgentTask task) =>
        AgentLaunchEnv.ProjectionsEqual(
            AgentLaunchEnv.Parse(agent.LaunchEnvJson),
            AgentLaunchEnv.Parse(task.InheritedLaunchEnvJson),
            _settings.LlmEnvInheritance.Names);

    /// <summary>
    /// Standing agents keep the env the operator configured. When that disagrees with the
    /// task's inherited snapshot, record the differing NAMES (never values) so the mismatch
    /// is visible. Empty inherited (follow-up / pin skip) is not a mismatch to warn about.
    /// </summary>
    private void WarnIfStandingInheritedEnvDiffers(AgentTask claimed, Agent standing, DateTime now)
    {
        var names = _settings.LlmEnvInheritance.Names;
        var inherited = AgentLaunchEnv.Parse(claimed.InheritedLaunchEnvJson);
        if (AgentLaunchEnv.FilterTo(inherited, names).Count == 0)
            return;

        var differing = AgentLaunchEnv.DifferingNames(
            inherited,
            AgentLaunchEnv.Parse(standing.LaunchEnvJson),
            names);
        if (differing.Count == 0)
            return;

        _db.AgentTaskEvents.Add(new AgentTaskEvent
        {
            Id = Guid.NewGuid(),
            AgentTaskId = claimed.Id,
            Type = AgentTaskEventType.Warning,
            Detail = $"Standing agent '{standing.Name}' launch env differs from inherited on: "
                + string.Join(", ", differing),
            At = now,
        });
    }

    private async Task<ReuseOutcome> PlaceOnStandingAgentAsync(
        AgentTask claimed, Agent standing, DateTime now, CancellationToken ct)
    {
        if (await LiveSessionIdOfAsync(standing, ct) is not Guid session)
            return standing.AlwaysOn ? ReuseOutcome.WaitForAgent : ReuseOutcome.SpawnFresh;

        // The standing agent's own row is not the evidence here. CARD-0138 keeps that column in
        // sync with the attached TUI profile, but the LIVE SESSION is still strictly better: it
        // names the program that is actually running. Only when that session names a delegate
        // kind: a legacy or hand-seeded row carries the enum's zero (Raw), which is absence of
        // evidence and must not refuse a dispatch (CARD-0084 S3).
        var sessionKind = await _db.AgentSessions.AsNoTracking()
            .Where(s => s.Id == session)
            .Select(s => (AgentKind?)s.AgentKind)
            .FirstOrDefaultAsync(ct);
        if (sessionKind is { } running
            && running != claimed.AgentKind
            && AgentTaskService.DelegatableKinds.Contains(running))
        {
            // Loud, not queued: the pin names an agent that runs a different program, and no amount
            // of waiting changes that. Delivering anyway would type the brief into the wrong TUI.
            throw new InvalidOperationException(
                $"Task {DelegationReportFormatter.Short(claimed.Id)} runs on {claimed.AgentKind}, but "
                + $"it is pinned to agent '{standing.Name}' whose live session {session:D} is "
                + $"{running}. Pin it to a {claimed.AgentKind} agent, or create the task without a "
                + "kind so it runs on a fresh delegate.");
        }

        // One task at a time on the LIVE composer. A brief delivered while the agent is mid-task
        // lands BETWEEN the running task's turns and corrupts both correlations — the same
        // invariant the warm pool holds. Occupancy is that session, not "any Dispatched row on
        // this agent": a Dispatched task whose AgentSessionId is a previous AlwaysOn generation
        // (or a dead session) must not block. That is the CARD-0079 zombie.
        var busy = await _db.AgentTasks.AnyAsync(
            t => t.AgentId == standing.Id
                && t.Id != claimed.Id
                && t.AgentSessionId == session
                && (t.Status == AgentTaskStatus.Dispatched || t.Status == AgentTaskStatus.Working),
            ct);
        if (busy)
            return ReuseOutcome.WaitForAgent;

        WarnIfStandingInheritedEnvDiffers(claimed, standing, now);

        claimed.AgentName = standing.Name;
        claimed.AgentSessionId = session;
        claimed.Status = AgentTaskStatus.Dispatched;
        claimed.DispatchedAt = now;
        claimed.ConcurrencyToken = Guid.NewGuid();
        ArmFirstCheck(claimed, now);

        // The task's own bearer token is discarded rather than rebound: a standing agent's session
        // was not launched by the dispatcher, so its environment carries no ANTIPHON_TASK_TOKEN and
        // never can (a live process's env cannot change). Correlation therefore rides the brief's
        // marker alone — which is all settlement needs, and all a specialist reading a bundle uses.
        AgentTaskService.RawTokens.TryRemove(claimed.Id, out _);

        _db.AgentTaskEvents.Add(new AgentTaskEvent
        {
            Id = Guid.NewGuid(),
            AgentTaskId = claimed.Id,
            Type = AgentTaskEventType.Dispatched,
            ModelLevel = claimed.ModelLevel,
            Detail = $"Delivered into standing agent '{standing.Name}'s live session",
            At = now,
        });

        _logger.LogInformation(
            "Task {ShortId} delivered into standing agent '{Agent}'s live session {SessionId}",
            DelegationReportFormatter.Short(claimed.Id), standing.Name, session);
        return ReuseOutcome.Reused;
    }

    /// <summary>
    /// The brief for a reused session — preceded, when the new work is UNRELATED to what the
    /// session last did, by a focused /compact: shrink the old context down to whatever could help
    /// the new task before the task arrives. Same-run follow-ups skip it — their old context is
    /// exactly the value being reused.
    ///
    /// <para>Each enqueue is independently fault-isolated (CARD-0077). The compact delivers
    /// INLINE on a live idle session (measured 106 s on the live miss), and a single try around
    /// both calls used to lose the brief silently: a non-OCE throw logged a false "queued"
    /// claim, and an HttpClient timeout (TaskCanceledException, an OCE subclass) escaped the
    /// catch entirely. A failure in either is logged accurately and raised as an incident; the
    /// other still runs.</para>
    /// </summary>
    internal async Task DeliverReuseMessagesAsync(AgentTask task, CancellationToken ct)
    {
        if (task.AgentSessionId is not Guid session)
            return;

        var previousRoot = await _db.AgentTasks.AsNoTracking()
            .Where(t => t.AgentSessionId == session
                && t.Id != task.Id
                && t.DispatchedAt != null
                && t.DispatchedAt < task.DispatchedAt)
            .OrderByDescending(t => t.DispatchedAt)
            .Select(t => (Guid?)t.RootTaskId)
            .FirstOrDefaultAsync(ct);

        // The live session's own kind (CARD-0084 S1 / CARD-0117 S3). Looked up BEFORE the compact
        // so a kind that does not implement /compact as housekeeping never receives one. A missing
        // session row is not evidence and sends nothing; the brief's own rendering keeps the
        // existing ?? ClaudeCode default so no current rendering changes.
        var sessionKind = await _db.AgentSessions.AsNoTracking()
            .Where(s => s.Id == session)
            .Select(s => (AgentKind?)s.AgentKind)
            .FirstOrDefaultAsync(ct);

        // A Check task NEVER compacts its session. Every interpretation is its own root, so the
        // "unrelated work" test is true of every single one — and it is exactly wrong here: the
        // specialist's work is homogeneous, and the accumulated experience of reading bundles is
        // the whole reason it is a standing agent rather than a fresh Claude per check.
        var unrelated = task.Role != AgentTaskRole.Check
            && previousRoot is not null && previousRoot != task.RootTaskId;

        var compact = sessionKind is AgentKind kind
            ? ProviderContractCatalog.For(kind).RefocusCompact
            : null;
        var compactSupported = compact is { State: AgentTuiCapabilityState.Supported, Command: not null };

        if (unrelated && compactSupported)
        {
            // One line: a slash command is parsed from the submitted composer text, and the
            // focus argument tells the summariser what the surviving context must serve.
            // Type only the catalog's declared Command — never a guessed body.
            var focus = task.Goal.ReplaceLineEndings(" ").Trim();
            if (focus.Length > 300) focus = focus[..300];
            await TryEnqueueReuseAsync(
                task, session,
                $"{compact!.Command} This session is being handed NEW, unrelated work. Keep only context useful for: {focus}",
                "refocus compact", ct);
        }

        var briefKind = sessionKind ?? AgentKind.ClaudeCode;
        var brief = FitBriefForTyping(
            task, _settings, _ptyProfile?.Ceilings, _logger, briefKind,
            refocus: unrelated && !compactSupported);
        await TryEnqueueReuseAsync(task, session, brief, "brief", ct);
    }

    /// <summary>
    /// CARD-0077 test seam. Production is null and uses <see cref="SessionMessageQueueService"/>.
    /// Tests set this to throw on one body without losing the other.
    /// </summary>
    internal Func<Guid, string, CancellationToken, Task>? ReuseEnqueueOverride { get; set; }

    private async Task TryEnqueueReuseAsync(
        AgentTask task, Guid session, string body, string what, CancellationToken ct)
    {
        try
        {
            if (ReuseEnqueueOverride is { } enqueue)
                await enqueue(session, body, ct);
            else
                await _queue.EnqueueAsync(
                    session, body, MessageSendMode.WhenIdle, ct, QueuedMessageOrigin.Delegation);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Not "queued but could not be delivered yet": EnqueueAsync persists then delivers
            // inline, so a throw here means this row may not exist at all. The spawn path's
            // "rows are persisted before the runner is probed" contract does not hold here.
            _logger.LogWarning(
                ex, "Task {ShortId}: reuse {What} was not queued ({Message})",
                DelegationReportFormatter.Short(task.Id), what, ex.Message);
            await RecordReuseEnqueueFailedAsync(task, session, what, ex, ct);
        }
    }

    private async Task RecordReuseEnqueueFailedAsync(
        AgentTask task, Guid session, string what, Exception ex, CancellationToken ct)
    {
        if (task.AgentId is not Guid agentId)
            return;

        var message =
            $"Reuse {what} was not queued for task {DelegationReportFormatter.Short(task.Id)}: {ex.Message}";
        _db.AgentIncidents.Add(new AgentIncident
        {
            Id = Guid.NewGuid(),
            AgentId = agentId,
            SessionId = session,
            Kind = AgentIncidentKind.DeliveryTransportFailed,
            Severity = AlertSeverity.Warning,
            Message = message,
            FailureReason = ex.GetType().Name,
            CreatedAt = UtcNow(),
        });
        await _db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Retire warm delegates that outstayed their welcome: idle past the TTL, or beyond the
    /// per-directory cap (oldest first). This bound is what makes "keep Claudes warm" a trade
    /// instead of a leak.
    ///
    /// <para>CARD-0221: rows that carry an <see cref="AgentIncident"/> are left <c>Stopped</c>
    /// instead of deleted (incidents cascade with the agent). A second pass sweeps pool delegates
    /// whose session is already terminal and who have no open task — the eight CARD-0085 Worktree
    /// rows that recovery used to abandon as <c>Running</c>.</para>
    /// </summary>
    internal async Task<int> RetireIdleWarmAgentsAsync(CancellationToken ct)
    {
        var pool = await _db.Agents
            .Where(a => a.IsPoolDelegate && a.Status != AgentStatus.Stopped)
            .ToListAsync(ct);
        if (pool.Count == 0)
            return 0;

        var now = UtcNow();
        var cutoff = now.AddMinutes(-Math.Max(1, _settings.PoolIdleRetireMinutes));
        var warm = pool
            .Where(a => a.Status == AgentStatus.Idle && a.PoolIdleSince != null)
            .ToList();
        var retire = new HashSet<Agent>(warm.Where(a => !_settings.PoolEnabled || a.PoolIdleSince <= cutoff));

        // Per (directory, KIND), not per directory: the cap bounds how many warm processes of one
        // program sit in one place, and two programs are two pools. Counting them together would
        // let three warm Claudes evict the only warm Grok in that directory — retiring the delegate
        // no Claude task could ever have used, and leaving the cap spent on rows the Grok tasks
        // there cannot claim.
        foreach (var surplus in warm
            .GroupBy(a => (
                Directory: DelegationWorkspaceResolver.NormalizeSeparators(a.WorkingDirectory).ToUpperInvariant(),
                a.Kind))
            .SelectMany(g => g.OrderByDescending(a => a.PoolIdleSince)
                .Skip(Math.Max(0, _settings.PoolMaxIdlePerDirectory))))
        {
            retire.Add(surplus);
        }

        var parsed = pool
            .Select(a => (
                Agent: a,
                SessionId: Guid.TryParse(a.PersistentSessionId, out var sid) ? sid : (Guid?)null))
            .ToList();
        var sessionIds = parsed
            .Where(p => p.SessionId.HasValue)
            .Select(p => p.SessionId!.Value)
            .Distinct()
            .ToList();
        var liveSessionIds = new HashSet<Guid>();
        if (sessionIds.Count > 0)
        {
            foreach (var id in await _db.AgentSessions.AsNoTracking()
                .Where(s => sessionIds.Contains(s.Id)
                    && (s.Status == SessionStatus.Starting || s.Status == SessionStatus.Running))
                .Select(s => s.Id)
                .ToListAsync(ct))
            {
                liveSessionIds.Add(id);
            }
        }

        var agentIds = pool.Select(a => a.Id).ToList();
        var busyAgentIds = (await _db.AgentTasks.AsNoTracking()
            .Where(t => t.AgentId != null
                && agentIds.Contains(t.AgentId.Value)
                && (t.Status == AgentTaskStatus.Queued
                    || t.Status == AgentTaskStatus.Dispatched
                    || t.Status == AgentTaskStatus.Working
                    || t.Status == AgentTaskStatus.Blocked))
            .Select(t => t.AgentId!.Value)
            .Distinct()
            .ToListAsync(ct))
            .ToHashSet();

        var stale = new HashSet<Agent>();
        foreach (var row in parsed)
        {
            if (retire.Contains(row.Agent))
                continue;
            if (row.SessionId is Guid sid && liveSessionIds.Contains(sid))
                continue;
            if (busyAgentIds.Contains(row.Agent.Id))
                continue;
            stale.Add(row.Agent);
        }

        var incidentAgentIds = (await _db.AgentIncidents.AsNoTracking()
            .Where(i => i.AgentId != null && agentIds.Contains(i.AgentId.Value))
            .Select(i => i.AgentId!.Value)
            .Distinct()
            .ToListAsync(ct))
            .ToHashSet();

        var acted = 0;
        foreach (var agent in retire)
        {
            var idleSince = agent.PoolIdleSince;
            await KillPooledSessionAsync(agent, ct);
            FinishPoolRetire(agent, incidentAgentIds.Contains(agent.Id), now);
            _logger.LogInformation(
                "Retired warm delegate '{Name}' from {Dir} (idle since {Since:O})",
                agent.Name, agent.WorkingDirectory, idleSince);
            acted++;
        }

        foreach (var agent in stale)
        {
            // Session is already terminal in the DB. Do not KillAsync: Failed is
            // SessionReconciliationService's re-adopt arm (CARD-0056), Stopped is its only
            // auto-kill. The agent row is the junk this pass is for.
            FinishPoolRetire(agent, incidentAgentIds.Contains(agent.Id), now);
            _logger.LogInformation(
                "Swept stale pool delegate '{Name}' (session not live, no open task)",
                agent.Name);
            acted++;
        }

        if (acted > 0)
            await _db.SaveChangesAsync(ct);
        return acted;
    }

    private async Task KillPooledSessionAsync(Agent agent, CancellationToken ct)
    {
        if (!Guid.TryParse(agent.PersistentSessionId, out var sessionId))
            return;
        try
        {
            await _sessions.KillAsync(sessionId, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Could not stop pooled session {SessionId}", sessionId);
        }
    }

    private void FinishPoolRetire(Agent agent, bool hasIncident, DateTime now)
    {
        if (hasIncident)
        {
            agent.Status = AgentStatus.Stopped;
            agent.PoolIdleSince = null;
            agent.PoolReservedForRootTaskId = null;
            agent.UpdatedAt = now;
            return;
        }

        _db.Agents.Remove(agent);
    }

    /// <summary>
    /// Antiphon worktree leaf: <c>card-task-</c> plus 8 hex. A recovered Worktree delegate sits
    /// Idle in one of these so the janitor can see it; reuse must not hand it to a Shared task.
    /// </summary>
    internal static bool IsWorktreeWorkingDirectory(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;
        var name = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (name.Length != 18 || !name.StartsWith("card-task-", StringComparison.OrdinalIgnoreCase))
            return false;
        for (var i = 10; i < 18; i++)
        {
            if (!char.IsAsciiHexDigit(name[i]))
                return false;
        }

        return true;
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
    /// The repo's declared areas, or an empty map when nothing is registered to read one (a
    /// harness that predates CARD-0063 S2) — in which case a name compares as an opaque label,
    /// exactly as in S1.
    /// </summary>
    private AreaMap AreasFor(string? repoPath) => _areas?.Load(repoPath) ?? AreaMap.Empty;

    /// <summary>
    /// Advisory lease check: same repo and overlapping scopes. Kept as a two-tuple entry point for
    /// the unit tests that exercise the comparison alone; the parse-and-compare rules live in
    /// <see cref="ScopeResolver"/>.
    /// </summary>
    internal static bool ScopesIntersect((string Key, string Scope) a, (string Key, string Scope) b) =>
        SameRepo(a.Key, b.Key) && ScopeResolver.Intersects(a.Scope, b.Scope);

    /// <inheritdoc cref="ScopesIntersect((string, string), (string, string))"/>
    internal static bool ScopesIntersect(
        (string Key, string Scope) a, (string Key, string Scope) b, AreaMap map) =>
        SameRepo(a.Key, b.Key)
        && ScopeResolver.Intersects(
            ScopeResolver.Resolve(a.Scope, map), ScopeResolver.Resolve(b.Scope, map));

    private static bool SameRepo(string a, string b) =>
        string.Equals(
            DelegationWorkspaceResolver.NormalizeSeparators(a),
            DelegationWorkspaceResolver.NormalizeSeparators(b),
            StringComparison.OrdinalIgnoreCase);

    private DateTime UtcNow() => _timeProvider.GetUtcNow().UtcDateTime;
}
