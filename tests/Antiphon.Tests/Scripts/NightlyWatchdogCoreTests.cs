using System.Text.Json.Nodes;
using Antiphon.NightlyWatchdog;
using Antiphon.Tests.TestHelpers;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// CARD-0545 V-545-1..9, 13..16: the independent watchdog's core components in-process. Real ledger on a
/// temp SQLite file, real evaluator/body/snapshot server; fakes only at Windmill, Telegram, the MTProto reader
/// and the clock (see <see cref="C545World"/>). Spawns no process.
/// </summary>
[Category("Integration")]
public sealed class NightlyWatchdogCoreTests
{
    private const string Due = "2026-09-18";

    private static DateTime Utc(string iso) => DateTime.Parse(iso, System.Globalization.CultureInfo.InvariantCulture,
        System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal);

    private static DateOnly Day(string day) => DateOnly.Parse(day, System.Globalization.CultureInfo.InvariantCulture);

    [Test]
    public void C545_LondonDueDays()
    {
        foreach (var (utc, day) in new[]
                 {
                     ("2026-06-30T23:00:00Z", "2026-07-01"), ("2026-06-30T22:59:59Z", "2026-06-30"),
                     ("2026-12-31T23:59:59Z", "2026-12-31"), ("2027-01-01T00:00:00Z", "2027-01-01"),
                 })
            LondonClock.Format(LondonClock.DueDay(Utc(utc))).ShouldBe(day, $"DueDay {utc}");

        foreach (var (day, due) in new[]
                 {
                     ("2026-03-28", "2026-03-28T00:30:00Z"), ("2026-03-29", "2026-03-29T00:30:00Z"), ("2026-03-30", "2026-03-29T23:30:00Z"),
                     ("2026-10-24", "2026-10-23T23:30:00Z"), ("2026-10-25", "2026-10-24T23:30:00Z"), ("2026-10-26", "2026-10-26T00:30:00Z"),
                 })
        {
            LondonClock.DueUtc(Day(day)).ShouldBe(Utc(due), $"DueUtc {day}");
            LondonClock.DueUtc(Day(day)).Kind.ShouldBe(DateTimeKind.Utc, $"DueUtc kind {day}");
            LondonClock.GraceEndUtc(Day(day)).ShouldBe(Utc(due).AddMinutes(30), $"GraceEndUtc {day}");
        }

        foreach (var (day, deadline) in new[] { ("2026-03-29", "2026-03-29T07:00:00Z"), ("2026-10-25", "2026-10-25T08:00:00Z"), ("2026-09-17", "2026-09-17T07:00:00Z") })
            LondonClock.MorningDeadlineUtc(Day(day)).ShouldBe(Utc(deadline), $"MorningDeadlineUtc {day}");

        LondonClock.PreviousDayInScope(Utc("2026-09-18T06:59:59Z")).ShouldBeTrue("previous day in scope at 07:59:59 London");
        LondonClock.PreviousDayInScope(Utc("2026-09-18T07:00:00Z")).ShouldBeFalse("previous day out of scope at 08:00 London");
        new[] { "Europe/London", "GMT Standard Time" }.ShouldContain(LondonClock.TimeZone.Id, "zone lookup");
    }

    [Test]
    public void C545_OptionsRefuseWildcardBind()
    {
        IReadOnlyList<string> Validate(Action<WatchdogOptions> change)
        {
            var o = new WatchdogOptions { Namespace = "mc/test", SnapshotBind = "127.0.0.1:17290", Runtime = "native", WorkingDirectory = Path.GetTempPath(), StateDir = "./state-test" };
            change(o);
            return o.Validate();
        }

        Validate(o => o.SnapshotBind = "0.0.0.0:17290").ShouldContain("snapshot-bind-wildcard", "0.0.0.0/native");
        Validate(o => o.SnapshotBind = "[::]:17290").ShouldContain("snapshot-bind-wildcard", "[::]/native");
        Validate(o => { o.SnapshotBind = "0.0.0.0:17290"; o.Runtime = "container"; }).ShouldBeEmpty("0.0.0.0/container");
        Validate(_ => { }).ShouldBeEmpty("127.0.0.1/native");
        Validate(o => o.SnapshotBind = "").ShouldContain("snapshot-bind-missing", "empty bind");
        Validate(o => o.Namespace = "").ShouldContain("namespace-missing", "empty namespace");
        Validate(o => o.DestinationChatId = null).ShouldBeEmpty("unset destination is a send-time refusal");
        Validate(o => { o.Namespace = "mc"; o.StateDir = "./state"; o.AllowFaultInjection = true; }).ShouldContain("fault-injection-forbidden", "mc with faults");
    }

    private static async Task<(C545World World, TickReport Report)> Row(Action<C545World> setup, string? clock = null, int ticks = 1)
    {
        var world = await C545World.CreateAsync(start: clock is null ? null : new DateTimeOffset(Utc(clock)));
        setup(world);
        TickReport report = null!;
        for (var i = 0; i < ticks; i++)
        {
            if (i > 0) world.Clock.Advance(TimeSpan.FromMinutes(10));
            report = await world.TickAsync();
        }
        return (world, report);
    }

    private static IReadOnlyList<string> Opened(TickReport report) =>
        report.Transitions.Where(t => t.Change == OutageChange.Opened).Select(t => t.Kind).ToList();

