using System.Security.Cryptography;
using System.Text;
using Antiphon.Agents.Pty;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
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
    // CARD-0136. Optional so every harness that predates this card keeps constructing this.
    private readonly SubscriptionQuotaGate? _quotaGate;
    // CARD-0022. Same optional contract: absent, create does not consult model holds.
    private readonly ModelAvailability? _modelAvailability;
    // CARD-0063 S2. Optional for the same reason; absent, every scope name is an opaque label.
    private readonly AreaMapLoader? _areas;
    // CARD-0263 S2. Optional so focused harnesses without API-key plumbing retain their setup.
    private readonly ApiKeyEnvResolver? _apiKeyEnvResolver;
    // CARD-0305. Same optional contract: absent, create resolves routing exactly as it does today
    // and no pin is ever consulted.
    private readonly RoutingPinService? _routingPins;
    // CARD-0090. Same optional contract: absent, -Complexity is 422 (no walker to consult).
    private readonly ComplexityRoutingService? _complexityRouting;
    // CARD-0324. Optional so predating harnesses keep constructing this; absent, the
    // create-time Grok store probe uses the shipped default (enabled).
    private readonly AgentRegistrySettings? _registrySettings;
    // CARD-0033. Optional so predating harnesses keep constructing this; absent, blocked
    // progress degrades to Unavailable rather than failing the drawer GET.
    private readonly DelegateCheckProbe? _checkProbe;
    // CARD-0352 S3. Optional so predating harnesses keep constructing this; absent, create
    // never queues a title diagnosis.
    private readonly DiagnoseQueue? _diagnoseQueue;
    // CARD-0147. Optional so every harness that predates this card keeps constructing this;
    // absent, create does not consult the fleet/role cap.
    private readonly DelegationOpenGate? _openGate;

    public AgentTaskService(
        AppDbContext db,
        DelegationWorkspaceResolver workspace,
        IOptions<DelegationSettings> settings,
        IEventBus eventBus,
        IDelegateSessionStopper sessions,
        TimeProvider timeProvider,
        ILogger<AgentTaskService> logger,
        SubscriptionQuotaGate? quotaGate = null,
        AreaMapLoader? areas = null,
        ApiKeyEnvResolver? apiKeyEnvResolver = null,
        ModelAvailability? modelAvailability = null,
        RoutingPinService? routingPins = null,
        ComplexityRoutingService? complexityRouting = null,
        IOptions<AgentRegistrySettings>? registrySettings = null,
        DelegateCheckProbe? checkProbe = null,
        DiagnoseQueue? diagnoseQueue = null,
        DelegationOpenGate? openGate = null)
    {
        _areas = areas;
        _db = db;
        _workspace = workspace;
        _settings = settings.Value;
        _eventBus = eventBus;
        _sessions = sessions;
        _timeProvider = timeProvider;
        _logger = logger;
        _quotaGate = quotaGate;
        _apiKeyEnvResolver = apiKeyEnvResolver;
        _modelAvailability = modelAvailability;
        _routingPins = routingPins;
        _complexityRouting = complexityRouting;
        _registrySettings = registrySettings?.Value;
        _checkProbe = checkProbe;
        _diagnoseQueue = diagnoseQueue;
        _openGate = openGate;
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

        var standingAuthority = string.IsNullOrWhiteSpace(request.Authority)
            ? null
            : request.Authority.Trim();
        if (standingAuthority is { Length: > BlockedNote.AuthorityMaxChars })
        {
            throw new ValidationException(
                nameof(request.Authority),
                $"Standing authority must be at most {BlockedNote.AuthorityMaxChars} characters (got {standingAuthority.Length}).");
        }

        if (request.AutoContinue && standingAuthority is null)
        {
            throw new ValidationException(
                nameof(request.AutoContinue),
                "AutoContinue requires Authority — the flag names what to replay, and there is nothing to replay without it.",
                "auto_continue_needs_authority");
        }

        var launchEnvOverride = AgentLaunchEnv.ValidateOverride(
            request.LaunchEnvOverride, "launchEnvOverride");
        var suppliedInheritedLlmEnv = AgentLaunchEnv.ValidateOverride(
            request.InheritedLlmEnv, "inheritedLlmEnv");
        // A live follow-up and a standing-agent pin continue an existing process — snapshotting
        // the caller's env onto them would record a routing the live process cannot take. A
        // follow-up whose prior agent has retired is deliberately a fresh dispatch, so it keeps
        // normal launch-env semantics.
        var skipInheritedSnapshot = false;

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

        Agent? subscriptionOwner = null;
        Agent? pinnedStandingAgent = null;
        // CARD-0040: a follow-up continues the earlier task's work, so it continues its card too.
        Guid? followUpCardId = null;
        string? followUpMessage = null;
        var liveFollowUp = false;

        // CARD-0291: a standing agent named by -Agent resolves HERE, before the CARD-0140 pin
        // block, so that path receives a plain AgentId and nothing downstream changes. Refusals
        // over silent reinterpretation throughout: this field exists because work handed to a
        // named child over raw session messages reports to nobody.
        if (!string.IsNullOrWhiteSpace(request.Agent))
        {
            if (!string.IsNullOrWhiteSpace(request.FollowUpOnTask))
            {
                throw new ValidationException(
                    nameof(request.Agent),
                    "Agent and FollowUpOnTask are two different \"run it on that agent\" idioms — "
                    + "a follow-up already pins to the agent that ran the prior task. Use one or "
                    + "the other.");
            }

            var resolvedAgent = await ResolveStandingAgentAsync(request.Agent, ct);
            if (request.AgentId is Guid explicitPinId && explicitPinId != resolvedAgent.Id)
            {
                throw new ValidationException(
                    nameof(request.Agent),
                    $"Agent '{request.Agent}' resolves to '{resolvedAgent.Name}' "
                    + $"({resolvedAgent.Id}), but agentId names {explicitPinId}. Drop one of them "
                    + "or make them agree.");
            }

            request = request with { AgentId = resolvedAgent.Id };
        }

        // Follow-up: run on the SAME agent that ran an earlier task, keeping its context. The
        // task inherits that agent's directory (that is where the context lives) and its TIER —
        // the model is already running; a role policy cannot change it mid-session.
        if (!string.IsNullOrWhiteSpace(request.FollowUpOnTask))
        {
            var priorId = await ResolveTaskIdAsync(request.FollowUpOnTask, ct);
            var prior = await _db.AgentTasks.AsNoTracking().FirstAsync(t => t.Id == priorId, ct);
            followUpCardId = prior.CardId;
            var followAgent = prior.AgentId is Guid followAgentId
                ? await _db.Agents.AsNoTracking().FirstOrDefaultAsync(a => a.Id == followAgentId, ct)
                : null;

            if (followAgent is null)
            {
                var completionHeader = await CompletionHeaderAsync(prior.Id, ct);
                var cardIdentifier = prior.CardId is Guid cardId
                    ? await _db.Cards.AsNoTracking()
                        .Where(card => card.Id == cardId)
                        .Select(card => card.Identifier)
                        .FirstOrDefaultAsync(ct)
                    : null;
                request = request with
                {
                    Goal = BuildInheritedFollowUpGoal(prior, completionHeader, cardIdentifier, request.Goal),
                };
                followUpMessage = "agent retired - fresh delegate with inherited context";
            }
            else
            {
                liveFollowUp = true;
                skipInheritedSnapshot = true;
                if (launchEnvOverride.Count > 0)
                {
                    throw new ValidationException(
                        "launchEnvOverride",
                        "A follow-up continues an existing process, so a launch-time env override cannot "
                        + "apply. Drop the override, or PATCH the agent's launchEnv for a durable change.");
                }

                subscriptionOwner = followAgent;

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
                followUpMessage = "follow-up on the live agent";
            }
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
            var message = AugmentWorktreeRejection(request, ex.Message);
            if (parent is not null)
                await RecordRejectionAsync(parent, message, ct);
            throw new ValidationException(nameof(request.WorkingDirectory), message);
        }

        if (request.Workspace == WorkspaceMode.Worktree && resolved.RepoPath is null)
        {
            throw new ValidationException(
                nameof(request.Workspace),
                $"'{resolved.WorkingDirectory}' is not a git repository, so there is nothing to branch. "
                + "Use the default shared workspace instead.");
        }

        var (workspace, warning) = ResolveWorkspace(request, caller, resolved);

        // CARD-0040. Resolved BEFORE the row so an explicit -Card that names nothing is a 422 on
        // creation rather than a task that runs with a binding its caller thinks it has. It is
        // resolved HERE, ahead of tier/kind, because a CARD-0305 routing pin is keyed on the card
        // and must decide the routing before the role policy fills anything in. Its warning is
        // still appended below, in the order it always was.
        var title = BuildTitle(request);
        var binding = await AgentTaskCardBinder.BindAsync(
            _db,
            request.Card,
            new AgentTaskCardBinder.Context(
                request.Role,
                title,
                // A parent's card outranks a follow-up's: a child created by an orchestrator that
                // is itself bound is working the orchestrator's card unless its title says otherwise.
                parent?.CardId ?? followUpCardId,
                caller.SessionId,
                resolved.RepoPath,
                resolved.WorkingDirectory),
            ct);

        // CARD-0090: an explicit pair is a single candidate the caller chose, and the shipped
        // rule is that an explicit choice is never silently rerouted. One or the other.
        if (request.Complexity is not null)
        {
            if (request.AgentKind is not null || request.ModelLevel is not null)
            {
                throw new ValidationException(
                    nameof(request.Complexity),
                    "complexity cannot be combined with agentKind or modelLevel. An explicit pair "
                    + "is a single candidate the caller chose and is never silently rerouted. Pass "
                    + "-Complexity without -Kind/-Level, or pass the pair without -Complexity.");
            }

            if (request.IgnoreModelDisabled)
            {
                throw new ValidationException(
                    nameof(request.IgnoreModelDisabled),
                    "ignoreModelDisabled cannot be combined with complexity: a chain skips a held "
                    + "candidate, so there is nothing to ignore. Omit the flag, or pass an explicit "
                    + "-Kind/-Level instead of -Complexity.");
            }

            if (_complexityRouting is null)
            {
                throw new ValidationException(
                    nameof(request.Complexity),
                    "complexity chains are not available in this host.");
            }
        }

        // CARD-0305: the standing instruction for THIS card+role (else this role's stage-wide
        // one). It fills what the caller left open and refuses what disagrees with a Required
        // human pin — before the role policy, the quota gate and the CARD-0309 hold, so the alias
        // Require sees is the pinned one. A live follow-up is already running as whatever it was
        // launched as, so its inherited kind/level are the "request" a pin is compared against.
        var pinDecision = RoutingPinService.Decision.None;
        if (_routingPins is not null)
        {
            pinDecision = await _routingPins.ResolveAsync(
                binding.CardId,
                request.Role,
                new RoutingPinService.Ask(
                    request.AgentKind, request.ModelLevel, request.AgentId, request.IgnoreRoutingPin),
                ct);
            if (pinDecision.Applied)
            {
                request = request with
                {
                    // A complexity walk composes the pin itself; overlaying kind/level here
                    // would make a Preferred pin look like an explicit request.
                    AgentKind = request.Complexity is null
                        ? pinDecision.AgentKind ?? request.AgentKind
                        : request.AgentKind,
                    ModelLevel = request.Complexity is null
                        ? pinDecision.ModelLevel ?? request.ModelLevel
                        : request.ModelLevel,
                    // Only when the caller named none: an explicit -Agent has already been
                    // reconciled against the pin above (or refused).
                    AgentId = liveFollowUp ? request.AgentId : request.AgentId ?? pinDecision.AgentId,
                };
            }

            if (pinDecision.Warning is { } pinWarning)
                warning = warning is null ? pinWarning : warning + " " + pinWarning;
        }

        // CARD-0140 S1: a bare pin to a STANDING agent settles kind the same way a follow-up
        // does. Unset inherits the agent's Kind (which CARD-0138 keeps equal to its profile);
        // an explicit mismatch is refused rather than silently reinterpreted. Pool delegates
        // are carved out — FollowUpOnTask already covers "same delegate again", and
        // TryReuseWarmAgentAsync plus ResolveAgentAsync own the kind-mismatch relaunch.
        if (!liveFollowUp && request.AgentId is Guid pinId)
        {
            var pinned = await _db.Agents.AsNoTracking()
                .FirstOrDefaultAsync(a => a.Id == pinId, ct);
            subscriptionOwner = pinned;
            if (pinned is { IsPoolDelegate: false })
            {
                skipInheritedSnapshot = true;
                pinnedStandingAgent = pinned;
                if (request.AgentKind is { } wantedKind && wantedKind != pinned.Kind)
                {
                    throw new ConflictException(
                        $"Agent '{pinned.Name}' is {pinned.Kind}, so a task pinned to it cannot run on "
                        + $"{wantedKind}. Pin it to a {wantedKind} agent, or omit agentKind to inherit "
                        + $"{pinned.Kind}.");
                }

                request = request with { AgentKind = pinned.Kind };
            }
        }

        var id = Guid.NewGuid();
        ComplexityRoutingService.Walk? routingWalk = null;
        var routingExhausted = false;
        AgentModelLevel level;
        AgentKind agentKind;
        if (request.Complexity is { } complexity)
        {
            routingWalk = await _complexityRouting!.WalkAsync(
                complexity,
                request.Kind,
                request.Role,
                pinDecision,
                binding.CardId,
                subscriptionOwner,
                request.IgnoreSubscriptionQuota,
                ct);

            if (pinDecision.Applied
                && pinDecision.Pin!.Strength == RoutingPinStrength.Required
                && routingWalk.Source.StartsWith("pin:", StringComparison.Ordinal))
            {
                var bypass =
                    $"complexity chain bypassed by {RoutingPinService.Describe(pinDecision.Pin, pinDecision.CardIdentifier)}.";
                warning = warning is null ? bypass : warning + " " + bypass;
            }

            if (routingWalk.Chosen is { } chosen)
            {
                agentKind = chosen.Kind;
                level = chosen.Level;
                var skipped = routingWalk.SkippedWarning();
                if (skipped.Length > 0)
                    warning = warning is null ? skipped : warning + " " + skipped;
            }
            else if (pinDecision.Applied
                && pinDecision.Pin!.Strength == RoutingPinStrength.Required
                && !routingWalk.Walked)
            {
                // Required pin, single candidate: today's 409s, never Blocked.
                agentKind = routingWalk.Outcomes.Count > 0
                    ? routingWalk.Outcomes[0].Candidate.Kind
                    : ResolveAgentKind(request.Kind, request.Role, request.AgentKind);
                level = routingWalk.Outcomes.Count > 0
                    ? routingWalk.Outcomes[0].Candidate.Level
                    : ResolveLevel(request.Kind, request.Role, request.ModelLevel);
                _routingPins?.EnforceForbiddenAliases(pinDecision, agentKind, level);
                if (_quotaGate is not null)
                {
                    await _quotaGate.EnforceAsync(
                        agentKind,
                        SubscriptionUsageKey.For(subscriptionOwner, agentKind),
                        request.IgnoreSubscriptionQuota,
                        $"task '{BuildTitle(request)}'",
                        ct);
                }

                if (_modelAvailability is not null)
                {
                    var alias = ModelLevelAliases.For(agentKind, level);
                    try
                    {
                        await _modelAvailability.RequireAsync(agentKind, alias, ct);
                    }
                    catch (ModelDisabledException ex)
                    {
                        throw ex.WithCoda(
                            $"this work is pinned to {alias} by {RoutingPinService.Describe(pinDecision.Pin, pinDecision.CardIdentifier)} "
                            + $"(\"{pinDecision.Pin.Reason}\") — the available list does not satisfy the pin. "
                            + "Wait for the hold to clear, pass ignoreModelDisabled to queue it anyway, or "
                            + "replace the pin.");
                    }
                }
            }
            else if (request.RefuseIfExhausted)
            {
                throw new RoutingExhaustedException(
                    routingWalk.ExhaustedSentence()
                    + " A human decides: clear a hold, wait for a reset, or POST /api/agent-tasks/{id}/reroute. "
                    + "Do NOT pick a kind yourself.",
                    routingWalk.ToDto());
            }
            else
            {
                routingExhausted = true;
                if (routingWalk.Outcomes.Count > 0)
                {
                    agentKind = routingWalk.Outcomes[0].Candidate.Kind;
                    level = routingWalk.Outcomes[0].Candidate.Level;
                }
                else
                {
                    agentKind = ResolveAgentKind(request.Kind, request.Role, request.AgentKind);
                    level = ResolveLevel(request.Kind, request.Role, request.ModelLevel);
                }
            }
        }
        else
        {
            level = ResolveLevel(request.Kind, request.Role, request.ModelLevel);
            agentKind = ResolveAgentKind(request.Kind, request.Role, request.AgentKind);
            // The stage-wide forbid list bites the alias that was ACTUALLY resolved, so it runs after
            // the role policy has filled in whatever the request and the pin both left open.
            _routingPins?.EnforceForbiddenAliases(pinDecision, agentKind, level);
            if (_quotaGate is not null)
            {
                var quotaKey = SubscriptionUsageKey.For(subscriptionOwner, agentKind);
                var quotaOverride = await _quotaGate.EnforceAsync(
                    agentKind,
                    quotaKey,
                    request.IgnoreSubscriptionQuota,
                    $"task '{BuildTitle(request)}'",
                    ct);
                if (quotaOverride is not null)
                {
                    var quotaWarning = SubscriptionQuotaPolicy.FormatOverride(quotaOverride);
                    warning = warning is null ? quotaWarning : warning + " " + quotaWarning;
                }
            }

            if (_modelAvailability is not null)
            {
                var alias = ModelLevelAliases.For(agentKind, level);
                if (pinnedStandingAgent is { ModelId: { Length: > 0 } modelId }
                    && ModelAlias.Normalize(agentKind, modelId) is { } pinned)
                {
                    alias = pinned;
                }

                if (request.IgnoreModelDisabled)
                {
                    var hold = await _modelAvailability.GetActiveHoldAsync(agentKind, alias, ct);
                    if (hold is not null)
                    {
                        var name = hold.ModelAlias == ModelAlias.KindWide
                            ? hold.Kind.ToString()
                            : hold.ModelAlias;
                        var untilBit = hold.DisabledUntil is { } until
                            ? $"until {until:yyyy-MM-ddTHH:mm:ssZ}"
                            : "(no re-enable time)";
                        var holdWarning =
                            $"{name} is held {untilBit}; queued, will dispatch when the hold clears (ignoreModelDisabled).";
                        warning = warning is null ? holdWarning : warning + " " + holdWarning;
                    }
                }
                else
                {
                    // CARD-0305 handshake: the pin decided the alias, the hold decides whether it may
                    // run. A held alias that a Required pin named is STILL 409 model_disabled with the
                    // available list — never a silent reroute onto something the pin excludes — but the
                    // sentence says the list does not satisfy the pin, so the operator knows waiting,
                    // ignoreModelDisabled, or REPLACING the pin are the three real options.
                    try
                    {
                        await _modelAvailability.RequireAsync(agentKind, alias, ct);
                    }
                    catch (ModelDisabledException ex)
                        when (pinDecision.Applied
                            && pinDecision.Pin!.Strength == RoutingPinStrength.Required)
                    {
                        throw ex.WithCoda(
                            $"this work is pinned to {alias} by {RoutingPinService.Describe(pinDecision.Pin, pinDecision.CardIdentifier)} "
                            + $"(\"{pinDecision.Pin.Reason}\") — the available list does not satisfy the pin. "
                            + "Wait for the hold to clear, pass ignoreModelDisabled to queue it anyway, or "
                            + "replace the pin.");
                    }
                }
            }
        }

        // A task token always carries the parent task's identity, including null. The work may run
        // in another checkout, but its commissioning project — not its filesystem path — decides
        // the eventual API-key scope (CARD-0115 S1).
        var projectId = parent is null
            ? await DeriveCallerProjectAsync(caller, ct)
            : parent.ProjectId;
        var now = UtcNow();
        var (token, tokenHash) = NewToken();

        if (binding.Warning is not null)
            warning = warning is null ? binding.Warning : warning + " " + binding.Warning;

        var inheritedLaunchEnv = skipInheritedSnapshot
            ? AgentLaunchEnv.Empty
            : request.InheritedLlmEnv is not null
                ? FilterSuppliedInheritedLlmEnv(suppliedInheritedLlmEnv)
                : await ComputeInheritedLlmEnvAsync(caller, false, ct);

        if (!skipInheritedSnapshot)
        {
            var projectDefaultEnv = _apiKeyEnvResolver is null
                ? AgentLaunchEnv.Empty
                : await _apiKeyEnvResolver.GetProjectDefaultEnvAsync(projectId, ct);
            var proxyWarning = ValidateLlmProxyPreview(
                projectDefaultEnv,
                inheritedLaunchEnv,
                AgentLaunchEnv.ParseForAgent(pinnedStandingAgent),
                launchEnvOverride,
                agentKind);
            if (proxyWarning is not null)
                warning = warning is null ? proxyWarning : warning + " " + proxyWarning;
        }

        var unknownInheritedNamesWarning = UnknownInheritedNamesWarning(suppliedInheritedLlmEnv);
        if (unknownInheritedNamesWarning is not null)
            warning = warning is null ? unknownInheritedNamesWarning : warning + " " + unknownInheritedNamesWarning;

        var task = new AgentTask
        {
            Id = id,
            RootTaskId = parent?.RootTaskId ?? id,
            ParentTaskId = parent?.Id,
            // Where the report goes. The dispatcher fills the parent's session id at dispatch time
            // for a task created before its parent's session existed.
            ParentSessionId = caller.SessionId,
            Depth = depth,
            Title = title,
            Goal = request.Goal.Trim(),
            Kind = request.Kind,
            Role = request.Role,
            ProjectId = projectId,
            CardId = binding.CardId,
            LaunchEnvOverrideJson = AgentLaunchEnv.Serialize(launchEnvOverride),
            InheritedLaunchEnvJson = AgentLaunchEnv.Serialize(inheritedLaunchEnv),
            AgentKind = agentKind,
            ModelLevel = level,
            Complexity = request.Complexity,
            Workspace = workspace,
            DenyDirectEdits = request.DenyDirectEdits,
            WorkingDirectory = resolved.WorkingDirectory,
            RepoPath = resolved.RepoPath,
            Scope = string.IsNullOrWhiteSpace(request.Scope) ? null : request.Scope.Trim(),
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
            StandingAuthority = standingAuthority,
            AutoContinueOnWait = request.AutoContinue && standingAuthority is not null,
        };

        var repeatOf = await FindLaunchFailureRepeatAsync(
            task.CardId, task.Goal, task.Kind, task.Role, task.AgentKind, ct);
        if (repeatOf is null && !routingExhausted)
        {
            await RefuseUnauthenticatedGrokAsync(
                agentKind, task.AgentId, request.LaunchEnvOverride, request.InheritedLlmEnv,
                request.AllowUnauthenticatedProvider, ct);
        }

        if (repeatOf is not null)
        {
            task.Status = AgentTaskStatus.Blocked;
            task.FailureReason = RepeatBlockReason(repeatOf, task.AgentKind);
        }
        else if (routingExhausted && routingWalk is not null)
        {
            task.Status = AgentTaskStatus.Blocked;
            task.FailureReason = routingWalk.ExhaustedSentence()
                + " A human must choose; do not pick a kind yourself.";
            task.AgentSessionId = null;
        }

        // CARD-0147: refuse at create, not at the dispatcher tick. Specialists and live
        // follow-ups are sequential continuation / no new process — they skip the gate.
        // The lock is held only across count+insert, not the HTTP-ish work above.
        var gateCreate = _openGate is not null
            && !AgentTaskRoles.IsSpecialist(request.Role)
            && !liveFollowUp;
        IDbContextTransaction? gateTx = null;
        DelegationOpenGate.Snapshot? openSnapshot = null;
        if (gateCreate)
        {
            gateTx = _db.Database.CurrentTransaction is null
                ? await _db.Database.BeginTransactionAsync(ct)
                : null;
        }

        try
        {
            if (gateCreate)
                openSnapshot = await _openGate!.EnsureCanCreateAsync(
                    projectId, request.Role, request.IgnoreConcurrencyLimit, ct);

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
                    : $" on {agentKind}{(request.AgentKind is null ? " (role policy)" : " (explicit)")}")
                + ProjectScopeSuffix(projectId)
                + CardScopeSuffix(binding.Identifier)
                // CARD-0305: which standing instruction produced that kind/tier. Without it the
                // event says "Codex Frontier" and nothing records that a human pinned it there.
                + (pinDecision.EventNote is { } pinNote ? $" [{pinNote}]" : string.Empty)
                + (routingWalk is { } walk ? FormatComplexityCreatedDetail(walk) : string.Empty),
            At = now,
        });
        if (repeatOf is not null || routingExhausted)
        {
            AddEvent(id, AgentTaskEventType.Blocked, null, task.FailureReason!, now);
            await EnqueueBlockedParentNoteAsync(task, task.FailureReason!, ct);
            warning = warning is null ? task.FailureReason : warning + " " + task.FailureReason;
        }
        if (warning is not null && ((repeatOf is null && !routingExhausted) || warning != task.FailureReason))
            AddEvent(id, AgentTaskEventType.Warning, null, warning, now);
        // D1: an area name the repo's map does not know is ACCEPTED as an opaque label and warned
        // about. A bookkeeping field must never refuse a launch — this one would be refusing it for
        // a typo — and the label still exact-matches another task that wrote the same name, which
        // is strictly better than the string-prefix comparison it replaces.
        if (UnknownAreaWarning(task) is { } areaWarning)
            AddEvent(id, AgentTaskEventType.Warning, null, areaWarning, now);
        if (request.IgnoreConcurrencyLimit && openSnapshot is { WouldRefuse: true })
        {
            AddEvent(
                id,
                AgentTaskEventType.Warning,
                null,
                ConcurrencyLimitException.FormatOverrideWarning(
                    openSnapshot.AbsoluteCount,
                    openSnapshot.AbsoluteLimit,
                    openSnapshot.Role,
                    openSnapshot.RoleCount,
                    openSnapshot.RoleLimit,
                    projectId),
                now);
        }

        await _db.SaveChangesAsync(ct);
        if (gateTx is not null)
            await gateTx.CommitAsync(ct);
        }
        catch
        {
            if (gateTx is not null)
                await gateTx.RollbackAsync(ct);
            throw;
        }
        finally
        {
            if (gateTx is not null)
                await gateTx.DisposeAsync();
        }

        await _eventBus.PublishToAllAsync("AgentTaskChanged", new { taskId = id, rootId = task.RootTaskId }, ct);
        _logger.LogInformation(
            "Delegated task {ShortId} ({Kind}/{Role}, {Level}, {AgentKind}) created in {Dir} at depth {Depth}",
            DelegationReportFormatter.Short(id), task.Kind, task.Role, level, agentKind,
            task.WorkingDirectory, depth);

        var titleDiagnosisQueued = false;
        if (ShouldQueueTitleDiagnosis(request, title))
            titleDiagnosisQueued = _diagnoseQueue!.TryEnqueue(DiagnoseRequest.ForTitle(id));

        // The raw token is returned ONCE, to be injected into the delegate's environment. It is
        // never persisted and never readable again.
        RawTokens[id] = token;
        return new AgentTaskCreatedDto(
            id, DelegationReportFormatter.Short(id), task.Status, level, warning, agentKind,
            NoReplyRouting: task.ReplyTo == AgentTaskReplyTo.None,
            ScopeOverlaps: await FindScopeOverlapsAsync(task, ct),
            CardId: binding.CardId,
            CardIdentifier: binding.Identifier,
            FollowUpMessage: followUpMessage,
            Complexity: request.Complexity,
            Routing: routingWalk?.ToDto(),
            TitleDiagnosisQueued: titleDiagnosisQueued);
    }

    private async Task<string?> CompletionHeaderAsync(Guid taskId, CancellationToken ct) =>
        await _db.SessionQueuedMessages.AsNoTracking()
            .Where(message => message.SourceTaskId == taskId && message.NoteHeader != null)
            .OrderByDescending(message => message.CreatedAt)
            .Select(message => message.NoteHeader)
            .FirstOrDefaultAsync(ct);

    private static string BuildInheritedFollowUpGoal(
        AgentTask prior,
        string? completionHeader,
        string? cardIdentifier,
        string requestedGoal)
    {
        var result = string.IsNullOrWhiteSpace(prior.Result)
            ? "[No result was recorded.]"
            : prior.Result;
        var header = string.IsNullOrWhiteSpace(completionHeader)
            ? "[No completion header was recorded.]"
            : completionHeader.Trim();
        var card = prior.CardId is not Guid cardId
            ? "[No card binding.]"
            : $"{cardIdentifier ?? "[card no longer exists]"} ({cardId:D})";
        var worktree = prior.WorktreePath is { Length: > 0 } path && Directory.Exists(path)
            ? $"{path}\nBranch: {prior.WorktreeBranch ?? "[No branch recorded.]"}"
            : "[No surviving worktree directory.]";

        return $"""
            --- inherited context from settled task {DelegationReportFormatter.Short(prior.Id)} ---
            Prior goal:
            {prior.Goal.Trim()}

            Prior result:
            {result}

            Prior completion header:
            {header}

            Prior worktree:
            {worktree}

            Prior repository:
            {prior.RepoPath ?? "[No repository recorded.]"}

            Prior card binding:
            {card}
            --- end inherited context ---

            {requestedGoal.Trim()}
            """;
    }

    /// <summary>
    /// Snapshot the caller's Antiphon-visible LLM-routing env for the child (CARD-0260 S1).
    /// Task-token: parent agent <c>LaunchEnvJson</c>, then the parent task's override.
    /// Session-token: the standing agent bound via <c>PersistentSessionId</c>.
    /// Filtered to <c>Delegation:LlmEnvInheritance:Names</c>. Never logs a value.
    /// </summary>
    private async Task<IReadOnlyDictionary<string, string>> ComputeInheritedLlmEnvAsync(
        Caller caller,
        bool skip,
        CancellationToken ct)
    {
        var inherit = _settings.LlmEnvInheritance;
        if (skip || !inherit.Enabled || inherit.Names.Count == 0)
            return AgentLaunchEnv.Empty;

        var merged = new Dictionary<string, string>(StringComparer.Ordinal);
        if (caller.Task is { } parent)
        {
            if (parent.AgentId is Guid agentId)
            {
                var json = await _db.Agents.AsNoTracking()
                    .Where(a => a.Id == agentId)
                    .Select(a => a.LaunchEnvJson)
                    .FirstOrDefaultAsync(ct);
                Overlay(merged, AgentLaunchEnv.Parse(json));
            }

            Overlay(merged, AgentLaunchEnv.Parse(parent.LaunchEnvOverrideJson));
        }
        else if (caller.SessionId is Guid sessionId)
        {
            var persistentSessionId = sessionId.ToString("D");
            var json = await _db.Agents.AsNoTracking()
                .Where(a => a.PersistentSessionId == persistentSessionId)
                .Select(a => a.LaunchEnvJson)
                .FirstOrDefaultAsync(ct);
            Overlay(merged, AgentLaunchEnv.Parse(json));
        }

        return AgentLaunchEnv.FilterTo(merged, inherit.Names);
    }

    /// <summary>
    /// The process-env snapshot supplied by delegate.ps1 is truer than the server's stored caller
    /// layers, but it remains constrained to the same allowlist. Unknown names are deliberately
    /// ignored: this is routing metadata, not a second arbitrary launch-env surface.
    /// </summary>
    private IReadOnlyDictionary<string, string> FilterSuppliedInheritedLlmEnv(
        IReadOnlyDictionary<string, string> supplied) =>
        !_settings.LlmEnvInheritance.Enabled
            ? AgentLaunchEnv.Empty
            : AgentLaunchEnv.FilterTo(supplied, _settings.LlmEnvInheritance.Names);

    private string? UnknownInheritedNamesWarning(IReadOnlyDictionary<string, string> supplied)
    {
        if (supplied.Count == 0)
            return null;

        var allowed = new HashSet<string>(_settings.LlmEnvInheritance.Names, StringComparer.Ordinal);
        var unknown = supplied.Keys
            .Where(name => !allowed.Contains(name))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        return unknown.Length == 0
            ? null
            : $"Ignored inheritedLlmEnv names outside Delegation:LlmEnvInheritance:Names: {string.Join(", ", unknown)}.";
    }

    /// <summary>
    /// Checks the environment that would reach a newly launched child before profile resolution.
    /// A local proxy without its project marker is never a usable child route; the converse is only
    /// a warning because wrapper-managed credentials are the normal default for many agents.
    /// </summary>
    private string? ValidateLlmProxyPreview(
        IReadOnlyDictionary<string, string> projectDefaultEnv,
        IReadOnlyDictionary<string, string> inheritedEnv,
        IReadOnlyDictionary<string, string> pinnedAgentEnv,
        IReadOnlyDictionary<string, string> launchEnvOverride,
        AgentKind agentKind)
    {
        var settings = _settings.LlmEnvInheritance;
        if (!settings.Enabled)
            return null;

        var preview = new Dictionary<string, string>(StringComparer.Ordinal);
        Overlay(preview, projectDefaultEnv);
        Overlay(preview, inheritedEnv);
        Overlay(preview, pinnedAgentEnv);
        Overlay(preview, launchEnvOverride);

        var hasProjectMarker = preview.TryGetValue(settings.ProjectMarkerName, out var projectMarker)
            && !string.IsNullOrWhiteSpace(projectMarker);
        var localProxyVariable = settings.ProxyUrlNames.FirstOrDefault(name =>
            preview.TryGetValue(name, out var value) && IsLocalProxyUrl(value, settings.ProxyHostMarkers));
        if (settings.RequireProjectAtProxy && localProxyVariable is not null && !hasProjectMarker)
        {
            throw new ValidationException(
                settings.ProjectMarkerName,
                $"'{localProxyVariable}' routes this child to a local key proxy, but '{settings.ProjectMarkerName}' is missing. "
                + $"Pass launchEnvOverride {{ {settings.ProjectMarkerName} = '...' }} or seed the project's default launch environment.",
                "llm_project_required");
        }

        if (!hasProjectMarker)
            return null;

        string[] requiredRouteNames = agentKind switch
        {
            AgentKind.ClaudeCode => ["ANTHROPIC_BASE_URL"],
            AgentKind.Grok => ["GROK_CLI_CHAT_PROXY_BASE_URL", "GROK_BASE_URL"],
            _ => [],
        };
        if (requiredRouteNames.Any(name => preview.ContainsKey(name)))
            return null;

        var missingRoute = agentKind switch
        {
            AgentKind.ClaudeCode => "ANTHROPIC_BASE_URL",
            AgentKind.Grok => "GROK_CLI_CHAT_PROXY_BASE_URL or GROK_BASE_URL",
            _ => "a Codex TUI profile route",
        };
        return $"'{settings.ProjectMarkerName}' is set, but {missingRoute} is absent; child will not route through the key proxy and its turns bill the wrapper credentials.";
    }

    private static bool IsLocalProxyUrl(string value, IReadOnlyList<string> proxyHostMarkers) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && proxyHostMarkers.Any(host => string.Equals(uri.Host, host, StringComparison.OrdinalIgnoreCase));

    private static void Overlay(
        Dictionary<string, string> target,
        IReadOnlyDictionary<string, string> source)
    {
        foreach (var (key, value) in source)
            target[key] = value;
    }

    /// <summary>
    /// Resolves a root task's project identity from the calling session at creation time. A card
    /// binding wins over the owning standing agent's board, matching the card-launch precedent:
    /// the work's card names its project even if the agent happens to belong elsewhere.
    /// </summary>
    private async Task<Guid?> DeriveCallerProjectAsync(Caller caller, CancellationToken ct)
    {
        if (caller.Task is not null || caller.SessionId is not Guid sessionId)
            return null;

        var cardProjectId = await (
            from session in _db.AgentSessions.AsNoTracking()
            join card in _db.Cards.AsNoTracking() on session.CardId equals card.Id
            join board in _db.Boards.AsNoTracking() on card.BoardId equals board.Id
            where session.Id == sessionId
            select (Guid?)board.ProjectId)
            .FirstOrDefaultAsync(ct);
        if (cardProjectId is not null)
            return cardProjectId;

        var persistentSessionId = sessionId.ToString("D");
        return await (
            from agent in _db.Agents.AsNoTracking()
            join board in _db.Boards.AsNoTracking() on agent.BoardId equals board.Id
            where agent.PersistentSessionId == persistentSessionId
            select (Guid?)board.ProjectId)
            .FirstOrDefaultAsync(ct);
    }

    private static string ProjectScopeSuffix(Guid? projectId) =>
        projectId is { } id ? $" — project scope: {id}" : string.Empty;

    /// <summary>CARD-0040: the Created event says which card this task will move, or says nothing.</summary>
    private static string CardScopeSuffix(string? identifier) =>
        identifier is { Length: > 0 } ? $" — bound to {identifier}" : string.Empty;

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
    /// Show specialist rows (Check, Distill, Diagnose) — machinery the standing seats create, not
    /// anybody's delegated work. Off by default, and the default is SERVER-side on purpose: a busy
    /// fleet would otherwise bury the board under them. The query-string name is kept from
    /// CARD-0047; it now hides every specialist role.
    /// </param>
    public Task<IReadOnlyList<AgentTaskSummaryDto>> ListAsync(
        Guid? rootId, AgentTaskStatus? status, bool includeChecks, CancellationToken ct) =>
        ListAsync(rootId, status is { } singleStatus ? [singleStatus] : null, includeChecks, since: null, ct);

    /// <summary>
    /// Lists delegated work. A history window only trims settled rows: queued, dispatched,
    /// working, and blocked tasks always remain visible, even when their run began long ago.
    /// </summary>
    public async Task<IReadOnlyList<AgentTaskSummaryDto>> ListAsync(
        Guid? rootId,
        IReadOnlyCollection<AgentTaskStatus>? statuses,
        bool includeChecks,
        DateTime? since,
        CancellationToken ct)
    {
        var query = _db.AgentTasks.AsNoTracking();
        if (rootId is { } root) query = query.Where(t => t.RootTaskId == root);
        if (statuses is { Count: > 0 })
        {
            var requested = statuses.ToArray();
            query = query.Where(t => requested.Contains(t.Status));
        }
        if (!includeChecks) query = query.Where(AgentTaskRoles.NotSpecialist);

        // AgentTask has no mutable UpdatedAt column. A settled row's CompletedAt is its final
        // state transition, and every not-yet-settled state is retained irrespective of age.
        if (since is { } windowStart)
        {
            query = query.Where(t => t.CompletedAt >= windowStart
                || (t.Status != AgentTaskStatus.Succeeded
                    && t.Status != AgentTaskStatus.Failed
                    && t.Status != AgentTaskStatus.Canceled));
        }

        var tasks = await query.OrderBy(t => t.CreatedAt).ToListAsync(ct);
        var cardIdentifiers = await LoadCardIdentifiersAsync(tasks, ct);
        return tasks.Select(t => ToSummary(t, tasks, cardIdentifiers)).ToList();
    }

    /// <summary>Fleet counters for the board header. Specialist machinery stays hidden.</summary>
    public async Task<AgentTaskListSummaryDto> GetListSummaryAsync(CancellationToken ct)
    {
        var tasks = _db.AgentTasks.AsNoTracking().Where(AgentTaskRoles.NotSpecialist);
        var byStatus = await tasks
            .GroupBy(t => t.Status)
            .Select(group => new { Status = group.Key, Count = group.Count() })
            .ToListAsync(ct);
        var runs = await tasks.Select(t => t.RootTaskId).Distinct().CountAsync(ct);
        var totalCostUsd = await tasks.SumAsync(t => t.CostUsd, ct);

        var counts = byStatus.ToDictionary(group => group.Status.ToString(), group => group.Count);
        return new AgentTaskListSummaryDto(
            Active: byStatus.Where(group => group.Status is AgentTaskStatus.Dispatched or AgentTaskStatus.Working)
                .Sum(group => group.Count),
            Blocked: byStatus.Where(group => group.Status == AgentTaskStatus.Blocked).Sum(group => group.Count),
            Runs: runs,
            TotalCostUsd: totalCostUsd,
            ByStatus: counts);
    }

    public async Task<AgentTaskDetailDto> GetAsync(Guid id, CancellationToken ct, Guid? pollingSessionId = null)
    {
        var task = await _db.AgentTasks.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id, ct)
            ?? throw new NotFoundException(nameof(AgentTask), id);

        var report = !string.IsNullOrEmpty(task.Result) ? task.Result : task.FailureReason;
        if (IsSettled(task.Status)
            && !string.IsNullOrEmpty(report)
            && pollingSessionId is not null
            && pollingSessionId == task.ParentSessionId)
        {
            var now = _timeProvider.GetUtcNow().UtcDateTime;
            await _db.AgentTasks.Where(t => t.Id == id).ExecuteUpdateAsync(setters => setters
                .SetProperty(t => t.LastPolledResultHash, DelegationNoteDigest.Compute(report))
                .SetProperty(t => t.LastPolledResultAt, now), ct);
        }

        // Subtree cost needs the whole run, not just this row.
        var family = await _db.AgentTasks.AsNoTracking()
            .Where(t => t.RootTaskId == task.RootTaskId)
            .ToListAsync(ct);

        var events = await _db.AgentTaskEvents.AsNoTracking()
            .Where(e => e.AgentTaskId == id)
            .OrderBy(e => e.At)
            .Select(e => new AgentTaskEventDto(e.Type, e.ModelLevel, e.Detail, e.At))
            .ToListAsync(ct);

        var blocked = await BlockedContextBuilder.BuildAsync(task, family, events, _checkProbe, ct);

        return new AgentTaskDetailDto(
            ToSummary(task, family, await LoadCardIdentifiersAsync([task], ct)), task.Goal, task.Result,
            task.ResultFilePath, task.DeliverablePath, task.DeliverableRef,
            task.FailureReason, task.MergeTargetRef, events, task.FailureCode, blocked,
            task.StandingAuthority, task.AutoContinueOnWait, task.NextStage, task.NextHandoff);
    }

    /// <summary>Record the first operator read; repeat opens deliberately preserve that timestamp.</summary>
    public async Task<AgentTaskSummaryDto> MarkReadAsync(Guid id, CancellationToken ct)
    {
        var task = await _db.AgentTasks.FirstOrDefaultAsync(t => t.Id == id, ct)
            ?? throw new NotFoundException(nameof(AgentTask), id);

        if (task.ReadAt is null)
        {
            task.ReadAt = UtcNow();
            task.ConcurrencyToken = Guid.NewGuid();
            await _db.SaveChangesAsync(ct);
        }

        var family = await _db.AgentTasks.AsNoTracking()
            .Where(t => t.RootTaskId == task.RootTaskId)
            .ToListAsync(ct);
        return ToSummary(task, family, await LoadCardIdentifiersAsync([task], ct));
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

        var repeatOf = await FindLaunchFailureRepeatAsync(
            task.CardId, task.Goal, task.Kind, task.Role, task.AgentKind, ct);
        if (repeatOf is not null)
        {
            await BlockRepeatAsync(task, repeatOf, ct);
            return await SummaryOfAsync(task, ct);
        }

        await RefuseUnauthenticatedGrokAsync(
            task.AgentKind, task.AgentId,
            AgentLaunchEnv.Parse(task.LaunchEnvOverrideJson),
            AgentLaunchEnv.Parse(task.InheritedLaunchEnvJson),
            allowUnauthenticated: false, ct);

        await RequeueAsync(
            task, AgentTaskEventType.Retried, task.ModelLevel,
            $"Retried at {ModelLevelAliases.For(task.AgentKind, task.ModelLevel)}.", ct);
        return await SummaryOfAsync(task, ct);
    }

    /// <summary>
    /// Explicit human pick of (kind, level) that ends chain governance (CARD-0090).
    /// Blocked-for-routing or Queued only. Require applies: a held alias is 409
    /// <c>model_disabled</c>.
    /// </summary>
    public async Task<AgentTaskSummaryDto> RerouteAsync(
        Guid id, AgentKind agentKind, AgentModelLevel modelLevel, CancellationToken ct)
    {
        var task = await _db.AgentTasks.FirstOrDefaultAsync(t => t.Id == id, ct)
            ?? throw new NotFoundException(nameof(AgentTask), id);

        if (task.Status is AgentTaskStatus.Working or AgentTaskStatus.Dispatched)
        {
            throw new ConflictException(
                $"Task {DelegationReportFormatter.Short(id)} is {task.Status}; reroute is for "
                + "Blocked-for-routing or Queued tasks only.");
        }

        if (task.Status is not (AgentTaskStatus.Queued or AgentTaskStatus.Blocked))
        {
            throw new ConflictException(
                $"Task {DelegationReportFormatter.Short(id)} is {task.Status} and cannot be rerouted.");
        }

        if (task.Status == AgentTaskStatus.Blocked
            && (task.Complexity is null
                || task.FailureReason is null
                || !task.FailureReason.StartsWith(ComplexityRoutingService.RoutingExhaustedPrefix, StringComparison.Ordinal)))
        {
            throw new ConflictException(
                $"Task {DelegationReportFormatter.Short(id)} is blocked on a question, not routing. "
                + "Use reply, not reroute.");
        }

        if (!DelegatableKinds.Contains(agentKind))
        {
            throw new ValidationException(
                nameof(agentKind),
                $"{agentKind} is not a delegate kind. Reroute to "
                + $"{string.Join(" or ", DelegatableKinds)}.");
        }

        if (task.Kind == AgentTaskKind.Orchestrator && agentKind != AgentKind.ClaudeCode)
        {
            throw new ValidationException(
                nameof(agentKind),
                "An orchestrator cannot be rerouted off ClaudeCode.");
        }

        var alias = ModelLevelAliases.For(agentKind, modelLevel);
        if (_modelAvailability is not null)
            await _modelAvailability.RequireAsync(agentKind, alias, ct);

        task.AgentKind = agentKind;
        task.ModelLevel = modelLevel;
        task.Complexity = null;
        var detail = $"rerouted to {alias} (explicit; chain governance ended)";

        if (task.Status == AgentTaskStatus.Queued)
        {
            var now = UtcNow();
            task.ConcurrencyToken = Guid.NewGuid();
            AddEvent(task.Id, AgentTaskEventType.Rerouted, modelLevel, detail, now);
            await _db.SaveChangesAsync(ct);
            await _eventBus.PublishToAllAsync(
                "AgentTaskChanged", new { taskId = id, rootId = task.RootTaskId }, ct);
            return await SummaryOfAsync(task, ct);
        }

        task.FailureReason = null;
        await RequeueAsync(task, AgentTaskEventType.Rerouted, modelLevel, detail, ct);
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
                + $"({ModelLevelAliases.For(task.AgentKind, from)}).");
        }

        task.EscalatedFrom = from;
        task.ModelLevel = target.Value;
        var detail = $"Escalated {ModelLevelAliases.For(task.AgentKind, from)} "
            + $"-> {ModelLevelAliases.For(task.AgentKind, target.Value)}."
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
    /// What an escalation buys when both rungs map to the SAME model. Grok's ladder has no rungs
    /// left at all (CARD-0169 — every level maps to grok-4.6, the operator's own instruction), so
    /// EVERY escalation on Grok moves no model at all. That is still worth doing (a fresh context
    /// is most of what escalation buys in practice: the stalled session is killed and the next
    /// attempt starts from the handoff block rather than the dead end), but the event must SAY so.
    /// Silence here would read as a promise of a larger model that xAI does not currently offer,
    /// and the operator would spend the escalation expecting something the ladder cannot deliver.
    ///
    /// <para>The test is the ALIAS COMPARISON, not the kind (CARD-0084 S4): now that
    /// <see cref="ModelLevelAliases.For"/> answers per kind, "both rungs are the same model" is
    /// exactly what a short ladder looks like, whoever owns it — so a future kind that collapses
    /// two rungs gets the note with no edit here. Claude's four aliases are all distinct and
    /// <see cref="ResolveEscalationTarget"/> never returns the current rung, so a Claude escalation
    /// can never take this arm and its event text is byte-identical to before.</para>
    /// </summary>
    private static string SameModelEscalationNote(AgentTask task, AgentModelLevel from, AgentModelLevel to)
    {
        var alias = ModelLevelAliases.For(task.AgentKind, to);
        if (!string.Equals(ModelLevelAliases.For(task.AgentKind, from), alias, StringComparison.Ordinal))
            return string.Empty;

        return $" On {task.AgentKind} the {from} and {to} tiers both map to {alias}, so this is a"
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
        // This field describes the current settlement only. A retry is a fresh attempt on the
        // same row, so it must not inherit the previous attempt's recovery provenance.
        task.RecoveredAt = null;
        // CARD-0348: attempt-scoped. Sequences are per session — a stale watermark from the
        // old session would refuse the new session's first hundred rows.
        task.RepliedAt = null;
        task.RepliedAtSequence = null;
        // CARD-0349: the closing-line nudge is likewise session-scoped. Its boundary sequence
        // and queued-message delivery belong to the old session, so retaining any part of the
        // tuple could skip the new attempt's nudge or settle/block it as though it had been asked.
        task.ReportNudgedAt = null;
        task.ReportNudgedSequence = null;
        task.ReportNudgeMessageId = null;
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
            ModelLevelAliases.For(task.AgentKind, task.ModelLevel), detail);
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
    /// CARD-0291: resolve a caller-typed standing-agent reference — a guid, an exact slug, or a
    /// case-insensitive exact name, tried in that order. Neither Name nor Slug carries a unique
    /// index (<c>AppDbContext</c>), so an ambiguous reference is refused naming the candidates and
    /// their guids rather than silently picking one. Pool delegates are refused as a class: the
    /// dispatcher-spawned ephemeral population is all <see cref="Agent.IsPoolDelegate"/>, and
    /// "same delegate again" is <see cref="CreateAgentTaskRequest.FollowUpOnTask"/>'s job.
    /// </summary>
    private Task<Agent> ResolveStandingAgentAsync(string reference, CancellationToken ct) =>
        StandingAgentResolver.ResolveAsync(_db, reference, nameof(CreateAgentTaskRequest.Agent), ct);

    /// <summary>
    /// Spawn the Merge-role delegate that resolves a Worktree task's rebase conflict. SYSTEM
    /// spawned — the server decides this, so it bypasses the caller checks — but the run's task
    /// cap still applies: a run at its limit gets the Blocked task and no fixer, stated in events.
    /// It is a CHILD of the conflicted task (the tree records that this work needed a merge hand)
    /// and reports to the same parent session, working directly in the conflicted worktree.
    /// </summary>
    internal async Task<AgentTask?> CreateMergeTaskAsync(
        AgentTask conflicted, IReadOnlyList<string> conflictFiles, CancellationToken ct,
        string? landingTarget = null)
    {
        var siblings = await _db.AgentTasks.CountAsync(t => t.RootTaskId == conflicted.RootTaskId, ct);
        if (siblings >= _settings.MaxTasksPerRoot || conflicted.Depth + 1 > _settings.MaxDepth)
        {
            _logger.LogWarning(
                "Task {ShortId}: merge conflict but the run is at its task/depth cap — leaving Blocked",
                DelegationReportFormatter.Short(conflicted.Id));
            return null;
        }

        if (conflicted.WorktreePath is not { } worktree || conflicted.WorktreeBranch is not { } branch)
        {
            return null;
        }
        var target = conflicted.MergeTargetRef ?? landingTarget;
        if (target is null)
            return null;

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
                4. git push origin {target}; then remove this worktree and delete {branch}.
                Report which files conflicted and how you resolved each. Do NOT redo or review the
                task's work — only integrate and finish landing it.
                """,
            Kind = AgentTaskKind.Worker,
            Role = AgentTaskRole.Merge,
            ProjectId = conflicted.ProjectId,
            // CARD-0040: integrating a task's work is still that task's card's work.
            CardId = conflicted.CardId,
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
            // CARD-0294 S1: a conflict resolver has no approval wait to skip.
            StandingAuthority = conflicted.StandingAuthority,
            AutoContinueOnWait = false,
        };

        _db.AgentTasks.Add(task);
        _db.AgentTaskEvents.Add(new AgentTaskEvent
        {
            Id = Guid.NewGuid(),
            AgentTaskId = id,
            Type = AgentTaskEventType.Created,
            ModelLevel = task.ModelLevel,
            Detail = $"Spawned by the server to resolve {conflictFiles.Count} conflicted file(s) from "
                + $"task {DelegationReportFormatter.Short(conflicted.Id)}."
                + ProjectScopeSuffix(task.ProjectId),
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
    public static readonly IReadOnlyList<AgentKind> DelegatableKinds =
        [AgentKind.ClaudeCode, AgentKind.Grok, AgentKind.Codex];

    /// <summary>
    /// Resolve WHICH AGENT PROGRAM runs the task: an explicit request wins, else the role policy's
    /// <c>Kind</c> (unset everywhere as shipped), else ClaudeCode. The mirror of
    /// <see cref="ResolveLevel"/>, and the same shape of decision.
    ///
    /// <para>Two refusals, both loud. A kind outside <see cref="DelegatableKinds"/> is rejected with
    /// its reason — nothing has been exercised on OpenCode/Raw as a DELEGATE, and quietly
    /// substituting Claude for what the caller asked for is worse than failing. And an orchestrator
    /// is ClaudeCode only: its contract (the PreToolUse deny hook, delegate.ps1 usage, the check
    /// interpreter) has only ever run on Claude, so Grok and Codex are WORKER kinds. Unlike the tier
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
                + $"{string.Join(" or ", DelegatableKinds)} (CARD-0084, CARD-0099); the others have never been "
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
    public async Task<AgentTaskSummaryDto> GetSummaryAsync(
        AgentTask task, IReadOnlyList<AgentTask> family, CancellationToken ct = default) =>
        ToSummary(task, family, await LoadCardIdentifiersAsync([task], ct));

    /// <summary>The DTO for one task, re-reading its run for the cost rollup.</summary>
    private async Task<AgentTaskSummaryDto> SummaryOfAsync(AgentTask task, CancellationToken ct)
    {
        var family = await _db.AgentTasks.AsNoTracking()
            .Where(t => t.RootTaskId == task.RootTaskId).ToListAsync(ct);
        return ToSummary(task, family, await LoadCardIdentifiersAsync([task], ct));
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

    /// <summary>
    /// The identifiers of every card the given tasks are bound to (CARD-0040). One query for the
    /// whole page: a per-row lookup on a board listing hundreds of tasks is a hundred round-trips
    /// for a string that is already denormalisable.
    /// </summary>
    private async Task<IReadOnlyDictionary<Guid, string>> LoadCardIdentifiersAsync(
        IEnumerable<AgentTask> tasks, CancellationToken ct)
    {
        var cardIds = tasks.Where(t => t.CardId is not null).Select(t => t.CardId!.Value).Distinct().ToList();
        if (cardIds.Count == 0)
            return new Dictionary<Guid, string>();

        return await _db.Cards.AsNoTracking()
            .Where(c => cardIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => c.Identifier, ct);
    }

    private static AgentTaskSummaryDto ToSummary(
        AgentTask task,
        IReadOnlyList<AgentTask> family,
        IReadOnlyDictionary<Guid, string>? cardIdentifiers = null)
    {
        // Walk the parent chain rather than recursing children — the same O(n) pass answers both
        // "my subtree's cost" and "my child count" for every row in a run.
        var subtreeCost = AgentTaskCostWalk.Calculate([task], family)[task.Id];
        var childCount = 0;
        foreach (var other in family)
        {
            if (other.Id == task.Id) continue;
            if (other.ParentTaskId == task.Id) childCount++;
        }

        return new AgentTaskSummaryDto(
            task.Id, task.RootTaskId, task.ParentTaskId, task.Depth, task.Title, task.Kind, task.Role,
            task.AgentKind,
            task.ModelLevel, task.EscalatedFrom, task.Status, task.Workspace, task.WorkingDirectory,
            task.RepoPath, task.WorktreePath, task.WorktreeBranch, task.Scope, task.ObservedScope,
            task.AgentId,
            // Snapshotted at dispatch — survives the ephemeral agent row's deletion on settle.
            task.AgentName,
            task.AgentSessionId, task.Attempt,
            task.CreatedAt, task.DispatchedAt, task.CompletedAt, task.ReadAt,
            task.DeliverablePath, task.DeliverableRef, task.RecoveredAt,
            task.TokensIn, task.CacheReadTokens, task.CacheCreationTokens, task.TokensOut,
            task.CostUsd, task.CostPricingVersion, subtreeCost, childCount,
            task.ExpectedDurationMinutes, task.NextCheckAt, task.CheckCount,
            task.LandRequestedAt, task.LandStartedAt, task.LandAttempt,
            task.CardId,
            task.CardId is Guid cardId && cardIdentifiers is not null
                && cardIdentifiers.TryGetValue(cardId, out var identifier)
                    ? identifier
                    : null,
            task.ReportEvidence,
            task.Complexity,
            task.RepliedAt);
    }

    private static string FormatComplexityCreatedDetail(ComplexityRoutingService.Walk walk)
    {
        if (walk.Chosen is { } chosen)
        {
            var index = 0;
            for (var i = 0; i < walk.Outcomes.Count; i++)
            {
                if (walk.Outcomes[i].Outcome == "chosen")
                {
                    index = i + 1;
                    break;
                }
            }

            var skipped = walk.SkippedWarning();
            return $" complexity={walk.Complexity} candidate {index}/{walk.Outcomes.Count} {chosen.Alias}"
                + (skipped.Length > 0 ? $"; {skipped}" : string.Empty);
        }

        return $" complexity={walk.Complexity} exhausted";
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

        return FallbackTitle(request.Goal);
    }

    /// <summary>
    /// The Goal-first-line fallback stored when create is given no Title (CARD-0352 S3).
    /// Diagnose compares the live title to this to know whether something else already renamed it.
    /// </summary>
    internal static string FallbackTitle(string goal)
    {
        var firstLine = goal.ReplaceLineEndings("\n").Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault()?.Trim() ?? "Delegated task";
        return Clamp(firstLine, 300);
    }

    private bool ShouldQueueTitleDiagnosis(CreateAgentTaskRequest request, string storedTitle) =>
        _diagnoseQueue is not null
        && _settings.DiagnoseEnabled
        && _settings.DiagnoseTitleEnabled
        && string.IsNullOrWhiteSpace(request.Title)
        && !AgentTaskRoles.IsSpecialist(request.Role)
        && storedTitle.Length > _settings.DiagnoseTitleMinFallbackChars;

    private static string Clamp(string value, int max) =>
        value.Length <= max ? value : value[..(max - 1)] + "…";

    /// <summary>
    /// CARD-0256. A Worktree task's <c>-Dir</c> is the source repository; Antiphon creates the
    /// worktree at dispatch. The 422 still enforces <c>AllowedRoots</c> — this only names the
    /// valid shape so a caller who pointed <c>-Dir</c> at the worktree path itself can recover.
    /// </summary>
    internal const string WorktreeSourceRepositoryGuidance =
        " A Worktree task takes the source repository as -Dir (or inherits it), and Antiphon "
        + "creates a new worktree at dispatch. Use -Dir <repo> -Worktree rather than pointing "
        + "-Dir at the worktree path itself.";

    private static string AugmentWorktreeRejection(CreateAgentTaskRequest request, string message)
    {
        if (request.Workspace == WorkspaceMode.Worktree
            && !string.IsNullOrWhiteSpace(request.WorkingDirectory))
            return message + WorktreeSourceRepositoryGuidance;
        return message;
    }

    /// <summary>
    /// CARD-0324: 409 <c>provider_sign_in_required</c> for a registry-Grok create/retry
    /// whose store is Absent/Empty. Standing-profile Grok and API-key launches skip.
    /// </summary>
    private async Task RefuseUnauthenticatedGrokAsync(
        AgentKind agentKind,
        Guid? agentId,
        IReadOnlyDictionary<string, string>? launchEnvOverride,
        IReadOnlyDictionary<string, string>? inheritedEnv,
        bool allowUnauthenticated,
        CancellationToken ct)
    {
        var settings = _registrySettings;
        if (allowUnauthenticated
            || agentKind != AgentKind.Grok
            || settings is not { GrokCredentialProbeEnabled: true })
            return;

        if (agentId is Guid pinnedId)
        {
            var pinned = await _db.Agents.AsNoTracking()
                .Where(a => a.Id == pinnedId)
                .Select(a => new { a.IsPoolDelegate, a.TuiProfileId })
                .FirstOrDefaultAsync(ct);
            if (pinned is { IsPoolDelegate: false, TuiProfileId: not null })
                return;
        }

        var env = new Dictionary<string, string>(StringComparer.Ordinal);
        if (settings.Definitions.TryGetValue("grok", out var def))
        {
            foreach (var (key, value) in def.Env)
                env[key] = value;
        }

        if (inheritedEnv is not null)
        {
            foreach (var (key, value) in inheritedEnv)
                env[key] = value;
        }

        if (launchEnvOverride is not null)
        {
            foreach (var (key, value) in launchEnvOverride)
                env[key] = value;
        }

        var grokHome = GrokCredentialStore.ResolveGrokHome(env);
        var finding = GrokCredentialStore.Inspect(grokHome, env);
        if (GrokCredentialStore.IsLaunchBlocking(finding))
            throw new ProviderSignInRequiredException(grokHome);
    }

    private async Task<AgentTask?> FindLaunchFailureRepeatAsync(
        Guid? cardId,
        string goal,
        AgentTaskKind kind,
        AgentTaskRole role,
        AgentKind agentKind,
        CancellationToken ct)
    {
        var trimmed = goal.Trim();
        var matches = await _db.AgentTasks.AsNoTracking()
            .Where(t =>
                t.Status == AgentTaskStatus.Failed
                && (t.FailureCode == AgentTaskFailureCode.StoppedBeforeFirstPrompt
                    || t.FailureCode == AgentTaskFailureCode.AuthenticationRequired
                    || t.FailureCode == AgentTaskFailureCode.CompletedWithoutProgress)
                && t.Kind == kind
                && t.Role == role
                && t.AgentKind == agentKind
                && t.Goal == trimmed)
            .OrderByDescending(t => t.CompletedAt)
            .ToListAsync(ct);
        return matches.FirstOrDefault(t => t.CardId == cardId);
    }

    private static string RepeatBlockReason(AgentTask prior, AgentKind kind) =>
        $"Repeat of task {DelegationReportFormatter.Short(prior.Id)} "
        + $"({prior.FailureCode}) is blocked; no {kind} process or "
        + "worktree was started.";

    private async Task BlockRepeatAsync(AgentTask task, AgentTask prior, CancellationToken ct)
    {
        var now = UtcNow();
        var reason = RepeatBlockReason(prior, task.AgentKind);
        task.Status = AgentTaskStatus.Blocked;
        task.FailureReason = reason;
        task.AgentSessionId = null;
        task.ConcurrencyToken = Guid.NewGuid();
        AddEvent(task.Id, AgentTaskEventType.Blocked, null, reason, now);
        await EnqueueBlockedParentNoteAsync(task, reason, ct);
        await _db.SaveChangesAsync(ct);
        await _eventBus.PublishToAllAsync(
            "AgentTaskChanged", new { taskId = task.Id, rootId = task.RootTaskId }, ct);
        _logger.LogWarning(
            "Task {ShortId} blocked as a launch-failure repeat of {PriorShortId} ({FailureCode})",
            DelegationReportFormatter.Short(task.Id), DelegationReportFormatter.Short(prior.Id),
            prior.FailureCode);
    }

    internal async Task EnqueueBlockedParentNoteAsync(AgentTask task, string reason, CancellationToken ct)
    {
        if (task.ParentSessionId is not Guid parentSession)
            return;
        if (!await _db.AgentSessions.AnyAsync(s => s.Id == parentSession, ct))
            return;

        var note = DelegationReportFormatter.BuildCompletionNote(task, _settings, reason);
        var nextSequence = (await _db.SessionQueuedMessages
            .Where(m => m.AgentSessionId == parentSession)
            .MaxAsync(m => (long?)m.Sequence, ct) ?? 0) + 1;
        _db.SessionQueuedMessages.Add(new SessionQueuedMessage
        {
            Id = Guid.NewGuid(),
            AgentSessionId = parentSession,
            Body = note.Body,
            Status = QueuedMessageStatus.Pending,
            Sequence = nextSequence,
            Origin = QueuedMessageOrigin.Delegation,
            SourceTaskId = task.Id,
            ContentDigest = DelegationNoteDigest.Compute(reason),
            NoteHeader = note.Header,
            CreatedAt = UtcNow(),
        });
    }

    private async Task RecordRejectionAsync(AgentTask task, string detail, CancellationToken ct)
    {
        AddEvent(task.Id, AgentTaskEventType.Rejected, null, detail, UtcNow());
        await _db.SaveChangesAsync(ct);
        _logger.LogWarning("Delegation rejected for task {ShortId}: {Detail}",
            DelegationReportFormatter.Short(task.Id), detail);
    }

    /// <summary>
    /// The area names this task declared that its repo's <c>antiphon.areas.json</c> does not know,
    /// as one sentence naming the known list — or null when everything resolved (CARD-0063 D1).
    /// </summary>
    private string? UnknownAreaWarning(AgentTask task)
    {
        if (_areas is null || string.IsNullOrWhiteSpace(task.Scope))
            return null;

        var map = _areas.Load(task.RepoPath);
        var unknown = ScopeResolver.Resolve(task.Scope, map).UnknownAreaNames;
        if (unknown.Count == 0)
            return null;

        var known = map.Count == 0
            ? "this repo declares no areas (no antiphon.areas.json)"
            : "known areas: " + string.Join(", ", map.Names);
        return $"Scope names no area this repo knows: {string.Join(", ", unknown)} "
            + $"— kept as a label, matched exactly against other tasks using it ({known}).";
    }

    /// <summary>
    /// Running tasks in this task's repo whose areas it touches, and what each one will cost it
    /// (CARD-0063 S3). Answered at CREATE time because that is the one moment the caller can still
    /// change its mind: the dispatcher's own verdict is 5 seconds and one queue away, and by then
    /// nobody is reading.
    ///
    /// <para>Mirrors the tick's arms exactly — the pair-weighted policy, D3's undeclared shared
    /// writers, ReadOnly and specialists outside the lease — so the answer here and the event the task
    /// earns cannot disagree. Never throws: an overlap listing that broke a create would be a
    /// bookkeeping field refusing a launch.</para>
    /// </summary>
    private async Task<IReadOnlyList<ScopeOverlapDto>?> FindScopeOverlapsAsync(
        AgentTask task, CancellationToken ct)
    {
        if (task.Workspace == WorkspaceMode.ReadOnly || AgentTaskRoles.IsSpecialist(task.Role))
            return null;

        try
        {
            var key = ScopeResolver.KeyFor(task.RepoPath, task.WorkingDirectory);
            var map = _areas?.Load(task.RepoPath) ?? AreaMap.Empty;
            var scope = ScopeResolver.Resolve(task.Scope, map);

            var running = await _db.AgentTasks.AsNoTracking()
                .Where(t => t.Id != task.Id
                    && (t.Status == AgentTaskStatus.Dispatched || t.Status == AgentTaskStatus.Working)
                    && t.Workspace != WorkspaceMode.ReadOnly)
                .Where(AgentTaskRoles.NotSpecialist)
                .Select(t => new
                {
                    t.Id, t.Title, t.WorkingDirectory, t.RepoPath, t.Scope, t.Workspace, t.WorktreeBranch,
                })
                .ToListAsync(ct);

            var overlaps = new List<ScopeOverlapDto>();
            foreach (var other in running)
            {
                if (!string.Equals(
                        DelegationWorkspaceResolver.NormalizeSeparators(
                            ScopeResolver.KeyFor(other.RepoPath, other.WorkingDirectory)),
                        DelegationWorkspaceResolver.NormalizeSeparators(key),
                        StringComparison.OrdinalIgnoreCase))
                    continue;

                var intersection = ScopeResolver.Intersect(
                    ScopeResolver.Resolve(other.Scope, map), scope);
                ScopeOverlapPolicy policy;
                string? areas;
                if (intersection.Any)
                {
                    policy = ScopeResolver.PolicyFor(task.Workspace, other.Workspace, intersection.AllAllow);
                    areas = intersection.Describe();
                }
                else if (_settings.SerialiseSharedWriters
                    && task.Workspace == WorkspaceMode.Shared
                    && other.Workspace == WorkspaceMode.Shared)
                {
                    policy = ScopeOverlapPolicy.Serialise;
                    areas = null;
                }
                else
                {
                    continue;
                }

                if (policy == ScopeOverlapPolicy.Allow)
                    continue;

                overlaps.Add(new ScopeOverlapDto(
                    other.Id,
                    DelegationReportFormatter.Short(other.Id),
                    other.Title,
                    other.Workspace,
                    other.WorktreeBranch,
                    policy == ScopeOverlapPolicy.Serialise ? "serialise" : "warn",
                    areas));
            }

            return overlaps.Count == 0 ? null : overlaps;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Could not compute scope overlaps for task {ShortId}.",
                DelegationReportFormatter.Short(task.Id));
            return null;
        }
    }

    /// <summary>The declared areas of the repo a directory belongs to, for the areas endpoint.</summary>
    public async Task<AreaMapDto> ListAreasAsync(string? directory, Caller caller, CancellationToken ct)
    {
        var target = string.IsNullOrWhiteSpace(directory) ? caller.WorkingDirectory : directory.Trim();
        if (string.IsNullOrWhiteSpace(target))
            return new AreaMapDto(string.Empty, null, []);

        var repoPath = await _workspace.GetRepoToplevelAsync(target, ct) ?? target;
        var map = _areas?.Load(repoPath) ?? AreaMap.Empty;
        return new AreaMapDto(
            repoPath,
            map.SourcePath,
            map.Areas
                .Select(a => new AreaDto(
                    a.Name, a.Paths, a.Weight == AreaWeight.Allow ? "allow" : "serialise"))
                .OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
                .ToList());
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
