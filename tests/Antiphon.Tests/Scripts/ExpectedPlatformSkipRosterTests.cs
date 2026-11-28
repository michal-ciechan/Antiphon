using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Antiphon.Nightly;
using Antiphon.Tests.Scripts.Nightly;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

// The Windows coverage gate's committed expected-skip roster. In process since unit 33 of
// docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md (it ran scripts/test-expected-platform-skips.ps1): the subject
// was already C# (NightlyChunkVerdict, NightlyTrx, NightlyExpectedSkips, SuiteCompletionParts), so each EPS row calls it here.
[Category("Integration")]
public sealed class ExpectedPlatformSkipRosterTests
{
    private const string S7 = "S7 real phone-home recipient fixture requires Linux; Windows handle qualification is separate.";
    private const string Cleanup = "Antiphon.Tests.Application.C0824RefOnlyCleanupTests.Remote_cleanup_still_uses_its_binding_and_generation";
    private const string Sync = "Antiphon.Tests.Application.C0824RefOnlySyncTests.Reported_tip_and_mirror_publication_keep_existing_contract";
    private const string Missing = "Antiphon.Tests.Missing.NoSuchTests.MissingMethod";
    private const string Trace = "Skipped: " + S7;

    private static string Repo => NightlyPlanCases.Repo;

    private static string RosterPath => Path.Combine(Repo, "tests", "expected-platform-skips.json");

    private static PlainObject Row(string identity, string outcome, string debugTrace) =>
        new(("Outcome", outcome), ("Identity", identity), ("DebugTrace", debugTrace));

    private static PlainObject Entry(string platform, string test, string reasonPrefix, string? reasonClass = null) =>
        reasonClass is null
            ? new(("platform", platform), ("test", test), ("reasonPrefix", reasonPrefix))
            : new(("platform", platform), ("test", test), ("reasonPrefix", reasonPrefix), ("reasonClass", reasonClass));

    private static object[] TestRoster() =>
    [
        Entry("windows", Cleanup, S7),
        Entry("windows", Sync, S7),
        Entry("windows", Missing, "gone"),
    ];

    // Invoke-EpsVerdict: the verdict judges by the roster it is given (null: the committed one).
    private static PlainObject Verdict(object[] rows, int processExit = 0, object[]? roster = null, string platform = "windows", string[]? known = null) =>
        (PlainObject)NightlyChunkVerdict.CoverageVerdict(rows, processExit, "a", "a", "r", "r", "p", "p", [], [], false, platform,
            roster ?? NightlyChunkVerdict.SkipRoster(RosterPath), known!);

    private static bool Flag(PlainObject verdict, string name) => PsValue.IsTrue(verdict[name]);

    private static string Joined(object? values) => string.Join(",", PsValue.AsArray(values!).Select(PsValue.Str));