    [Test]
    public async Task C545_OutageKinds()
    {
        // A running job keeps start-overdue out of rows that are not about it.
        void Running(C545World w) => w.Windmill.Jobs.Add(w.Jobs.Running(Utc("2026-09-17T23:31:00Z")));

        foreach (var (kind, setup) in new (string, Action<C545World>)[]
                 {
                     ("windmill-unreachable", w => w.Windmill.Reachable = false),
                     ("windmill-auth-failed", w => w.Windmill.Auth = FakeAuth.Unauthorized),
                 })
        {
            var world = await C545World.CreateAsync();
            await using (world)
            {
                Running(world);
                setup(world);
                (await world.TickAsync()).Transitions.ShouldBeEmpty($"{kind}/first-tick");
                var second = await world.AdvanceAndTickAsync(TimeSpan.FromMinutes(10));
                var opened = second.Transitions.Where(t => t.Change == OutageChange.Opened && t.Kind == kind).ToList();
                opened.Count.ShouldBe(1, $"{kind}/second-tick");
                opened[0].OutageId.ShouldBe($"nw:mc/test:{Due}:{kind}:1", $"{kind}/second-tick identity");
                opened[0].Nid.ShouldNotBeNullOrWhiteSpace($"{kind}/second-tick nid");
                var row = world.Outages().Single(o => o.Kind == kind);
                row.JobId.ShouldBeNull($"{kind}/second-tick no job");
                row.RunId.ShouldBeNull($"{kind}/second-tick no run");
                (await world.AdvanceAndTickAsync(TimeSpan.FromMinutes(10))).Transitions.Where(t => t.Kind == kind).ShouldBeEmpty($"{kind}/steady");
                world.Windmill.Reachable = true;
                world.Windmill.Auth = FakeAuth.Ok;
                (await world.AdvanceAndTickAsync(TimeSpan.FromMinutes(10))).Transitions.Where(t => t.Kind == kind).ShouldBeEmpty($"{kind}/close first clean tick");
                var closed = (await world.AdvanceAndTickAsync(TimeSpan.FromMinutes(10))).Transitions.Where(t => t.Kind == kind && t.Change == OutageChange.Closed).ToList();
                closed.Count.ShouldBe(1, $"{kind}/close");
                world.Outages().Single(o => o.Kind == kind).RecoveryNid.ShouldNotBeNull($"{kind}/close recovery nid");
            }
        }

        foreach (var (row, setup, expected) in new (string, Action<C545World>, string)[]
                 {
                     ("script-missing/open", w => w.Windmill.Script = null, "script-missing"),
                     ("script-hash-drift/open", w => w.Windmill.Script = "h2", "script-hash-drift"),
                     ("schedule-missing/open", w => w.Windmill.Schedule = null, "schedule-missing"),
                     ("schedule-disabled/open", w => w.Windmill.Schedule = FakeSchedule.Disabled, "schedule-disabled"),
                 })
        {
            var (world, report) = await Row(w => { Running(w); setup(w); });
            await using (world) Opened(report).ShouldBe([expected], row);
        }

        {
            var (world, report) = await Row(w => { Running(w); w.Windmill.WorkerLastPingUtc = C545World.Start.UtcDateTime.AddMinutes(-59); });
            await using (world) Opened(report).ShouldBeEmpty("desktop-worker-missing/59m");
        }
        {
            var (world, report) = await Row(w => { Running(w); w.Windmill.WorkerLastPingUtc = C545World.Start.UtcDateTime.AddMinutes(-60); });
            await using (world)
            {
                Opened(report).ShouldBe(["desktop-worker-missing"], "desktop-worker-missing/60m");
                world.Outages().Single().JobId.ShouldBeNull("desktop-worker-missing/60m no job");
            }
        }
        {
            var (world, report) = await Row(w => w.Windmill.WorkerLastPingUtc = C545World.Start.UtcDateTime.AddMinutes(-75));
            await using (world)
            {
                Opened(report).OrderBy(k => k).ToArray().ShouldBe(new[] {"desktop-worker-missing", "start-overdue" }, "desktop-worker-missing/after-grace");
                world.Outages().ShouldAllBe(o => o.JobId == null, "desktop-worker-missing/after-grace no job invented");
                world.Outages().ShouldAllBe(o => o.RunId == null, "desktop-worker-missing/after-grace no run invented");
            }
        }
        {
            var world = await C545World.CreateAsync();
            await using (world)
            {
                Running(world);
                world.Windmill.WorkerLastPingUtc = C545World.Start.UtcDateTime.AddMinutes(-60);
                Opened(await world.TickAsync()).ShouldBe(["desktop-worker-missing"], "desktop-worker-missing/close open");
                world.Windmill.WorkerLastPingUtc = world.Clock.GetUtcNow().UtcDateTime.AddMinutes(10);
                (await world.AdvanceAndTickAsync(TimeSpan.FromMinutes(10))).Transitions.ShouldBeEmpty("desktop-worker-missing/close first ping tick");
                (await world.AdvanceAndTickAsync(TimeSpan.FromMinutes(10))).Transitions.Where(t => t.Change == OutageChange.Closed)
                    .Select(t => t.Kind).ShouldBe(["desktop-worker-missing"], "desktop-worker-missing/close");
            }
        }

        {
            var (world, report) = await Row(_ => { }, "2026-09-17T23:59:59Z");
            await using (world) Opened(report).ShouldNotContain("start-overdue", "start-overdue/before-grace");
        }
        {
            var (world, report) = await Row(_ => { }, "2026-09-18T00:00:00Z");
            await using (world)
            {
                Opened(report).ShouldContain("start-overdue", "start-overdue/at-grace");
                world.Outages().Single(o => o.Kind == "start-overdue").OutageId.ShouldBe($"nw:mc/test:{Due}:start-overdue:1", "start-overdue/at-grace identity");
            }
        }
        {
            var (world, report) = await Row(w => w.Windmill.Jobs.Add(w.Jobs.Queued(Due)));
            await using (world) Opened(report).ShouldContain("start-overdue", "start-overdue/queued");
        }
        {
            var (world, report) = await Row(Running);
            await using (world) Opened(report).ShouldBeEmpty("start-overdue/running");
        }
        {
            var (world, report) = await Row(_ => { }, "2026-03-30T00:00:00Z");
            await using (world)
                world.Outages().Where(o => o.Kind == "start-overdue").Select(o => o.DueDay).ShouldBe(["2026-03-30"], "start-overdue/dst-bst");
        }

        {
            var (world, report) = await Row(w => w.Windmill.Jobs.Add(w.Jobs.Running(C545World.Start.UtcDateTime - new TimeSpan(5, 59, 59))));
            await using (world) Opened(report).ShouldNotContain("run-stalled", "run-stalled/5h59m59s");
        }
        {
            var (world, report) = await Row(w => w.Windmill.Jobs.Add(w.Jobs.Running(C545World.Start.UtcDateTime - TimeSpan.FromHours(6), "j-stalled", Due)));
            await using (world)
            {
                Opened(report).ShouldContain("run-stalled", "run-stalled/6h");
                world.Outages().Single(o => o.Kind == "run-stalled").JobId.ShouldBe("j-stalled", "run-stalled/6h job");
            }
        }

        foreach (var (row, logs) in new[]
                 {
                     ("windows-hop-failed/exit-255", "bash: line 3\nexit 255"),
                     ("windows-hop-failed/connection-refused", "ssh: connect to host h port 22: Connection refused"),
                     ("windows-hop-failed/timed-out", "ssh: connect to host h port 22: Connection timed out"),
                     ("windows-hop-failed/permission-denied", "lndco@host: Permission denied (publickey)."),
                     ("windows-hop-failed/no-such-identity", "Warning: Identity file /tmp/windmill/no_such_key not accessible: no such identity"),
                 })
        {
            var (world, report) = await Row(w => w.Windmill.Jobs.Add(w.Jobs.HopFailed(Due, "j-hop", logs)));
            await using (world)
            {
                Opened(report).ShouldBe(["windows-hop-failed"], row);
                var outage = world.Outages().Single();
                outage.JobId.ShouldBe("j-hop", row + " job");
                outage.RunId.ShouldBeNull(row + " no run");
            }
        }
        foreach (var (row, logs) in new[] { ("job-failed/assertion", "Assertion failed: expected 1"), ("job-failed/empty-logs", "") })
        {
            var (world, report) = await Row(w => w.Windmill.Jobs.Add(w.Jobs.Failed(Due, "j-fail", logs)));
            await using (world) Opened(report).ShouldBe(["job-failed"], row);
        }

        foreach (var (row, result) in new (string, JsonObject?)[]
                 {
                     ("result-missing/null-result", null),
                     ("result-missing/no-run-id", new JsonObject { ["localDueDate"] = Due, ["sha"] = "s18" }),
                     ("result-missing/no-due-date", new JsonObject { ["nativeRunId"] = "r18", ["sha"] = "s18" }),
                     ("result-missing/wrong-due-date", new JsonObject { ["nativeRunId"] = "r18", ["localDueDate"] = "2026-09-17", ["sha"] = "s18" }),
                 })
        {
            var (world, report) = await Row(w => w.Windmill.Jobs.Add(w.Jobs.SuccessWithResult(Due, "j-res", result)));
            await using (world) Opened(report).ShouldBe(["result-missing"], row);
        }
        {
            var (world, report) = await Row(w => w.Windmill.Jobs.Add(w.Jobs.Success(Due, "r18", "s18")));
            await using (world)
            {
                report.Transitions.ShouldBeEmpty("result-matching/control");
                world.Heartbeat().LastDueDay.ShouldBe(new LastDueDay(Due, "j-ok", "success", "r18", "s18"), "result-matching/control last due day");
            }
        }
        {
            var (world, report) = await Row(w => w.Windmill.Jobs.Add(w.Jobs.Success(Due, "r18", reportDelivered: false)));
            await using (world)
            {
                Opened(report).ShouldBe(["report-undelivered"], "report-undelivered/open");
                world.Outages().Single().RunId.ShouldBe("r18", "report-undelivered/open run");
            }
        }
        foreach (var (row, tests, coverage, flag) in new[] { ("not-outage/tests-red", false, true, "testsPassed"), ("not-outage/coverage-incomplete", true, false, "coverageComplete") })
        {
            var (world, report) = await Row(w => w.Windmill.Jobs.Add(w.Jobs.Success(Due, "r18", testsPassed: tests, coverageComplete: coverage)));
            await using (world)
            {
                report.Transitions.ShouldBeEmpty(row);
                var probeJob = world.Ledger.LatestProbe()!["dueDays"]!.AsArray().SelectMany(d => d!["jobs"]!.AsArray()).Single(j => j!["id"]!.GetValue<string>() == "j-ok")!;
                probeJob[flag]!.GetValue<bool>().ShouldBeFalse(row + " probe row records the flag");
            }
        }

        // RunBudgetHours is raised so the running job is not also stalled at the deadline.
        {
            var (world, report) = await Row(w => { w.Options.RunBudgetHours = 12; w.Windmill.Jobs.Add(w.Jobs.Running(Utc("2026-09-17T23:31:00Z"))); }, "2026-09-18T06:59:59Z");
            await using (world) Opened(report).ShouldNotContain("deadline-missed", "deadline-missed/before");
        }
        {
            var (world, report) = await Row(w => { w.Options.RunBudgetHours = 12; w.Windmill.Jobs.Add(w.Jobs.Running(Utc("2026-09-17T23:31:00Z"))); }, "2026-09-18T07:00:00Z");
            await using (world) Opened(report).ShouldBe(["deadline-missed"], "deadline-missed/at");
        }

        {
            var world = await C545World.CreateAsync();
            await using (world)
            {
                Running(world);
                world.Windmill.Jobs.Add(world.Jobs.Failed("2026-09-17", "j-17"));
                (await world.TickAsync()).Transitions.Select(t => (t.Kind, t.Change)).ShouldBe([("job-failed", OutageChange.Opened)], "previous-day/closes-before-0800 open");
                world.Windmill.Jobs.Add(world.Jobs.Success("2026-09-17", "r17", id: "j-17-ok", createdOffset: TimeSpan.FromMinutes(90)));
                var report = await world.AdvanceAndTickAsync(TimeSpan.FromMinutes(1));
                report.Transitions.Where(t => t.Change == OutageChange.Closed).Select(t => t.Kind).ShouldBe(["job-failed"], "previous-day/closes-before-0800");
            }
        }
        {
            var (world, report) = await Row(w => { w.Options.RunBudgetHours = 12; w.Windmill.Jobs.Add(w.Jobs.Running(Utc("2026-09-17T23:31:00Z"))); w.Windmill.Jobs.Add(w.Jobs.HopFailed("2026-09-17", "j-17-hop")); }, "2026-09-18T07:00:00Z");
            await using (world) report.Transitions.Where(t => t.OutageId.Contains(":2026-09-17:")).ShouldBeEmpty("previous-day/ignored-after-0800");
        }
    }

