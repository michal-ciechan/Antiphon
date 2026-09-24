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

public sealed record RunnerRequestIntent(RunnerRequestSource Source, string? RemoteRunnerId)
{
    public const int MaxRunnerIdLength = 64;

    /// <summary>Case-insensitive, like <c>delegate.ps1 -Runner local</c> is typed.</summary>
    public static bool IsLocalToken(string? value) =>
        value is not null && string.Equals(value.Trim(), PhoneHomeProtocol.LocalRunnerId, StringComparison.OrdinalIgnoreCase);

    public static RunnerRequestIntent Parse(string? runnerId)
    {
        if (string.IsNullOrWhiteSpace(runnerId))
            return new RunnerRequestIntent(RunnerRequestSource.Unset, null);
        var trimmed = runnerId.Trim();
        if (DescribeInvalid(trimmed) is { } problem)
            throw new ValidationException(nameof(Dtos.CreateAgentTaskRequest.RunnerId), $"runnerId {problem}.", "runner_id_invalid");
        return IsLocalToken(trimmed)
            ? new RunnerRequestIntent(RunnerRequestSource.ExplicitLocal, null)
            : new RunnerRequestIntent(RunnerRequestSource.ExplicitRemote, trimmed);
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

    private readonly string? _defaultRunnerId;
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
                return new DefaultRunnerDecision(null, "explicit-local", PhoneHomeProtocol.LocalRunnerId,
                    _defaultRunnerId, ReasonLocalRequested, Warn: false);
            case RunnerRequestSource.ExplicitRemote:
                return new DefaultRunnerDecision(intent.RemoteRunnerId, "explicit", intent.RemoteRunnerId!,
                    _defaultRunnerId, ReasonRequested, Warn: false);
        }

        if (_defaultRunnerId is not { } configured)
            return null;

        if (ExclusionFor(shape) is { } excluded)
            return Local(configured, excluded, warn: false);

        if (_phoneHome?.IsRunnerBound(configured) != true)
            return Local(configured, ReasonNotEnabled, warn: true);
        if (!_phoneHome.AllowDelegatedTasks)
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
        if (!PhoneHomeLaunchPolicy.IsAdmittedKind(shape.Kind))
            return ReasonKindNotSupported;
        // A delegated sub-orchestrator on the runner is supported for Claude Code only.
        if (shape.TaskKind == AgentTaskKind.Orchestrator && shape.Kind != AgentKind.ClaudeCode)
            return ReasonKindNotSupported;
        if (AgentTaskRoles.IsSpecialist(shape.Role))
            return ReasonKindNotSupported;
        // The dispatcher's launch policy still refuses a runner-bound SourceLanding task, so an
        // automatic choice would only queue work to be refused; an explicit -Runner keeps the
        // existing create admission.
        if (shape.SourceLanding)
            return ReasonSourceLandingNotSupported;
        return null;
    }

    private static DefaultRunnerDecision Local(string configured, string reason, bool warn) =>
        new(null, "default", "unset", configured, reason, warn);
}
