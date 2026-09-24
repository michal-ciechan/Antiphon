using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// Diagnostic class for one dispatcher hold sentence. Not a task status and not dispatch authority.
/// </summary>
public enum ExpectationHoldClass
{
    Unknown = 0,
    RepositoryFenced = 1,
    RepositoryOwnerUnknown = 2,
    RunnerUnavailable = 3,
    ModelHeld = 4,
    OrdinaryWait = 5,
}

public readonly record struct ExpectationHoldClassification(ExpectationHoldClass Class, string Evidence);

public enum ExpectationScopeDecision
{
    Excluded = 0,
    Included = 1,
    AmbiguousUnbound = 2,
}

public enum ExpectationAdmissionState
{
    Open = 0,
    QuotaRefused = 1,
    ModelHeld = 2,
    Unknown = 3,
}

public static class ExpectationWindows
{
    public static readonly TimeSpan Queue = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan Capacity = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan MissingSession = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan Note = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan Cooldown = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan Repeat = TimeSpan.FromMinutes(30);
    public static readonly TimeSpan ClearGap = TimeSpan.FromMinutes(1);
}

public static class ExpectationSubjects
{
    public static string Pipeline(string directiveId, string repositoryScope) =>
        "pipeline:" + directiveId.Trim() + ":" + repositoryScope;

    public static string Fence(string directiveId, string scope) =>
        "fence:" + directiveId.Trim() + ":" + scope;

    public static string Capacity(string directiveId, string? runnerId) =>
        "capacity:" + directiveId.Trim() + ":" + RunnerKey(runnerId);

    /// <summary>Task plus dispatch stint. A new dispatch is a new subject.</summary>
    public static string Silent(string directiveId, Guid taskId, DateTime dispatchedAt) =>
        "silent:" + directiveId.Trim() + ":" + taskId.ToString("D") + ":"
        + dispatchedAt.Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture);

    public static string Note(string directiveId, Guid notificationId) =>
        "note:" + directiveId.Trim() + ":" + notificationId.ToString("D");

    public static string RunnerKey(string? runnerId) =>
        string.IsNullOrWhiteSpace(runnerId) ? "local" : runnerId.Trim();

    public static bool SameRunner(string? left, string? right) =>
        string.Equals(RunnerKey(left), RunnerKey(right), StringComparison.Ordinal);
}

public static class ExpectationRepository
{
    public static string For(string? repoPath, Guid boardId) =>
        string.IsNullOrWhiteSpace(repoPath)
            ? "board:" + boardId.ToString("D")
            : "repo:" + repoPath.Trim();
}

public readonly record struct ExpectationScopeContext(Guid BoardId, Guid ProjectId, Guid StandingAgentId);

public readonly record struct ExpectationTaskScopeFacts(
    Guid? CardBoardId,
    Guid? TaskProjectId,
    Guid? CardProjectId,
    Guid? ParentStandingAgentId,
    bool ParentSessionKnown);

public static class ExpectationScope
{
    public static ExpectationScopeDecision Decide(ExpectationTaskScopeFacts facts, ExpectationScopeContext scope)
    {
        if (facts.CardBoardId is Guid boardId)
        {
            if (boardId != scope.BoardId)
                return ExpectationScopeDecision.Excluded;
            if (facts.TaskProjectId is Guid taskProject && taskProject != scope.ProjectId)
                return ExpectationScopeDecision.Excluded;
            if (facts.TaskProjectId is null
                && facts.CardProjectId is Guid cardProject
                && cardProject != scope.ProjectId)
                return ExpectationScopeDecision.Excluded;
            return ExpectationScopeDecision.Included;
        }

        if (facts.TaskProjectId != scope.ProjectId)
            return ExpectationScopeDecision.Excluded;
        if (!facts.ParentSessionKnown || facts.ParentStandingAgentId != scope.StandingAgentId)
            return ExpectationScopeDecision.AmbiguousUnbound;
        return ExpectationScopeDecision.Included;
    }
}

public readonly record struct ExpectationStintEvent(AgentTaskEventType Type, DateTime At);

public static class ExpectationQueueStint
{
    public static DateTime Start(DateTime createdAt, IEnumerable<ExpectationStintEvent> events)
    {
        var start = createdAt;
        foreach (var row in events)
        {
            if (!Resets(row.Type) || row.At <= start)
                continue;
            start = row.At;
        }

        return start;
    }

    public static bool Resets(AgentTaskEventType type) =>
        type is AgentTaskEventType.Created
            or AgentTaskEventType.Retried
            or AgentTaskEventType.Escalated
            or AgentTaskEventType.Rerouted;
}

public readonly record struct ExpectationDispatchMark(
    DateTime At,
    Guid? CardBoardId,
    Guid? TaskProjectId,
    Guid? CardProjectId,
    Guid? ParentStandingAgentId,
    bool ParentSessionKnown);

