using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// Pure queue, fence and capacity decisions. Detection only: no send, no journal recovery.
/// </summary>
public static class ExpectationWatchdogPolicy
{
    public const int MaxEvidenceChars = 2000;
    public const int MaxExamples = 3;

    public static ExpectationEvaluation Evaluate(ExpectationSnapshot snapshot, ExpectationDirectiveSettings directive)
    {
        if (!snapshot.DirectiveActive)
        {
            return new ExpectationEvaluation
            {
                PreservesOpenEpisodes = true,
            };
        }

        if (snapshot.ProbeUnknown)
        {
            return new ExpectationEvaluation
            {
                ObservationUnknown = true,
                PreservesOpenEpisodes = true,
                ProbeError = snapshot.ProbeError,
            };
        }

        var pipelines = Pipelines(snapshot, directive);
        var (global, scoped) = Fences(snapshot, directive);
        global ??= AdmissionFence(snapshot, directive);
        var capacity = Capacity(snapshot, directive);
        var fenceKnown = snapshot.Queued.All(task => task.HoldClass != ExpectationHoldClass.Unknown);
        var fencePresent = global is not null || scoped.Count > 0;
        var deficitRemains = capacity.Any(lane => lane.InDeficit);
        var hadOpen = snapshot.OpenEpisodes.Count > 0;
        var observedClear = hadOpen && fenceKnown && !fencePresent && pipelines.Count == 0 && !deficitRemains;

        return new ExpectationEvaluation
        {
            StalledPipelines = pipelines,
            DispatchFence = global,
            ScopedFences = scoped,
            Capacity = capacity,
            ObservedClear = observedClear,
            PreservesOpenEpisodes = hadOpen && !fenceKnown,
        };
    }

    private static List<ExpectationCondition> Pipelines(
        ExpectationSnapshot snapshot, ExpectationDirectiveSettings directive)
    {
        var due = new List<ExpectationCondition>();
        if (snapshot.Queued.Count == 0)
            return due;

        var recent = snapshot.LastScopedDispatchAt is { } dispatched
            && snapshot.AsOf - dispatched < ExpectationWindows.Queue;
        foreach (var group in snapshot.Queued.GroupBy(task => task.RepositoryScope, StringComparer.Ordinal))
        {
            // A live land or a full cap is ordinary occupancy. A dead journal, an unknown
            // lease owner, and an ineligible runner stay in the set and can still page.
            var ordered = group
                .OrderBy(task => task.StintStartedAt)
                .ThenBy(task => task.TaskId)
                .Where(task => !IsProvenContinuingOrdinaryWait(task, snapshot))
                .ToList();
            if (ordered.Count == 0)
                continue;
            var oldest = ordered[0].StintStartedAt;
            if (snapshot.AsOf - oldest < ExpectationWindows.Queue || recent)
                continue;

            var examples = Examples(ordered.Select(task => task.TaskId));
            var reason = ordered.Select(task => task.HoldDetail).FirstOrDefault(text => !string.IsNullOrWhiteSpace(text));
            var evidence = Clip(
                "queued "
                + FormatIds(examples)
                + " stint since "
                + oldest.ToString("O")
                + "; last dispatch "
                + (snapshot.LastScopedDispatchAt?.ToString("O") ?? "none")
                + "; held "
                + (reason ?? "none")
                + "; as-of "
                + snapshot.AsOf.ToString("O"));
            due.Add(new ExpectationCondition
            {
                Kind = ExpectationEpisodeKind.StalledPipeline,
                SubjectKey = ExpectationSubjects.Pipeline(directive.Id, group.Key),
                Scope = group.Key,
                ReasonCode = "stalled-pipeline",
                Evidence = evidence,
                IsDue = true,
                Immediate = false,
                ExampleTaskIds = examples,
            });
        }

        return due;
    }

