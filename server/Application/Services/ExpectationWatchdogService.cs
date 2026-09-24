using System.Text.Json;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// Records observations, keeps one episode per condition, resolves after two clear scans, and
/// commits at most one aggregate nudge per directive per cooldown with its audit. It never sends
/// the nudge (S4) or pages anyone (S5), and never dispatches, cancels, recovers or moves a card.
/// </summary>
public sealed class ExpectationWatchdogService
{
    private readonly AppDbContext _db;
    private readonly ExpectationSnapshotReader _reader;
    private readonly ExpectationLedger _ledger;
    private readonly TimeProvider _time;
    private readonly IExpectationCatchUp _catchUp;

    public ExpectationWatchdogService(
        AppDbContext db,
        ExpectationLedger ledger,
        TimeProvider time,
        DelegationSettings? delegation = null,
        IExpectationCatchUp? catchUp = null)
    {
        _db = db;
        _reader = new ExpectationSnapshotReader(db, delegation);
        _ledger = ledger;
        _time = time;
        _catchUp = catchUp ?? NoExpectationCatchUp.Instance;
    }

    /// <summary>
    /// Scan every directive, least recently scanned successfully first. One directive's failure is
    /// recorded on its own state and does not skip the others.
    /// </summary>
    public async Task<IReadOnlyDictionary<string, ExpectationScanResult?>> ScanAllAsync(
        IReadOnlyList<ExpectationDirectiveSettings> directives,
        Func<ExpectationDirectiveSettings, ExpectationProbeInput> probes,
        CancellationToken ct)
    {
        var ids = directives.Select(directive => directive.Id.Trim()).ToList();
        var cursors = await _db.ExpectationWatchStates.AsNoTracking()
            .Where(state => ids.Contains(state.DirectiveId))
            .ToDictionaryAsync(state => state.DirectiveId, state => state.LastSuccessfulScanAt, ct);
        var ordered = directives
            .OrderBy(directive => cursors.TryGetValue(directive.Id.Trim(), out var at) ? at ?? DateTime.MinValue : DateTime.MinValue)
            .ThenBy(directive => directive.Id, StringComparer.Ordinal)
            .ToList();
        var results = new Dictionary<string, ExpectationScanResult?>(StringComparer.Ordinal);
        foreach (var directive in ordered)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                results[directive.Id] = await ScanAsync(directive, probes(directive), ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _db.ChangeTracker.Clear();
                results[directive.Id] = null;
                var asOf = DateTime.SpecifyKind(_time.GetUtcNow().UtcDateTime, DateTimeKind.Utc);
                await _ledger.RecordObservationAsync(
                    directive.Id,
                    ExpectationDirectiveDigest.Compute(directive),
                    asOf,
                    successful: false,
                    "scan failed: " + ex.GetType().Name,
                    ct);
                _db.ChangeTracker.Clear();
            }
        }