    [Test]
    public async Task C545_DeadlineMissedSuppressedByOpenOutage()
    {
        var world = await C545World.CreateAsync();
        await using (world)
        {
            world.Windmill.Jobs.Add(world.Jobs.HopFailed(Due, "j-hop"));
            Opened(await world.TickAsync()).ShouldBe(["windows-hop-failed"], "hop open");
            var hop = world.Outages().Single();
            world.Clock.SetUtcNow(new DateTimeOffset(Utc("2026-09-18T07:00:00Z")));
            var report = await world.TickAsync();
            report.Transitions.ShouldContain(t => t.OutageId == hop.OutageId && t.Change == OutageChange.Amended, "amended");
            report.Transitions.ShouldNotContain(t => t.Change == OutageChange.Opened && t.Kind == "deadline-missed", "not opened");
            world.Notifications().Count.ShouldBe(1, "one notification");
            world.Outages().Single().EvidenceJson.ShouldContain("\"deadlineMissedAt\":\"2026-09-18T07:00:00.0000000Z\"", Case.Sensitive, "evidence");
        }

        var control = await C545World.CreateAsync();
        await using (control)
        {
            control.Options.RunBudgetHours = 12;
            control.Windmill.Jobs.Add(control.Jobs.Running(Utc("2026-09-18T01:00:00Z")));
            control.Clock.SetUtcNow(new DateTimeOffset(Utc("2026-09-18T07:00:00Z")));
            var report = await control.TickAsync();
            var opened = report.Transitions.Single(t => t.Change == OutageChange.Opened && t.Kind == "deadline-missed");
            opened.Nid.ShouldNotBeNullOrWhiteSpace("control own nid");
        }
    }

