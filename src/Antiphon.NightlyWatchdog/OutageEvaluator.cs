using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Antiphon.NightlyWatchdog;

/// <summary>What one tick's probes observed. A null member was not probed (an earlier probe failed).</summary>
public sealed record ProbeSet(
    DateTime NowUtc,
    bool Reachable,
    string? WindmillVersion,
    bool? AuthOk,
    WindmillCall<ScriptInfo>? Script,
    WindmillCall<ScheduleInfo>? Schedule,
    bool WorkersKnown,
    DateTime? WorkerLastPingUtc,
    IReadOnlyList<WindmillJob>? Jobs);

public sealed record OutageDecision(OutageChange Change, string Kind, string DueDay, string? OutageId, string? JobId, string? RunId,
    JsonObject Evidence);

public sealed record Evaluation(
    IReadOnlyList<OutageDecision> Decisions,
    IReadOnlyDictionary<string, int> Counters,
    LastDueDay? LastDueDay,
    JsonObject ProbeRecord);

/// <summary>
/// CARD-0545 D-3 as a pure function of the tick's probes, the ledger's outages at tick start and the
/// persisted consecutive-tick counters. It never reads Windows-side state and never invents a job or run:
/// job facts come only from Windmill rows.
/// </summary>
public static class OutageEvaluator
{
    public const string WindmillUnreachable = "windmill-unreachable";
    public const string WindmillAuthFailed = "windmill-auth-failed";
    public const string ScriptMissing = "script-missing";
    public const string ScriptHashDrift = "script-hash-drift";
    public const string ScheduleMissing = "schedule-missing";
    public const string ScheduleDisabled = "schedule-disabled";
    public const string DesktopWorkerMissing = "desktop-worker-missing";
    public const string StartOverdue = "start-overdue";
    public const string RunStalled = "run-stalled";
    public const string WindowsHopFailed = "windows-hop-failed";
    public const string JobFailed = "job-failed";
    public const string ResultMissing = "result-missing";
    public const string ReportUndelivered = "report-undelivered";
    public const string DeadlineMissed = "deadline-missed";

    public static readonly IReadOnlySet<string> DueDayKinds = new HashSet<string>(StringComparer.Ordinal)
    {
        StartOverdue, RunStalled, WindowsHopFailed, JobFailed, ResultMissing, ReportUndelivered, DeadlineMissed,
    };

