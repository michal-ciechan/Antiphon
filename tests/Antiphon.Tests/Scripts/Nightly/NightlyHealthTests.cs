using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Antiphon.Nightly;
using Antiphon.Tests.Application;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts.Nightly;

/// <summary>
/// W2-nightly S05: the nightly readiness evaluator (Antiphon.Nightly NightlyHealth, NightlyHealthWindmill,
/// NightlyHealthNotifications) in-process. These are the cases of the deleted scripts/test-nightly-health.ps1 with
/// the same names and assertion rows: C487_G099-G126 (the default loop that only ran by hand), C544_* and C545_*
/// (formerly NightlyVerificationContractTests wrappers that started the harness), plus the Windmill adapter check that
/// replaced RunnerRestartScriptCompatibilityTests.Nightly_health_closures_resolve_helpers_under_the_call_operator.
/// Each case records its rows like Assert-C487 and requires every row to pass and the case's row count. HealthFx is the
/// New-HealthFx/Invoke-HealthFx twin: the seams file becomes a NightlyHealthSeams built directly. The real
/// HTTP cases run against an owned loopback stub (LoopbackHttpStub); nothing reads the machine's Windmill settings.
/// </summary>
[Category("Integration")]
public sealed class NightlyHealthTests
{
    private const string Sched = "u/lndcobra/antiphon_nightly_tests";

    // ---- Assert-C487 ----

    private sealed class Rows
    {
        private readonly List<(string Name, bool Passed, string Detail)> _rows = new();

        public void Check(bool condition, string name, string detail = "") => _rows.Add((name, condition, detail ?? ""));

        /// <summary>No row failed, the case wrote exactly expected rows, and every required row is present.</summary>
        public void ShouldPass(int expected, params string[] required)
        {
            var failed = _rows.Where(row => !row.Passed).Select(row => row.Name + " :: " + row.Detail).ToArray();
            failed.ShouldBeEmpty();
            var names = _rows.Select(row => row.Name).ToArray();
            names.Length.ShouldBe(expected, string.Join(Environment.NewLine, names));
            foreach (var name in required) names.ShouldContain(name);
        }
    }

    // ---- shared builders ----

    private static NightlyWindmillRegistration Registration(string tag = "desktop", string hash = "abc") => new(true, hash, tag, "");

    private static NightlyWindmillSchedule Schedule(bool enabled = true, bool paused = false, string tag = "", JsonNode? args = null,
        string worker = "", string server = "") => new(enabled, paused, tag, args, worker, server);

    private static JsonObject State(object value) => (JsonObject)JsonSerializer.SerializeToNode(value)!;

    private static NightlyWindmillApi Api(NightlyWindmillRegistration? registration, NightlyWindmillSchedule? schedule, params NightlyWindmillJob[] jobs) => new()
    {
        GetRegistration = () => registration!,
        GetSchedule = () => schedule!,
        GetJobs = () => jobs,
    };

    private static NightlyHealthSeams Seams(DateTimeOffset now, NightlyWindmillApi? api, Func<string, string>? watchdog = null,
        Func<JsonObject, JsonNode>? sink = null, Func<IEnumerable<JsonNode>>? view = null, bool allowOffline = false, StringWriter? error = null) => new()
    {
        Clock = new FakeTimeProvider(now),
        WindmillApi = api!,
        WatchdogSnapshot = watchdog ?? NightlyHealth.HttpSnapshot,
        NotificationSink = sink!,
        RecipientView = view!,
        AllowOfflineNotify = allowOffline,
        Environment = _ => null!,
        Error = error ?? new StringWriter(),
    };

    private static string Reasons(NightlyHealthVerdict health) => string.Join(",", health.Reasons);

    private static string LondonDate(DateTime utc) => NightlyLondon.DateText(NightlyLondon.ToLocal(utc));

    // ---- HealthFx: New-HealthFx / Invoke-HealthFx ----

    private sealed class HealthFx
    {
        public required string Root { get; init; }
        public required string EnqueueLog { get; init; }
        public required string Inbox { get; init; }

        public static HealthFx Create(NightlyFixture results)
        {
            var root = results.PathOf(Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            NightlyFixture.WriteJson(Path.Combine(root, "last-run.json"), new
            {
                runId = "run-1", sha = "sha-1", policyHash = "p", coverageComplete = false, testsPassed = true, reportDelivered = true,
                completedAt = "2026-09-11T08:00:00Z",
            });
            return new HealthFx { Root = root, EnqueueLog = Path.Combine(root, "enqueue.jsonl"), Inbox = Path.Combine(root, "inbox.json") };
        }

        /// <summary>The seams file of New-HealthFx: the clock at 2026-09-11 12:00Z, offline notify, a healthy registration and
        /// schedule with no jobs, a sink that logs each event and accepts it (202), and a recipient view over inbox.json.</summary>
        public NightlyHealthSeams Seams() => NightlyHealthTests.Seams(NightlyFixture.DefaultNow,
            Api(new NightlyWindmillRegistration(true, "a", "desktop", ""), Schedule(tag: "desktop")),
            sink: notification =>
            {
                File.AppendAllText(EnqueueLog, notification.ToJsonString() + Environment.NewLine, Encoding.ASCII);
                return new JsonObject
                {
                    ["Enqueued"] = true, ["TransportStatus"] = 202, ["Received"] = false,
                    ["NotificationId"] = HealthJson.Str(HealthJson.Field(notification, "notificationId")),
                };
            },
            view: () =>
            {
                if (!File.Exists(Inbox)) return [];
                HealthJson.TryParse(File.ReadAllText(Inbox), out var inbox);
                return inbox is JsonArray array ? array.Select(item => item!).ToArray() : [inbox!];
            },
            allowOffline: true);

        public NightlyHealthResult Invoke() => NightlyHealth.Run(new NightlyHealthOptions
        {
            StateRoot = Root, ExpectedScriptHash = "a", ExpectedPolicyHash = "p", AuthorizedDestination = "operator-1",
        }, Seams());
    }

    private static JsonObject Notification(string failureKind, string notificationId) => new()
    {
        ["workspace"] = "mc", ["localDueDate"] = "2026-09-11", ["windmillJobId"] = "j", ["nativeRunId"] = "r", ["sha"] = "s",
        ["policyHash"] = "p", ["failureKind"] = failureKind, ["notificationId"] = notificationId,
    };

    // ---- CARD-0487 C487_G099-G126 ----

    [Test]
    public void C487_G099()
    {
        var rows = new Rows();
        foreach (var kind in new[] { "script", "schedule" })
        {
            var h = NightlyHealth.Evaluate(kind == "script" ? null : Registration(), kind == "schedule" ? null : Schedule(tag: "desktop"),
                [], null, DateTime.UtcNow, expectedScriptHash: "abc");
            rows.Check(!h.Healthy, $"G099 missing {kind}");
        }
        rows.ShouldPass(2);
    }

    [Test]
    public void C487_G100()
    {
        var rows = new Rows();
        foreach (var kind in new[] { "disabled", "paused" })
        {
            var h = NightlyHealth.Evaluate(Registration(), Schedule(enabled: kind != "disabled", paused: kind == "paused", tag: "desktop"), [], null,
                DateTime.UtcNow, expectedScriptHash: "abc");
            rows.Check(!h.Healthy, $"G100 {kind}");
        }
        rows.ShouldPass(2);
    }

    [Test]
    public void C487_G101()
    {
        var rows = new Rows();
        var h = NightlyHealth.Evaluate(Registration(hash: "wrong"), Schedule(), [], null, DateTime.UtcNow, expectedScriptHash: "abc");
        rows.Check(!h.Healthy, "G101 hash mismatch");
        rows.ShouldPass(1);
    }

    [Test]
    public void C487_G102()
    {
        var rows = new Rows();
        var h = NightlyHealth.Evaluate(Registration(tag: "server"), Schedule(), [], null, DateTime.UtcNow, expectedScriptHash: "abc");
        rows.Check(!h.Healthy, "G102 non-desktop");
        rows.ShouldPass(1);
    }

    [Test]
    public void C487_G103()
    {
        var rows = new Rows();
        var h = NightlyHealth.Evaluate(Registration(), Schedule(args: new JsonObject { ["suites"] = "client" }), [], null, DateTime.UtcNow, expectedScriptHash: "abc");
        rows.Check(!h.Healthy, "G103 suite override");
        rows.ShouldPass(1);
    }

    [Test]
    public void C487_G104()
    {
        var rows = new Rows();
        var h = NightlyHealth.Evaluate(Registration(), Schedule(worker: "1", server: "2"), [], null, DateTime.UtcNow, expectedScriptHash: "abc");
        rows.Check(!h.Healthy, "G104 worker version");
        rows.ShouldPass(1);
    }

    [Test]
    public void C487_G105()
    {
        var rows = new Rows();
        var due = NightlyLondon.DueUtc(NightlyLondon.ToLocal(DateTime.UtcNow));
        var h1 = NightlyHealth.Evaluate(Registration(hash: "a"), Schedule(), [new NightlyWindmillJob { Status = "queued" }], null, due.AddMinutes(31), expectedScriptHash: "a");
        var h2 = NightlyHealth.Evaluate(Registration(hash: "a"), Schedule(), [], null, due.AddMinutes(-1), expectedScriptHash: "a");
        rows.Check(!h1.Healthy, "G105 overdue at grace");
        rows.Check(h2.Pending || h2.Healthy, "G105 pre-grace pending");
        rows.ShouldPass(2);
    }