    [Test]
    public async Task C545_ClosureNeverInventsRun()
    {
        async Task<C545World> OpenJobFailed()
        {
            var w = await C545World.CreateAsync();
            w.Windmill.Jobs.Add(w.Jobs.Failed(Due, "j1"));
            Opened(await w.TickAsync()).ShouldBe(["job-failed"], "setup job-failed");
            return w;
        }

        foreach (var (row, job) in new (string, Func<C545World, WindmillJob>)[]
                 {
                     ("manual-success", w => w.Jobs.Success(Due, "r18", scheduled: false, id: "jm", createdOffset: TimeSpan.FromMinutes(30))),
                     ("earlier-day-success", w => w.Jobs.Success("2026-09-17", "r17", id: "j17", createdOffset: TimeSpan.FromMinutes(30))),
                     ("missing-due-date", w => w.Jobs.SuccessWithResult(Due, "jn", new JsonObject { ["nativeRunId"] = "r18" }, TimeSpan.FromMinutes(30))),
                 })
        {
            var world = await OpenJobFailed();
            await using (world)
            {
                world.Windmill.Jobs.Add(job(world));
                await world.AdvanceAndTickAsync(TimeSpan.FromMinutes(10));
                world.Outages().Single(o => o.Kind == "job-failed").ClosedAt.ShouldBeNull(row);
            }
        }

        {
            var world = await OpenJobFailed();
            await using (world)
            {
                world.Windmill.Jobs.Add(world.Jobs.Failed(Due, "j2", createdOffset: TimeSpan.FromMinutes(30)));
                var report = await world.AdvanceAndTickAsync(TimeSpan.FromMinutes(10));
                var outage = world.Outages().Single();
                outage.ClosedAt.ShouldBeNull("failed-again still open");
                report.Transitions.ShouldContain(t => t.Change == OutageChange.Amended, "failed-again amended");
                outage.EvidenceJson.ShouldContain("\"j2\"", Case.Sensitive, "failed-again evidence names j2");
            }
        }
        {
            var world = await OpenJobFailed();
            await using (world)
            {
                var failureRun = world.Notifications().Single().RunId;
                world.Windmill.Jobs.Add(world.Jobs.Success(Due, "r18", id: "j3", createdOffset: TimeSpan.FromMinutes(30)));
                await world.AdvanceAndTickAsync(TimeSpan.FromMinutes(10));
                var outage = world.Outages().Single();
                outage.ClosedAt.ShouldNotBeNull("scheduled-matching closed");
                var evidence = JsonNode.Parse(outage.EvidenceJson)!;
                evidence["closedByJobId"]!.GetValue<string>().ShouldBe("j3", "scheduled-matching closedByJobId");
                evidence["closedByRunId"]!.GetValue<string>().ShouldBe("r18", "scheduled-matching closedByRunId");
                world.Notifications().Single(n => n.Kind == "failure").RunId.ShouldBe(failureRun, "failure run unchanged");
                world.Outages().Count.ShouldBe(1, "no new outage");
                world.Heartbeat().LastDueDay!.NativeRunId.ShouldBe("r18", "last due day run");
            }
        }
        {
            var world = await OpenJobFailed();
            await using (world)
            {
                world.Windmill.Jobs.Add(world.Jobs.Success("2026-09-19", "r19", id: "j19"));
                await world.AdvanceAndTickAsync(TimeSpan.FromMinutes(10));
                world.Outages().Single(o => o.Kind == "job-failed").ClosedAt.ShouldNotBeNull("later-day-success closes");
            }
        }
    }

