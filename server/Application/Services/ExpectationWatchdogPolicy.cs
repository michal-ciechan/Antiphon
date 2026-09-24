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
        var (silent, unknownSubjects) = Silent(snapshot, directive);
        var notes = Notes(snapshot, directive);
        var observedClear = hadOpen && fenceKnown && !fencePresent && pipelines.Count == 0 && !deficitRemains
            && silent.Count == 0 && notes.Count == 0 && unknownSubjects.Count == 0;

        return new ExpectationEvaluation
        {
            StalledPipelines = pipelines,
            DispatchFence = global,
            ScopedFences = scoped,
            Capacity = capacity,
            ObservedClear = observedClear,
            PreservesOpenEpisodes = hadOpen && !fenceKnown,
            SilentInFlight = silent,
            UndeliveredNotes = notes,
            UnknownSubjectKeys = unknownSubjects,
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
            // A land or a cap hold is ordinary occupancy only with current evidence about that
            // hold itself. A dead journal, an unknown lease owner, and an ineligible runner stay in
            // the set and can still page.
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
                AffectedTaskIds = ordered.Select(task => task.TaskId).Distinct().ToList(),
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
                AffectedTaskIds = queued.Select(task => task.TaskId).Distinct().ToList(),
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
                    AffectedTaskIds = group.Select(task => task.TaskId).Distinct().ToList(),
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

    /// <summary>
    /// V-3a. Missing or terminal session plus ten silent minutes since max(dispatch, task-local
    /// activity, transcript). A live session only counts through the existing progress-stall
    /// verdict. An unreachable runner is Unknown and never proves a missing session.
    /// </summary>
    private static (List<ExpectationCondition> Silent, List<string> Unknown) Silent(
        ExpectationSnapshot snapshot, ExpectationDirectiveSettings directive)
    {
        var silent = new List<ExpectationCondition>();
        var unknown = new List<string>();
        foreach (var task in snapshot.InFlight.OrderBy(row => row.DispatchedAt).ThenBy(row => row.TaskId))
        {
            var subject = ExpectationSubjects.Silent(directive.Id, task.TaskId, task.DispatchedAt);
            var session = task.AgentSessionId?.ToString("D") ?? "none";
            switch (task.SessionState)
            {
                case ExpectationSessionState.Unknown:
                    unknown.Add(subject);
                    continue;
                case ExpectationSessionState.Missing or ExpectationSessionState.Terminal:
                {
                    var quiet = snapshot.AsOf - task.LastActivityAt;
                    if (quiet < ExpectationWindows.MissingSession)
                        continue;
                    var reason = task.SessionState == ExpectationSessionState.Missing
                        ? "missing-session"
                        : "terminal-session";
                    silent.Add(new ExpectationCondition
                    {
                        Kind = ExpectationEpisodeKind.SilentInFlight,
                        SubjectKey = subject,
                        Scope = "task:" + task.TaskId.ToString("D"),
                        ReasonCode = reason,
                        Evidence = Clip(
                            reason
                            + " task "
                            + task.TaskId.ToString("D")
                            + " session "
                            + session
                            + "; dispatched "
                            + task.DispatchedAt.ToString("O")
                            + "; no report and no activity since "
                            + task.LastActivityAt.ToString("O")
                            + " ("
                            + Minutes(quiet)
                            + "); as-of "
                            + snapshot.AsOf.ToString("O")
                            + "; inspect task status, transcript and checkpoint; no automatic cancel or stop"),
                        IsDue = true,
                        ExampleTaskIds = [task.TaskId],
                        AffectedTaskIds = [task.TaskId],
                    });
                    continue;
                }
                case ExpectationSessionState.Live when !string.IsNullOrWhiteSpace(task.ProgressStall):
                    silent.Add(new ExpectationCondition
                    {
                        Kind = ExpectationEpisodeKind.SilentInFlight,
                        SubjectKey = subject,
                        Scope = "task:" + task.TaskId.ToString("D"),
                        ReasonCode = "progress-stalled",
                        Evidence = Clip(
                            "progress-stalled task "
                            + task.TaskId.ToString("D")
                            + " session "
                            + session
                            + "; "
                            + task.ProgressStall!.Trim()
                            + " as-of "
                            + snapshot.AsOf.ToString("O")),
                        IsDue = true,
                        ExampleTaskIds = [task.TaskId],
                        AffectedTaskIds = [task.TaskId],
                    });
                    continue;
            }
        }

        return (silent, unknown);
    }

    /// <summary>
    /// V-3b. Age runs from CreatedAt; a retry, a new NextAttemptAt or a Sent queue row never
    /// resets it.
    /// </summary>
    private static List<ExpectationCondition> Notes(ExpectationSnapshot snapshot, ExpectationDirectiveSettings directive)
    {
        var due = new List<ExpectationCondition>();
        foreach (var note in snapshot.Notes.OrderBy(row => row.CreatedAt).ThenBy(row => row.NotificationId))
        {
            var age = snapshot.AsOf - note.CreatedAt;
            if (age < ExpectationWindows.Note)
                continue;
            due.Add(new ExpectationCondition
            {
                Kind = ExpectationEpisodeKind.UndeliveredNote,
                SubjectKey = ExpectationSubjects.Note(directive.Id, note.NotificationId),
                Scope = "note:" + note.NotificationId.ToString("D"),
                ReasonCode = "undelivered-note",
                Evidence = Clip(
                    "note "
                    + note.NotificationId.ToString("D")
                    + " task "
                    + note.TaskId.ToString("D")
                    + " kind "
                    + note.Kind
                    + " state "
                    + note.State
                    + " age "
                    + Minutes(age)
                    + " destination "
                    + (note.ParentSessionId?.ToString("D") ?? "none")
                    + (note.QueueStatus is { } queue ? " queue " + queue : string.Empty)
                    + " last error "
                    + (string.IsNullOrWhiteSpace(note.LastErrorCode) ? "none" : note.LastErrorCode.Trim())
                    + "; as-of "
                    + snapshot.AsOf.ToString("O")),
                IsDue = true,
                ExampleTaskIds = [note.TaskId],
                AffectedTaskIds = [note.TaskId],
            });
        }

        return due;
    }

    private static string Minutes(TimeSpan span) =>
        ((int)Math.Floor(span.TotalMinutes)).ToString(System.Globalization.CultureInfo.InvariantCulture) + "m";

    /// <summary>
    /// A land or cap hold is ordinary occupancy only with current evidence about that hold itself.
    /// Other running work on the board proves nothing: it is exactly what hid a five-hour stalled
    /// queue overnight. Every other ordinary wait stays in the set and can page once aged.
    /// </summary>
    private static bool IsProvenContinuingOrdinaryWait(ExpectationQueuedTask task, ExpectationSnapshot snapshot)
    {
        if (task.HoldClass != ExpectationHoldClass.OrdinaryWait)
            return false;
        if (string.IsNullOrWhiteSpace(task.HoldDetail))
            return false;
        var reason = DispatchHoldDetails.Reason(task.HoldDetail);
        if (DispatchHoldDetails.LandHolder(reason) is { } holder)
            return IsLandProgressing(holder.TaskShort, holder.RequestShort, snapshot);
        if (DispatchHoldDetails.ConcurrencyCapLimit(reason) is int cap)
            return IsCapFullOfProgressingTasks(cap, snapshot);
        return false;
    }

    /// <summary>
    /// The named land (and, when the hold names it, that exact request) is still pending, queued or
    /// running rather than held or needing resolution, and moved within the land monitor's window.
    /// </summary>
    private static bool IsLandProgressing(string taskShort, string? requestShort, ExpectationSnapshot snapshot) =>
        snapshot.Lands.Any(land =>
            land.TaskId.ToString("N").StartsWith(taskShort, StringComparison.Ordinal)
            && (requestShort is null || land.RequestId.ToString("N").StartsWith(requestShort, StringComparison.Ordinal))
            && land.State is LandRequestState.Queued or LandRequestState.Running
            && snapshot.AsOf - land.LastProgressAt < snapshot.LandProgressWindow);

    /// <summary>
    /// The cap is full now, and every occupant is itself progressing: a live session with no
    /// progress-stall verdict. An occupant with a report, a missing or terminal session, or an
    /// unknown runner answer is not proof.
    /// </summary>
    private static bool IsCapFullOfProgressingTasks(int cap, ExpectationSnapshot snapshot) =>
        cap > 0
        && snapshot.CapOccupantCount >= cap
        && snapshot.CapOccupants.Count == snapshot.CapOccupantCount
        && snapshot.CapOccupants.All(occupant =>
            occupant.SessionState == ExpectationSessionState.Live
            && string.IsNullOrWhiteSpace(occupant.ProgressStall));

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