public static class ExpectationDispatchProgress
{
    public static DateTime? Latest(IEnumerable<ExpectationDispatchMark> marks, ExpectationScopeContext scope, DateTime asOf)
    {
        DateTime? latest = null;
        foreach (var mark in marks)
        {
            if (mark.At > asOf)
                continue;
            var decision = ExpectationScope.Decide(
                new ExpectationTaskScopeFacts(
                    mark.CardBoardId,
                    mark.TaskProjectId,
                    mark.CardProjectId,
                    mark.ParentStandingAgentId,
                    mark.ParentSessionKnown),
                scope);
            if (decision != ExpectationScopeDecision.Included)
                continue;
            if (latest is null || mark.At > latest)
                latest = mark.At;
        }

        return latest;
    }
}

public static class ExpectationAdmission
{
    public static ExpectationAdmissionState Classify(
        SubscriptionUsageSnapshot? sample,
        bool sampleKnown,
        bool? modelHeld,
        SubscriptionQuotaGateSettings settings,
        DateTime now)
    {
        if (!sampleKnown || sample is null || modelHeld is null)
            return ExpectationAdmissionState.Unknown;
        if (sample.Age > TimeSpan.FromMinutes(settings.MaxSampleAgeMinutes))
            return ExpectationAdmissionState.Unknown;

        if (SubscriptionQuotaPolicy.Evaluate(sample, settings, now) is not null)
            return ExpectationAdmissionState.QuotaRefused;
        if (modelHeld.Value)
            return ExpectationAdmissionState.ModelHeld;
        return ExpectationAdmissionState.Open;
    }
}

public sealed record ExpectationQueuedTask
{
    public Guid TaskId { get; init; }
    public string? RunnerId { get; init; }
    public string RepositoryScope { get; init; } = string.Empty;
    public DateTime StintStartedAt { get; init; }
    public ExpectationHoldClass? HoldClass { get; init; }
    public string? HoldDetail { get; init; }
    public DateTime? HoldObservedAt { get; init; }
}

public sealed record ExpectationLaneSnapshot
{
    public string? RunnerId { get; init; }
    public int Target { get; init; }
    public int Running { get; init; }
    public int Queued { get; init; }
    public int Blocked { get; init; }
    public DateTime? DeficitSince { get; init; }
    public IReadOnlyList<Guid> RunningTaskIds { get; init; } = [];
    public IReadOnlyList<Guid> QueuedTaskIds { get; init; } = [];
}

public sealed record ExpectationAdmissionCandidate
{
    public string? RunnerId { get; init; }
    public AgentKind AgentKind { get; init; }
    public AgentModelLevel ModelLevel { get; init; }
    public string SubscriptionKey { get; init; } = string.Empty;
    public ExpectationAdmissionState State { get; init; }
    public string? Evidence { get; init; }
}

/// <summary>What the watchdog knows about a dispatched task's worker session.</summary>
public enum ExpectationSessionState
{
    /// <summary>No binding, no row, or the runner confirmed the session is gone.</summary>
    Missing = 0,
    Terminal = 1,
    Live = 2,
    /// <summary>Runner unreachable. Not proof that the session is missing.</summary>
    Unknown = 3,
}

public sealed record ExpectationInFlightTask
{
    public Guid TaskId { get; init; }
    public Guid? AgentSessionId { get; init; }
    public string? RunnerId { get; init; }
    public DateTime DispatchedAt { get; init; }
    public DateTime LastActivityAt { get; init; }
    public ExpectationSessionState SessionState { get; init; }
    public string? ProgressStall { get; init; }
}

public sealed record ExpectationNoteDebt
{
    public Guid NotificationId { get; init; }
    public Guid TaskId { get; init; }
    public LandNotificationKind Kind { get; init; }
    public LandNotificationState State { get; init; }
    public DateTime CreatedAt { get; init; }
    public Guid? ParentSessionId { get; init; }
    public string? LastErrorCode { get; init; }
    public QueuedMessageStatus? QueueStatus { get; init; }
}

public sealed record ExpectationOpenEpisode
{
    public ExpectationEpisodeKind Kind { get; init; }
    public string SubjectKey { get; init; } = string.Empty;
    public DateTime FirstObservedAt { get; init; }
    public string Evidence { get; init; } = string.Empty;
}