    [Test]
    public async Task C545_OutageIdentityEpoch()
    {
        var world = await C545World.CreateAsync();
        await using (world)
        {
            world.Windmill.Jobs.Add(world.Jobs.HopFailed(Due, "j1"));
            var first = (await world.TickAsync()).Transitions.Single(t => t.Change == OutageChange.Opened);
            first.OutageId.ShouldEndWith(":1", Case.Sensitive, "first epoch");
            (await world.AdvanceAndTickAsync(TimeSpan.FromMinutes(10))).Transitions.ShouldNotContain(t => t.Change == OutageChange.Opened, "no reopen while open");
            world.Outages().Single().FailureNid.ShouldBe(first.Nid, "same failure nid");

            world.Windmill.Jobs.Add(world.Jobs.Success(Due, "r18", id: "j3", createdOffset: TimeSpan.FromMinutes(30)));
            (await world.AdvanceAndTickAsync(TimeSpan.FromMinutes(10))).Transitions.ShouldContain(t => t.Change == OutageChange.Closed, "closed");

            world.Windmill.Jobs.Add(world.Jobs.HopFailed(Due, "j4", createdOffset: TimeSpan.FromMinutes(60)));
            var second = (await world.AdvanceAndTickAsync(TimeSpan.FromMinutes(10))).Transitions.Single(t => t.Change == OutageChange.Opened);
            second.OutageId.ShouldBe($"nw:mc/test:{Due}:windows-hop-failed:2", "second epoch");
            second.Nid.ShouldNotBe(first.Nid, "new nid");
            world.Outages().Single(o => o.Epoch == 1).ClosedAt.ShouldNotBeNull(":1 remains closed");
            world.Outages().Count.ShouldBe(2, "two outage rows");
        }
    }

    [Test]
    public void C545_BodyHashRoundTrip()
    {
        const string nid = "01J8Z0000000000000000000A1";
        var text = NotificationBody.RenderFailure("windows-hop-failed", Due, "mc", "u/lndcobra/antiphon_nightly_tests",
            "01a0aaaa-0000-4000-8000-000000000001", "ssh exit 255 at 2026-09-18T00:31:07Z", "nw:mc:2026-09-18:windows-hop-failed:1",
            nid, 1, null, null, "3f9c0f2a");
        var headLines = new[]
        {
            "Antiphon nightly watchdog: FAILURE windows-hop-failed",
            "due 2026-09-18 Europe/London; workspace mc; schedule u/lndcobra/antiphon_nightly_tests",
            "job 01a0aaaa-0000-4000-8000-000000000001: ssh exit 255 at 2026-09-18T00:31:07Z",
            "outage nw:mc:2026-09-18:windows-hop-failed:1",
            "notification 01J8Z0000000000000000000A1; attempt 1; sha none; run none; policy 3f9c0f2a",
        };
        var h = NotificationBody.Sha256Hex(string.Join('\n', headLines))[..16];
        var expected = string.Join('\n', headLines) + "\n"
            + $"#antiphon-nightly nid={nid} oid=nw:mc:2026-09-18:windows-hop-failed:1 kind=failure attempt=1 due=2026-09-18 h={h}";
        text.ShouldBe(expected, "exact render");
        h.ShouldMatch("^[0-9a-f]{16}$", "h format");

        var marker = NotificationBody.ParseMarker(text);
        (marker.Nid, marker.Oid, marker.Kind, marker.Attempt, marker.Due, marker.H).ShouldBe(
            (nid, "nw:mc:2026-09-18:windows-hop-failed:1", "failure", (int?)1, Due, h), "marker fields");
        (marker.Job, marker.Run, marker.Sha, marker.Policy).ShouldBe(("01a0aaaa-0000-4000-8000-000000000001", "none", "none", "3f9c0f2a"), "header fields");
        marker.HashMismatch.ShouldBeFalse("hash matches");

        var edited = text.Replace("ssh exit 255", "ssh exit 254");
        NotificationBody.Sha256Hex(edited).ShouldNotBe(NotificationBody.Sha256Hex(text), "edit changes body sha");
        NotificationBody.ParseMarker(edited).HashMismatch.ShouldBeTrue("edit reports hash mismatch");

        var recovery = NotificationBody.Render(new NotificationContent("recovery", "windows-hop-failed", Due, "mc", "u/lndcobra/antiphon_nightly_tests",
            "j3", "closed by job j3 run r18", "nw:mc:2026-09-18:windows-hop-failed:1", null, null, null, nid, false), "01J8Z0000000000000000000B2", 1);
        recovery.ShouldStartWith("Antiphon nightly watchdog: RECOVERED windows-hop-failed", Case.Sensitive, "recovery head");
        recovery.ShouldContain($"kind=recovery attempt=1 due={Due} link={nid} failureReceived=false h=", Case.Sensitive, "recovery marker");

        var notice = NotificationBody.Render(new NotificationContent("qualification", null, Due, "mc", "u/lndcobra/antiphon_nightly_tests",
            null, "qualification notice", null, null, null, null), "01J8Z0000000000000000000C3", 1);
        notice.ShouldContain("oid=none kind=qualification", Case.Sensitive, "qualification marker");

        foreach (var body in new[] { text, recovery, notice })
        {
            body.All(ch => ch == '\n' || ch is >= ' ' and <= '~').ShouldBeTrue("ascii");
            body.Split('\n').ShouldAllBe(line => line == line.TrimEnd(), "no trailing whitespace");
            body.EndsWith('\n').ShouldBeFalse("no trailing newline");
            body.Length.ShouldBeLessThan(4096, "under telegram limit");
        }
    }