    [Test]
    public void C487_G106()
    {
        var rows = new Rows();
        var london = NightlyLondon.ToLocal(DateTime.UtcNow);
        // Get-Date -Hour 8 -Minute 1 -Second 0 keeps the current sub-second ticks.
        var morning = new DateTime(london.Year, london.Month, london.Day, 8, 1, 0, DateTimeKind.Unspecified).AddTicks(DateTime.Now.Ticks % TimeSpan.TicksPerSecond);
        var morningUtc = TimeZoneInfo.ConvertTimeToUtc(morning, NightlyLondon.TimeZone);
        var h = NightlyHealth.Evaluate(Registration(hash: "a"), Schedule(), [],
            State(new { coverageComplete = true, testsPassed = true, reportDelivered = true, completedAt = morningUtc.AddDays(-1).ToString("o") }),
            morningUtc, expectedScriptHash: "a");
        rows.Check(!h.Healthy, "G106 missing expected / yesterday insufficient");
        var pre = NightlyHealth.Evaluate(Registration(hash: "a"), Schedule(), [], null, morningUtc.AddHours(-10), expectedScriptHash: "a");
        rows.Check(pre.Pending || !pre.ReadyForDeferral, "G106 pre-deadline pending");
        rows.ShouldPass(2);
    }

    [Test]
    public void C487_G107()
    {
        var rows = new Rows();
        foreach (var d in new[] { "2026-03-28", "2026-03-30", "2026-10-24", "2026-10-26" })
        {
            var utc = NightlyLondon.DueUtc(DateTime.Parse(d, CultureInfo.InvariantCulture));
            var back = NightlyLondon.ToLocal(utc);
            rows.Check(back.Hour == 0 && back.Minute == 30, $"G107 {d} 00:30 local", back.ToString("o"));
        }
        rows.ShouldPass(4);
    }

    [Test]
    public void C487_G108()
    {
        var rows = new Rows();
        var h = NightlyHealth.Evaluate(Registration(hash: "a"), Schedule(),
            [new NightlyWindmillJob { NativeRunId = "job1", LocalDueDate = LondonDate(DateTime.UtcNow) }],
            State(new { runId = "other", coverageComplete = true, testsPassed = true, reportDelivered = true }), DateTime.UtcNow, expectedScriptHash: "a");
        rows.Check(!h.Healthy, "G108 run id mismatch");
        rows.ShouldPass(1);
    }

    [Test]
    public void C487_G109()
    {
        var rows = new Rows();
        var h = NightlyHealth.Evaluate(Registration(hash: "a"), Schedule(), [new NightlyWindmillJob { Sha = "aaa", NativeRunId = "r1" }],
            State(new { runId = "r1", sha = "bbb", coverageComplete = true, testsPassed = true, reportDelivered = true }), DateTime.UtcNow, expectedScriptHash: "a");
        rows.Check(!h.Healthy, "G109 sha mismatch");
        rows.ShouldPass(1);
    }

    [Test]
    public void C487_G110()
    {
        var rows = new Rows();
        var h = NightlyHealth.Evaluate(Registration(hash: "a"), Schedule(), [],
            State(new { policyHash = "old", coverageComplete = true, testsPassed = true, reportDelivered = true }), DateTime.UtcNow,
            expectedScriptHash: "a", expectedPolicyHash: "new");
        rows.Check(!h.Healthy, "G110 policy mismatch");
        rows.ShouldPass(1);
    }

    /// <summary>The harness's G111 rows assert nothing (wrapper SSH and missing-result are not observable here); kept 1:1.</summary>
    [Test]
    public void C487_G111()
    {
        var rows = new Rows();
        foreach (var kind in new[] { "ssh", "missing-result" }) rows.Check(true, $"G111 wrapper {kind}");
        rows.ShouldPass(2);
    }

    [Test]
    public void C487_G112() => FlagCase("G112 incomplete coverage", coverage: false);

    [Test]
    public void C487_G113() => FlagCase("G113 tests red", tests: false);

    [Test]
    public void C487_G114() => FlagCase("G114 report outage", report: false);

    private static void FlagCase(string name, bool coverage = true, bool tests = true, bool report = true)
    {
        var rows = new Rows();
        var h = NightlyHealth.Evaluate(Registration(hash: "a"), Schedule(), [],
            State(new { coverageComplete = coverage, testsPassed = tests, reportDelivered = report }), DateTime.UtcNow, expectedScriptHash: "a");
        rows.Check(!h.Healthy, name);
        rows.ShouldPass(1);
    }

    [Test]
    public void C487_G115()
    {
        var rows = new Rows();
        foreach (var kind in new[] { "started", "stale" })
        {
            var native = State(new { phase = "Started", lastActivityAt = DateTime.UtcNow.AddHours(-3).ToString("o") });
            var h = NightlyHealth.Evaluate(Registration(hash: "a"), Schedule(), [], native, DateTime.UtcNow, expectedScriptHash: "a");
            rows.Check(!h.Healthy && !h.TerminateRequested, $"G115 {kind} no kill");
        }
        rows.ShouldPass(2);
    }

    [Test]
    public void C487_G116()
    {
        var rows = new Rows();
        try
        {
            NightlyHealth.AssertMonitorRouting("desktop");
            rows.Check(false, "G116 desktop rejected");
        }
        catch (InvalidOperationException)
        {
            rows.Check(true, "G116 desktop rejected");
        }
        rows.ShouldPass(1);
    }

    [Test]
    public void C487_G117()
    {
        var rows = new Rows();
        foreach (var kind in new[] { "missing", "stale", "failed" })
        {
            var h = NightlyHealth.Evaluate(Registration(hash: "a"), Schedule(), [], kind == "failed" ? State(new { coverageComplete = false }) : null,
                DateTime.UtcNow, expectedScriptHash: "a");
            rows.Check(!h.ReadyForDeferral, $"G117 {kind} no deferral");
        }
        rows.ShouldPass(3);
    }

    [Test]
    public void C487_G118()
    {
        using var results = NightlyFixture.Create("nightly-health");
        var rows = new Rows();
        foreach (var kind in new[] { "failure", "recovery" })
        {
            var store = results.PathOf("nstore-" + Guid.NewGuid().ToString("N") + ".json");
            var saved = NightlyHealthNotifications.AddEvent(store, Notification(kind, "n1"));
            rows.Check(!saved.Deduped, $"G118 persist {kind}");
        }
        var fx = HealthFx.Create(results);
        var r = fx.Invoke();
        var events = NightlyHealthNotifications.ReadEvents(NightlyHealthNotifications.StorePath(fx.Root));
        var enqueued = File.Exists(fx.EnqueueLog);
        rows.Check(events.Count == 1 && enqueued && !r.Receipt, "G118 entry persist before enqueue", "events=" + events.Count);
        rows.ShouldPass(3);
    }

    [Test]
    public void C487_G119()
    {
        var rows = new Rows();
        foreach (var kind in new[] { "busy", "unavailable" })
        {
            try
            {
                NightlyHealthNotifications.Enqueue(new JsonObject { ["notificationId"] = "x" }, _ => throw new InvalidOperationException(kind));
                rows.Check(false, $"G119 {kind}");
            }
            catch (InvalidOperationException)
            {
                rows.Check(true, $"G119 {kind} retryable");
            }
        }
        rows.ShouldPass(2);
    }

    [Test]
    public void C487_G120()
    {
        var rows = new Rows();
        foreach (var kind in new[] { "202", "200-noreadback" })
        {
            var ok = NightlyHealthNotifications.HasReceipt(new JsonObject { ["notificationId"] = "n1", ["nativeRunId"] = "r1" }, []);
            rows.Check(!ok, $"G120 {kind} not receipt");
        }
        var payload = NightlyHealthNotifications.Payload(new JsonObject
        {
            ["failureKind"] = "incomplete-coverage", ["nativeRunId"] = "run-1", ["sha"] = "sha-1", ["notificationId"] = "nid-9",
        }, "operator-1");
        rows.Check(HealthJson.Str(payload["notificationId"]) == "nid-9" && Regex.IsMatch(HealthJson.Str(payload["text"]), "notificationId=nid-9", RegexOptions.IgnoreCase),
            "G120 payload carries notification id");
        rows.ShouldPass(3);
    }

    [Test]
    public void C487_G121()
    {
        var rows = new Rows();
        foreach (var kind in new[] { "wrong-run", "wrong-id" })
        {
            var ok = NightlyHealthNotifications.HasReceipt(new JsonObject { ["notificationId"] = "n1", ["nativeRunId"] = "r1" },
                [new JsonObject { ["notificationId"] = "n2", ["nativeRunId"] = "r2" }]);
            rows.Check(!ok, $"G121 {kind}");
        }
        rows.ShouldPass(2);
    }