    private static readonly Regex SshEvidence = new(@"exit 255|Connection refused|timed out|Permission denied|no such identity",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static Evaluation Evaluate(ProbeSet p, IReadOnlyList<OutageRow> outages, IReadOnlyDictionary<string, int> counters,
        WatchdogOptions options)
    {
        var now = LondonClock.AsUtc(p.NowUtc);
        var today = LondonClock.Format(LondonClock.DueDay(now));
        var c = new Dictionary<string, int>(counters, StringComparer.Ordinal);
        var decisions = new List<OutageDecision>();
        var open = outages.Where(o => o.ClosedAt is null).ToList();
        var probe = new JsonObject
        {
            ["at"] = Ledger.Iso(now),
            ["reachable"] = p.Reachable,
            ["windmillVersion"] = p.WindmillVersion,
        };

        // Non-day kinds: condition true opens after the configured consecutive ticks; closure needs clean ticks.
        void Track(string kind, bool? failing, int ticksToOpen, string detail)
        {
            if (failing is null) return;
            var existing = open.FirstOrDefault(o => o.Kind == kind);
            if (failing.Value)
            {
                c["fail:" + kind] = Get(c, "fail:" + kind) + 1;
                c["clean:" + kind] = 0;
                if (existing is null && c["fail:" + kind] >= ticksToOpen)
                    decisions.Add(new OutageDecision(OutageChange.Opened, kind, today, null, null, null,
                        Evidence(now, detail + $" for {c["fail:" + kind]} consecutive tick(s)")));
            }
            else
            {
                c["fail:" + kind] = 0;
                if (existing is null) { c["clean:" + kind] = 0; return; }
                c["clean:" + kind] = Get(c, "clean:" + kind) + 1;
                if (c["clean:" + kind] >= options.ConsecutiveCleanTicksToClose)
                {
                    c["clean:" + kind] = 0;
                    decisions.Add(new OutageDecision(OutageChange.Closed, kind, existing.DueDay, existing.OutageId, null, null,
                        new JsonObject { ["closedByProbeAt"] = Ledger.Iso(now) }));
                }
            }
        }

        Track(WindmillUnreachable, !p.Reachable, options.ConsecutiveTicksToOpen, "windmill unreachable");
        if (!p.Reachable)
            return new Evaluation(decisions, c, null, probe);

        Track(WindmillAuthFailed, p.AuthOk is null ? null : !p.AuthOk.Value, options.ConsecutiveTicksToOpen, "windmill authentication refused");
        probe["authOk"] = p.AuthOk;
        if (p.AuthOk != true)
            return new Evaluation(decisions, c, null, probe);

        if (p.Script is { Status: WindmillCallStatus.Ok or WindmillCallStatus.NotFound } script)
        {
            var missing = script.Status == WindmillCallStatus.NotFound;
            var drift = !missing && !string.IsNullOrEmpty(options.ExpectedScriptHash)
                && !string.Equals(script.Value!.Hash, options.ExpectedScriptHash, StringComparison.Ordinal);
            probe["scriptHash"] = script.Value?.Hash;
            Track(ScriptMissing, missing, 1, $"script {options.ScriptPath} not registered");
            Track(ScriptHashDrift, drift, 1, $"script hash {script.Value?.Hash} differs from expected {options.ExpectedScriptHash}");
        }
        if (p.Schedule is { Status: WindmillCallStatus.Ok or WindmillCallStatus.NotFound } schedule)
        {
            var missing = schedule.Status == WindmillCallStatus.NotFound;
            probe["scheduleEnabled"] = schedule.Value?.Enabled;
            Track(ScheduleMissing, missing, 1, $"schedule {options.SchedulePath} not registered");
            Track(ScheduleDisabled, !missing && !schedule.Value!.Enabled, 1, $"schedule {options.SchedulePath} disabled");
        }
        if (p.WorkersKnown)
        {
            var workerMissing = p.WorkerLastPingUtc is null
                || now - LondonClock.AsUtc(p.WorkerLastPingUtc.Value) >= TimeSpan.FromMinutes(options.DesktopWorkerMissingMinutes);
            probe["workerLastPingAt"] = p.WorkerLastPingUtc is { } ping ? Ledger.Iso(ping) : null;
            Track(DesktopWorkerMissing, workerMissing, 1,
                $"no {options.DesktopWorkerGroup} worker ping for {options.DesktopWorkerMissingMinutes} minutes");
        }

        LastDueDay? lastDueDay = null;
        if (p.Jobs is { } jobs)
            lastDueDay = EvaluateJobs(now, jobs, outages, open, options, decisions, probe);
        return new Evaluation(decisions, c, lastDueDay, probe);
    }

    private static LastDueDay? EvaluateJobs(DateTime now, IReadOnlyList<WindmillJob> jobs, IReadOnlyList<OutageRow> outages,
        List<OutageRow> open, WatchdogOptions options, List<OutageDecision> decisions, JsonObject probe)
    {
        var d = LondonClock.DueDay(now);
        var days = new List<DateOnly> { d };
        if (LondonClock.PreviousDayInScope(now)) days.Add(d.AddDays(-1));

        var attributed = jobs
            .Select(j => (Job: j, Day: DayOf(j)))
            .Where(x => x.Day is not null)
            .Select(x => (x.Job, Day: x.Day!.Value))
            .OrderBy(x => x.Job.CreatedAtUtc)
            .ToList();
        LastDueDay? last = null;
        var probeDays = new JsonArray();

        foreach (var day in days)
        {
            var dayText = LondonClock.Format(day);
            var dayJobs = attributed.Where(x => x.Day == day).Select(x => x.Job).ToList();
            var dayOpen = open.Where(o => o.DueDay == dayText && DueDayKinds.Contains(o.Kind)).ToList();
            var probeJobs = new JsonArray();
            probeDays.Add(new JsonObject { ["dueDay"] = dayText, ["jobs"] = probeJobs });

            if (day == d && now >= LondonClock.GraceEndUtc(day, options.StartGraceMinutes)
                && !dayJobs.Any(j => j.Kind is JobKind.Running or JobKind.Completed)
                && !Exists(outages, StartOverdue, dayText))
            {
                decisions.Add(new OutageDecision(OutageChange.Opened, StartOverdue, dayText, null, null, null,
                    Evidence(now, $"no scheduled run started by {Ledger.Iso(LondonClock.GraceEndUtc(day, options.StartGraceMinutes))}")));
            }

            foreach (var job in dayJobs)
            {
                probeJobs.Add(new JsonObject
                {
                    ["id"] = job.Id, ["kind"] = job.Kind.ToString(), ["success"] = job.Success,
                    ["testsPassed"] = Bool(job.Result, "testsPassed"), ["coverageComplete"] = Bool(job.Result, "coverageComplete"),
                    ["reportDelivered"] = Bool(job.Result, "reportDelivered"),
                });

                if (job.Kind == JobKind.Running && job.StartedAtUtc is { } started
                    && now - LondonClock.AsUtc(started) >= TimeSpan.FromHours(options.RunBudgetHours)
                    && !Recorded(outages, RunStalled, dayText, job.Id))
                {
                    decisions.Add(new OutageDecision(OutageChange.Opened, RunStalled, dayText, null, job.Id, null,
                        Evidence(now, $"running since {Ledger.Iso(started)}", job)));
                    continue;
                }
                if (job.Kind != JobKind.Completed) continue;

                if (job.Success == false)
                {
                    if (Recorded(outages, WindowsHopFailed, dayText, job.Id) || Recorded(outages, JobFailed, dayText, job.Id)) continue;
                    if (dayJobs.Any(s => IsMatchingSuccess(s, day) && s.CreatedAtUtc > job.CreatedAtUtc)) continue;
                    var ssh = SshEvidence.Match(job.Logs ?? "");
                    var kind = ssh.Success ? WindowsHopFailed : JobFailed;
                    var detail = ssh.Success ? $"ssh {ssh.Value} at {Completed(job)}" : $"job failed at {Completed(job)}";
                    OpenOrAmend(kind, dayText, job, null, detail);
                }
                else if (job.Success == true)
                {
                    if (!ResultMatches(job, day))
                    {
                        if (!Recorded(outages, ResultMissing, dayText, job.Id))
                            OpenOrAmend(ResultMissing, dayText, job, null, $"completed without a matching result at {Completed(job)}");
                    }
                    else if (Bool(job.Result, "reportDelivered") == false)
                    {
                        if (!Recorded(outages, ReportUndelivered, dayText, job.Id))
                            OpenOrAmend(ReportUndelivered, dayText, job, Text(job.Result, "nativeRunId"), "nightly report card was not filed");
                    }
                    else if (last is null || string.CompareOrdinal(dayText, last.DueDay) > 0)
                    {
                        last = new LastDueDay(dayText, job.Id, "success", Text(job.Result, "nativeRunId")!, Text(job.Result, "sha") ?? "");
                    }
                }
            }

            if (day == d && now >= LondonClock.MorningDeadlineUtc(day)
                && !dayJobs.Any(j => IsMatchingSuccess(j, day))
                && !Exists(outages, DeadlineMissed, dayText))
            {
                if (dayOpen.Count > 0)
                {
                    foreach (var existing in dayOpen.Where(o => JsonNode.Parse(o.EvidenceJson)?["deadlineMissedAt"] is null))
                        decisions.Add(new OutageDecision(OutageChange.Amended, existing.Kind, dayText, existing.OutageId, null, null,
                            new JsonObject { ["deadlineMissedAt"] = Ledger.Iso(LondonClock.MorningDeadlineUtc(day)) }));
                }
                else
                {
                    decisions.Add(new OutageDecision(OutageChange.Opened, DeadlineMissed, dayText, null, null, null,
                        Evidence(now, $"no matching scheduled success by {Ledger.Iso(LondonClock.MorningDeadlineUtc(day))}")));
                }
            }
        }
        probe["dueDays"] = probeDays;

        // Closure: a later scheduled success with a matching result for the same or a later due day.
        foreach (var outage in open.Where(o => DueDayKinds.Contains(o.Kind)))
        {
            var outageDay = DateOnly.ParseExact(outage.DueDay, "yyyy-MM-dd", CultureInfo.InvariantCulture);
            var evidence = JsonNode.Parse(outage.EvidenceJson) as JsonObject ?? new JsonObject();
            var recordedIds = (evidence["jobIds"] as JsonArray)?.Select(n => n?.GetValue<string>()).ToHashSet() ?? [];
            var lastCreated = evidence["lastJobCreatedAt"]?.GetValue<string>() is { } lc
                ? DateTime.Parse(lc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind).ToUniversalTime()
                : (DateTime?)null;
            var closer = attributed.FirstOrDefault(x => x.Day >= outageDay && IsMatchingSuccess(x.Job, x.Day)
                && !recordedIds.Contains(x.Job.Id) && (lastCreated is null || LondonClock.AsUtc(x.Job.CreatedAtUtc) > lastCreated.Value));
            if (closer.Job is null) continue;
            var runId = Text(closer.Job.Result, "nativeRunId");
            decisions.Add(new OutageDecision(OutageChange.Closed, outage.Kind, outage.DueDay, outage.OutageId, closer.Job.Id, runId,
                new JsonObject { ["closedByJobId"] = closer.Job.Id, ["closedByRunId"] = runId }));
            var closerDay = LondonClock.Format(closer.Day);
            if (last is null || string.CompareOrdinal(closerDay, last.DueDay) > 0)
                last = new LastDueDay(closerDay, closer.Job.Id, "success", runId!, Text(closer.Job.Result, "sha") ?? "");
        }
        return last;

        void OpenOrAmend(string kind, string dayText, WindmillJob job, string? runId, string detail)
        {
            var existing = open.FirstOrDefault(o => o.Kind == kind && o.DueDay == dayText);
            var pending = decisions.FirstOrDefault(x => x.Change == OutageChange.Opened && x.Kind == kind && x.DueDay == dayText);
            if (existing is not null)
            {
                decisions.Add(new OutageDecision(OutageChange.Amended, kind, dayText, existing.OutageId, job.Id, runId,
                    new JsonObject { ["addJobId"] = job.Id, ["lastJobCreatedAt"] = Ledger.Iso(job.CreatedAtUtc) }));
            }
            else if (pending is not null)
            {
                (pending.Evidence["jobIds"] as JsonArray)!.Add(job.Id);
                pending.Evidence["lastJobCreatedAt"] = Ledger.Iso(job.CreatedAtUtc);
            }
            else
            {
                decisions.Add(new OutageDecision(OutageChange.Opened, kind, dayText, null, job.Id, runId, Evidence(now, detail, job)));
            }
        }
    }

    /// <summary>
    /// A scheduled row belongs to due day D when created in [DueUtc(D) - 1 min, MorningDeadlineUtc(D));
    /// otherwise a fetched result's <c>localDueDate</c> names it. Manual rows belong to no due day.
    /// </summary>
    public static DateOnly? DayOf(WindmillJob job)
    {
        if (string.IsNullOrWhiteSpace(job.SchedulePath)) return null;
        var created = LondonClock.AsUtc(job.CreatedAtUtc);
        var day = LondonClock.DueDay(created);
        if (created >= LondonClock.DueUtc(day).AddMinutes(-1) && created < LondonClock.MorningDeadlineUtc(day))
            return day;
        if (Text(job.Result, "localDueDate") is { } due
            && DateOnly.TryParseExact(due, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
            return parsed;
        return null;
    }

    /// <summary>D-3: a result matches a due day only with a nativeRunId and a localDueDate equal to that day.</summary>
    public static bool ResultMatches(WindmillJob job, DateOnly day) =>
        !string.IsNullOrWhiteSpace(Text(job.Result, "nativeRunId"))
        && string.Equals(Text(job.Result, "localDueDate"), LondonClock.Format(day), StringComparison.Ordinal);

    public static bool IsMatchingSuccess(WindmillJob job, DateOnly day) =>
        job.Kind == JobKind.Completed && job.Success == true && ResultMatches(job, day) && Bool(job.Result, "reportDelivered") != false;

    private static bool Exists(IReadOnlyList<OutageRow> outages, string kind, string day) =>
        outages.Any(o => o.Kind == kind && o.DueDay == day);

    private static bool Recorded(IReadOnlyList<OutageRow> outages, string kind, string day, string jobId) =>
        outages.Any(o => o.Kind == kind && o.DueDay == day && (o.JobId == jobId
            || (JsonNode.Parse(o.EvidenceJson)?["jobIds"] as JsonArray)?.Any(n => n?.GetValue<string>() == jobId) == true));

    private static JsonObject Evidence(DateTime now, string detail, WindmillJob? job = null)
    {
        var evidence = new JsonObject { ["detectedAt"] = Ledger.Iso(now), ["detail"] = detail, ["jobIds"] = new JsonArray() };
        if (job is not null)
        {
            ((JsonArray)evidence["jobIds"]!).Add(job.Id);
            evidence["lastJobCreatedAt"] = Ledger.Iso(job.CreatedAtUtc);
            evidence["sha"] = Text(job.Result, "sha");
            evidence["policyHash"] = Text(job.Result, "policyHash");
        }
        return evidence;
    }

    private static string Completed(WindmillJob job) =>
        job.CompletedAtUtc is { } at ? LondonClock.AsUtc(at).ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture) : "unknown";

    internal static string? Text(JsonObject? result, string key) =>
        result is not null && result.TryGetPropertyValue(key, out var node) && node is JsonValue v && v.TryGetValue<string>(out var s)
            && !string.IsNullOrWhiteSpace(s) ? s : null;

    internal static bool? Bool(JsonObject? result, string key) =>
        result is not null && result.TryGetPropertyValue(key, out var node) && node is JsonValue v && v.TryGetValue<bool>(out var b) ? b : null;

    private static int Get(Dictionary<string, int> c, string key) => c.TryGetValue(key, out var v) ? v : 0;
}