public sealed record ExpectationSnapshot
{
    public DateTime AsOf { get; init; }
    public bool DirectiveActive { get; init; } = true;
    public bool ProbeUnknown { get; init; }
    public string? ProbeError { get; init; }
    public bool ConfigChanged { get; init; }
    public string RepositoryScope { get; init; } = string.Empty;
    public DateTime? LastScopedDispatchAt { get; init; }
    public int EligibleBacklog { get; init; }
    public IReadOnlyList<Guid> BacklogCandidateIds { get; init; } = [];
    public int AmbiguousUnboundExcluded { get; init; }
    public IReadOnlyList<Guid> IncludedTaskIds { get; init; } = [];
    public IReadOnlyList<ExpectationQueuedTask> Queued { get; init; } = [];
    public IReadOnlyList<ExpectationLaneSnapshot> Lanes { get; init; } = [];
    public IReadOnlyList<ExpectationAdmissionCandidate> Admission { get; init; } = [];
    public IReadOnlyList<ExpectationOpenEpisode> OpenEpisodes { get; init; } = [];
    public IReadOnlyList<ExpectationInFlightTask> InFlight { get; init; } = [];
    public IReadOnlyList<ExpectationNoteDebt> Notes { get; init; } = [];
}

public sealed record ExpectationCondition
{
    public required ExpectationEpisodeKind Kind { get; init; }
    public required string SubjectKey { get; init; }
    public required string Scope { get; init; }
    public required string ReasonCode { get; init; }
    public required string Evidence { get; init; }
    public bool IsDue { get; init; }
    public bool Immediate { get; init; }
    public bool ExplainsHeldQueue { get; init; }
    public IReadOnlyList<Guid> ExampleTaskIds { get; init; } = [];
    /// <summary>Every task the condition names, for audit. The prompt shows at most three.</summary>
    public IReadOnlyList<Guid> AffectedTaskIds { get; init; } = [];
}

public sealed record ExpectationCapacityVerdict
{
    public string? RunnerId { get; init; }
    public required string SubjectKey { get; init; }
    public bool InDeficit { get; init; }
    public bool IsDue { get; init; }
    public bool ExplainsHeldQueue { get; init; }
    public DateTime? ClockStartedAt { get; init; }
    public int Running { get; init; }
    public int Target { get; init; }
    public int Queued { get; init; }
    public int EligibleBacklog { get; init; }
    public string Evidence { get; init; } = string.Empty;
}

public sealed record ExpectationEvaluation
{
    public IReadOnlyList<ExpectationCondition> StalledPipelines { get; init; } = [];
    public ExpectationCondition? DispatchFence { get; init; }
    public IReadOnlyList<ExpectationCondition> ScopedFences { get; init; } = [];
    public IReadOnlyList<ExpectationCapacityVerdict> Capacity { get; init; } = [];
    public bool ObservationUnknown { get; init; }
    public bool PreservesOpenEpisodes { get; init; }
    public bool ObservedClear { get; init; }
    public string? ProbeError { get; init; }
    public IReadOnlyList<ExpectationCondition> SilentInFlight { get; init; } = [];
    public IReadOnlyList<ExpectationCondition> UndeliveredNotes { get; init; } = [];
    /// <summary>Subjects whose evidence was unknown this scan. Their open episodes stay open.</summary>
    public IReadOnlyList<string> UnknownSubjectKeys { get; init; } = [];
}

public sealed record ExpectationRunnerProbe(bool? Available, DateTime? AsOf, string? Detail);

/// <summary>Runner answer for one session. Live null means the runner could not be asked.</summary>
public sealed record ExpectationSessionProbe(bool? Live, DateTime? AsOf, string? Detail);

public sealed record ExpectationCandidateProbe(
    string? RunnerId,
    AgentKind AgentKind,
    AgentModelLevel ModelLevel,
    string SubscriptionKey,
    SubscriptionUsageSnapshot? Sample,
    bool SampleKnown,
    bool? ModelHeld);

public sealed record ExpectationProbeInput(
    SubscriptionQuotaGateSettings? QuotaSettings,
    IReadOnlyList<ExpectationCandidateProbe> Candidates,
    IReadOnlyDictionary<string, ExpectationRunnerProbe> Runners,
    bool Unavailable,
    string? Error,
    IReadOnlyDictionary<Guid, ExpectationSessionProbe>? Sessions = null,
    IReadOnlyDictionary<Guid, WorkspaceProgressArm>? Workspace = null)
{
    public static ExpectationProbeInput None { get; } = new(
        null,
        [],
        new Dictionary<string, ExpectationRunnerProbe>(StringComparer.Ordinal),
        false,
        null);
}

public sealed record ExpectationScanResult(
    ExpectationEvaluation Evaluation,
    int NudgesCommitted,
    Guid? NudgeId = null,
    IReadOnlyList<string>? NudgedSubjectKeys = null);

/// <summary>
/// Transcript and receipt catch-up before a nudge is minted (CARD-0055). Production pulls the
/// named sessions' transcripts; it never marks a note delivered or resends it.
/// </summary>
public interface IExpectationCatchUp
{
    Task CatchUpAsync(IReadOnlyCollection<Guid> sessionIds, CancellationToken ct);
}

public sealed class NoExpectationCatchUp : IExpectationCatchUp
{
    public static NoExpectationCatchUp Instance { get; } = new();

    public Task CatchUpAsync(IReadOnlyCollection<Guid> sessionIds, CancellationToken ct) => Task.CompletedTask;
}