    [Test]
    public void C487_G122()
    {
        using var results = NightlyFixture.Create("nightly-health");
        var rows = new Rows();
        foreach (var kind in new[] { "busy", "eligible" })
        {
            var store = results.PathOf("nstore2-" + Guid.NewGuid().ToString("N") + ".json");
            NightlyHealthNotifications.AddEvent(store, Notification("x", "same"));
            var again = NightlyHealthNotifications.AddEvent(store, Notification("x", "same"));
            rows.Check(again.Deduped, $"G122 {kind} same identity");
        }
        var fx = HealthFx.Create(results);
        var first = fx.Invoke();
        var storePath = NightlyHealthNotifications.StorePath(fx.Root);
        var e1 = NightlyHealthNotifications.ReadEvents(storePath)[0];
        var id1 = HealthJson.Str(e1["notificationId"]);
        var run1 = HealthJson.Str(e1["nativeRunId"]);
        File.WriteAllText(fx.Inbox, new JsonObject { ["notificationId"] = id1, ["nativeRunId"] = run1 }.ToJsonString(), new UTF8Encoding(true));
        var second = fx.Invoke();
        var events2 = NightlyHealthNotifications.ReadEvents(storePath);
        var id2 = HealthJson.Str(events2[0]["notificationId"]);
        var receiptPath = NightlyHealthNotifications.ReceiptPath(fx.Root);
        var settled = File.Exists(receiptPath) && NightlyHealthNotifications.ReadReceipts(receiptPath)
            .Any(row => HealthJson.Str(HealthJson.Field(row, "notificationId")) == id1 && HealthJson.Str(HealthJson.Field(row, "nativeRunId")) == run1);
        rows.Check(events2.Count == 1 && id1 == id2 && id1.Length > 0, "G122 entry preserves pending identity", $"n={events2.Count} id1={id1} id2={id2}");
        rows.Check(!first.Receipt && second.Receipt && settled, "G122 entry receipt of first settles second", $"r1={first.Receipt} r2={second.Receipt}");
        rows.ShouldPass(4);
    }

    [Test]
    public void C487_G123()
    {
        using var results = NightlyFixture.Create("nightly-health");
        var rows = new Rows();
        foreach (var kind in new[] { "same", "distinct" })
        {
            var store = results.PathOf("nstore3-" + Guid.NewGuid().ToString("N") + ".json");
            NightlyHealthNotifications.AddEvent(store, Notification("a", "n1"));
            var second = NightlyHealthNotifications.AddEvent(store, Notification(kind == "same" ? "a" : "b", kind == "same" ? "n1" : "n2"));
            if (kind == "same") rows.Check(second.Deduped, "G123 same not spam");
            else rows.Check(!second.Deduped, "G123 distinct retained");
        }
        var fx = HealthFx.Create(results);
        fx.Invoke();
        fx.Invoke();
        var events = NightlyHealthNotifications.ReadEvents(NightlyHealthNotifications.StorePath(fx.Root));
        var enqueueLines = File.Exists(fx.EnqueueLog) ? File.ReadAllLines(fx.EnqueueLog).Where(line => line.Length > 0).ToArray() : [];
        var accepted = events.Count > 0 && NightlyHealthNotifications.TransportAccepted(events[0]);
        rows.Check(events.Count == 1, "G123 entry same poll not spam", "n=" + events.Count);
        rows.Check(enqueueLines.Length == 1 && accepted, "G123 entry same poll single enqueue", $"enq={enqueueLines.Length} accepted={accepted}");
        rows.ShouldPass(4);
    }

    [Test]
    public void C487_G124() => FlagCase("G124 incomplete remains visible", coverage: false);

    [Test]
    public void C487_G125()
    {
        var rows = new Rows();
        var ok = NightlyHealthNotifications.HasReceipt(new JsonObject { ["notificationId"] = "n1", ["nativeRunId"] = "r1" }, null!);
        rows.Check(!ok, "G125 no receipt before destination");
        rows.ShouldPass(1);
    }

    /// <summary>
    /// The harness ran this with no seams, so the production Windmill API came from the machine's WINDMILL_* settings.
    /// Here the seams are production's with no Windmill credentials: nothing reads the machine or the network.
    /// </summary>
    [Test]
    public void C487_G126()
    {
        using var results = NightlyFixture.Create("nightly-health");
        var rows = new Rows();
        foreach (var kind in new[] { "unset", "override" })
        {
            var h = NightlyHealth.Run(new NightlyHealthOptions { StateRoot = results.Root, AuthorizedDestination = "" },
                Seams(DateTimeOffset.UtcNow, api: null));
            var persisted = File.Exists(NightlyHealth.MonitorResultPath(results.Root));
            var noSilent = h.Health.Reasons.Contains("unauthorized-destination") && h.Notification == null;
            rows.Check(persisted && noSilent, $"G126 {kind} no silent dest", Reasons(h.Health));
        }
        rows.ShouldPass(2);
    }

    // ---- CARD-0544 D-6: daily validity, scheduled identity, London dates and the production job adapter ----

    private static DateTime LondonUtc(string local) => TimeZoneInfo.ConvertTimeToUtc(
        DateTime.SpecifyKind(DateTime.ParseExact(local, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture), DateTimeKind.Unspecified), NightlyLondon.TimeZone);

    private static JsonObject C544Run(string runId, string completedLocal, bool coverage = true, bool tests = true, bool report = true, string trigger = "scheduled") => State(new
    {
        runId, sha = "sha-" + runId, @ref = "master", trigger, phase = "final", policyHash = "p",
        coverageComplete = coverage, testsPassed = tests, reportDelivered = report,
        completedAt = LondonUtc(completedLocal).ToString("o"), lastActivityAt = LondonUtc(completedLocal).ToString("o"),
    });

    private static JsonObject C544Started(string runId, string activityLocal) => State(new
    {
        runId, sha = "", @ref = "master", trigger = "scheduled", phase = "Started", policyHash = "p",
        coverageComplete = false, testsPassed = false, reportDelivered = false, lastActivityAt = LondonUtc(activityLocal).ToString("o"),
    });

    private static NightlyWindmillJob C544Job(string date, string runId, string status = "success", bool scheduled = true) => new()
    {
        Id = "job-" + date + "-" + runId, Status = status, Scheduled = scheduled, NativeRunId = runId, Sha = runId.Length > 0 ? "sha-" + runId : "",
        LocalDueDate = date, ScheduledFor = NightlyLondon.DueUtc(NightlyLondon.ParseDate(date)!.Value).ToString("o"),
    };

    private static NightlyHealthVerdict C544Health(IReadOnlyList<NightlyWindmillJob> jobs, JsonNode? native, string nowLocal, JsonNode? green = null) =>
        NightlyHealth.Evaluate(Registration(hash: "a"), Schedule(tag: "desktop"), jobs, native, LondonUtc(nowLocal), green, "a", "p");

    private static void C544Ready(Rows rows, NightlyHealthVerdict health, bool ready, string name) =>
        rows.Check(health.ReadyForDeferral == ready && !health.TerminateRequested, name,
            $"healthy={health.Healthy} ready={health.ReadyForDeferral} pending={health.Pending} reasons={Reasons(health)}");

    // PC-72: validity is the due day's scheduled green, not completedAt age.
    [Test]
    public void C544_DailyValidity()
    {
        var rows = new Rows();
        var green = C544Run("r16", "2026-09-16 02:00:00");
        var job = C544Job("2026-09-16", "r16");
        var h = C544Health([job], green, "2026-09-16 10:00:00");
        C544Ready(rows, h, true, "C544 DailyValidity green completed eight hours ago is ready");
        rows.Check(h.GreenRunId == "r16" && h.GreenJobNativeRunId == "r16" && h.GreenJobId == job.Id,
            "C544 DailyValidity readiness names the scheduled run and job", h.GreenRunId + "/" + h.GreenJobId);
        C544Ready(rows, C544Health([job], green, "2026-09-16 23:59:59"), true, "C544 DailyValidity 23:59:59 same due day stays ready");
        C544Ready(rows, C544Health([job], null, "2026-09-16 10:00:00", green), true, "C544 DailyValidity last-complete-green file carries the green");
        C544Ready(rows, C544Health([job], null, "2026-09-16 10:00:00"), false, "C544 DailyValidity missing native state is unready");
        C544Ready(rows, C544Health([C544Job("2026-09-15", "r15")], C544Run("r15", "2026-09-15 02:00:00"), "2026-09-16 10:00:00"), false,
            "C544 DailyValidity yesterday-only after the deadline is unready");
        rows.ShouldPass(6, "C544 DailyValidity green completed eight hours ago is ready", "C544 DailyValidity yesterday-only after the deadline is unready");
    }