    [Test]
    public void C545_IntentBeforeNetwork()
    {
        var dir = Directory.CreateTempSubdirectory("antiphon-c545-intent").FullName;
        try
        {
            var path = Path.Combine(dir, "ledger.db");
            var clock = new FakeTimeProvider(C545World.Start);
            NotificationContent Content(string oid) => new("failure", "job-failed", Due, "mc", "s", "j1", "failed", oid, null, null, null);
            using (var throwing = new Ledger(path, "mc/test", clock, () => throw new InvalidOperationException("nid factory failed")))
            {
                Should.Throw<InvalidOperationException>(() => throwing.OpenOutageWithIntent("job-failed", Due, "j1", null, new JsonObject(), Content));
                throwing.Outages().ShouldBeEmpty("rolled back outages");
                throwing.Notifications().ShouldBeEmpty("rolled back notifications");
                throwing.Attempts().ShouldBeEmpty("rolled back attempts");
            }
            using var ledger = new Ledger(path, "mc/test", clock);
            var (_, notification, attempt) = ledger.OpenOutageWithIntent("job-failed", Due, "j1", null, new JsonObject(), Content);
            ledger.Outages().Count.ShouldBe(1, "one outage");
            ledger.Notifications().Single().State.ShouldBe("pending", "pending notification");
            ledger.Attempts(notification.Nid).Single().Accepted.ShouldBeFalse("attempt not accepted");
            using (var second = new Ledger(path, "mc/test", clock))
            {
                second.Attempts(notification.Nid).Single().Attempt.ShouldBe(attempt.Attempt, "second connection sees the attempt");
                second.PendingNotifications().Single().Nid.ShouldBe(notification.Nid, "pending visible");
            }
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try { Directory.Delete(dir, recursive: true); } catch (IOException) { }
        }
    }

    [Test]
    public async Task C545_ReceiptIdempotent()
    {
        var world = await C545World.CreateAsync();
        await using (world)
        {
            var notice = world.Ledger.OpenNoticeWithIntent(new NotificationContent("qualification", null, Due, "mc", "s", null, "n", null, null, null, null));
            world.Ledger.ImportReceipt(notice.Nid, 1, "7", C545World.Start.UtcDateTime, "peer", "sha", false).ShouldBe("imported", "first");
            var receivedAt = world.Notification(notice.Nid).ReceivedAt;
            world.Clock.Advance(TimeSpan.FromMinutes(5));
            world.Ledger.ImportReceipt(notice.Nid, 1, "7", C545World.Start.UtcDateTime, "peer", "sha", false).ShouldBe("duplicate", "second");
            world.Receipts().Count.ShouldBe(1, "one receipt row");
            world.Notification(notice.Nid).ReceivedAt.ShouldBe(receivedAt, "receivedAt unchanged");
            world.Ledger.CreateAttempt(notice.Nid, 2);
            world.Ledger.ImportReceipt(notice.Nid, 2, "9", C545World.Start.UtcDateTime, "peer", "sha", false);
            world.Receipts().Count.ShouldBe(2, "second message id adds a row");
            world.Notification(notice.Nid).State.ShouldBe("received", "state unchanged");
        }
    }

    [Test]
    public void C545_LedgerNamespaceBound()
    {
        var dir = Directory.CreateTempSubdirectory("antiphon-c545-ns").FullName;
        try
        {
            var path = Path.Combine(dir, "ledger.db");
            var clock = new FakeTimeProvider(C545World.Start);
            using (var ledger = new Ledger(path, "mc/test", clock))
                ledger.Heartbeat().Namespace.ShouldBe("mc/test", "stored namespace");
            Should.Throw<LedgerNamespaceMismatchException>(() => new Ledger(path, "mc/qual", clock).Dispose());

            var shared = new WatchdogOptions { Namespace = "mc/qual", WorkingDirectory = dir, StateDir = "./state", SnapshotBind = "127.0.0.1:17291" };
            shared.Validate().ShouldContain("state-dir-shared-with-production", "qual on production state dir");
            var own = new WatchdogOptions { Namespace = "mc/qual", WorkingDirectory = dir, StateDir = "./state-qual", SnapshotBind = "127.0.0.1:17291" };
            own.Validate().ShouldBeEmpty("qual on its own state dir");
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try { Directory.Delete(dir, recursive: true); } catch (IOException) { }
        }
    }