    [Test]
    public void Herdr_scheduling_skips_are_accepted_only_on_the_opposite_platform()
    {
        const string testNamespace = "Antiphon.SessionRunner.Tests";
        const string shortName = "HerdrLabelFollowSchedulingTests";
        const string className = testNamespace + "." + shortName;
        var windowsSkips = new (string Method, string Reason)[]
        {
            ("Saved_snapshot_reads_while_writer_holds_mutex_on_unix", "Unix snapshot replacement permits a concurrent unlocked read."),
            ("Label_pump_readiness_waits_for_the_sweep_timer_on_unix", "The Unix fixture requires the label sweep timer before advancing fake time."),
        };
        var linuxSkips = new (string Method, string Reason)[]
        {
            ("Saved_snapshot_read_times_out_while_writer_holds_mutex", "Windows snapshot replacement sharing requires a bounded writer mutex wait in the reader."),
            ("Saved_snapshot_poll_expires_under_contention_and_rejects_late_success", "Only the Windows snapshot poll waits for the writer mutex within its deadline."),
            ("Saved_snapshot_mutex_name_matches_production_writer", "Only the Windows fixture opens the writer's named snapshot mutex."),
        };
        using var fixture = NightlyFixture.Create("eps-herdr");
        XNamespace ns = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010";
        var roster = NightlyChunkVerdict.SkipRoster(RosterPath);
        var rcRoster = roster.Select(entry => new RcSkipEntry(PsValue.Str(PsValue.Prop(entry, "test")),
            PsValue.Str(PsValue.Prop(entry, "platform")), PsValue.Str(PsValue.Prop(entry, "reasonPrefix")),
            NightlyChunkVerdict.ReasonClass(entry))).ToArray();
        var policy = NightlyPolicy.Read(Repo, "").Object;
        var sourceIndex = new NightlyCsMethodIndex(Path.Combine(Repo, "tests"));
        foreach (var (platform, nativePlatform, skips) in new[] { ("windows", "linux", windowsSkips), ("linux", "windows", linuxSkips) })
        {
            var trx = fixture.PathOf(platform + ".trx");
            new XElement(ns + "TestRun",
                new XElement(ns + "TestDefinitions", skips.Select(skip =>
                    new XElement(ns + "UnitTest", new XAttribute("id", skip.Method),
                        new XElement(ns + "TestMethod", new XAttribute("className", className), new XAttribute("name", skip.Method))))),
                new XElement(ns + "Results", skips.Select(skip =>
                    new XElement(ns + "UnitTestResult", new XAttribute("testId", skip.Method),
                        new XAttribute("testName", skip.Method), new XAttribute("outcome", "NotExecuted"),
                        new XElement(ns + "Output", new XElement(ns + "DebugTrace", "Skipped: " + skip.Reason))))),
                new XElement(ns + "ResultSummary", new XAttribute("outcome", "Completed"),
                    new XElement(ns + "Counters", new XAttribute("total", skips.Length), new XAttribute("executed", 0),
                        new XAttribute("passed", 0), new XAttribute("failed", 0), new XAttribute("notExecuted", skips.Length))))
                .Save(trx);
            var parsed = NightlyTrx.ReadIdentities(trx);
            var expectedIds = skips.Select(skip => className + "." + skip.Method).ToArray();
            parsed.Select(row => row.Identity).ShouldBe(expectedIds);
            var accepted = Verdict(parsed, processExit: 8, roster: roster, platform: platform);
            Flag(accepted, "coverageComplete").ShouldBeTrue(platform + ": " + Joined(accepted["reasons"]));
            Flag(accepted, "testsPassed").ShouldBeTrue(Joined(accepted["reasons"]));
            PsValue.AsArray(accepted["toleratedSkips"]).Select(row => PsValue.Str(PsValue.Prop(row, "identity"))).ShouldBe(expectedIds);

            var refused = Verdict(parsed, processExit: 8, roster: roster, platform: nativePlatform);
            Flag(refused, "coverageComplete").ShouldBeFalse();
            PsValue.AsArray(refused["reasons"]).Select(PsValue.Str)
                .ShouldContain("unexecuted-outcome:" + string.Join(",", expectedIds));

            var rcAccepted = RcChunkOutcome.Read(0, false, trx, rcRoster, platform);
            rcAccepted.Outcome.ShouldBe("Passed", rcAccepted.Reason);
            var rcRefused = RcChunkOutcome.Read(0, false, trx, rcRoster, nativePlatform);
            rcRefused.Outcome.ShouldBe("Failed");
            rcRefused.Reason.ShouldBe("unexecuted-outcome:" + string.Join(",", expectedIds));

            var herdrEntries = roster.Where(entry => expectedIds.Contains(PsValue.Str(PsValue.Prop(entry, "test")))).ToArray();
            herdrEntries.Length.ShouldBe(expectedIds.Length);
            herdrEntries.ShouldAllBe(entry => PsValue.Str(PsValue.Prop(entry, "platform")) == platform);

            var classExclusions = NightlyPolicy.ProfileExclusions(policy, "rc", "session-runner", platform)
                .Where(row => string.Equals(row.Class, shortName, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(row.Class, className, StringComparison.OrdinalIgnoreCase)).ToArray();
            classExclusions.SelectMany(row => row.Methods).Order(StringComparer.Ordinal)
                .ShouldBe(skips.Select(skip => skip.Method).Order(StringComparer.Ordinal));
            classExclusions.ShouldAllBe(row => row.Methods.Length > 0);
            var nativeExclusions = NightlyPolicy.ProfileExclusions(policy, "rc", "session-runner", nativePlatform);
            foreach (var (method, reason) in skips)
            {
                var node = new PlainObject(("ClassName", className), ("Method", method));
                NightlyPolicy.NodeExcluded(node, classExclusions).ShouldBeTrue(method);
                NightlyPolicy.NodeExcluded(node, nativeExclusions).ShouldBeFalse(method);
                sourceIndex.ReachableSkipTexts(shortName, method, testNamespace, fold: false).ShouldContain(reason, method);
            }
        }
    }

    [Test]
    public void Windows_qualification_tolerates_the_three_linux_only_witnesses()
    {
        const string handleProbe = "Antiphon.Tests.Scripts.ScriptHarnessHandleProbeTests.Early_exit_reports_process_and_receipt_evidence";
        var verdict = Verdict([
            Row(handleProbe, "NotExecuted", "Skipped: This fake child uses a Linux shell."),
            Row(handleProbe, "NotExecuted", "Skipped: This fake child uses a Linux shell."),
            Row("Antiphon.Tests.Checkpoints.EvidenceFolderTests.generated_replay_preserves_native_property_arguments",
                "NotExecuted", "Skipped: Native Linux shell argument witness."),
            Row("Antiphon.Tests.Checkpoints.CheckpointWarmBuildTests.fifo_seal_falls_back_cold_without_hanging",
                "NotExecuted", "Skipped: FIFO seal requires Linux mkfifo"),
        ]);

        Flag(verdict, "coverageComplete").ShouldBeTrue(Joined(verdict["reasons"]));
        PsValue.AsArray(verdict["toleratedSkips"]).Length.ShouldBe(4);
    }

    [Test]
    public void Verdict_accepts_only_rostered_skips()
    {
        var rows = new NightlyCaseRows();
        var roster = TestRoster();
        var skip = Row(Cleanup, "NotExecuted", Trace);

        var accepted = Verdict([skip], roster: roster);
        var acceptedReasons = Joined(accepted["reasons"]);
        rows.Check(Flag(accepted, "coverageComplete") && Flag(accepted, "testsPassed") && !acceptedReasons.Contains("unexecuted-outcome"),
            "EPS expected-skip-accepted", acceptedReasons);

        var longer = Verdict([Row(Cleanup, "NotExecuted", Trace + " extra detail")], roster: roster);
        var longerReasons = Joined(longer["reasons"]);
        rows.Check(!Flag(longer, "coverageComplete") && longerReasons.Contains("unexecuted-outcome"), "EPS prefix-extension-rejected", longerReasons);

        var noSpace = Verdict([Row(Cleanup, "NotExecuted", "Skipped:" + S7)], roster: roster);
        var noSpaceReasons = Joined(noSpace["reasons"]);
        rows.Check(!Flag(noSpace, "coverageComplete") && noSpaceReasons.Contains("unexecuted-outcome"), "EPS skipped-without-space-rejected", noSpaceReasons);

        var loaded = Verdict([skip]);
        var loadedReasons = Joined(loaded["reasons"]);
        rows.Check(Flag(loaded, "coverageComplete") && Flag(loaded, "testsPassed") && !loadedReasons.Contains("unexecuted-outcome"),
            "EPS autoload-roster-accepted", loadedReasons);

        var unexpected = Verdict([Row("Antiphon.Tests.Scripts.SampleTests.NotOnTheRoster", "NotExecuted", Trace)], roster: roster);
        var unexpectedReasons = Joined(unexpected["reasons"]);
        rows.Check(!Flag(unexpected, "coverageComplete") && Regex.IsMatch(unexpectedReasons, @"unexecuted-outcome:Antiphon\.Tests\.Scripts\.SampleTests\.NotOnTheRoster"),
            "EPS unexpected-skip-rejected", unexpectedReasons);

        var mismatch = Verdict([Row(Cleanup, "NotExecuted", "Skipped: a different reason")], 8, roster);
        var mismatchReasons = Joined(mismatch["reasons"]);
        rows.Check(!Flag(mismatch, "coverageComplete") && !Flag(mismatch, "testsPassed") && mismatchReasons.Contains("unexecuted-outcome")
            && mismatchReasons.Contains("process-exit-8") && !Flag(mismatch, "processExitTolerated"), "EPS reason-mismatch-rejected", mismatchReasons);

        var onlySkip = Verdict([skip], 8, roster);
        var onlyReasons = Joined(onlySkip["reasons"]);
        rows.Check(Flag(onlySkip, "coverageComplete") && Flag(onlySkip, "testsPassed") && Flag(onlySkip, "processExitTolerated")
            && !onlyReasons.Contains("process-exit-8") && !onlyReasons.Contains("unexecuted-outcome"), "EPS all-skip-exit-8-tolerated", onlyReasons);

        var mixed = Verdict([skip, Row("Antiphon.Tests.Scripts.SampleTests.Ran", "Passed", "")], 8, roster);
        var mixedReasons = Joined(mixed["reasons"]);
        rows.Check(!Flag(mixed, "testsPassed") && mixedReasons.Contains("process-exit-8") && !Flag(mixed, "processExitTolerated"),
            "EPS mixed-exit-8-rejected", mixedReasons);

        var exitOne = Verdict([skip], 1, roster);
        var exitOneReasons = Joined(exitOne["reasons"]);
        rows.Check(!Flag(exitOne, "testsPassed") && exitOneReasons.Contains("process-exit-1") && !Flag(exitOne, "processExitTolerated"),
            "EPS other-nonzero-still-fails", exitOneReasons);

        var ran = Verdict([Row(Cleanup, "Passed", "")], roster: roster);
        var ranReasons = Joined(ran["reasons"]);
        rows.Check(Flag(ran, "coverageComplete") && Flag(ran, "testsPassed") && !ranReasons.Contains("unexecuted-outcome"),
            "EPS ran-roster-entry-stays-complete", ranReasons);

        var group = Verdict([Row("Antiphon.Tests.Scripts.SampleTests.Ran", "Passed", ""), skip], roster: roster);
        var groupReasons = Joined(group["reasons"]);
        rows.Check(Flag(group, "coverageComplete") && Flag(group, "testsPassed") && !groupReasons.Contains("unexecuted-outcome"),
            "EPS passed-with-expected-skip-complete", groupReasons);

        var stale = Verdict([Row(Cleanup, "Passed", "")], roster: roster, known: [Cleanup, Sync]);
        var staleText = Joined(stale["warnings"]);
        var staleReasons = Joined(stale["reasons"]);
        rows.Check(Flag(stale, "coverageComplete") && Flag(stale, "testsPassed") && staleText == "stale-roster:" + Missing
            && !staleReasons.Contains("stale-roster") && !staleReasons.Contains("unexecuted-outcome"), "EPS stale-roster-warns", staleText + " | " + staleReasons);

        TrxDebugTrace(rows);
        Census(rows);
        LiveContext(rows);

        var digest = PsValue.Str(accepted["rosterDigest"]);
        var tolerated = PsValue.AsArray(accepted["toleratedSkips"]);
        var toleratedIds = tolerated.Select(t => PsValue.Str(PsValue.Prop(t, "identity"))).ToArray();
        var toleratedClass = tolerated.Length == 1 ? PsValue.Str(PsValue.Prop(tolerated[0], "reasonClass")) : "";
        var digestAgain = PsValue.Str(Verdict([skip], roster: roster)["rosterDigest"]);
        rows.Check(digest.Length == 64 && digest == digestAgain && toleratedIds.Length == 1 && toleratedIds[0] == Cleanup && toleratedClass == "platform",
            "EPS tolerated-list-and-digest", "digest=" + digest + " ids=" + string.Join(",", toleratedIds) + " class=" + toleratedClass);

        // W2-nightly S13: suite completion (the stale-roster warnings) is Antiphon.Nightly/NightlySuiteRunner.cs.
        var implText = File.ReadAllText(Path.Combine(Repo, "scripts", "lib", "Antiphon.Nightly", "NightlySuiteRunner.cs"));
        var knownIds = SuiteCompletionParts.KnownIdentities([new PlainObject(("ClassName", "Antiphon.Tests.Application.C0824RefOnlyCleanupTests"),
            ("Method", "Remote_cleanup_still_uses_its_binding_and_generation"))]);
        var knownWarnings = NightlyChunkVerdict.StaleWarnings("windows", roster, knownIds);
        var wired = implText.Contains("SuiteCompletionParts.KnownIdentities(", StringComparison.Ordinal);
        rows.Check(knownIds.Contains(Cleanup) && knownWarnings.Contains("stale-roster:" + Sync) && !knownWarnings.Contains("stale-roster:" + Cleanup) && wired,
            "EPS discovery-known-identities", "known=" + string.Join(",", knownIds) + " warnings=" + string.Join(",", knownWarnings) + " wired=" + wired);

        rows.ShouldAllPass(23);
    }

    // EPS trx-debug-trace: the TRX reader keeps the skip's DebugTrace, and an all-skip exit 8 from it is tolerated.
    private static void TrxDebugTrace(NightlyCaseRows rows)
    {
        var root = Path.Combine(Path.GetTempPath(), "eps-trx-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var trx = Path.Combine(root, "expected-skip.trx");
            File.WriteAllText(trx, """
                <?xml version="1.0" encoding="utf-8"?>
                <TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
                  <TestDefinitions>
                    <UnitTest id="t1" name="Antiphon.Tests.Application.C0824RefOnlyCleanupTests.Remote_cleanup_still_uses_its_binding_and_generation">
                      <TestMethod className="Antiphon.Tests.Application.C0824RefOnlyCleanupTests" name="Remote_cleanup_still_uses_its_binding_and_generation" />
                    </UnitTest>
                  </TestDefinitions>
                  <Results>
                    <UnitTestResult testId="t1" testName="Antiphon.Tests.Application.C0824RefOnlyCleanupTests.Remote_cleanup_still_uses_its_binding_and_generation" outcome="NotExecuted">
                      <Output><DebugTrace>Skipped: S7 real phone-home recipient fixture requires Linux; Windows handle qualification is separate.</DebugTrace></Output>
                    </UnitTestResult>
                  </Results>
                </TestRun>
                """);
            var parsed = NightlyTrx.ReadIdentities(trx);
            var parsedTrace = parsed.Length == 1 ? parsed[0].DebugTrace : "";
            PlainObject? fromTrx = parsed.Length == 1 ? Verdict([parsed[0]], 8) : null;
            var fromReasons = fromTrx is null ? "" : Joined(fromTrx["reasons"]);
            rows.Check(parsed.Length == 1 && parsed[0].Outcome == "NotExecuted" && parsed[0].Identity == Cleanup && parsedTrace == Trace && fromTrx is not null
                && Flag(fromTrx, "coverageComplete") && Flag(fromTrx, "testsPassed") && Flag(fromTrx, "processExitTolerated"),
                "EPS trx-debug-trace", parsedTrace + " | " + fromReasons);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    // The committed roster against tests/: its Windows rows, their source and reason text, and WindowsUnitSkipRoster.cs.
    private static void Census(NightlyCaseRows rows)
    {
        var committed = NightlyChunkVerdict.SkipRoster(RosterPath);
        var windows = committed.Where(e => PsValue.Str(PsValue.Prop(e, "platform")) == "windows").ToArray();
        var unique = windows.Select(e => PsValue.Str(PsValue.Prop(e, "test"))).Distinct(StringComparer.Ordinal).Count();
        var gaps = NightlyExpectedSkips.SourceGaps(Repo, "windows", committed, null);
        rows.Check(windows.Length == 37 && unique == windows.Length && gaps.Length == 0, "EPS census-matches-source",
            "windows=" + windows.Length + " unique=" + unique + " gaps=" + string.Join(",", gaps));

        var planted = committed.Append(Entry("windows", Missing, "gone")).ToArray();
        var plantedGaps = NightlyExpectedSkips.SourceGaps(Repo, "windows", planted, null);
        rows.Check(plantedGaps.Length == 1 && plantedGaps[0] == Missing, "EPS census-reports-missing", string.Join(",", plantedGaps));

        var reasonGaps = NightlyExpectedSkips.ReasonGaps(Repo, "windows", committed, null);
        rows.Check(reasonGaps.Length == 0, "EPS reason-text-matches-source", string.Join(",", reasonGaps));

        const string wrongId = "Antiphon.Tests.Scripts.RemoteScriptContractTests.C1105_Resume_extraction_is_closed";
        var wrongReason = committed.Select(e => PsValue.Str(PsValue.Prop(e, "test")) == wrongId
            ? Entry("windows", wrongId, "not-the-method-skip-text") : e).ToArray();
        var wrongGaps = NightlyExpectedSkips.ReasonGaps(Repo, "windows", wrongReason, null);
        rows.Check(wrongGaps.Contains(wrongId), "EPS reason-text-reports-mismatch", string.Join(",", wrongGaps));

        var drift = NightlyExpectedSkips.RosterDrift(Repo, committed, null);
        var overlap = PsValue.AsArray(PsValue.Prop(drift, "Overlap"));
        var mismatches = PsValue.AsArray(PsValue.Prop(drift, "Mismatches")).Select(PsValue.Str).ToArray();
        rows.Check(overlap.Length == 9 && mismatches.Length == 0, "EPS roster-drift-stable",
            "overlap=" + overlap.Length + " mismatches=" + string.Join(",", mismatches));

        var drifted = committed.Select(e => PsValue.Str(PsValue.Prop(e, "test")) == wrongId
            ? Entry(PsValue.Str(PsValue.Prop(e, "platform")), wrongId, "drifted-reason", "platform") : e).ToArray();
        var driftBad = PsValue.AsArray(PsValue.Prop(NightlyExpectedSkips.RosterDrift(Repo, drifted, null), "Mismatches")).Select(PsValue.Str).ToArray();
        rows.Check(driftBad.Contains(wrongId), "EPS roster-drift-reports-mismatch", string.Join(",", driftBad));
    }

    // A live-context entry tolerates its skip on any platform. The committed roster carries live-context rows, and none for
    // the C680 canary: unit 60 moved it into the live Claude project, which no nightly or rc suite runs.
    private static void LiveContext(NightlyCaseRows rows)
    {
        const string c680 = "Antiphon.LiveClaude.Tests.ClaudeDialogWatchdogCanaryTests.C680_LiveNudgeRefusesPermissionAndQuestionDialogs";
        var liveRoster = new object[] { Entry("windows", c680, "20 missing-context", "live-context") };
        var liveLinux = Verdict([Row(c680, "NotExecuted", "Skipped: 20 missing-context")], roster: liveRoster, platform: "linux");
        var liveReasons = Joined(liveLinux["reasons"]);
        rows.Check(Flag(liveLinux, "coverageComplete") && Flag(liveLinux, "testsPassed") && !liveReasons.Contains("unexecuted-outcome"),
            "EPS live-context-any-platform", liveReasons);

        var committed = NightlyChunkVerdict.SkipRoster(RosterPath);
        var c680Rows = committed.Where(e => PsValue.Str(PsValue.Prop(e, "test")).Contains("ClaudeDialogWatchdogCanaryTests", StringComparison.Ordinal)).ToArray();
        var liveRows = committed.Where(e => PsValue.Str(PsValue.Prop(e, "reasonClass")) == "live-context").ToArray();
        rows.Check(c680Rows.Length == 0 && liveRows.Length > 0, "EPS live-context-labeled", "c680=" + c680Rows.Length + " live-context=" + liveRows.Length);
    }

    /// <summary>
    /// EPS title-omits-tolerated-exit (from Test-EpsVerdict; in-process since W2-nightly S06): a dry-run report of a run
    /// whose tolerated chunk exited 8 names only the real failures in its title and shows the roster digest and the
    /// tolerated skip in its body.
    /// </summary>
    [Test]
    public void Report_title_omits_the_tolerated_exit()
    {
        const string cleanup = "Antiphon.Tests.Application.C0824RefOnlyCleanupTests.Remote_cleanup_still_uses_its_binding_and_generation";
        var digest = new string('a', 64);
        using var fixture = NightlyFixture.Create("eps-report");
        var summary = NightlyReportTests.Save(new JsonObject
        {
            ["startedAt"] = "2026-11-10T00:00:00Z", ["succeeded"] = false, ["outcome"] = "TESTS", ["sha"] = "abc", ["gitRef"] = "rc/test", ["profile"] = "rc",
            ["durationSeconds"] = 1, ["builds"] = new JsonArray(),
            ["suites"] = new JsonArray(
                new JsonObject { ["id"] = "antiphon", ["name"] = "antiphon chunk-a", ["result"] = "pass", ["exitCode"] = 8, ["processExitTolerated"] = true, ["skipped"] = false, ["failed"] = 0 },
                new JsonObject { ["id"] = "antiphon", ["name"] = "antiphon chunk-b", ["result"] = "FAIL", ["exitCode"] = 1, ["skipped"] = false, ["failed"] = 2 }),
            ["expectedSkipRosterDigest"] = digest,
            ["toleratedSkips"] = new JsonArray(new JsonObject { ["identity"] = cleanup, ["reasonClass"] = "platform" }),
        }, fixture.Root);
        NightlyReportHttpResponse Board(string method, string uri, IReadOnlyDictionary<string, string> headers, string? body)
        {
            if (uri.EndsWith("/api/boards", StringComparison.Ordinal)) return new(200, "{\"id\":\"board-1\",\"name\":\"Antiphon\"}");
            if (uri.Contains("/columns", StringComparison.Ordinal)) return new(200, "[]");
            if (uri.Contains("/api/cards", StringComparison.Ordinal)) return new(200, "{\"cards\":[],\"truncated\":false}");
            return new(200, "");
        }
        var output = new StringWriter();
        var report = NightlyReport.Run(new NightlyReportOptions { Summary = summary, DryRun = true, Board = "Antiphon", Api = "http://nightly.test" },
            new NightlyReportSeams { Clock = TimeProvider.System, Http = Board, Environment = _ => null!, Out = output, Error = output });

        var titleOk = report.Title.Contains("2 failed", StringComparison.OrdinalIgnoreCase) && !report.Title.Contains("exit 8", StringComparison.OrdinalIgnoreCase)
            && report.Body.Contains(digest, StringComparison.Ordinal) && report.Body.Contains(cleanup, StringComparison.Ordinal)
            && report.Body.Contains("Tolerated skips", StringComparison.Ordinal);
        titleOk.ShouldBeTrue("EPS title-omits-tolerated-exit: " + report.Title + " | " + report.Body[..Math.Min(400, report.Body.Length)] + " | " + output);
    }
}