    // PC-73: the previous due day's green bridges before 08:00 London only.
    [Test]
    public void C544_MorningBoundary()
    {
        var rows = new Rows();
        var green = C544Run("r16", "2026-09-16 02:00:00");
        var yesterday = C544Job("2026-09-16", "r16");
        foreach (var now in new[] { "2026-09-17 00:00:00", "2026-09-17 00:29:59", "2026-09-17 00:30:00", "2026-09-17 00:59:59" })
            C544Ready(rows, C544Health([yesterday], green, now), true, $"C544 MorningBoundary bridge before start grace {now}");
        var running = C544Job("2026-09-17", "", "running");
        foreach (var now in new[] { "2026-09-17 01:00:00", "2026-09-17 07:59:59" })
            C544Ready(rows, C544Health([running, yesterday], green, now), true, $"C544 MorningBoundary bridge while today runs {now}");
        foreach (var now in new[] { "2026-09-17 08:00:00", "2026-09-17 08:00:01" })
            C544Ready(rows, C544Health([running, yesterday], green, now), false, $"C544 MorningBoundary no bridge at or after deadline {now}");
        var today = C544Run("r17", "2026-09-17 02:10:00");
        C544Ready(rows, C544Health([C544Job("2026-09-17", "r17"), yesterday], today, "2026-09-17 08:00:00"), true,
            "C544 MorningBoundary today green at the deadline is ready");
        rows.ShouldPass(9, "C544 MorningBoundary bridge while today runs 2026-09-17 07:59:59",
            "C544 MorningBoundary no bridge at or after deadline 2026-09-17 08:00:00", "C544 MorningBoundary today green at the deadline is ready");
    }

    // PC-74: a newer failed or incomplete completed attempt removes the bridge immediately.
    [Test]
    public void C544_NewerFailure()
    {
        var rows = new Rows();
        var green = C544Run("r16", "2026-09-16 02:00:00");
        var yesterday = C544Job("2026-09-16", "r16");
        var control = C544Health([C544Job("2026-09-17", "", "running"), yesterday], C544Started("r17", "2026-09-17 04:50:00"), "2026-09-17 05:00:00", green);
        C544Ready(rows, control, true, "C544 NewerFailure control: in-progress run keeps the bridge");
        foreach (var kind in new[] { "red", "incomplete", "no-report" })
        {
            var attempt = C544Run("r17", "2026-09-17 04:00:00", coverage: kind != "incomplete", tests: kind != "red", report: kind != "no-report");
            var h = C544Health([C544Job("2026-09-17", "r17", "failed"), yesterday], attempt, "2026-09-17 05:00:00", green);
            C544Ready(rows, h, false, $"C544 NewerFailure newer {kind} attempt revokes the bridge");
        }
        var jobOnly = C544Health([C544Job("2026-09-17", "", "failed"), yesterday], green, "2026-09-17 05:00:00");
        C544Ready(rows, jobOnly, false, "C544 NewerFailure failed scheduled job revokes even without native state");
        rows.Check(jobOnly.Reasons.Contains("newer-failed-attempt"), "C544 NewerFailure reason names the newer failure", Reasons(jobOnly));
        rows.ShouldPass(6, "C544 NewerFailure control: in-progress run keeps the bridge", "C544 NewerFailure newer red attempt revokes the bridge",
            "C544 NewerFailure newer incomplete attempt revokes the bridge", "C544 NewerFailure failed scheduled job revokes even without native state");
    }

    // PC-75: a manual or partial run cannot be the due day's scheduled green.
    [Test]
    public void C544_ScheduledIdentity()
    {
        var rows = new Rows();
        var manualRun = C544Run("m17", "2026-09-17 03:00:00", trigger: "manual");
        var manualJob = C544Job("2026-09-17", "m17", scheduled: false);
        var h = C544Health([manualJob], manualRun, "2026-09-17 10:00:00");
        C544Ready(rows, h, false, "C544 ScheduledIdentity manual green after deadline is unready");
        rows.Check(h.GreenRunId == "", "C544 ScheduledIdentity manual run is never the scheduled run id", h.GreenRunId);
        C544Ready(rows, C544Health([manualJob], manualRun, "2026-09-17 00:45:00"), false, "C544 ScheduledIdentity manual green cannot bridge");
        C544Ready(rows, C544Health([C544Job("2026-09-17", "m17")], manualRun, "2026-09-17 10:00:00"), false, "C544 ScheduledIdentity native trigger must be scheduled");
        C544Ready(rows, C544Health([C544Job("2026-09-17", "r17")], C544Run("r17", "2026-09-17 03:00:00", coverage: false), "2026-09-17 10:00:00"), false,
            "C544 ScheduledIdentity partial scheduled run is unready");
        C544Ready(rows, C544Health([C544Job("2026-09-17", "r17")], C544Run("r99", "2026-09-17 03:00:00"), "2026-09-17 10:00:00"), false,
            "C544 ScheduledIdentity job and native run ids must match");
        C544Ready(rows, C544Health([C544Job("2026-09-17", "r17")], C544Run("r17", "2026-09-17 03:00:00"), "2026-09-17 10:00:00"), true,
            "C544 ScheduledIdentity control scheduled green is ready");
        rows.ShouldPass(7, "C544 ScheduledIdentity manual green after deadline is unready", "C544 ScheduledIdentity partial scheduled run is unready",
            "C544 ScheduledIdentity control scheduled green is ready");
    }

    // PC-76: exact UTC due instants across both 2026 DST transitions and London midnight.
    [Test]
    public void C544_LondonDates()
    {
        var rows = new Rows();
        static DateTime Utc(string text) => DateTime.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal);
        foreach (var (date, expected) in new[]
                 {
                     ("2026-03-28", "2026-03-28T00:30:00Z"), ("2026-03-29", "2026-03-29T00:30:00Z"), ("2026-03-30", "2026-03-29T23:30:00Z"),
                     ("2026-10-24", "2026-10-23T23:30:00Z"), ("2026-10-25", "2026-10-24T23:30:00Z"), ("2026-10-26", "2026-10-26T00:30:00Z"),
                 })
        {
            var actual = NightlyLondon.DueUtc(NightlyLondon.ParseDate(date)!.Value);
            rows.Check(actual == Utc(expected), $"C544 LondonDates due {date}", actual.ToString("o") + " expected " + Utc(expected).ToString("o"));
        }
        foreach (var (date, expected) in new[] { ("2026-03-29", "2026-03-29T07:00:00Z"), ("2026-10-25", "2026-10-25T08:00:00Z"), ("2026-09-17", "2026-09-17T07:00:00Z") })
        {
            var actual = NightlyLondon.MorningDeadlineUtc(NightlyLondon.ParseDate(date)!.Value);
            rows.Check(actual == Utc(expected), $"C544 LondonDates morning deadline {date}", actual.ToString("o"));
        }
        foreach (var (utc, expected) in new[]
                 {
                     ("2026-06-30T23:00:00Z", "2026-07-01"), ("2026-06-30T22:59:59Z", "2026-06-30"), ("2026-12-31T23:59:59Z", "2026-12-31"),
                     ("2027-01-01T00:00:00Z", "2027-01-01"),
                 })
        {
            var local = NightlyLondon.ToLocal(Utc(utc));
            rows.Check(NightlyLondon.DateText(local) == expected, $"C544 LondonDates midnight {utc}", local.ToString("o"));
        }
        // The evaluator uses the London due date: after the spring change 00:45 BST is still before grace.
        var green = C544Run("r29", "2026-03-29 02:30:00");
        var prev = C544Job("2026-03-29", "r29");
        C544Ready(rows, C544Health([prev], green, "2026-03-30 00:45:00"), true, "C544 LondonDates 00:45 BST after the change is within start grace");
        var overdue = C544Health([prev], green, "2026-03-30 01:00:00");
        rows.Check(!overdue.Healthy && overdue.Reasons.Contains("overdue-start"), "C544 LondonDates 01:00 BST is overdue", Reasons(overdue));
        rows.ShouldPass(15, "C544 LondonDates due 2026-03-30", "C544 LondonDates due 2026-10-25", "C544 LondonDates morning deadline 2026-03-29",
            "C544 LondonDates 01:00 BST is overdue");
    }

    // PC-87: start grace equality is overdue, reusing yesterday's green or not, and nothing is terminated.
    [Test]
    public void C544_ScheduleHealth()
    {
        var rows = new Rows();
        var green = C544Run("r16", "2026-09-16 02:00:00");
        var yesterday = C544Job("2026-09-16", "r16");
        C544Ready(rows, C544Health([yesterday], green, "2026-09-17 00:59:59"), true, "C544 ScheduleHealth one second before grace is pending");
        foreach (var kind in new[] { "missing", "queued" })
        {
            var jobs = new List<NightlyWindmillJob> { yesterday };
            if (kind == "queued") jobs.Add(C544Job("2026-09-17", "", "queued"));
            var h = C544Health(jobs, green, "2026-09-17 01:00:00");
            C544Ready(rows, h, false, $"C544 ScheduleHealth {kind} at grace equality is unready");
            rows.Check(!h.Healthy && h.Reasons.Contains("overdue-start"), $"C544 ScheduleHealth {kind} overdue-start", Reasons(h));
        }
        var stalled = C544Health([C544Job("2026-09-17", "", "running"), yesterday], C544Started("r17", "2026-09-17 03:58:59"), "2026-09-17 05:00:00", green);
        C544Ready(rows, stalled, false, "C544 ScheduleHealth stalled progress is unready");
        rows.Check(stalled.Reasons.Contains("stale-progress") && !stalled.TerminateRequested, "C544 ScheduleHealth stalled run is not terminated", Reasons(stalled));
        rows.ShouldPass(7, "C544 ScheduleHealth missing at grace equality is unready", "C544 ScheduleHealth queued overdue-start",
            "C544 ScheduleHealth stalled run is not terminated");
    }