    [Test]
    public async Task C545_SnapshotShape()
    {
        var world = await C545World.CreateAsync();
        await using (world)
        {
            world.Transport.Mode = TransportMode.AcceptWithoutDelivery;
            world.Windmill.Jobs.Add(world.Jobs.HopFailed(Due, "j-hop"));
            await world.TickAsync();

            var (status, body) = await world.GetSnapshotAsync();
            status.ShouldBe(200, "status");
            var snapshot = (JsonNode.Parse(body) as JsonObject)!;
            snapshot.Select(p => p.Key).OrderBy(k => k, StringComparer.Ordinal).ShouldBe(new[]
            {
                "schemaVersion", "instanceId", "version", "configHash", "namespace", "heartbeatAt", "tickSeconds", "windmill", "desktopWorker",
                "schedule", "reader", "destination", "openOutages", "recentNotifications", "lastDueDay", "truncated",
            }.OrderBy(k => k, StringComparer.Ordinal), "top-level keys");
            snapshot["schemaVersion"]!.GetValue<int>().ShouldBe(1, "schema");
            snapshot["namespace"]!.GetValue<string>().ShouldBe("mc/test", "namespace");
            snapshot["instanceId"]!.GetValue<string>().ShouldStartWith("wd-", Case.Sensitive, "instance");
            snapshot["destination"]!["qualified"]!.GetValue<bool>().ShouldBeTrue("destination qualified");
            snapshot["destination"]!["hash"]!.GetValue<string>().ShouldBe(WatchdogOptions.Sha256Hex(C545World.ChatId)[..16], "destination hash");
            var open = snapshot["openOutages"]!.AsArray().Single()!;
            open["outageId"]!.GetValue<string>().ShouldBe(world.Outages().Single().OutageId, "open outage id");
            open["failureNid"]!.GetValue<string>().ShouldBe(world.Outages().Single().FailureNid, "failure nid");
            open["failureState"]!.GetValue<string>().ShouldBe("sent", "failure state");
            var recent = snapshot["recentNotifications"]!.AsArray()[0]!.AsObject();
            foreach (var key in new[] { "nid", "kind", "state", "attempts", "acceptedAt", "messageId", "receivedAt", "readObservedAt" })
                recent.ContainsKey(key).ShouldBeTrue($"recent notification carries {key}");

            foreach (var secret in new[] { C545World.BotToken, world.Options.WindmillToken!, C545World.ChatId, world.Options.ReaderApiHash! })
                body.ShouldNotContain(secret, Case.Sensitive, "snapshot carries no secret or raw chat id");

            for (var i = 0; i < 500; i++)
                world.Ledger.OpenNoticeWithIntent(new NotificationContent("qualification", null, Due, "mc", "s", null, "notice " + i, null, null, null, null));
            var (_, big) = await world.GetSnapshotAsync();
            System.Text.Encoding.UTF8.GetByteCount(big).ShouldBeLessThanOrEqualTo(65536, "64 KB cap");
            JsonNode.Parse(big)!["truncated"]!.GetValue<bool>().ShouldBeTrue("truncated flag");

            (await world.SendSnapshotRequestAsync(HttpMethod.Get, "/other")).Status.ShouldBe(404, "other path");
            (await world.SendSnapshotRequestAsync(HttpMethod.Post, "/snapshot.json")).Status.ShouldBe(405, "post");
        }
    }

    [Test]
    public async Task C545_TelegramTransportRequestShape()
    {
        var options = new WatchdogOptions { DestinationChatId = C545World.ChatId, TelegramBotToken = C545World.BotToken };
        async Task<(TransportResult Result, RecordingHttpHandler Handler)> Send(ScriptedResponse response, TimeSpan? timeout = null)
        {
            var handler = new RecordingHttpHandler([response]);
            using var http = new HttpClient(handler) { Timeout = timeout ?? TimeSpan.FromSeconds(15) };
            var transport = new TelegramBotTransport(http, options, () => null);
            return (await transport.SendAsync(new OutboundMessage("n1", 1, "hello body"), CancellationToken.None), handler);
        }

        var (ok, recorded) = await Send(new ScriptedResponse(200, """{"ok":true,"result":{"message_id":42}}"""));
        var request = recorded.Requests.Single();
        request.Method.ShouldBe(HttpMethod.Post, "method");
        request.Uri.ToString().ShouldBe($"https://api.telegram.org/bot{C545World.BotToken}/sendMessage", "uri");
        var json = (JsonNode.Parse(request.BodyJson!) as JsonObject)!;
        json.Select(p => p.Key).OrderBy(k => k).ToArray().ShouldBe(new[] { "chat_id", "disable_web_page_preview", "text" }, "body keys");
        json["chat_id"]!.GetValue<string>().ShouldBe(C545World.ChatId, "chat_id");
        json["text"]!.GetValue<string>().ShouldBe("hello body", "text");
        json["disable_web_page_preview"]!.GetValue<bool>().ShouldBeTrue("preview disabled");
        (ok.Accepted, ok.MessageId).ShouldBe((true, "42"), "accepted");

        foreach (var (row, response, errorClass, retryAfter, timeout) in new (string, ScriptedResponse, string, int?, TimeSpan?)[]
                 {
                     ("ok-false", new(200, """{"ok":false}"""), "api-error", null, null),
                     ("401", new(401, """{"ok":false,"description":"Unauthorized"}"""), "bad-token", null, null),
                     ("400-chat", new(400, """{"ok":false,"description":"Bad Request: chat not found"}"""), "chat-not-found", null, null),
                     ("403-blocked", new(403, """{"ok":false,"description":"Forbidden: bot was blocked by the user"}"""), "blocked", null, null),
                     ("429", new(429, """{"ok":false,"parameters":{"retry_after":7}}"""), "rate-limited", 7, null),
                     ("503", new(503, "{}"), "transport", null, null),
                     ("timeout", new(200, """{"ok":true,"result":{"message_id":1}}""", 2000), "transport", null, TimeSpan.FromMilliseconds(200)),
                 })
        {
            TransportResult result;
            try
            {
                (result, _) = await Send(response, timeout);
            }
            catch (Exception ex)
            {
                ex.ToString().ShouldNotContain(C545World.BotToken, Case.Sensitive, row + " exception carries no token");
                throw;
            }
            result.Accepted.ShouldBeFalse(row);
            result.ErrorClass.ShouldBe(errorClass, row);
            result.RetryAfterSeconds.ShouldBe(retryAfter, row + " retry after");
            (result.Error ?? "").ShouldNotContain(C545World.BotToken, Case.Sensitive, row + " error carries no token");
        }
    }