    private static (ExpectationCondition? Global, List<ExpectationCondition> Scoped) Fences(
        ExpectationSnapshot snapshot, ExpectationDirectiveSettings directive)
    {
        var scoped = new List<ExpectationCondition>();
        if (snapshot.Queued.Count == 0)
            return (null, scoped);

        var queued = snapshot.Queued;
        var anyUnknown = queued.Any(task => task.HoldClass == ExpectationHoldClass.Unknown);
        var anyOrdinaryOrOpen = queued.Any(task =>
            task.HoldClass is null or ExpectationHoldClass.OrdinaryWait);
        var allExplicit = queued.All(IsExplicitFence);
        ExpectationCondition? global = null;
        if (allExplicit && !anyUnknown && !anyOrdinaryOrOpen && AllRepositoryFence(queued))
        {
            var scope = queued[0].RepositoryScope;
            var ownerUnknown = queued.All(task => task.HoldClass == ExpectationHoldClass.RepositoryOwnerUnknown);
            var examples = Examples(queued.OrderBy(task => task.TaskId).Select(task => task.TaskId));
            var reason = ownerUnknown ? "repository-owner-unknown" : "repository-fenced";
            var action = ownerUnknown
                ? "Inspect who holds the repository lease. Do not unlock it from this signal."
                : "Inspect the recorded fence read-only; journal recovery stays a human decision.";
            global = new ExpectationCondition
            {
                Kind = ExpectationEpisodeKind.DispatchFence,
                SubjectKey = ExpectationSubjects.Fence(directive.Id, scope),
                Scope = scope,
                ReasonCode = reason,
                Evidence = Clip(
                    reason
                    + " "
                    + scope
                    + " tasks "
                    + FormatIds(examples)
                    + "; as-of "
                    + snapshot.AsOf.ToString("O")
                    + "; "
                    + action
                    + " "
                    + FirstDetail(queued)),
                IsDue = true,
                Immediate = true,
                ExampleTaskIds = examples,
            };
        }

        if (global is null)
        {
            foreach (var group in queued.GroupBy(task => ExpectationSubjects.RunnerKey(task.RunnerId), StringComparer.Ordinal))
            {
                if (!group.All(task => task.HoldClass == ExpectationHoldClass.RunnerUnavailable))
                    continue;
                var runner = group.Key;
                var examples = Examples(group.OrderBy(task => task.TaskId).Select(task => task.TaskId));
                scoped.Add(new ExpectationCondition
                {
                    Kind = ExpectationEpisodeKind.DispatchFence,
                    SubjectKey = ExpectationSubjects.Fence(directive.Id, "runner:" + runner),
                    Scope = "runner:" + runner,
                    ReasonCode = "runner-unavailable",
                    Evidence = Clip(
                        "runner "
                        + runner
                        + " is unavailable for every queued task on that runner; not an all-board fence; tasks "
                        + FormatIds(examples)
                        + "; as-of "
                        + snapshot.AsOf.ToString("O")),
                    IsDue = true,
                    Immediate = true,
                    ExampleTaskIds = examples,
                });
            }
        }

        return (global, scoped);
    }

    private static ExpectationCondition? AdmissionFence(
        ExpectationSnapshot snapshot, ExpectationDirectiveSettings directive)
    {
        if (snapshot.EligibleBacklog <= 0 || snapshot.Admission.Count == 0)
            return null;
        if (!snapshot.Admission.All(candidate =>
                candidate.State is ExpectationAdmissionState.QuotaRefused or ExpectationAdmissionState.ModelHeld))
            return null;

        var quota = snapshot.Admission.All(candidate => candidate.State == ExpectationAdmissionState.QuotaRefused);
        var model = snapshot.Admission.All(candidate => candidate.State == ExpectationAdmissionState.ModelHeld);
        var reason = quota
            ? "quota-new-admission"
            : model ? "model-held-admission" : "admission-blocked";
        var quotaSentence = quota
            ? "every declared candidate is quota-refused; already queued work is not blocked by quota"
            : "every declared new-admission candidate is blocked";
        return new ExpectationCondition
        {
            Kind = ExpectationEpisodeKind.DispatchFence,
            SubjectKey = ExpectationSubjects.Fence(directive.Id, "admission"),
            Scope = "admission",
            ReasonCode = reason,
            Evidence = Clip(
                "new-admission fence; "
                + quotaSentence
                + "; eligible backlog "
                + snapshot.EligibleBacklog.ToString(System.Globalization.CultureInfo.InvariantCulture)
                + "; as-of "
                + snapshot.AsOf.ToString("O")),
            IsDue = true,
            Immediate = true,
            ExampleTaskIds = [],
        };
    }