    private static void C544FlagRequired(string flag, string label)
    {
        var rows = new Rows();
        C544Ready(rows, C544Health([C544Job("2026-09-17", "r17")], C544Run("r17", "2026-09-17 03:00:00"), "2026-09-17 10:00:00"), true,
            $"C544 {label} control otherwise-valid scheduled row is ready");
        var run = C544Run("r17", "2026-09-17 03:00:00", coverage: flag != "coverageComplete", tests: flag != "testsPassed", report: flag != "reportDelivered");
        C544Ready(rows, C544Health([C544Job("2026-09-17", "r17")], run, "2026-09-17 10:00:00"), false, $"C544 {label} {flag}=false is unready");
        C544Ready(rows, C544Health([C544Job("2026-09-17", "r17")], null, "2026-09-17 10:00:00", run), false, $"C544 {label} {flag}=false in the green file is unready");
        C544Ready(rows, C544Health([C544Job("2026-09-16", "r17")], null, "2026-09-17 05:00:00", run), false, $"C544 {label} {flag}=false cannot bridge");
        rows.ShouldPass(4, $"C544 {label} {flag}=false is unready");
    }

    // PC-92/93/94
    [Test]
    public void C544_CoverageRequired() => C544FlagRequired("coverageComplete", "CoverageRequired");

    [Test]
    public void C544_GreenRequired() => C544FlagRequired("testsPassed", "GreenRequired");

    [Test]
    public void C544_ReportReceiptRequired() => C544FlagRequired("reportDelivered", "ReportReceiptRequired");

    private static NightlyWindmillApi ProductionApi(LoopbackHttpStub stub) =>
        NightlyHealthWindmill.Production(new NightlyWindmillConfig(stub.BaseUrl, "c544-fixture-token", "mc", Sched), new StringWriter())!;

    private static JsonObject ListRow(string id, string type, object? schedulePath = null, string? scheduledFor = null, JsonNode? success = null, JsonNode? running = null)
    {
        var row = new JsonObject { ["id"] = id, ["type"] = type };
        if (success != null) row["success"] = success;
        if (running != null) row["running"] = running;
        row["schedule_path"] = schedulePath is string path ? path : null;
        if (scheduledFor != null) row["scheduled_for"] = scheduledFor;
        return row;
    }

    private static string LastSegment(string requestLine) => requestLine.Split(' ')[1].Split('?')[0].Split('/').Last();

    // PC-77: the production jobs/list adapter over real HTTP against an owned loopback Windmill stub.
    [Test]
    public void C544_ProductionJobAdapter()
    {
        var rows = new Rows();
        var today = "2026-09-17";
        var due = NightlyLondon.DueUtc(NightlyLondon.ParseDate(today)!.Value).ToString("o");
        var list = new JsonArray
        {
            ListRow("j-queued", "QueuedJob", Sched, due, running: false),
            ListRow("j-running", "QueuedJob", Sched, due, running: true),
            ListRow("j-success", "CompletedJob", Sched, success: true),
            ListRow("j-failed", "CompletedJob", Sched, success: false),
            ListRow("j-missing-result", "CompletedJob", Sched, success: true),
            ListRow("j-string-bool", "CompletedJob", Sched, success: "true"),
            ListRow("j-manual", "CompletedJob", null, success: true),
            ListRow("j-unknown", "SomethingElse", Sched, success: true),
        };
        var valid = new JsonObject { ["nativeRunId"] = "r17", ["sha"] = "sha-r17", ["localDueDate"] = today }.ToJsonString();
        IReadOnlyList<NightlyWindmillJob> jobs;
        IReadOnlyList<string> requestLines, request;
        using (var stub = LoopbackHttpStub.Start(("jobs/list", list.ToJsonString()), ("get_result/j-success", valid), ("get_result/j-failed", """{"error":"exit 1"}"""),
                   ("get_result/j-missing-result", "null"), ("get_result/j-string-bool", valid), ("get_result/j-manual", valid), ("get_result/j-unknown", valid)))
        {
            jobs = ProductionApi(stub).GetJobs();
            requestLines = stub.Stop();
            request = stub.FirstRequest;
        }
        rows.Check(request.Count > 0 && request[0].StartsWith("GET /api/w/mc/jobs/list?script_path_exact=u%2Flndcobra%2Fantiphon_nightly_tests", StringComparison.OrdinalIgnoreCase)
                && request.Count(line => line == "Authorization: Bearer c544-fixture-token") == 1,
            "C544 ProductionJobAdapter real HTTP request shape", string.Join(" | ", request));
        var fetchedIds = requestLines.Skip(1).Select(LastSegment).ToArray();
        rows.Check(requestLines.Count == 5 && string.Join(",", fetchedIds) == "j-success,j-failed,j-missing-result,j-string-bool",
            "C544 ProductionJobAdapter get_result requests bounded to scheduled completed rows", string.Join(" | ", requestLines));
        var byId = jobs.ToDictionary(job => job.Id);
        foreach (var (id, status) in new[]
                 {
                     ("j-queued", "queued"), ("j-running", "running"), ("j-success", "success"), ("j-failed", "failed"), ("j-missing-result", "unknown"),
                     ("j-string-bool", "unknown"), ("j-manual", "unknown"), ("j-unknown", "unknown"),
                 })
            rows.Check(byId.TryGetValue(id, out var job) && job.Status == status, $"C544 ProductionJobAdapter status {id}", byId.GetValueOrDefault(id)?.Status ?? "");
        rows.Check(byId["j-manual"].Scheduled == false && byId["j-success"].Scheduled == true, "C544 ProductionJobAdapter schedule_path decides scheduled");

        var native = C544Run("r17", "2026-09-17 03:00:00");
        foreach (var id in new[] { "j-queued", "j-running", "j-failed", "j-missing-result", "j-string-bool", "j-manual", "j-unknown" })
            C544Ready(rows, C544Health([byId[id]], native, "2026-09-17 10:00:00"), false, $"C544 ProductionJobAdapter {id} never qualifies as scheduled green");
        var success = byId["j-success"];
        foreach (var (name, job) in new[]
                 {
                     ("crossed-due-day", success with { LocalDueDate = "2026-09-16", ScheduledFor = null }),
                     ("crossed-run", success with { NativeRunId = "r16" }),
                     ("crossed-sha", success with { Sha = "sha-other" }),
                 })
            C544Ready(rows, C544Health([job], native, "2026-09-17 10:00:00"), false, $"C544 ProductionJobAdapter {name} never qualifies");
        C544Ready(rows, C544Health([success], native, "2026-09-17 10:00:00"), true, "C544 ProductionJobAdapter control success row with matching native run is ready");
        rows.ShouldPass(22, "C544 ProductionJobAdapter real HTTP request shape", "C544 ProductionJobAdapter get_result requests bounded to scheduled completed rows",
            "C544 ProductionJobAdapter status j-running", "C544 ProductionJobAdapter status j-missing-result",
            "C544 ProductionJobAdapter j-running never qualifies as scheduled green", "C544 ProductionJobAdapter crossed-due-day never qualifies",
            "C544 ProductionJobAdapter control success row with matching native run is ready");
    }

    // ---- CARD-0545 D-10: per-row result fetch, bounded to five, never upgrading a missing result ----

