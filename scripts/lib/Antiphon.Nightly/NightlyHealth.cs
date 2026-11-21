// W2-nightly S05: the nightly readiness evaluator (CARD-0487 S3, CARD-0544 D-6/D-7, CARD-0545 D-9/D-10), ported from
// scripts/lib/nightly-health.ps1 (plan 2026-11-21-w2-nightly-port-slices.md). `antiphon-scripts nightly-health` runs
// NightlyHealth.Run with NightlyHealthSeams.Production; scripts/nightly-health.ps1 is the Windows PowerShell 5.1
// launcher Windmill's u/lndcobra/antiphon_nightly_readiness job runs. NightlyHealthTests build the seams directly.
//
// The evaluator reads loose JSON (last-run.json, last-complete-green.json, readiness-config.json, Windmill responses,
// the watchdog snapshot) the way the Windows PowerShell 5.1 script read it with ConvertFrom-Json: strings stay strings
// (no date conversion), property names match case-insensitively, [string] and [bool] are PowerShell's conversions and
// -eq on text ignores case. A step that failed in the script without stopping it (a statement-terminating error under
// ErrorActionPreference Continue: an HTTP error, a malformed file, a failed write) writes one line to Seams.Error and
// leaves its value empty, as the script did. Plain C#: no PowerShell types. ASCII only.
#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Antiphon.Nightly
{
    /// <summary>The evaluator's parameters (Invoke-AntiphonNightlyHealth). Empty strings mean "not given".</summary>
    public sealed class NightlyHealthOptions
    {
        /// <summary>The nightly state root (last-run.json, last-complete-green.json, last-monitor.json, notifications.json).</summary>
        public string StateRoot { get; init; } = NightlyHealth.DefaultStateRoot;

        /// <summary>The Windmill script hash the registration must carry; empty skips the check.</summary>
        public string ExpectedScriptHash { get; init; } = "";

        /// <summary>The policy hash the native run must carry; empty skips the check.</summary>
        public string ExpectedPolicyHash { get; init; } = "";

        /// <summary>The notification destination; empty records unauthorized-destination on an unhealthy run.</summary>
        public string AuthorizedDestination { get; init; } = "";

        /// <summary>Identity.RepositoryPath (else readiness config, else ANTIPHON_NIGHTLY_REPOSITORY_PATH).</summary>
        public string RepositoryPath { get; init; } = "";

        /// <summary>Identity.ProjectId (else readiness config, else ANTIPHON_NIGHTLY_PROJECT_ID).</summary>
        public string ProjectId { get; init; } = "";

        /// <summary>The watchdog snapshot URL (else readiness config watchdogSnapshotUrl).</summary>
        public string WatchdogSnapshotUrl { get; init; } = "";

        /// <summary>The qualified watchdog instance id (else readiness config watchdogInstanceId).</summary>
        public string ExpectedWatchdogInstanceId { get; init; } = "";

        /// <summary>readiness-config.json (written at qualification); supplies any value not given.</summary>
        public string ReadinessConfigPath { get; init; } = "";
    }

    /// <summary>
    /// The OS observations the evaluator makes ($script:NightlySeams in the script). Seams replace observation only,
    /// never decisions, and never cross a process: the CLI fills Production, tests build one directly.
    /// </summary>
    public sealed class NightlyHealthSeams
    {
        /// <summary>The clock (Get-NightlyUtcNow): TimeProvider.System in production.</summary>
        public required TimeProvider Clock { get; init; }

        /// <summary>The Windmill API; null when there are no credentials (no registration, schedule or jobs are read).</summary>
        public required NightlyWindmillApi WindmillApi { get; init; }

        /// <summary>Reads the watchdog snapshot text at the URL; throws when it is unreachable. Production: NightlyHealth.HttpSnapshot.</summary>
        public required Func<string, string> WatchdogSnapshot { get; init; }

        /// <summary>The notification sink: takes the event, returns the transport's answer. Null in production (CARD-0545 D-9).</summary>
        public required Func<JsonObject, JsonNode> NotificationSink { get; init; }

        /// <summary>Recipient receipts beyond notification-receipts.json. Null in production.</summary>
        public required Func<IEnumerable<JsonNode>> RecipientView { get; init; }

        /// <summary>Lets an unhealthy run without a destination still use the ledger (harness only). False in production.</summary>
        public required bool AllowOfflineNotify { get; init; }

        /// <summary>Environment variables (ANTIPHON_NIGHTLY_REPOSITORY_PATH, ANTIPHON_NIGHTLY_PROJECT_ID): null when unset.</summary>
        public required Func<string, string> Environment { get; init; }

        /// <summary>Where a failed step reports (the script's error stream): stderr in production.</summary>
        public required TextWriter Error { get; init; }

        /// <summary>
        /// The production seams: the system clock, the Windmill API from WINDMILL_BASE_URL / WINDMILL_TOKEN (or
        /// WINDMILL_TOKEN_FILE) / WINDMILL_WORKSPACE / ANTIPHON_NIGHTLY_WINDMILL_SCRIPT (null without credentials), the
        /// HTTP watchdog read, no notification sink, no recipient view and no offline notify.
        /// </summary>
        public static NightlyHealthSeams Production(Func<string, string> environment, TextWriter error)
        {
            if (environment == null) throw new ArgumentNullException(nameof(environment));
            if (error == null) throw new ArgumentNullException(nameof(error));
            return new NightlyHealthSeams
            {
                Clock = TimeProvider.System,
                WindmillApi = NightlyHealthWindmill.Production(NightlyWindmillConfig.FromEnvironment(environment, error), error),
                WatchdogSnapshot = NightlyHealth.HttpSnapshot,
                NotificationSink = null,
                RecipientView = null,
                AllowOfflineNotify = false,
                Environment = environment,
                Error = error,
            };
        }
    }

    /// <summary>Test-NightlyMonitorHealth's verdict (Health in last-monitor.json).</summary>
    public sealed class NightlyHealthVerdict
    {
        /// <summary>No reason was found.</summary>
        public bool Healthy { get; set; } = true;

        /// <summary>Today's run is still to come or in progress.</summary>
        public bool Pending { get; set; }

        /// <summary>The stable reasons, in the order found (a reason may repeat, as in the script).</summary>
        public List<string> Reasons { get; } = new List<string>();

        /// <summary>Healthy with a valid scheduled green for the due day (or the pre-08:00 bridge).</summary>
        public bool ReadyForDeferral { get; set; }

        /// <summary>Always false: the evaluator never terminates a run.</summary>
        public bool TerminateRequested { get; set; }

        /// <summary>The green native run id, empty when there is none.</summary>
        public string GreenRunId { get; set; } = "";

        /// <summary>The green's Windmill job id, empty when there is none.</summary>
        public string GreenJobId { get; set; } = "";

        /// <summary>The native run id the green's job names, empty when there is none.</summary>
        public string GreenJobNativeRunId { get; set; } = "";

        /// <summary>The verdict as last-monitor.json records it (same names and order as the script's object).</summary>
        public JsonObject ToJson()
        {
            var reasons = new JsonArray();
            foreach (var reason in Reasons) reasons.Add(reason);
            return new JsonObject
            {
                ["Healthy"] = Healthy,
                ["Pending"] = Pending,
                ["Reasons"] = reasons,
                ["ReadyForDeferral"] = ReadyForDeferral,
                ["TerminateRequested"] = TerminateRequested,
                ["GreenRunId"] = GreenRunId,
                ["GreenJobId"] = GreenJobId,
                ["GreenJobNativeRunId"] = GreenJobNativeRunId,
            };
        }
    }

    /// <summary>CARD-0544 D-7: the identities the server's readiness reader matches against its receipt.</summary>
    public sealed record NightlyHealthIdentity(string RepositoryPath, string ProjectId, string PolicyHash, string ScriptHash,
        string ScheduledRunId, string JobNativeRunId, string WindmillJobId, string WatchdogInstanceId, string WatchdogHeartbeatAt)
    {
        /// <summary>The identity as last-monitor.json records it.</summary>
        public JsonObject ToJson() => new JsonObject
        {
            ["RepositoryPath"] = RepositoryPath,
            ["ProjectId"] = ProjectId,
            ["PolicyHash"] = PolicyHash,
            ["ScriptHash"] = ScriptHash,
            ["ScheduledRunId"] = ScheduledRunId,
            ["JobNativeRunId"] = JobNativeRunId,
            ["WindmillJobId"] = WindmillJobId,
            ["WatchdogInstanceId"] = WatchdogInstanceId,
            ["WatchdogHeartbeatAt"] = WatchdogHeartbeatAt,
        };
    }

    /// <summary>The evaluator's result (the -PassThru object and last-monitor.json).</summary>
    public sealed class NightlyHealthResult
    {
        /// <summary>0 healthy, 1 unhealthy.</summary>
        public int ExitCode { get; init; }

        /// <summary>The verdict, watchdog reasons included.</summary>
        public NightlyHealthVerdict Health { get; init; }

        /// <summary>The notification attempt (null when none was made).</summary>
        public JsonNode Notification { get; init; }

        /// <summary>The recipient receipt was found.</summary>
        public bool Receipt { get; init; }

        /// <summary>The evaluation instant, round-trip ("o") UTC.</summary>
        public string RecordedAt { get; init; }

        /// <summary>The identities the readiness reader matches.</summary>
        public NightlyHealthIdentity Identity { get; init; }

        /// <summary>The record: ExitCode, Health, Notification, Receipt, RecordedAt, Identity.</summary>
        public JsonObject ToJson() => new JsonObject
        {
            ["ExitCode"] = ExitCode,
            ["Health"] = Health.ToJson(),
            ["Notification"] = Notification?.DeepClone(),
            ["Receipt"] = Receipt,
            ["RecordedAt"] = RecordedAt,
            ["Identity"] = Identity.ToJson(),
        };
    }

    /// <summary>Get-NightlyWatchdogSnapshot: whether the URL answered, its text, the parsed snapshot and the failure.</summary>
    public sealed record NightlyWatchdogFetch(bool Reachable, string Raw, JsonNode Snapshot, string Error);

    /// <summary>Test-NightlyWatchdogFreshness: fresh, or exactly one stable reason.</summary>
    public sealed class NightlyWatchdogFreshness
    {
        /// <summary>Reachable, well formed, at most 20:00 old, the expected identity and no open outage.</summary>
        public bool Fresh { get; set; }

        /// <summary>The one reason when not fresh.</summary>
        public List<string> Reasons { get; } = new List<string>();

        /// <summary>The snapshot's instanceId (empty when unreadable).</summary>
        public string InstanceId { get; set; } = "";

        /// <summary>The heartbeat, round-trip UTC (empty when unreadable).</summary>
        public string HeartbeatAt { get; set; } = "";
    }

    /// <summary>The London calendar the nightly runs on (nightly-common.ps1 Get-NightlyLondonTimeZone, ConvertTo-NightlyLondonLocal).</summary>
    public static class NightlyLondon
    {
        private static readonly Lazy<TimeZoneInfo> Zone = new Lazy<TimeZoneInfo>(Find);

        /// <summary>Europe/London ("GMT Standard Time" on Windows).</summary>
        public static TimeZoneInfo TimeZone => Zone.Value;

        private static TimeZoneInfo Find()
        {
            foreach (var id in new[] { "GMT Standard Time", "Europe/London" })
            {
                try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
                catch (TimeZoneNotFoundException) { }
                catch (InvalidTimeZoneException) { }
            }
            throw new InvalidOperationException("Europe/London timezone not available");
        }

        /// <summary>ConvertTo-NightlyLondonLocal: an unspecified time is read as UTC, a local one is converted first.</summary>
        public static DateTime ToLocal(DateTime utc)
        {
            utc = utc.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(utc, DateTimeKind.Utc) : utc.ToUniversalTime();
            return TimeZoneInfo.ConvertTimeFromUtc(utc, TimeZone);
        }

        /// <summary>Get-NightlyDueUtcForLondonDate: exactly 00:30:00.0000000 London on that date, in UTC.</summary>
        public static DateTime DueUtc(DateTime londonDate) => AtLocal(londonDate, 0, 30);

        /// <summary>Get-NightlyMorningDeadlineUtc: 08:00 London on that date, in UTC.</summary>
        public static DateTime MorningDeadlineUtc(DateTime londonDate) => AtLocal(londonDate, 8, 0);

        private static DateTime AtLocal(DateTime date, int hour, int minute) =>
            TimeZoneInfo.ConvertTimeToUtc(new DateTime(date.Year, date.Month, date.Day, hour, minute, 0, DateTimeKind.Unspecified), TimeZone);

        /// <summary>yyyy-MM-dd of a date (invariant culture).</summary>
        public static string DateText(DateTime date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        /// <summary>[datetime]::ParseExact(text, 'yyyy-MM-dd', invariant); null when it does not parse.</summary>
        public static DateTime? ParseDate(string text) =>
            DateTime.TryParseExact(text ?? "", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : (DateTime?)null;
    }

    /// <summary>The evaluator (scripts/lib/nightly-health.ps1, less the Windmill adapter and the notification ledger).</summary>
    public static class NightlyHealth
    {
        /// <summary>scripts/nightly-health.ps1's -StateRoot default.</summary>
        public const string DefaultStateRoot = @"C:\Antiphon\nightly";

        /// <summary>The oldest heartbeat that is still fresh.</summary>
        public static readonly TimeSpan WatchdogFreshFor = TimeSpan.FromMinutes(20);

        /// <summary>The watchdog snapshot read's bound (Invoke-WebRequest -TimeoutSec 10).</summary>
        public static readonly TimeSpan WatchdogTimeout = TimeSpan.FromSeconds(10);

        /// <summary>Get-NightlyMonitorResultPath.</summary>
        public static string MonitorResultPath(string stateRoot) => Path.Combine(stateRoot ?? "", "last-monitor.json");

        // ---- Test-NightlyMonitorHealth and its helpers ----

        /// <summary>
        /// Test-NightlyMonitorHealth: the registration, schedule and jobs from Windmill, the native state
        /// (last-run.json) and the last complete green (last-complete-green.json) judged at nowUtc. Validity is the
        /// London due day's scheduled green, never the age of completedAt; before 08:00 the previous due day's green
        /// bridges; a newer failed attempt or failed scheduled job revokes it.
        /// </summary>
        public static NightlyHealthVerdict Evaluate(NightlyWindmillRegistration registration, NightlyWindmillSchedule schedule,
            IReadOnlyList<NightlyWindmillJob> jobs, JsonNode nativeState, DateTime nowUtc, JsonNode lastGreen = null,
            string expectedScriptHash = "", string expectedPolicyHash = "", string expectedTag = "desktop", int startGraceMinutes = 30)
        {
            jobs ??= Array.Empty<NightlyWindmillJob>();
            expectedScriptHash ??= "";
            expectedPolicyHash ??= "";
            var result = new NightlyHealthVerdict();
            void Fail(string reason)
            {
                result.Healthy = false;
                result.Reasons.Add(reason);
            }

            if (registration == null || !registration.Script) Fail("missing-script");
            var registrationTag = registration?.Tag ?? "";
            if (schedule == null) Fail("missing-schedule");
            else
            {
                if (!schedule.Enabled) Fail("disabled");
                if (schedule.Paused) Fail("paused");
                var tag = schedule.Tag ?? "";
                if (!HealthJson.Eq(tag, expectedTag) && !HealthJson.Eq(tag, "") && HealthJson.Eq(expectedTag, "desktop"))
                {
                    if (!HealthJson.Eq(registrationTag, expectedTag)) Fail("non-desktop-tag");
                }
                if (registration != null && !HealthJson.Eq(registrationTag, expectedTag) && HealthJson.Eq(expectedTag, "desktop")) Fail("non-desktop-tag");
                var args = schedule.Args;
                if (HealthJson.IsTrue(args))
                {
                    if (HealthJson.IsTrue(HealthJson.Field(args, "suite")) || HealthJson.IsTrue(HealthJson.Field(args, "suites")) || HealthJson.IsTrue(HealthJson.Field(args, "ref")))
                        Fail("suite-ref-override");
                }
                if (!string.IsNullOrEmpty(schedule.WorkerVersion) && !string.IsNullOrEmpty(schedule.ServerVersion)
                    && !HealthJson.Eq(schedule.WorkerVersion, schedule.ServerVersion))
                    Fail("worker-version-mismatch");
            }
            if (registration != null && expectedScriptHash.Length > 0 && !HealthJson.Eq(registration.Hash ?? "", expectedScriptHash)) Fail("script-hash-mismatch");

            var londonNow = NightlyLondon.ToLocal(nowUtc);
            var todayDate = londonNow.Date;
            var dueUtc = NightlyLondon.DueUtc(todayDate);
            var graceEnd = dueUtc.AddMinutes(startGraceMinutes);
            var morningUtc = NightlyLondon.MorningDeadlineUtc(todayDate);

            // CARD-0544 D-6: only a scheduled job can be the due day's run; a manual run never stands in for it.
            var todayJob = FindScheduledJob(jobs, todayDate);
            var todayWaiting = todayJob == null || HealthJson.Eq(todayJob.Status ?? "", "queued");
            if (nowUtc >= graceEnd && todayWaiting) Fail("overdue-start");
            else if (nowUtc < graceEnd && todayWaiting) result.Pending = true;

            // Daily run validity is judged against the London due date, never against the age of completedAt.
            var states = new[] { nativeState, lastGreen };
            var todayGreen = ScheduledGreen(todayJob, states);
            var green = todayGreen;
            var greenJob = todayJob;
            if (nowUtc >= morningUtc)
            {
                if (todayGreen == null)
                {
                    var yesterday = false;
                    if (HealthJson.IsTrue(nativeState) && HealthJson.IsTrue(HealthJson.Field(nativeState, "completedAt")))
                    {
                        var completed = HealthJson.CastDateTime(HealthJson.Field(nativeState, "completedAt"));
                        if (completed != null && completed.Value.ToUniversalTime().Date < dueUtc.Date) yesterday = true;
                    }
                    Fail(yesterday ? "yesterday-green-insufficient" : "missing-expected-run");
                }
            }
            else if (todayGreen == null)
            {
                result.Pending = true;
                // Before today's 08:00 deadline the previous due day's complete scheduled green bridges.
                var previousJob = FindScheduledJob(jobs, todayDate.AddDays(-1));
                green = ScheduledGreen(previousJob, states);
                greenJob = previousJob;
            }

            if (green != null)
            {
                // A known newer failed or incomplete completed attempt removes permission immediately.
                var greenAt = HealthJson.CastDateTime(HealthJson.Field(green, "completedAt"))?.ToUniversalTime();
                if (HealthJson.IsTrue(nativeState) && HealthJson.IsTrue(HealthJson.Field(nativeState, "completedAt"))
                    && !HealthJson.Eq(HealthJson.Str(HealthJson.Field(nativeState, "runId")), HealthJson.Str(HealthJson.Field(green, "runId"))))
                {
                    var attemptAt = HealthJson.CastDateTime(HealthJson.Field(nativeState, "completedAt"))?.ToUniversalTime();
                    if (HealthJson.Greater(attemptAt, greenAt) && !GreenFlags(nativeState))
                    {
                        result.Reasons.Add("newer-failed-attempt");
                        green = null;
                    }
                }
                if (green != null)
                {
                    foreach (var job in jobs)
                    {
                        if (job == null || !HealthJson.Eq(job.Status ?? "", "failed")) continue;
                        if (!JobScheduled(job)) continue;
                        if (!string.IsNullOrEmpty(job.Id) && HealthJson.Eq(job.Id, greenJob?.Id ?? "")) continue;
                        var failedFor = JobDueUtc(job);
                        var greenFor = JobDueUtc(greenJob);
                        if (failedFor != null && greenFor != null && failedFor.Value > greenFor.Value)
                        {
                            result.Reasons.Add("newer-failed-attempt");
                            green = null;
                            break;
                        }
                    }
                }
            }

            if (HealthJson.IsTrue(nativeState))
            {
                var nativeRunId = HealthJson.Str(HealthJson.Field(nativeState, "runId"));
                var nativeSha = HealthJson.Field(nativeState, "sha");
                if (todayJob != null && !HealthJson.Eq(nativeRunId, todayJob.NativeRunId ?? "") && !string.IsNullOrEmpty(todayJob.NativeRunId)) Fail("run-id-mismatch");
                if (expectedPolicyHash.Length > 0 && !HealthJson.Eq(HealthJson.Str(HealthJson.Field(nativeState, "policyHash")), expectedPolicyHash)) Fail("policy-mismatch");
                if (todayJob != null && !string.IsNullOrEmpty(todayJob.Sha) && HealthJson.IsTrue(nativeSha) && !HealthJson.Eq(todayJob.Sha, HealthJson.Str(nativeSha))) Fail("sha-mismatch");
                var inProgress = HealthJson.Eq(HealthJson.Str(HealthJson.Field(nativeState, "phase")), "Started") && !HealthJson.IsTrue(HealthJson.Field(nativeState, "completedAt"));
                if (!inProgress)
                {
                    if (HealthJson.Has(nativeState, "coverageComplete") && !HealthJson.IsTrue(HealthJson.Field(nativeState, "coverageComplete"))) Fail("incomplete-coverage");
                    if (HealthJson.Has(nativeState, "testsPassed") && !HealthJson.IsTrue(HealthJson.Field(nativeState, "testsPassed"))) Fail("tests-red");
                    if (HealthJson.Has(nativeState, "reportDelivered") && !HealthJson.IsTrue(HealthJson.Field(nativeState, "reportDelivered"))) Fail("report-outage");
                }
                else
                {
                    var activity = HealthJson.Field(nativeState, "lastActivityAt");
                    var fresh = false;
                    if (HealthJson.IsTrue(activity))
                    {
                        var at = HealthJson.CastDateTime(activity);
                        if (at != null && (nowUtc - at.Value.ToUniversalTime()).TotalMinutes <= 60) fresh = true;
                    }
                    // A run still making progress is today's pending state; a stalled one is unhealthy.
                    if (fresh) result.Pending = true;
                    else if (HealthJson.IsTrue(activity)) Fail("stale-progress");
                    else Fail("started-incomplete");
                }
            }

            if (result.Reasons.Count > 0) result.Healthy = false;
            // Monitor freshness is the reader's RecordedAt check; readiness here is health plus a valid scheduled
            // green for the due day (or the pre-08:00 bridge), never completedAt age.
            result.ReadyForDeferral = result.Healthy && green != null;
            result.GreenRunId = green != null ? HealthJson.Str(HealthJson.Field(green, "runId")) : "";
            result.GreenJobId = green != null && greenJob != null ? greenJob.Id ?? "" : "";
            result.GreenJobNativeRunId = green != null && greenJob != null ? greenJob.NativeRunId ?? "" : "";
            return result;
        }

        /// <summary>Test-NightlyJobScheduled: a job that is not marked unscheduled and whose trigger, if any, is scheduled.</summary>
        public static bool JobScheduled(NightlyWindmillJob job)
        {
            if (job == null) return false;
            if (job.Scheduled == false) return false;
            if (!string.IsNullOrEmpty(job.Trigger) && !HealthJson.Eq(job.Trigger, "scheduled")) return false;
            return true;
        }

        /// <summary>Get-NightlyJobDueUtc: the due instant of the job's localDueDate, else its scheduledFor, else null.</summary>
        public static DateTime? JobDueUtc(NightlyWindmillJob job)
        {
            if (job == null) return null;
            if (!string.IsNullOrEmpty(job.LocalDueDate))
            {
                var date = NightlyLondon.ParseDate(job.LocalDueDate);
                return date == null ? (DateTime?)null : NightlyLondon.DueUtc(date.Value);
            }
            if (!string.IsNullOrEmpty(job.ScheduledFor)) return HealthJson.CastDateTime(job.ScheduledFor)?.ToUniversalTime();
            return null;
        }

        /// <summary>
        /// Find-NightlyScheduledJob: the first scheduled job whose localDueDate is the London date, else the last one
        /// whose scheduledFor is within a minute of that date's 00:30 London.
        /// </summary>
        public static NightlyWindmillJob FindScheduledJob(IReadOnlyList<NightlyWindmillJob> jobs, DateTime londonDate)
        {
            var date = NightlyLondon.DateText(londonDate);
            var dueUtc = NightlyLondon.DueUtc(londonDate);
            NightlyWindmillJob found = null;
            foreach (var job in jobs ?? Array.Empty<NightlyWindmillJob>())
            {
                if (job == null || !JobScheduled(job)) continue;
                if (HealthJson.Eq(job.LocalDueDate ?? "", date)) return job;
                if (!string.IsNullOrEmpty(job.ScheduledFor))
                {
                    var scheduledFor = HealthJson.CastDateTime(job.ScheduledFor)?.ToUniversalTime();
                    if (scheduledFor != null && scheduledFor.Value >= dueUtc.AddMinutes(-1) && scheduledFor.Value <= dueUtc.AddMinutes(1)) found = job;
                }
            }
            return found;
        }

        /// <summary>Test-NightlyGreenFlags: coverageComplete, testsPassed and reportDelivered are all true.</summary>
        public static bool GreenFlags(JsonNode state) =>
            HealthJson.IsTrue(state) && HealthJson.IsTrue(HealthJson.Field(state, "coverageComplete")) && HealthJson.IsTrue(HealthJson.Field(state, "testsPassed"))
            && HealthJson.IsTrue(HealthJson.Field(state, "reportDelivered"));

        /// <summary>
        /// Get-NightlyScheduledGreen: the due day's complete green. Its scheduled job succeeded with a result naming the
        /// native run, and that native run completed scheduled, master, with full coverage, green tests and a delivered report.
        /// </summary>
        public static JsonNode ScheduledGreen(NightlyWindmillJob job, IEnumerable<JsonNode> states)
        {
            if (job == null || !HealthJson.Eq(job.Status ?? "", "success")) return null;
            var runId = job.NativeRunId ?? "";
            if (string.IsNullOrWhiteSpace(runId)) return null;
            foreach (var state in states ?? Array.Empty<JsonNode>())
            {
                if (state == null || !HealthJson.Eq(HealthJson.Str(HealthJson.Field(state, "runId")), runId)) continue;
                if (!HealthJson.Eq(HealthJson.Str(HealthJson.Field(state, "trigger")), "scheduled")) continue;
                if (!GreenFlags(state)) continue;
                if (!HealthJson.IsTrue(HealthJson.Field(state, "completedAt"))) continue;
                var gitRef = HealthJson.Str(HealthJson.Field(state, "ref"));
                if (gitRef.Length > 0 && !HealthJson.Eq(gitRef, "master") && !HealthJson.Eq(gitRef, "origin/master")) continue;
                var stateSha = HealthJson.Str(HealthJson.Field(state, "sha"));
                if (!string.IsNullOrEmpty(job.Sha) && stateSha.Length > 0 && !HealthJson.Eq(job.Sha, stateSha)) continue;
                return state;
            }
            return null;
        }

        /// <summary>Test-NightlyMonitorRouting (G-116): the monitor must never run on the desktop tag.</summary>
        public static bool AssertMonitorRouting(string tag)
        {
            if (HealthJson.Eq(tag ?? "", "desktop")) throw new InvalidOperationException("monitor must not use desktop tag");
            return true;
        }

        // ---- CARD-0545 D-9: the independent watchdog's heartbeat folds into the local evaluator ----

        /// <summary>The production watchdog read: GET the URL (10 s bound); a non-success status throws.</summary>
        public static string HttpSnapshot(string url)
        {
            using var client = new HttpClient { Timeout = WatchdogTimeout };
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            using var response = client.Send(request);
            response.EnsureSuccessStatusCode();
            return response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        }

        /// <summary>Get-NightlyWatchdogSnapshot: a bounded read through the seam; never invents a snapshot.</summary>
        public static NightlyWatchdogFetch FetchWatchdogSnapshot(string url, Func<string, string> read)
        {
            if (string.IsNullOrWhiteSpace(url)) return new NightlyWatchdogFetch(false, "", null, "watchdog snapshot url not configured");
            string raw;
            try { raw = (read ?? HttpSnapshot)(url) ?? ""; }
            catch (Exception e) { return new NightlyWatchdogFetch(false, "", null, Message(e)); }
            HealthJson.TryParse(raw, out var snapshot);
            return new NightlyWatchdogFetch(true, raw, snapshot, "");
        }

        /// <summary>
        /// Test-NightlyWatchdogFreshness: fresh only when reachable, well formed (schemaVersion 1, an instanceId, a
        /// parseable heartbeatAt not in the future), at most 20:00 old, in the expected namespace with the expected
        /// instance id (ordinal), and with no open outage. Each failure is one stable reason.
        /// </summary>
        public static NightlyWatchdogFreshness WatchdogFreshness(NightlyWatchdogFetch fetch, DateTime nowUtc, string expectedNamespace = "mc", string expectedInstanceId = "")
        {
            var result = new NightlyWatchdogFreshness();
            NightlyWatchdogFreshness Refuse(string reason)
            {
                result.Reasons.Add(reason);
                return result;
            }
            if (fetch == null || !fetch.Reachable) return Refuse("watchdog-unreachable");
            var snapshot = fetch.Snapshot;
            if (snapshot == null || snapshot is JsonArray || HealthJson.IsString(snapshot)) return Refuse("watchdog-malformed");
            result.InstanceId = HealthJson.Str(HealthJson.Field(snapshot, "instanceId"));
            if (!HealthJson.Has(snapshot, "schemaVersion") || HealthJson.IntegerLiteral(HealthJson.Field(snapshot, "schemaVersion")) != 1) return Refuse("watchdog-malformed");
            if (string.IsNullOrWhiteSpace(result.InstanceId)) return Refuse("watchdog-malformed");
            DateTime? beat = null;
            var beatNode = HealthJson.Field(snapshot, "heartbeatAt");
            if (beatNode != null && DateTime.TryParse(HealthJson.Str(beatNode), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out var parsed)) beat = parsed;
            if (beat == null) return Refuse("watchdog-malformed");
            var beatUtc = DateTime.SpecifyKind(beat.Value, DateTimeKind.Utc);
            result.HeartbeatAt = beatUtc.ToString("o", CultureInfo.InvariantCulture);
            var now = nowUtc.Kind == DateTimeKind.Utc ? nowUtc : DateTime.SpecifyKind(nowUtc.ToUniversalTime(), DateTimeKind.Utc);
            var age = now - beatUtc;
            if (age < TimeSpan.Zero) return Refuse("watchdog-malformed");
            if (age > WatchdogFreshFor) return Refuse("watchdog-stale");
            if (!string.Equals(HealthJson.Str(HealthJson.Field(snapshot, "namespace")), expectedNamespace, StringComparison.Ordinal)) return Refuse("watchdog-identity-mismatch");
            if (string.IsNullOrWhiteSpace(expectedInstanceId) || !string.Equals(result.InstanceId, expectedInstanceId, StringComparison.Ordinal)) return Refuse("watchdog-identity-mismatch");
            if (OpenOutages(HealthJson.Field(snapshot, "openOutages")) > 0) return Refuse("watchdog-outage-open");
            result.Fresh = true;
            return result;
        }

        // @($snap.openOutages | Where-Object { $null -ne $_ }).Count: an array's non-null items; absent or null is none;
        // any other value is one.
        private static int OpenOutages(JsonNode outages)
        {
            if (outages == null) return 0;
            if (outages is JsonArray array) return array.Count(item => item != null);
            return 1;
        }

        // ---- the run (Invoke-AntiphonNightlyHealth) ----

        /// <summary>
        /// Invoke-AntiphonNightlyHealth: reads the readiness config, Windmill, the native state and the watchdog,
        /// keeps the notification ledger for an unhealthy verdict, writes last-monitor.json and returns the result.
        /// </summary>
        public static NightlyHealthResult Run(NightlyHealthOptions options, NightlyHealthSeams seams)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (seams == null) throw new ArgumentNullException(nameof(seams));
            var error = seams.Error ?? TextWriter.Null;
            var stateRoot = options.StateRoot ?? "";
            var expectedScriptHash = options.ExpectedScriptHash ?? "";
            var expectedPolicyHash = options.ExpectedPolicyHash ?? "";
            var repositoryPath = options.RepositoryPath ?? "";
            var projectId = options.ProjectId ?? "";
            var watchdogUrl = options.WatchdogSnapshotUrl ?? "";
            var watchdogInstanceId = options.ExpectedWatchdogInstanceId ?? "";

            // CARD-0545 D-9: readiness-config.json (written at qualification) supplies any value not passed.
            var config = ReadReadinessConfig(options.ReadinessConfigPath);
            if (config != null)
            {
                string Config(string name) => HealthJson.Str(HealthJson.Field(config, name));
                if (string.IsNullOrWhiteSpace(expectedScriptHash)) expectedScriptHash = Config("expectedScriptHash");
                if (string.IsNullOrWhiteSpace(expectedPolicyHash)) expectedPolicyHash = Config("expectedPolicyHash");
                if (string.IsNullOrWhiteSpace(watchdogUrl)) watchdogUrl = Config("watchdogSnapshotUrl");
                if (string.IsNullOrWhiteSpace(watchdogInstanceId)) watchdogInstanceId = Config("watchdogInstanceId");
                if (string.IsNullOrWhiteSpace(repositoryPath)) repositoryPath = Config("repositoryPath");
                if (string.IsNullOrWhiteSpace(projectId)) projectId = Config("projectId");
            }
            if (string.IsNullOrWhiteSpace(repositoryPath)) repositoryPath = seams.Environment?.Invoke("ANTIPHON_NIGHTLY_REPOSITORY_PATH") ?? "";
            if (string.IsNullOrWhiteSpace(projectId)) projectId = seams.Environment?.Invoke("ANTIPHON_NIGHTLY_PROJECT_ID") ?? "";

            var now = seams.Clock.GetUtcNow().UtcDateTime;
            var api = seams.WindmillApi;
            NightlyWindmillRegistration registration = null;
            NightlyWindmillSchedule schedule = null;
            IReadOnlyList<NightlyWindmillJob> jobs = Array.Empty<NightlyWindmillJob>();
            if (api != null)
            {
                registration = Step(error, "windmill registration", () => api.GetRegistration?.Invoke());
                schedule = Step(error, "windmill schedule", () => api.GetSchedule?.Invoke());
                jobs = Step(error, "windmill jobs", () => api.GetJobs?.Invoke()?.ToArray()) ?? Array.Empty<NightlyWindmillJob>();
            }
            var native = ReadStateFile(Path.Combine(stateRoot, "last-run.json"), error);
            var lastGreen = ReadStateFile(Path.Combine(stateRoot, "last-complete-green.json"), error);
            var health = Evaluate(registration, schedule, jobs, native, now, lastGreen, expectedScriptHash, expectedPolicyHash);

            JsonNode notify = null;
            var receiptOk = false;
            if (!health.Healthy)
            {
                if (string.IsNullOrWhiteSpace(options.AuthorizedDestination) && !seams.AllowOfflineNotify)
                {
                    health.Reasons.Add("unauthorized-destination");
                }
                else
                {
                    var nativeTrue = HealthJson.IsTrue(native);
                    var notification = new JsonObject
                    {
                        ["workspace"] = "mc",
                        ["localDueDate"] = NightlyLondon.DateText(NightlyLondon.ToLocal(now)),
                        ["windmillJobId"] = jobs.Count > 0 ? jobs[0]?.Id ?? "" : "",
                        ["nativeRunId"] = nativeTrue ? HealthJson.Str(HealthJson.Field(native, "runId")) : "",
                        ["sha"] = nativeTrue ? HealthJson.Str(HealthJson.Field(native, "sha")) : "",
                        ["policyHash"] = nativeTrue ? HealthJson.Str(HealthJson.Field(native, "policyHash")) : expectedPolicyHash,
                        ["failureKind"] = string.Join(",", health.Reasons),
                        ["notificationId"] = NightlyHealthNotifications.NewId(),
                        ["status"] = "pending",
                    };
                    (notify, receiptOk) = Notify(stateRoot, notification, health, seams, error);
                }
            }

            // CARD-0545 D-9: a missing, stale, malformed, foreign or outage-reporting watchdog fails readiness closed.
            // Evaluated after the local ledger so the watchdog, not this evaluator, owns outage notification.
            var watchdogFetch = FetchWatchdogSnapshot(watchdogUrl, seams.WatchdogSnapshot ?? HttpSnapshot);
            var watchdog = WatchdogFreshness(watchdogFetch, now, "mc", watchdogInstanceId);
            if (!watchdog.Fresh)
            {
                health.Reasons.AddRange(watchdog.Reasons);
                health.Healthy = false;
                health.ReadyForDeferral = false;
            }
            var result = new NightlyHealthResult
            {
                ExitCode = health.Healthy ? 0 : 1,
                Health = health,
                Notification = notify,
                Receipt = receiptOk,
                RecordedAt = now.ToString("o", CultureInfo.InvariantCulture),
                // CARD-0544 D-7: the identities the server's readiness reader matches against its receipt.
                Identity = new NightlyHealthIdentity(repositoryPath, projectId, expectedPolicyHash, expectedScriptHash, health.GreenRunId,
                    health.GreenJobNativeRunId, health.GreenJobId, watchdog.InstanceId, watchdog.HeartbeatAt),
            };
            Step(error, "save " + MonitorResultPath(stateRoot), () =>
            {
                WriteResult(MonitorResultPath(stateRoot), result);
                return true;
            });
            return result;
        }

        // The ledger half of Invoke-AntiphonNightlyHealth for an unhealthy verdict: persist the event before any enqueue,
        // look for the recipient's receipt, enqueue once (never twice for the same logical event) and settle the receipt.
        private static (JsonNode Notify, bool Receipt) Notify(string stateRoot, JsonObject notification, NightlyHealthVerdict health,
            NightlyHealthSeams seams, TextWriter error)
        {
            var storePath = NightlyHealthNotifications.StorePath(stateRoot);
            var saved = Step(error, "save " + storePath, () => NightlyHealthNotifications.AddEvent(storePath, notification, error));
            var delivered = saved?.Event ?? notification;
            var sink = seams.NotificationSink;
            var receiptOk = NightlyHealthNotifications.HasReceipt(delivered, NightlyHealthNotifications.RecipientView(stateRoot, seams.RecipientView, error));
            JsonNode notify = null;
            var notifyIsRecord = false;
            if (!receiptOk)
            {
                if (!NightlyHealthNotifications.TransportAccepted(delivered))
                {
                    try
                    {
                        if (sink != null) notify = NightlyHealthNotifications.Enqueue(delivered, sink);
                        if (NightlyHealthNotifications.EnqueueAccepted(notify)) NightlyHealthNotifications.MarkTransportAccepted(storePath, delivered, notify, error);
                    }
                    catch (Exception e)
                    {
                        notify = new JsonObject { ["Enqueued"] = false, ["Error"] = Message(e), ["Received"] = false };
                        notifyIsRecord = true;
                        health.Healthy = false;
                        health.Reasons.Add("enqueue-failed");
                    }
                }
                receiptOk = NightlyHealthNotifications.HasReceipt(delivered, NightlyHealthNotifications.RecipientView(stateRoot, seams.RecipientView, error));
            }
            if (receiptOk)
            {
                Step(error, "save " + NightlyHealthNotifications.ReceiptPath(stateRoot), () =>
                {
                    // receivedAt is a fresh clock read (Get-NightlyUtcNow), not the evaluation instant.
                    NightlyHealthNotifications.SaveReceipt(stateRoot, delivered, seams.Clock.GetUtcNow().UtcDateTime, error);
                    return true;
                });
                Step(error, "save " + storePath, () =>
                {
                    NightlyHealthNotifications.MarkReceived(storePath, delivered, error);
                    return true;
                });
            }
            else
            {
                health.Healthy = false;
                if (!health.Reasons.Contains("notification-unreceived", StringComparer.OrdinalIgnoreCase)) health.Reasons.Add("notification-unreceived");
            }
            if (notifyIsRecord) notify["Received"] = receiptOk;
            else if (notify != null)
            {
                // The transport's answer is a map, not this record: it is carried under Transport.
                notify = new JsonObject
                {
                    ["Enqueued"] = HealthJson.IsTrue(HealthJson.Field(notify, "Enqueued")),
                    ["Received"] = receiptOk,
                    ["Transport"] = notify.DeepClone(),
                };
            }
            return (notify, receiptOk);
        }

        /// <summary>Read-NightlyReadinessConfig: the parsed file, or null when the path is blank, missing or not JSON.</summary>
        public static JsonNode ReadReadinessConfig(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
            try { return HealthJson.TryParse(File.ReadAllText(path, Encoding.UTF8), out var node) ? node : null; }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { return null; }
        }

        /// <summary>Writes the result record (indented UTF-8, no byte-order mark) atomically: last-monitor.json and --result-file.</summary>
        public static void WriteResult(string path, NightlyHealthResult result) => HealthJson.WriteAtomic(path, result.ToJson());

        // Get-Content -Raw | ConvertFrom-Json of a state file: null when it is missing or empty; a file that does not parse
        // fails that statement (one error line) and reads as null.
        private static JsonNode ReadStateFile(string path, TextWriter error)
        {
            if (!File.Exists(path)) return null;
            try
            {
                if (HealthJson.TryParse(File.ReadAllText(path, Encoding.UTF8), out var node)) return node;
                error.WriteLine("nightly-health: " + path + " is not JSON");
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                error.WriteLine("nightly-health: cannot read " + path + ": " + Message(e));
            }
            return null;
        }

        // One statement of the script under ErrorActionPreference Continue: a failure writes one line and the value stays empty.
        internal static T Step<T>(TextWriter error, string what, Func<T> step)
        {
            try { return step(); }
            catch (Exception e) when (!(e is OutOfMemoryException))
            {
                error.WriteLine("nightly-health: " + what + " failed: " + Message(e));
                return default;
            }
        }

        /// <summary>An exception's message on one line.</summary>
        public static string Message(Exception e) => (e.Message ?? e.GetType().Name).Replace("\r", " ").Replace("\n", " ");
    }

    /// <summary>
    /// The script's readings of ConvertFrom-Json values (Windows PowerShell 5.1): property names match case-insensitively,
    /// [string] and [bool] are PowerShell's conversions, -eq on text ignores case (invariant culture), [datetime] parses
    /// text with the invariant culture. And the JSON file writer (Write-NightlyAtomicJson).
    /// </summary>
    public static class HealthJson
    {
        private static readonly JsonSerializerOptions Indented = new JsonSerializerOptions { WriteIndented = true };

        /// <summary>ConvertFrom-Json: true and the value (null for JSON null or blank text) when the text is one JSON value.</summary>
        public static bool TryParse(string text, out JsonNode node)
        {
            node = null;
            if (string.IsNullOrWhiteSpace(text)) return true;
            try
            {
                node = JsonNode.Parse(text.TrimStart('\uFEFF'));
                // A JsonObject builds its properties lazily: a repeated name only throws when it is read.
                Touch(node);
                return true;
            }
            catch (Exception e) when (e is JsonException || e is ArgumentException || e is InvalidOperationException)
            {
                node = null;
                return false;
            }
        }

        private static void Touch(JsonNode node)
        {
            if (node is JsonObject obj) foreach (var pair in obj) Touch(pair.Value);
            else if (node is JsonArray array) foreach (var item in array) Touch(item);
        }

        /// <summary>$o.Name: the property's value (null when absent or o is not an object); the name matches case-insensitively.</summary>
        public static JsonNode Field(JsonNode node, string name)
        {
            if (!(node is JsonObject obj)) return null;
            if (obj.TryGetPropertyValue(name, out var value)) return value;
            foreach (var pair in obj)
                if (string.Equals(pair.Key, name, StringComparison.OrdinalIgnoreCase)) return pair.Value;
            return null;
        }

        /// <summary>Test-NightlyHasField / $o.PSObject.Properties[name]: the object has the property (even when it is null).</summary>
        public static bool Has(JsonNode node, string name)
        {
            if (!(node is JsonObject obj)) return false;
            if (obj.ContainsKey(name)) return true;
            foreach (var pair in obj)
                if (string.Equals(pair.Key, name, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        /// <summary>The JSON kind of a value (Null for a null node).</summary>
        public static JsonValueKind Kind(JsonNode node) => node == null ? JsonValueKind.Null : node.GetValueKind();

        /// <summary>The value is a JSON string.</summary>
        public static bool IsString(JsonNode node) => Kind(node) == JsonValueKind.String;

        /// <summary>[string]$value.</summary>
        public static string Str(JsonNode node)
        {
            switch (node)
            {
                case null: return "";
                case JsonObject obj: return "@{" + string.Join("; ", obj.Select(pair => pair.Key + "=" + Element(pair.Value))) + "}";
                case JsonArray array: return string.Join(" ", array.Select(Element));
            }
            switch (node.GetValueKind())
            {
                case JsonValueKind.String: return node.AsValue().TryGetValue<string>(out var text) ? text : JsonSerializer.Deserialize<string>(node.ToJsonString());
                case JsonValueKind.True: return "True";
                case JsonValueKind.False: return "False";
                case JsonValueKind.Number: return node.ToJsonString();
                default: return "";
            }
        }

        private static string Element(JsonNode node) => node is JsonArray ? "System.Object[]" : Str(node);

        /// <summary>[bool]$value (LanguagePrimitives.IsTrue).</summary>
        public static bool IsTrue(JsonNode node)
        {
            switch (node)
            {
                case null: return false;
                case JsonObject _: return true;
                case JsonArray array:
                    if (array.Count == 0) return false;
                    if (array.Count > 1) return true;
                    if (array[0] is JsonArray inner) return inner.Count > 0;
                    return IsTrue(array[0]);
            }
            switch (node.GetValueKind())
            {
                case JsonValueKind.True: return true;
                case JsonValueKind.String: return Str(node).Length != 0;
                case JsonValueKind.Number: return double.TryParse(node.ToJsonString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var number) && number != 0;
                default: return false;
            }
        }

        /// <summary>first -eq second for text: invariant culture, case-insensitive.</summary>
        public static bool Eq(string first, string second) => string.Equals(first ?? "", second ?? "", StringComparison.InvariantCultureIgnoreCase);

        /// <summary>[datetime]$value for text (invariant culture; "Z" and offsets give local time, no zone is unspecified); null when it does not parse.</summary>
        public static DateTime? CastDateTime(JsonNode node) => IsString(node) ? CastDateTime(Str(node)) : null;

        /// <summary>[datetime]'text'; null when it does not parse.</summary>
        public static DateTime? CastDateTime(string text) =>
            DateTime.TryParse(text ?? "", CultureInfo.InvariantCulture, DateTimeStyles.None, out var value) ? value : (DateTime?)null;

        /// <summary>first -gt second where a value that failed to convert is $null: $null is never greater, anything is greater than $null.</summary>
        public static bool Greater(DateTime? first, DateTime? second)
        {
            if (first == null) return false;
            if (second == null) return true;
            return first.Value > second.Value;
        }

        /// <summary>The value when it is a JSON integer literal that fits a long (ConvertFrom-Json's Int32/Int64), else null.</summary>
        public static long? IntegerLiteral(JsonNode node)
        {
            if (Kind(node) != JsonValueKind.Number) return null;
            var text = node.ToJsonString();
            if (text.IndexOfAny(new[] { '.', 'e', 'E' }) >= 0) return null;
            return long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value) ? value : (long?)null;
        }

        /// <summary>[int]$value (numbers round half to even, text parses, true is 1, null is 0); null when the cast fails.</summary>
        public static int? CastInt(JsonNode node)
        {
            try
            {
                switch (Kind(node))
                {
                    case JsonValueKind.Null: return 0;
                    case JsonValueKind.True: return 1;
                    case JsonValueKind.False: return 0;
                    case JsonValueKind.Number:
                        return Convert.ToInt32(Math.Round(double.Parse(node.ToJsonString(), NumberStyles.Float, CultureInfo.InvariantCulture), MidpointRounding.ToEven));
                    case JsonValueKind.String:
                        var text = Str(node);
                        if (string.IsNullOrWhiteSpace(text)) return 0;
                        return Convert.ToInt32(Math.Round(double.Parse(text, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture), MidpointRounding.ToEven));
                    default: return null;
                }
            }
            catch (Exception e) when (e is FormatException || e is OverflowException)
            {
                return null;
            }
        }

        /// <summary>The value as indented JSON.</summary>
        public static string Text(JsonNode node) => node == null ? "null" : node.ToJsonString(Indented);

        /// <summary>
        /// Write-NightlyAtomicJson: creates the directory, writes UTF-8 without a byte-order mark to a temporary file
        /// beside the target, then replaces the target (its .bak removed) or moves the file into place.
        /// </summary>
        public static void WriteAtomic(string path, JsonNode value)
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory)) Directory.CreateDirectory(directory);
            var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            var backup = path + ".bak";
            File.WriteAllText(temporary, Text(value), new UTF8Encoding(false));
            try
            {
                if (File.Exists(path) || Directory.Exists(path))
                {
                    File.Replace(temporary, path, backup);
                    try
                    {
                        if (File.Exists(backup))
                        {
                            File.SetAttributes(backup, FileAttributes.Normal);
                            File.Delete(backup);
                        }
                    }
                    catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { }
                }
                else
                {
                    File.Move(temporary, path);
                }
            }
            finally
            {
                if (File.Exists(temporary))
                {
                    try { File.Delete(temporary); }
                    catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { }
                }
            }
        }
    }
}