    private static List<ExpectationCapacityVerdict> Capacity(
        ExpectationSnapshot snapshot, ExpectationDirectiveSettings directive)
    {
        var results = new List<ExpectationCapacityVerdict>(snapshot.Lanes.Count);
        foreach (var lane in snapshot.Lanes)
        {
            var inDeficit = snapshot.EligibleBacklog > 0 && lane.Running < lane.Target;
            DateTime? clock = null;
            var due = false;
            if (inDeficit)
            {
                if (snapshot.ConfigChanged || lane.DeficitSince is null)
                {
                    clock = snapshot.AsOf;
                }
                else
                {
                    clock = lane.DeficitSince;
                    due = snapshot.AsOf - lane.DeficitSince.Value >= ExpectationWindows.Capacity;
                }
            }

            var explains = inDeficit && lane.Queued > 0;
            var runner = ExpectationSubjects.RunnerKey(lane.RunnerId);
            var evidence = inDeficit
                ? Clip(
                    "runner "
                    + runner
                    + " running "
                    + lane.Running.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    + " of "
                    + lane.Target.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    + "; queued "
                    + lane.Queued.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    + "; eligible backlog "
                    + snapshot.EligibleBacklog.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    + "; deficit since "
                    + (clock?.ToString("O") ?? "none")
                    + "; as-of "
                    + snapshot.AsOf.ToString("O")
                    + (explains
                        ? "; queued work already explains the deficit; do not duplicate it"
                        : "; select an admissible next card"))
                : string.Empty;
            results.Add(new ExpectationCapacityVerdict
            {
                RunnerId = lane.RunnerId,
                SubjectKey = ExpectationSubjects.Capacity(directive.Id, lane.RunnerId),
                InDeficit = inDeficit,
                IsDue = due,
                ExplainsHeldQueue = explains,
                ClockStartedAt = clock,
                Running = lane.Running,
                Target = lane.Target,
                Queued = lane.Queued,
                EligibleBacklog = snapshot.EligibleBacklog,
                Evidence = evidence,
            });
        }

        return results;
    }

    private static bool IsProvenContinuingOrdinaryWait(ExpectationQueuedTask task, ExpectationSnapshot snapshot)
    {
        if (task.HoldClass != ExpectationHoldClass.OrdinaryWait)
            return false;
        if (string.IsNullOrWhiteSpace(task.HoldDetail))
            return false;
        if (!snapshot.Lanes.Any(lane => lane.Running > 0))
            return false;
        return IsLandInProgress(task.HoldDetail) || IsCapWithRunningTasks(task.HoldDetail);
    }

    private static bool IsLandInProgress(string detail) =>
        detail.Contains("repository mutation lease is held by the land", StringComparison.Ordinal)
        || detail.Contains("is landing", StringComparison.Ordinal);

    private static bool IsCapWithRunningTasks(string detail)
    {
        const string marker = "concurrency cap reached (";
        var start = detail.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0)
            return false;
        start += marker.Length;
        var end = start;
        while (end < detail.Length && char.IsDigit(detail[end]))
            end++;
        if (end == start)
            return false;
        return int.TryParse(
                detail[start..end],
                System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture,
                out var running)
            && running > 0;
    }

    private static bool IsExplicitFence(ExpectationQueuedTask task) =>
        task.HoldClass is ExpectationHoldClass.RepositoryFenced
            or ExpectationHoldClass.RepositoryOwnerUnknown
            or ExpectationHoldClass.RunnerUnavailable
            or ExpectationHoldClass.ModelHeld;

    private static bool AllRepositoryFence(IReadOnlyList<ExpectationQueuedTask> queued)
    {
        var scope = queued[0].RepositoryScope;
        return queued.All(task =>
            task.RepositoryScope == scope
            && task.HoldClass is ExpectationHoldClass.RepositoryFenced
                or ExpectationHoldClass.RepositoryOwnerUnknown);
    }

    private static IReadOnlyList<Guid> Examples(IEnumerable<Guid> ids) =>
        ids.Distinct().Take(MaxExamples).ToList();

    private static string FormatIds(IReadOnlyList<Guid> ids) =>
        ids.Count == 0 ? "none" : string.Join(",", ids.Select(id => id.ToString("D")));

    private static string FirstDetail(IEnumerable<ExpectationQueuedTask> queued) =>
        queued.Select(task => task.HoldDetail).FirstOrDefault(text => !string.IsNullOrWhiteSpace(text)) ?? "none";

    private static string Clip(string value)
    {
        var trimmed = value.Trim();
        return trimmed.Length <= MaxEvidenceChars ? trimmed : trimmed[..MaxEvidenceChars];
    }
}