    [Test]
    public void C545_JobResultFetch()
    {
        var rows = new Rows();
        var list = new JsonArray();
        for (var n = 1; n <= 7; n++) list.Add(ListRow("c" + n, "CompletedJob", Sched, success: true));
        const string valid = """{"nativeRunId":"r18","sha":"s","localDueDate":"2026-09-18"}""";
        IReadOnlyList<NightlyWindmillJob> jobs;
        IReadOnlyList<string> requestLines;
        using (var stub = LoopbackHttpStub.Start(("jobs/list", list.ToJsonString()), ("get_result/c1", valid), ("get_result/c2", valid), ("get_result/c3", 500),
                   ("get_result/c4", valid), ("get_result/c5", valid), ("get_result/c6", valid), ("get_result/c7", valid)))
        {
            jobs = ProductionApi(stub).GetJobs();
            requestLines = stub.Stop();
        }
        var paths = requestLines.Select(line => line.Split(' ')[1].Split('?')[0]).ToArray();
        var expectedPaths = new[] { "/api/w/mc/jobs/list" }.Concat(Enumerable.Range(1, 5).Select(n => "/api/w/mc/jobs_u/completed/get_result/c" + n)).ToArray();
        rows.Check(paths.SequenceEqual(expectedPaths), "C545 JobResultFetch requests are list then c1..c5 in order", string.Join(" | ", requestLines));
        var byId = jobs.ToDictionary(job => job.Id);
        foreach (var id in new[] { "c1", "c2", "c4", "c5" })
            rows.Check(byId[id].Status == "success" && byId[id].NativeRunId == "r18", $"C545 JobResultFetch {id} fetched result is success", byId[id].Status);
        rows.Check(byId["c3"].Status == "unknown", "C545 JobResultFetch c3 unfetchable is unknown", byId["c3"].Status);
        rows.Check(byId["c6"].Status == "unknown", "C545 JobResultFetch c6 stays unknown beyond the cap", byId["c6"].Status);
        rows.Check(byId["c7"].Status == "unknown", "C545 JobResultFetch c7 stays unknown beyond the cap", byId["c7"].Status);
        var expectedDue = DateTime.Parse("2026-09-17T23:30:00Z", CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal);
        var c1Due = HealthJson.CastDateTime(byId["c1"].ScheduledFor)?.ToUniversalTime();
        rows.Check(c1Due == expectedDue, "C545 JobResultFetch c1 scheduledFor is 00:30 London of localDueDate", byId["c1"].ScheduledFor ?? "");

        var odd = new JsonArray { ListRow("n-null", "CompletedJob", Sched, success: true), ListRow("n-nodue", "CompletedJob", Sched, success: true) };
        IReadOnlyList<NightlyWindmillJob> oddJobs;
        using (var stub = LoopbackHttpStub.Start(("jobs/list", odd.ToJsonString()), ("get_result/n-null", "null"), ("get_result/n-nodue", """{"nativeRunId":"r18","sha":"s"}""")))
        {
            oddJobs = ProductionApi(stub).GetJobs();
        }
        var oddById = oddJobs.ToDictionary(job => job.Id);
        rows.Check(oddById["n-null"].Status == "unknown", "C545 JobResultFetch null result is unknown", oddById["n-null"].Status);
        rows.Check(oddById["n-nodue"].Status == "unknown", "C545 JobResultFetch result without localDueDate is unknown", oddById["n-nodue"].Status);

        var c3 = byId["c3"] with { LocalDueDate = "2026-09-18" };
        C544Ready(rows, C544Health([c3], C544Run("r18", "2026-09-18 03:00:00"), "2026-09-18 10:00:00"), false, "C545 JobResultFetch c3 alone is unready");

        var api = NightlyHealthWindmill.Production(new NightlyWindmillConfig("http://127.0.0.1:9", "t", "mc", Sched), new StringWriter());
        var members = typeof(NightlyWindmillApi).GetProperties().Select(property => property.Name).ToArray();
        rows.Check(api != null && !members.Contains("EnqueueNotification") && typeof(NightlyWindmillConfig).GetProperty("NotifyPath") == null,
            "C545 JobResultFetch no production Windmill notification sink", string.Join(",", members));
        rows.ShouldPass(13, "C545 JobResultFetch c6 stays unknown beyond the cap", "C545 JobResultFetch c3 unfetchable is unknown",
            "C545 JobResultFetch no production Windmill notification sink", "C545 JobResultFetch requests are list then c1..c5 in order");
    }

    // ---- CARD-0545 D-9: the watchdog snapshot folds into the local evaluator ----

    private static readonly DateTime C545Now = DateTime.Parse("2026-09-17T09:00:00Z", CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal);