        return results;
    }

    public async Task<ExpectationScanResult> ScanAsync(
        ExpectationDirectiveSettings directive,
        ExpectationProbeInput probes,
        CancellationToken ct)
    {
        var asOf = DateTime.SpecifyKind(_time.GetUtcNow().UtcDateTime, DateTimeKind.Utc);
        var digest = ExpectationDirectiveDigest.Compute(directive);
        var snapshot = await _reader.ReadAsync(directive, digest, asOf, probes, ct);
        var evaluation = ExpectationWatchdogPolicy.Evaluate(snapshot, directive);
        if (snapshot.ProbeUnknown || evaluation.ObservationUnknown || !snapshot.DirectiveActive)
        {
            await _ledger.RecordObservationAsync(
                directive.Id,
                digest,
                asOf,
                successful: false,
                snapshot.ProbeError ?? evaluation.ProbeError ?? "observation preserved",
                ct);
            _db.ChangeTracker.Clear();
            return new ExpectationScanResult(evaluation, 0);
        }

        var previousScan = await _db.ExpectationWatchStates.AsNoTracking()
            .Where(state => state.DirectiveId == directive.Id)
            .Select(state => state.LastSuccessfulScanAt)
            .SingleOrDefaultAsync(ct);

        if (snapshot.ConfigChanged)
        {
            await _ledger.ResolveDigestMismatchesAsync(directive.Id, digest, asOf, ct);
            _db.ChangeTracker.Clear();
        }

        var observed = new HashSet<string>(StringComparer.Ordinal);
        foreach (var condition in DueConditions(evaluation))
        {
            await OpenAsync(directive.Id, digest, condition, asOf, ct);
            observed.Add(condition.SubjectKey);
        }

        foreach (var lane in evaluation.Capacity.Where(lane => lane.InDeficit && !lane.IsDue))
        {
            await OpenAsync(directive.Id, digest, CapacityCondition(lane), asOf, ct);
            observed.Add(lane.SubjectKey);
        }

        if (!evaluation.PreservesOpenEpisodes && previousScan is DateTime previous)
        {
            var keep = observed.Concat(evaluation.UnknownSubjectKeys).ToList();
            await _ledger.ResolveClearedAsync(directive.Id, digest, keep, previous, asOf, ct);
        }

        await _ledger.RecordObservationAsync(directive.Id, digest, asOf, successful: true, error: null, ct);
        _db.ChangeTracker.Clear();

        var nudge = await NudgeAsync(directive, digest, asOf, probes, DueConditions(evaluation).ToList(), ct);
        return nudge is null
            ? new ExpectationScanResult(evaluation, 0)
            : new ExpectationScanResult(evaluation, 1, nudge.Value.Id, nudge.Value.Subjects);
    }

    /// <summary>
    /// Cooldown is ten minutes per directive. A never-nudged immediate fence may bypass it once;
    /// a bypass cannot follow a bypass. An episode already nudged repeats no sooner than thirty
    /// minutes later. Before commit: catch up transcripts, re-read, keep only what is still due.
    /// </summary>
    private async Task<(Guid Id, IReadOnlyList<string> Subjects)?> NudgeAsync(
        ExpectationDirectiveSettings directive,
        string digest,
        DateTime asOf,
        ExpectationProbeInput probes,
        IReadOnlyList<ExpectationCondition> due,
        CancellationToken ct)
    {
        if (due.Count == 0)
            return null;

        var subjects = due.Select(condition => condition.SubjectKey).Distinct().ToList();
        var episodes = await _db.ExpectationEpisodes.AsNoTracking()
            .Where(row => row.DirectiveId == directive.Id
                && row.ResolvedAt == null
                && row.ConfigDigest == digest
                && subjects.Contains(row.SubjectKey))
            .Select(row => new { row.Id, row.SubjectKey, row.FirstObservedAt })
            .ToListAsync(ct);
        if (episodes.Count == 0)
            return null;

        var since = episodes.Min(row => row.FirstObservedAt);
        var history = await _db.ExpectationNudges.AsNoTracking()
            .Where(row => row.DirectiveId == directive.Id && row.CreatedAt >= since)
            .Select(row => new { row.EpisodeIdsJson, row.CreatedAt })
            .ToListAsync(ct);
        var lastNudged = new Dictionary<Guid, DateTime>();
        foreach (var row in history)
        {
            foreach (var id in JsonSerializer.Deserialize<List<Guid>>(row.EpisodeIdsJson) ?? [])
            {
                if (!lastNudged.TryGetValue(id, out var at) || row.CreatedAt > at)
                    lastNudged[id] = row.CreatedAt;
            }
        }

        var episodeBySubject = episodes.ToDictionary(row => row.SubjectKey, StringComparer.Ordinal);
        bool Eligible(ExpectationCondition condition) =>
            episodeBySubject.TryGetValue(condition.SubjectKey, out var episode)
            && (!lastNudged.TryGetValue(episode.Id, out var at) || asOf - at >= ExpectationWindows.Repeat);
        bool NeverNudged(ExpectationCondition condition) =>
            episodeBySubject.TryGetValue(condition.SubjectKey, out var episode) && !lastNudged.ContainsKey(episode.Id);

        var eligible = due.Where(Eligible).ToList();
        if (eligible.Count == 0)
            return null;

        var recent = await _db.ExpectationNudges.AsNoTracking()
            .Where(row => row.DirectiveId == directive.Id)
            .OrderByDescending(row => row.Ordinal)
            .Select(row => new { row.Id, row.CreatedAt })
            .Take(2)
            .ToListAsync(ct);
        var latest = recent.Count > 0 ? recent[0] : null;
        if (latest is not null && asOf - latest.CreatedAt < ExpectationWindows.Cooldown)
        {
            var latestWasBypass = recent.Count > 1
                && latest.CreatedAt - recent[1].CreatedAt < ExpectationWindows.Cooldown;
            var urgent = eligible.Any(condition =>
                condition.Kind == ExpectationEpisodeKind.DispatchFence && condition.Immediate && NeverNudged(condition));
            if (!urgent || latestWasBypass)
                return null;
        }

        // Fresh evidence immediately before minting: a receipt or a settlement may have landed.
        var sessions = await CatchUpSessionsAsync(directive, eligible, ct);
        await _catchUp.CatchUpAsync(sessions, ct);
        _db.ChangeTracker.Clear();
        var fresh = await _reader.ReadAsync(directive, digest, asOf, probes, ct);
        var freshEvaluation = ExpectationWatchdogPolicy.Evaluate(fresh, directive);
        if (fresh.ProbeUnknown || freshEvaluation.ObservationUnknown || !fresh.DirectiveActive)
            return null;
        var stillDue = DueConditions(freshEvaluation).ToDictionary(row => row.SubjectKey, StringComparer.Ordinal);
        var batch = eligible
            .Where(condition => stillDue.ContainsKey(condition.SubjectKey))
            .Select(condition => stillDue[condition.SubjectKey])
            .ToList();
        if (batch.Count == 0)
            return null;

        var nudgeId = Guid.NewGuid();
        var body = ExpectationPromptFormatter.Format(nudgeId, directive.Id, directive.BoardId, batch, asOf);
        var batchSubjects = batch.Select(condition => condition.SubjectKey).ToList();
        var evidence = string.Join("; ", batch.Select(condition => condition.Kind + " " + condition.ReasonCode));
        if (evidence.Length > ExpectationLedger.MaxEvidenceChars)
            evidence = evidence[..ExpectationLedger.MaxEvidenceChars];
        var commit = await _ledger.CommitAggregateNudgeAsync(
            new ExpectationAggregateNudgeRequest(
                nudgeId,
                directive.Id,
                digest,
                directive.BoardId,
                directive.AuditCardId,
                batchSubjects.Select(subject => episodeBySubject[subject].Id).ToList(),
                batchSubjects,
                batch.SelectMany(condition => condition.AffectedTaskIds).Distinct().ToList(),
                evidence,
                body,
                latest?.Id,
                asOf,
                asOf + ExpectationWindows.Cooldown),
            ct);
        _db.ChangeTracker.Clear();
        return commit is null ? null : (commit.NudgeId, batchSubjects);
    }

    private async Task<IReadOnlyCollection<Guid>> CatchUpSessionsAsync(
        ExpectationDirectiveSettings directive,
        IReadOnlyList<ExpectationCondition> conditions,
        CancellationToken ct)
    {
        var taskIds = conditions.SelectMany(condition => condition.AffectedTaskIds).Distinct().ToList();
        var sessions = taskIds.Count == 0
            ? []
            : await _db.AgentTasks.AsNoTracking()
                .Where(task => taskIds.Contains(task.Id) && task.AgentSessionId != null)
                .Select(task => task.AgentSessionId!.Value)
                .ToListAsync(ct);
        var owned = await _db.AgentSessions.AsNoTracking()
            .Where(session => session.StandingAgentId == directive.AgentId
                && (session.Status == SessionStatus.Starting
                    || session.Status == SessionStatus.Running
                    || session.Status == SessionStatus.Stopping))
            .Select(session => session.Id)
            .ToListAsync(ct);
        return sessions.Concat(owned).Distinct().ToList();
    }

    private static IEnumerable<ExpectationCondition> DueConditions(ExpectationEvaluation evaluation)
    {
        if (evaluation.DispatchFence is { IsDue: true } fence)
            yield return fence;
        foreach (var condition in evaluation.ScopedFences.Where(condition => condition.IsDue))
            yield return condition;
        foreach (var condition in evaluation.SilentInFlight.Where(condition => condition.IsDue))
            yield return condition;
        foreach (var condition in evaluation.UndeliveredNotes.Where(condition => condition.IsDue))
            yield return condition;
        foreach (var condition in evaluation.StalledPipelines.Where(condition => condition.IsDue))
            yield return condition;
        foreach (var lane in evaluation.Capacity.Where(lane => lane.InDeficit && lane.IsDue))
            yield return CapacityCondition(lane);
    }

    private static ExpectationCondition CapacityCondition(ExpectationCapacityVerdict lane) => new()
    {
        Kind = ExpectationEpisodeKind.CapacityDeficit,
        SubjectKey = lane.SubjectKey,
        Scope = "runner:" + ExpectationSubjects.RunnerKey(lane.RunnerId),
        ReasonCode = "capacity-deficit",
        Evidence = string.IsNullOrWhiteSpace(lane.Evidence) ? "capacity deficit" : lane.Evidence,
        IsDue = lane.IsDue,
        ExplainsHeldQueue = lane.ExplainsHeldQueue,
    };

    private async Task OpenAsync(
        string directiveId,
        string digest,
        ExpectationCondition condition,
        DateTime asOf,
        CancellationToken ct)
    {
        await _ledger.OpenEpisodeAsync(
            new ExpectationEpisodeOpen(
                directiveId,
                digest,
                condition.Kind,
                condition.SubjectKey,
                condition.Evidence,
                asOf,
                null,
                null),
            ct);
        _db.ChangeTracker.Clear();
    }
}
