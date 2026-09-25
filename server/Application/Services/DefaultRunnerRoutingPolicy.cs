using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Enums;
using Antiphon.SessionRunner.Contracts;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// CARD-0659 D-1. What a create request said about its execution host, captured BEFORE the
/// reserved <c>local</c> sentinel is normalized out of the request.
/// </summary>
public enum RunnerRequestSource
{
    /// <summary>Omitted, null or whitespace: automatic selection.</summary>
    Unset = 0,

    /// <summary>The reserved <c>local</c> token (any case): the desktop, persisted as null.</summary>
    ExplicitLocal = 1,

    /// <summary>Any other value: an explicit remote runner id, never replaced or fallen back.</summary>
    ExplicitRemote = 2,
}

public sealed record RunnerRequestIntent(RunnerRequestSource Source, string? RemoteRunnerId, string? RequestedToken = null)
{
    public const int MaxRunnerIdLength = 64;

    /// <summary>Case-insensitive. <c>local</c> and <c>desktop</c> are the same explicit desktop choice.</summary>
    public static bool IsLocalToken(string? value) => IsDesktopAlias(value);

    public static bool IsDesktopAlias(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;
        var trimmed = value.Trim();
        return string.Equals(trimmed, PhoneHomeProtocol.LocalRunnerId, StringComparison.OrdinalIgnoreCase)
            || string.Equals(trimmed, RunnerPlatformWire.DesktopId, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Public catalogue id for a desktop alias: always <c>desktop</c>.</summary>
    public static string DesktopPublicId => RunnerPlatformWire.DesktopId;

    public static string? CanonicalRunnerId(string? runnerId)
    {
        if (string.IsNullOrWhiteSpace(runnerId) || IsDesktopAlias(runnerId))
            return null;
        return runnerId.Trim();
    }

    public static string DisplayRunnerId(string? storedRunnerId) =>
        string.IsNullOrWhiteSpace(storedRunnerId) ? DesktopPublicId : storedRunnerId;

    public static RunnerRequestIntent Parse(string? runnerId)
    {
        if (string.IsNullOrWhiteSpace(runnerId))
            return new RunnerRequestIntent(RunnerRequestSource.Unset, null);
        var trimmed = runnerId.Trim();
        if (DescribeInvalid(trimmed) is { } problem)
            throw new ValidationException(nameof(Dtos.CreateAgentTaskRequest.RunnerId), $"runnerId {problem}.", "runner_id_invalid");
        if (IsDesktopAlias(trimmed))
        {
            var token = string.Equals(trimmed, RunnerPlatformWire.DesktopId, StringComparison.OrdinalIgnoreCase)
                ? RunnerPlatformWire.DesktopId
                : PhoneHomeProtocol.LocalRunnerId;
            return new RunnerRequestIntent(RunnerRequestSource.ExplicitLocal, null, token);
        }

        return new RunnerRequestIntent(RunnerRequestSource.ExplicitRemote, trimmed, trimmed);
    }

    /// <summary>Null when the identifier is acceptable; otherwise what is wrong with it.</summary>
    public static string? DescribeInvalid(string trimmed) =>
        trimmed.Length > MaxRunnerIdLength ? $"must be at most {MaxRunnerIdLength} characters"
        : trimmed.Any(char.IsControl) ? "must not contain control characters"
        : null;
}

/// <summary>The resolved create shape the default is decided on (after pins, walks and workspace).</summary>
public sealed record DefaultRunnerShape(
    WorkspaceMode Workspace,
    AgentKind Kind,
    AgentTaskKind TaskKind,
    AgentTaskRole Role,
    bool ExistingProcess,
    bool SourceLanding,
    bool RoutingExhausted);

/// <summary>
/// One create-time placement decision. <see cref="AuditSegment"/> is the bounded, deterministic
/// Created-event text; <see cref="Warn"/> is set only when an otherwise eligible automatic request
/// fell back because the configured runner could not take it.
/// </summary>
public sealed record DefaultRunnerDecision(
    string? SelectedRunnerId,
    string Source,
    string Requested,
    string? DefaultRunnerId,
    string Reason,
    bool Warn)
{
    public string AuditSegment =>
        $"runner source={Source} requested={Requested} default={DefaultRunnerId ?? "unset"} "
        + $"selected={SelectedRunnerId ?? PhoneHomeProtocol.LocalRunnerId} reason={Reason}";

    public string? Warning => Warn
        ? $"Default runner '{DefaultRunnerId}' was not used ({Reason}); this task runs on the desktop. "
          + "Pass -Runner to require a runner, or -Local to choose the desktop explicitly."
        : null;
}

/// <summary>
/// CARD-0659 D-2/D-3. Chooses the execution host for a fresh delegated task whose create request
/// named none. It is a create-time decision only: it reads configuration and the runner
/// directory's synchronous dispatch-eligibility answer (lease + recovery), never inventory,
/// provider login, capacity or a health probe, and it never changes model, tier, pins or workspace.
/// </summary>
public sealed class DefaultRunnerRoutingPolicy
{
    public const string ReasonEligible = "eligible";
    public const string ReasonRequested = "requested";
    public const string ReasonLocalRequested = "local_requested";
    public const string ReasonRoutingExhausted = "routing_exhausted";
    public const string ReasonExistingProcess = "existing_process";
    public const string ReasonWorkspaceNotWorktree = "workspace_not_worktree";
    public const string ReasonKindNotSupported = "kind_not_supported";
    public const string ReasonSourceLandingNotSupported = "source_landing_not_supported";
    public const string ReasonNotEnabled = "default_runner_not_enabled";
    public const string ReasonTasksDisabled = "runner_tasks_disabled";
    public const string ReasonDirectoryUnavailable = "runner_directory_unavailable";
    public const string ReasonNotDispatchEligible = "runner_not_dispatch_eligible";
    public const string ReasonRunnerKindUnsupported = "runner_kind_unsupported";

    /// <summary>
    /// CARD-0659 D-5. The one host/kind rule every post-create kind change shares: a task bound to
    /// a runner may only run a kind that runner admits (Grok or Claude Code), whatever the model
    /// walk prefers. The desktop runs every kind. The host itself never changes after create.
    /// CARD-0660 D-10: this is the rule for a kind MOVE (reroute, rewalk, usage wall); it still
    /// excludes Codex until S7, even though an explicit create may now place Codex on a runner.
    /// </summary>
    public static bool IsHostKindCompatible(string? runnerId, AgentKind kind) =>
        string.IsNullOrWhiteSpace(runnerId) || PhoneHomeLaunchPolicy.IsWorkerAdmittedKind(kind);

    /// <summary>
    /// CARD-0660 D-7. The pre-claim fence on a task's PERSISTED kind: whatever an explicit
    /// <c>-Runner</c> create admitted (Grok, Claude Code or Codex) dispatches; anything else (a
    /// legacy or out-of-band mismatch) is Blocked, never launched remotely or moved to the desktop.
    /// </summary>
    public static bool IsHostKindAdmitted(string? runnerId, AgentKind kind) =>
        string.IsNullOrWhiteSpace(runnerId) || PhoneHomeLaunchPolicy.IsExplicitRunnerTaskKind(kind);

    /// <summary>
    /// The stable Blocked reason for an automatic choice of a kind the task's runner cannot run.
    /// It carries the routing-exhausted prefix so the existing reroute API (and nothing else) moves
    /// it on, and it names the candidate that was refused, not the kind the task keeps.
    /// </summary>
    public static string RunnerKindBlockedReason(string runnerId, AgentKind candidateKind, string candidateAlias) =>
        ComplexityRoutingService.RoutingExhaustedPrefix
        + $"{ReasonRunnerKindUnsupported} - the walk chose {candidateKind} {candidateAlias}, which runner '{runnerId}' "
        + "cannot run (Grok, Claude Code or Codex only). The task keeps its runner, kind and pins. "
        + "A human must choose; reroute to a runner-compatible kind.";

    private string? _defaultRunnerId;
    private RunnerDefaultSnapshot? _snapshot;
    private readonly PhoneHomeLaunchPolicy? _phoneHome;
    private readonly ISessionRunnerDirectory? _runners;

    public DefaultRunnerRoutingPolicy(
        DelegationSettings settings,
        PhoneHomeLaunchPolicy? phoneHome,
        ISessionRunnerDirectory? runners)
    {
        _defaultRunnerId = NormalizeDefault(settings.DefaultRunnerId);
        _phoneHome = phoneHome;
        _runners = runners;
    }

    /// <summary>The configured default, or null when unset/blank/<c>local</c> (automatic placement off).</summary>
    public string? DefaultRunnerId => _defaultRunnerId;

    /// <summary>CARD-0710. Replace the legacy key with one committed runtime snapshot for this create.</summary>
    public void ApplySnapshot(RunnerDefaultSnapshot snapshot)
    {
        _snapshot = snapshot;
        _defaultRunnerId = snapshot.GlobalRunnerId is null || snapshot.GlobalRunnerId == RunnerPlatformWire.DesktopId
            ? null
            : snapshot.GlobalRunnerId;
    }

    public static string? NormalizeDefault(string? configured) =>
        string.IsNullOrWhiteSpace(configured) || RunnerRequestIntent.IsLocalToken(configured)
            ? null
            : configured.Trim();

    /// <summary>
    /// The decision for <paramref name="intent"/>, or null for an unset request with no default
    /// configured: that request is wholly unchanged and records nothing new.
    /// </summary>
    public DefaultRunnerDecision? Decide(RunnerRequestIntent intent, DefaultRunnerShape shape)
    {
        switch (intent.Source)
        {
            case RunnerRequestSource.ExplicitLocal:
                return new DefaultRunnerDecision(null, "explicit-local", intent.RequestedToken ?? PhoneHomeProtocol.LocalRunnerId,
                    _defaultRunnerId, ReasonLocalRequested, Warn: false);
            case RunnerRequestSource.ExplicitRemote:
                return new DefaultRunnerDecision(intent.RemoteRunnerId, "explicit", intent.RemoteRunnerId!,
                    _defaultRunnerId, ReasonRequested, Warn: false);
        }

        if (_snapshot is not null
            && _snapshot.KindDefaults.TryGetValue(shape.Kind, out var kindRunner)
            && TryKindDefault(kindRunner, shape) is { } kindDecision)
            return kindDecision;

        if (_defaultRunnerId is not { } configured)
            return null;

        if (ExclusionFor(shape) is { } excluded)
            return Local(configured, excluded, warn: false);

        if (_phoneHome?.IsRunnerBound(configured) != true)
            return Local(configured, ReasonNotEnabled, warn: true);
        if (!_phoneHome.AllowsDelegatedTasks(configured))
            return Local(configured, ReasonTasksDisabled, warn: true);
        if (_runners is null)
            return Local(configured, ReasonDirectoryUnavailable, warn: true);

        try
        {
            _runners.Resolve(configured);
        }
        // Only the directory's own typed "not ready" refusal is a fallback. Cancellation and any
        // other fault propagate: they are not permission to run the work on the desktop.
        catch (HttpException ex) when (ex.Code == PhoneHomeProblemTypes.Unavailable
                                       && ex is ServiceUnavailableException or ConflictException)
        {
            return Local(configured, ReasonNotDispatchEligible, warn: true);
        }

        return new DefaultRunnerDecision(configured, "default", "unset", configured, ReasonEligible, Warn: false);
    }

    /// <summary>
    /// The first shape rule that keeps an automatic request on the desktop, in a fixed order so the
    /// recorded reason is deterministic. Null means the shape can run on a runner.
    /// </summary>
    public static string? ExclusionFor(DefaultRunnerShape shape)
    {
        if (shape.RoutingExhausted)
            return ReasonRoutingExhausted;
        if (shape.ExistingProcess)
            return ReasonExistingProcess;
        if (shape.Workspace != WorkspaceMode.Worktree)
            return ReasonWorkspaceNotWorktree;
        // CARD-0710 D-10: a Codex worker is placed like Grok and Claude Code. Named agents stay
        // on IsAdmittedKind. Orchestrators other than Claude Code, specialists and Codex
        // SourceLanding are still excluded below.
        if (!PhoneHomeLaunchPolicy.IsWorkerAdmittedKind(shape.Kind))
            return ReasonKindNotSupported;
        // Codex workers may be placed, but runner-side SourceLanding custody is still refused.
        if (shape.Kind == AgentKind.Codex && shape.SourceLanding)
            return ReasonKindNotSupported;
        // A delegated sub-orchestrator on the runner is supported for Claude Code only.
        if (shape.TaskKind == AgentTaskKind.Orchestrator && shape.Kind != AgentKind.ClaudeCode)
            return ReasonKindNotSupported;
        if (AgentTaskRoles.IsSpecialist(shape.Role))
            return ReasonKindNotSupported;
        // CARD-0604 Cut B: a SourceLanding Mutation has a runner shape (runner-side custody), so the
        // valid fresh Worker/Mutation/Worktree shape takes the default like any other task, and
        // custody admission then asks the SELECTED runner and refuses rather than falling back.
        // Create already refuses every other SourceLanding shape; this only keeps the policy honest.
        if (shape.SourceLanding && (shape.Role != AgentTaskRole.Mutation || shape.TaskKind != AgentTaskKind.Worker))
            return ReasonSourceLandingNotSupported;
        return null;
    }

    private DefaultRunnerDecision? TryKindDefault(string kindRunner, DefaultRunnerShape shape)
    {
        if (kindRunner == RunnerPlatformWire.DesktopId)
            return new DefaultRunnerDecision(null, "kind-default", "unset", RunnerPlatformWire.DesktopId,
                ReasonLocalRequested, Warn: false);
        if (ExclusionFor(shape) is not null)
            return null;
        if (_phoneHome?.IsRunnerBound(kindRunner) != true || !_phoneHome.AllowsDelegatedTasks(kindRunner) || _runners is null)
            return null;
        try
        {
            _runners.Resolve(kindRunner);
        }
        catch (HttpException ex) when (ex.Code == PhoneHomeProblemTypes.Unavailable
                                       && ex is ServiceUnavailableException or ConflictException)
        {
            return null;
        }

        return new DefaultRunnerDecision(kindRunner, "kind-default", "unset", kindRunner, ReasonEligible, Warn: false);
    }

    private static DefaultRunnerDecision Local(string configured, string reason, bool warn) =>
        new(null, "default", "unset", configured, reason, warn);
}