    [Test]
    public async Task C545_WindmillHttpApiRequestShape()
    {
        var options = new WatchdogOptions { WindmillBaseUrl = "http://windmill.test", WindmillToken = "wm-token", WindmillWorkspace = "mc" };
        var handler = new RecordingHttpHandler([
            new(200, "\"CE v1.700.2\""),
            new(200, """{"username":"watchdog"}"""),
            new(200, """{"hash":"h1","path":"u/lndcobra/antiphon_nightly_tests"}"""),
            new(200, """{"enabled":true}"""),
            new(200, """[{"worker_group":"desktop","last_ping":12}]"""),
            new(200, """[{"id":"c1","type":"CompletedJob","success":true,"schedule_path":"u/lndcobra/antiphon_nightly_tests","created_at":"2026-09-17T23:31:00Z","started_at":"2026-09-17T23:31:05Z","duration_ms":1000}]"""),
            new(200, """{"nativeRunId":"r18","localDueDate":"2026-09-18"}"""),
            new(200, "\"ssh exit 255\""),
        ]);
        using var http = new HttpClient(handler);
        var api = new WindmillHttpApi(http, options);
        (await api.GetVersionAsync(default)).Value.ShouldBe("CE v1.700.2", "version");
        (await api.WhoAmIAsync(default)).IsOk.ShouldBeTrue("whoami");
        (await api.GetScriptAsync(default)).Value!.Hash.ShouldBe("h1", "script");
        (await api.GetScheduleAsync(default)).Value!.Enabled.ShouldBeTrue("schedule");
        (await api.ListWorkersAsync(default)).Value!.Single().WorkerGroup.ShouldBe("desktop", "workers");
        var jobs = (await api.ListJobsAsync(default)).Value!;
        jobs.Single().Result.ShouldBeNull("list row has no result until get_result");
        jobs.Single().Kind.ShouldBe(JobKind.Completed, "completed kind");
        (await api.GetCompletedResultAsync("c1", default)).Value!["nativeRunId"]!.GetValue<string>().ShouldBe("r18", "get_result");
        (await api.GetLogsAsync("c1", default)).IsOk.ShouldBeTrue("logs");

        handler.Requests.Select(r => r.Uri.PathAndQuery).ShouldBe([
            "/api/version",
            "/api/users/whoami",
            "/api/w/mc/scripts/get/p/u/lndcobra/antiphon_nightly_tests",
            "/api/w/mc/schedules/get/u/lndcobra/antiphon_nightly_tests",
            "/api/workers/list?ping_since=900",
            "/api/w/mc/jobs/list?script_path_exact=u%2Flndcobra%2Fantiphon_nightly_tests&per_page=20",
            "/api/w/mc/jobs_u/completed/get_result/c1",
            "/api/w/mc/jobs_u/get_logs/c1",
        ], "request paths");
        handler.Requests.ShouldAllBe(r => r.Method == HttpMethod.Get, "all GET");
        handler.Requests[0].AuthorizationCount.ShouldBe(0, "version unauthenticated");
        handler.Requests.Skip(1).ShouldAllBe(r => r.AuthorizationCount == 1 && r.Authorization == "Bearer wm-token", "one bearer header");

        foreach (var (row, response, expected, timeout) in new (string, ScriptedResponse?, WindmillCallStatus, TimeSpan?)[]
                 {
                     ("5xx", new(502, "{}"), WindmillCallStatus.Unreachable, null),
                     ("timeout", new(200, "\"v\"", 2000), WindmillCallStatus.Unreachable, TimeSpan.FromMilliseconds(200)),
                     ("401", new(401, "{}"), WindmillCallStatus.Unauthorized, null),
                     ("403", new(403, "{}"), WindmillCallStatus.Unauthorized, null),
                 })
        {
            using var client = new HttpClient(new RecordingHttpHandler([response!])) { Timeout = timeout ?? TimeSpan.FromSeconds(15) };
            (await new WindmillHttpApi(client, options).WhoAmIAsync(default)).Status.ShouldBe(expected, row);
        }
        using var refused = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        var closedPort = ClosedPort();
        (await new WindmillHttpApi(refused, new WatchdogOptions { WindmillBaseUrl = $"http://127.0.0.1:{closedPort}", WindmillToken = "t" })
            .GetVersionAsync(default)).Status.ShouldBe(WindmillCallStatus.Unreachable, "connect failure");
    }

    private static int ClosedPort()
    {
        var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    [Test]
    public async Task C545_ReaderHeldWithoutSession()
    {
        var dir = Directory.CreateTempSubdirectory("antiphon-c545-reader").FullName;
        try
        {
            var configured = new WatchdogOptions
            {
                ReaderApiId = 12345, ReaderApiHash = "hash", ReaderPeer = "777", StateDir = dir, WorkingDirectory = dir,
                ReaderSessionPath = Path.Combine(dir, "reader.session"),
            };
            await using (var reader = new TelegramUserReader(configured))
            {
                var missing = await reader.ReadAsync(C545World.Start.UtcDateTime, default);
                missing.IsHeld.ShouldBeTrue("session missing is held");
                missing.HeldReason.ShouldBe("session-missing", "reason");
                missing.Observations.ShouldBeEmpty("no observations");
            }

            File.WriteAllText(Path.Combine(dir, "reader.session"), "");
            configured.ReaderApiId = 0;
            await using (var reader = new TelegramUserReader(configured))
            {
                var unconfigured = await reader.ReadAsync(C545World.Start.UtcDateTime, default);
                unconfigured.IsHeld.ShouldBeTrue("unconfigured is held");
                unconfigured.HeldReason.ShouldBe("reader-unconfigured", "reason");
            }

            var date = new DateTime(2026, 9, 18, 0, 41, 0, DateTimeKind.Utc);
            var message = new TL.Message { id = 17, peer_id = new TL.PeerUser { user_id = 777 }, date = date, message = "body" };
            RecipientObservation.From(message, 17).ShouldBe(new RecipientObservation("777", "17", date, "body", true), "read marker at id");
            RecipientObservation.From(message, 16).ReadByRecipient.ShouldBeFalse("read marker below id");
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch (IOException) { }
        }
    }
}