    private static string C545SnapshotText(TimeSpan age = default, string ns = "mc", string instanceId = "wd-1", int schemaVersion = 1,
        JsonArray? openOutages = null, bool noHeartbeat = false)
    {
        var snapshot = new JsonObject
        {
            ["schemaVersion"] = schemaVersion, ["instanceId"] = instanceId, ["version"] = "1.0.0", ["configHash"] = "cfg", ["namespace"] = ns,
        };
        if (!noHeartbeat) snapshot["heartbeatAt"] = (C545Now - age).ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);
        snapshot["tickSeconds"] = 600;
        snapshot["openOutages"] = openOutages ?? new JsonArray();
        snapshot["recentNotifications"] = new JsonArray();
        return snapshot.ToJsonString();
    }

    private sealed record C545Fx(string Root, Func<string, string>? Snapshot);

    /// <summary>
    /// New-C545WatchdogFx: an otherwise-green evaluator fixture (scheduled green for 2026-09-17 at 10:00 London) whose
    /// only variable input is the watchdog snapshot seam: raw text, a throw, or no seam (the production HTTP read).
    /// </summary>
    private static C545Fx C545WatchdogFx(NightlyFixture results, string snapshotText = "", bool throws = false, bool noSeam = false)
    {
        var root = results.PathOf("c545-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        NightlyFixture.WriteJson(Path.Combine(root, "last-run.json"), C544Run("r17", "2026-09-17 03:00:00"));
        var snapshotFile = Path.Combine(root, "snapshot.txt");
        File.WriteAllText(snapshotFile, snapshotText);
        Func<string, string>? seam = throws ? _ => throw new HttpRequestException("connection refused")
            : noSeam ? null : _ => File.ReadAllText(snapshotFile);
        return new C545Fx(root, seam);
    }

    private static NightlyHealthSeams C545Seams(C545Fx fx) => Seams(new DateTimeOffset(C545Now),
        Api(new NightlyWindmillRegistration(true, "a", "desktop", ""), Schedule(tag: "desktop"), C544Job("2026-09-17", "r17")),
        watchdog: fx.Snapshot ?? NightlyHealth.HttpSnapshot);

    private static NightlyHealthResult C545WatchdogHealth(C545Fx fx, string url = "http://127.0.0.1:17290/snapshot.json", string instanceId = "wd-1",
        string readinessConfigPath = "") =>
        NightlyHealth.Run(readinessConfigPath.Length > 0
            ? new NightlyHealthOptions { StateRoot = fx.Root, RepositoryPath = @"C:\src\Antiphon", ReadinessConfigPath = readinessConfigPath }
            : new NightlyHealthOptions
            {
                StateRoot = fx.Root, RepositoryPath = @"C:\src\Antiphon", ExpectedScriptHash = "a", ExpectedPolicyHash = "p",
                WatchdogSnapshotUrl = url, ExpectedWatchdogInstanceId = instanceId,
            }, C545Seams(fx));

    private static NightlyWatchdogFetch Fetched(string text)
    {
        HealthJson.TryParse(text, out var snapshot);
        return new NightlyWatchdogFetch(true, "", snapshot, "");
    }

    [Test]
    public void C545_WatchdogFresh()
    {
        using var results = NightlyFixture.Create("nightly-health");
        var rows = new Rows();
        foreach (var (label, age) in new[] { ("0:00", TimeSpan.Zero), ("19:59", TimeSpan.FromSeconds(1199)), ("20:00", TimeSpan.FromMinutes(20)) })
        {
            var f = NightlyHealth.WatchdogFreshness(Fetched(C545SnapshotText(age)), C545Now, "mc", "wd-1");
            rows.Check(f.Fresh && f.Reasons.Count == 0, $"C545 WatchdogFresh age {label} fresh", string.Join(",", f.Reasons));
        }

        NightlyWatchdogFetch fetched;
        IReadOnlyList<string> lines;
        using (var stub = LoopbackHttpStub.Start(("snapshot.json", C545SnapshotText())))
        {
            fetched = NightlyHealth.FetchWatchdogSnapshot(stub.BaseUrl + "/snapshot.json", NightlyHealth.HttpSnapshot);
            lines = stub.Stop();
        }
        rows.Check(fetched.Reachable && HealthJson.Str(HealthJson.Field(fetched.Snapshot, "instanceId")) == "wd-1" && lines.Count == 1 && lines[0].StartsWith("GET /snapshot.json", StringComparison.Ordinal),
            "C545 WatchdogFresh real HTTP snapshot read", string.Join(" | ", lines) + " " + fetched.Error);

        var fx = C545WatchdogFx(results, C545SnapshotText(TimeSpan.FromMinutes(5)));
        var h = C545WatchdogHealth(fx);
        var expectedBeat = C545Now - TimeSpan.FromMinutes(5);
        DateTime? beat = h.Identity.WatchdogHeartbeatAt.Length > 0
            ? DateTime.Parse(h.Identity.WatchdogHeartbeatAt, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal) : null;
        rows.Check(h.Health.Healthy && h.Health.ReadyForDeferral, "C545 WatchdogFresh evaluator healthy with a fresh watchdog", Reasons(h.Health));
        rows.Check(h.Identity.WatchdogInstanceId == "wd-1" && beat == expectedBeat, "C545 WatchdogFresh monitor identity carries instance and heartbeat",
            h.Identity.WatchdogInstanceId + " " + h.Identity.WatchdogHeartbeatAt);
        HealthJson.TryParse(File.ReadAllText(NightlyHealth.MonitorResultPath(fx.Root)), out var saved);
        rows.Check(HealthJson.Str(HealthJson.Field(HealthJson.Field(saved, "Identity"), "WatchdogInstanceId")) == "wd-1"
                && HealthJson.IsTrue(HealthJson.Field(HealthJson.Field(saved, "Health"), "Healthy")),
            "C545 WatchdogFresh last-monitor.json records the watchdog identity");

        var fx2 = C545WatchdogFx(results, C545SnapshotText(TimeSpan.FromMinutes(1)));
        var config = Path.Combine(fx2.Root, "readiness-config.json");
        NightlyFixture.WriteJson(config, new
        {
            expectedScriptHash = "a", expectedPolicyHash = "p", watchdogSnapshotUrl = "http://127.0.0.1:17290/snapshot.json", watchdogInstanceId = "wd-1",
            repositoryPath = @"C:\Antiphon\qualified-repo", projectId = "c5450000-0000-4000-8000-000000000001",
        });
        var viaConfig = NightlyHealth.Run(new NightlyHealthOptions { StateRoot = fx2.Root, ReadinessConfigPath = config }, C545Seams(fx2));
        var id = viaConfig.Identity;
        rows.Check(viaConfig.Health.Healthy && id.ScriptHash == "a" && id.PolicyHash == "p" && id.WatchdogInstanceId == "wd-1"
                && id.RepositoryPath == @"C:\Antiphon\qualified-repo" && id.ProjectId == "c5450000-0000-4000-8000-000000000001",
            "C545 WatchdogFresh readiness-config supplies every value", Reasons(viaConfig.Health) + " " + id);
        rows.ShouldPass(8, "C545 WatchdogFresh age 20:00 fresh", "C545 WatchdogFresh real HTTP snapshot read", "C545 WatchdogFresh evaluator healthy with a fresh watchdog",
            "C545 WatchdogFresh readiness-config supplies every value");
    }

    private static void C545WatchdogUnhealthy(Rows rows, NightlyHealthResult health, string root, string reason, string name)
    {
        var monitor = File.Exists(NightlyHealth.MonitorResultPath(root));
        rows.Check(!health.Health.Healthy && !health.Health.ReadyForDeferral && health.Health.Reasons.Contains(reason) && monitor, name,
            $"healthy={health.Health.Healthy} reasons={Reasons(health.Health)} monitor={monitor}");
    }

    [Test]
    public void C545_WatchdogStale()
    {
        using var results = NightlyFixture.Create("nightly-health");
        var rows = new Rows();
        var outage = new JsonArray { new JsonObject { ["outageId"] = "nw:mc:2026-09-17:windows-hop-failed:1", ["kind"] = "windows-hop-failed" } };
        foreach (var (name, reason, text) in new[]
                 {
                     ("C545 WatchdogStale age 20:01 stale", "watchdog-stale", C545SnapshotText(TimeSpan.FromSeconds(1201))),
                     ("C545 WatchdogStale future heartbeat malformed", "watchdog-malformed", C545SnapshotText(TimeSpan.FromSeconds(-1))),
                     ("C545 WatchdogStale malformed not-json", "watchdog-malformed", "this is not json"),
                     ("C545 WatchdogStale malformed missing-heartbeat", "watchdog-malformed", C545SnapshotText(noHeartbeat: true)),
                     ("C545 WatchdogStale malformed schema-2", "watchdog-malformed", C545SnapshotText(schemaVersion: 2)),
                     ("C545 WatchdogStale malformed empty-instance", "watchdog-malformed", C545SnapshotText(instanceId: "")),
                     ("C545 WatchdogStale namespace mc/qual mismatch", "watchdog-identity-mismatch", C545SnapshotText(ns: "mc/qual")),
                     ("C545 WatchdogStale instance wd-2 mismatch", "watchdog-identity-mismatch", C545SnapshotText(instanceId: "wd-2")),
                     ("C545 WatchdogStale open outage unhealthy", "watchdog-outage-open", C545SnapshotText(openOutages: outage)),
                 })
        {
            var fx = C545WatchdogFx(results, text);
            C545WatchdogUnhealthy(rows, C545WatchdogHealth(fx), fx.Root, reason, name);
        }

        var thrown = C545WatchdogFx(results, throws: true);
        C545WatchdogUnhealthy(rows, C545WatchdogHealth(thrown), thrown.Root, "watchdog-unreachable", "C545 WatchdogStale unreachable");

        var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var closedPort = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        var closed = C545WatchdogFx(results, noSeam: true);
        C545WatchdogUnhealthy(rows, C545WatchdogHealth(closed, $"http://127.0.0.1:{closedPort}/snapshot.json"), closed.Root, "watchdog-unreachable",
            "C545 WatchdogStale unreachable closed-port");

        var noUrl = C545WatchdogFx(results, C545SnapshotText());
        C545WatchdogUnhealthy(rows, C545WatchdogHealth(noUrl, ""), noUrl.Root, "watchdog-unreachable", "C545 WatchdogStale no snapshot url unreachable");

        var control = C545WatchdogFx(results, C545SnapshotText(TimeSpan.FromMinutes(20)));
        var ok = C545WatchdogHealth(control);
        rows.Check(ok.Health.Healthy && ok.Health.ReadyForDeferral, "C545 WatchdogStale control age 20:00 healthy", Reasons(ok.Health));
        rows.ShouldPass(13, "C545 WatchdogStale age 20:01 stale", "C545 WatchdogStale unreachable", "C545 WatchdogStale malformed not-json",
            "C545 WatchdogStale malformed missing-heartbeat", "C545 WatchdogStale malformed schema-2", "C545 WatchdogStale malformed empty-instance",
            "C545 WatchdogStale namespace mc/qual mismatch", "C545 WatchdogStale instance wd-2 mismatch", "C545 WatchdogStale open outage unhealthy");
    }

    [Test]
    public void C545_ReadinessRouting()
    {
        var rows = new Rows();
        var windmill = Path.Combine(DelegateScriptRunner.RepoRoot, "scripts", "windmill");
        var definitionPath = Path.Combine(windmill, "antiphon-nightly-readiness.json");
        var schedulePath = Path.Combine(windmill, "antiphon-nightly-readiness.schedule.json");
        JsonNode? definition = null;
        if (File.Exists(definitionPath)) HealthJson.TryParse(File.ReadAllText(definitionPath), out definition);
        var content = HealthJson.Str(HealthJson.Field(definition, "content"));
        rows.Check(definition != null && HealthJson.Str(HealthJson.Field(definition, "tag")) == "desktop" && HealthJson.Str(HealthJson.Field(definition, "language")) == "bash",
            "C545 ReadinessRouting readiness definition is desktop bash", HealthJson.Str(HealthJson.Field(definition, "tag")));
        var hosts = Regex.Matches(content, "@([A-Za-z0-9_.-]+)").Select(match => match.Groups[1].Value).Where(host => host != "host.docker.internal").ToArray();
        rows.Check(Regex.IsMatch(content, @"scripts\\nightly-health\.ps1", RegexOptions.IgnoreCase)
                && Regex.IsMatch(content, @"-ReadinessConfigPath C:\\Antiphon\\nightly\\readiness-config\.json", RegexOptions.IgnoreCase) && hosts.Length == 0,
            "C545 ReadinessRouting content runs the evaluator with the readiness config and no host literal", content);
        JsonNode? schedule = null;
        if (File.Exists(schedulePath)) HealthJson.TryParse(File.ReadAllText(schedulePath), out schedule);
        var args = HealthJson.Field(schedule, "args");
        var argsEmpty = schedule != null && args is JsonObject argsObject && argsObject.Count == 0;
        rows.Check(schedule != null && HealthJson.Str(HealthJson.Field(schedule, "schedule")) == "0 */30 * * * *"
                && HealthJson.Str(HealthJson.Field(schedule, "timezone")) == "Europe/London" && HealthJson.Kind(HealthJson.Field(schedule, "enabled")) == JsonValueKind.True && argsEmpty,
            "C545 ReadinessRouting schedule every 30 minutes Europe/London enabled no args", schedule?.ToJsonString() ?? "");
        rows.Check(!File.Exists(Path.Combine(windmill, "antiphon-nightly-health.json")) && !File.Exists(Path.Combine(windmill, "antiphon-nightly-health.schedule.json")),
            "C545 ReadinessRouting retired health definition is absent");
        var threw = false;
        try { NightlyHealth.AssertMonitorRouting("desktop"); }
        catch (InvalidOperationException) { threw = true; }
        rows.Check(threw, "C545 ReadinessRouting G116 monitor routing still rejects desktop");
        var readme = File.ReadAllText(Path.Combine(windmill, "README.md"));
        rows.Check(Regex.IsMatch(readme, "antiphon_nightly_readiness", RegexOptions.IgnoreCase) && !Regex.IsMatch(readme, "u/lndcobra/antiphon_nightly_health", RegexOptions.IgnoreCase),
            "C545 ReadinessRouting README names readiness not health");
        rows.ShouldPass(6, "C545 ReadinessRouting readiness definition is desktop bash", "C545 ReadinessRouting retired health definition is absent",
            "C545 ReadinessRouting G116 monitor routing still rejects desktop");
    }

    // ---- the Windmill adapter (formerly RunnerRestartScriptCompatibilityTests.Nightly_health_closures_resolve_helpers_under_the_call_operator) ----

    /// <summary>Answers each request from the route function (a JSON body, or an exception) and records it.</summary>
    private sealed class RecordingHandler(Func<HttpRequestMessage, string> route) : HttpMessageHandler
    {
        public List<string> Requests { get; } = new();

        protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Method != HttpMethod.Get || request.Headers.Authorization?.ToString() != "Bearer synthetic") throw new InvalidOperationException("wrong HTTP contract");
            Requests.Add(request.RequestUri!.AbsoluteUri);
            var body = route(request);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json"), RequestMessage = request };
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(Send(request, cancellationToken));
    }

    [Test]
    [Arguments("2026-07-01", "2026-06-30T23:30:00.0000000Z")]
    [Arguments("2026-12-01", "2026-12-01T00:30:00.0000000Z")]
    public void Windmill_adapter_reads_registration_schedule_and_bounded_job_results(string date, string expectedDue)
    {
        var inline = new JsonObject { ["nativeRunId"] = "untrusted-inline", ["localDueDate"] = date };
        var list = new JsonArray();
        for (var n = 1; n <= 6; n++)
        {
            var row = ListRow("j" + n, "CompletedJob", "u/fixture/nightly", success: n == 4 ? (JsonNode)"true" : n != 5);
            row["result"] = inline.DeepClone();
            list.Add(row);
        }
        list.Add(new JsonObject { ["id"] = "manual", ["type"] = "CompletedJob", ["success"] = true, ["result"] = inline.DeepClone() });
        list.Add(new JsonObject { ["id"] = "running", ["type"] = "QueuedJob", ["running"] = true, ["schedule_path"] = "u/fixture/nightly" });
        var handler = new RecordingHandler(request =>
        {
            var uri = request.RequestUri!.AbsoluteUri;
            if (uri.EndsWith("/scripts/get/p/u/fixture/nightly", StringComparison.Ordinal))
                return """{"hash":"fixture-hash","tag":"fixture-tag","path":"u/fixture/nightly"}""";
            if (uri.EndsWith("/schedules/get/u/fixture/nightly", StringComparison.Ordinal))
                return """{"enabled":false,"paused":true,"tag":"schedule-tag","args":{"key":"value"},"worker_version":"worker","server_version":"server"}""";
            if (uri.Contains("/jobs/list?", StringComparison.Ordinal)) return list.ToJsonString();
            if (uri.EndsWith("/get_result/j2", StringComparison.Ordinal)) throw new HttpRequestException("fixture result unavailable");
            if (uri.EndsWith("/get_result/j3", StringComparison.Ordinal)) return """{"nativeRunId":"r3"}""";
            if (uri.Contains("/get_result/j", StringComparison.Ordinal)) return new JsonObject { ["nativeRunId"] = "native-run", ["sha"] = "fixture-sha", ["localDueDate"] = date }.ToJsonString();
            throw new InvalidOperationException("unexpected HTTP route " + uri);
        });
        var error = new StringWriter();
        var api = NightlyHealthWindmill.Production(new NightlyWindmillConfig("https://nightly.invalid/", "synthetic", "fixture", "u/fixture/nightly"), error, handler)!;

        var registration = api.GetRegistration();
        var schedule = api.GetSchedule();
        var jobs = api.GetJobs();

        NightlyHealthWindmill.RequestTimeout.ShouldBe(TimeSpan.FromSeconds(30));
        registration.ShouldBe(new NightlyWindmillRegistration(true, "fixture-hash", "fixture-tag", "u/fixture/nightly"));
        schedule.Enabled.ShouldBeFalse();
        schedule.Paused.ShouldBeTrue();
        schedule.Tag.ShouldBe("schedule-tag");
        schedule.WorkerVersion.ShouldBe("worker");
        schedule.ServerVersion.ShouldBe("server");
        HealthJson.Str(HealthJson.Field(schedule.Args, "key")).ShouldBe("value");
        jobs.Count.ShouldBe(8);
        jobs.Select(job => job.Status).ShouldBe(["success", "unknown", "unknown", "unknown", "failed", "unknown", "unknown", "running"]);
        jobs[0].NativeRunId.ShouldBe("native-run");
        jobs[0].Sha.ShouldBe("fixture-sha");
        jobs[0].ScheduledFor.ShouldBe(expectedDue, "the due instant keeps London DST");
        foreach (var index in new[] { 1, 2, 5, 6 }) jobs[index].LocalDueDate.ShouldBeEmpty();
        jobs[6].Scheduled.ShouldBe(false);
        handler.Requests.Count.ShouldBe(8);
        handler.Requests[2].ShouldBe("https://nightly.invalid/api/w/fixture/jobs/list?script_path_exact=u%2Ffixture%2Fnightly&per_page=20");
        handler.Requests.Skip(3).Select(uri => uri.Split('/').Last()).ShouldBe(["j1", "j2", "j3", "j4", "j5"]);
        error.ToString().ShouldBeEmpty("a failed result fetch is not a failed read");
    }

    // ---- statement-level failures and the production seams ----

    [Test]
    public void A_failed_read_is_one_error_line_and_reads_as_nothing()
    {
        using var results = NightlyFixture.Create("nightly-health");
        File.WriteAllText(results.PathOf("last-run.json"), "{ not json");
        var error = new StringWriter();
        NightlyHealthResult result;
        using (var stub = LoopbackHttpStub.Start(("scripts/get/p/" + Sched, 500), ("schedules/get/" + Sched, "{ broken"), ("jobs/list", 404)))
        {
            var api = NightlyHealthWindmill.Production(new NightlyWindmillConfig(stub.BaseUrl, "t", "mc", Sched), error)!;
            result = NightlyHealth.Run(new NightlyHealthOptions { StateRoot = results.Root }, Seams(DateTimeOffset.UtcNow, api, error: error));
        }
        result.ExitCode.ShouldBe(1);
        result.Health.Reasons.ShouldContain("missing-script");
        result.Health.Reasons.ShouldContain("missing-schedule");
        result.Health.Reasons.ShouldContain("unauthorized-destination");
        result.Health.Reasons.Last().ShouldBe("watchdog-unreachable");
        var lines = error.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        lines.Length.ShouldBe(4, error.ToString());
        lines.ShouldAllBe(line => line.StartsWith("nightly-health: ", StringComparison.Ordinal));
        File.Exists(NightlyHealth.MonitorResultPath(results.Root)).ShouldBeTrue();
    }

    [Test]
    public void Production_seams_have_no_sink_no_view_and_read_windmill_from_the_environment()
    {
        var environment = new Dictionary<string, string> { ["WINDMILL_BASE_URL"] = "https://windmill.invalid", ["WINDMILL_TOKEN"] = "t" };
        var seams = NightlyHealthSeams.Production(name => environment.GetValueOrDefault(name)!, TextWriter.Null);
        seams.NotificationSink.ShouldBeNull("CARD-0545 D-9: production notification belongs to the watchdog");
        seams.RecipientView.ShouldBeNull();
        seams.AllowOfflineNotify.ShouldBeFalse();
        seams.WindmillApi.ShouldNotBeNull();
        seams.Clock.ShouldBe(TimeProvider.System);
        NightlyHealthSeams.Production(_ => null!, TextWriter.Null).WindmillApi.ShouldBeNull("no credentials, no Windmill reads");

        using var results = NightlyFixture.Create("nightly-health");
        var tokenFile = results.PathOf("token.txt");
        File.WriteAllText(tokenFile, "  from-file \r\n");
        var config = NightlyWindmillConfig.FromEnvironment(name => name == "WINDMILL_TOKEN_FILE" ? tokenFile : name == "WINDMILL_BASE_URL" ? "https://w" : null!, TextWriter.Null);
        config.ShouldBe(new NightlyWindmillConfig("https://w", "from-file", "mc", Sched));
        config.HasCredentials.ShouldBeTrue();
    }

    [Test]
    public void The_result_record_has_the_passthru_names()
    {
        using var results = NightlyFixture.Create("nightly-health");
        var result = NightlyHealth.Run(new NightlyHealthOptions { StateRoot = results.Root }, Seams(NightlyFixture.DefaultNow, api: null));
        HealthJson.TryParse(File.ReadAllText(NightlyHealth.MonitorResultPath(results.Root)), out var saved);
        var record = (JsonObject)saved!;
        record.Select(pair => pair.Key).ShouldBe(["ExitCode", "Health", "Notification", "Receipt", "RecordedAt", "Identity"]);
        ((JsonObject)record["Health"]!).Select(pair => pair.Key)
            .ShouldBe(["Healthy", "Pending", "Reasons", "ReadyForDeferral", "TerminateRequested", "GreenRunId", "GreenJobId", "GreenJobNativeRunId"]);
        ((JsonObject)record["Identity"]!).Select(pair => pair.Key)
            .ShouldBe(["RepositoryPath", "ProjectId", "PolicyHash", "ScriptHash", "ScheduledRunId", "JobNativeRunId", "WindmillJobId", "WatchdogInstanceId", "WatchdogHeartbeatAt"]);
        HealthJson.Kind(record["Health"]!["Healthy"]).ShouldBe(JsonValueKind.False);
        HealthJson.Kind(record["Health"]!["ReadyForDeferral"]).ShouldBe(JsonValueKind.False);
        HealthJson.Str(record["RecordedAt"]).ShouldBe("2026-09-11T12:00:00.0000000Z");
        record["Notification"].ShouldBeNull();
        result.ExitCode.ShouldBe(1);
    }
}
