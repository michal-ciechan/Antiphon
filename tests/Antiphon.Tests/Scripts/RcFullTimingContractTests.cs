using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Antiphon.Nightly;
using Antiphon.Tests.TestHelpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

// CARD-1514 S1. Portable contracts for the rc timing instrumentation.
// Native Windows owner proof is CP-2 and is not discharged here. The manifests, the summary and the phase-timing
// variables are the production C# (W2-nightly S09), called in-process, as are the owner observation and the setup
// phases (S12's NightlyChunkRunner, since S16); only the call-site case still runs the still-PowerShell nightly-tests
// executor (scripts/lib/nightly-tests-impl.ps1, Invoke-AntiphonNightlyTests) in a pwsh child.
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class RcFullTimingContractTests
{
    // W2-nightly S16: Publish-NightlyChunkObservation is NightlyChunkRunner.ObservationVerdict and
    // TestPhaseManifests.WriteExecuted (S09/S12), called in-process with the owner record the pwsh child built.
    [Test]
    [Timeout(60_000)]
    public Task OwnerObservationIsPersisted(CancellationToken cancellationToken)
    {
        var work = FreshRoot();
        var scope = JournalFixtures.Scope();
        object OwnerRun(string retainedImage) => new PlainObject(
            ("ExitCode", 0),
            ("RootStartedAtUtc", "2026-10-29T00:00:00.0000000Z"),
            ("RootExitedAtUtc", "2026-10-29T00:00:02.0000000Z"),
            ("JobEmptyObserved", true),
            ("RemainingProcessCount", 0),
            ("LaunchMilliseconds", 10),
            ("ExecutionMilliseconds", 20),
            ("CleanupMilliseconds", 4),
            ("DrainMilliseconds", 7),
            ("ChildrenExited", true),
            ("OutputDrained", true),
            ("MissedDescendantPids", new object[] { 45, 46 }),
            ("DescendantIdentities", new object[] { "43:2026-10-29T00:00:00.1000000Z", "44:2026-10-29T00:00:00.2000000Z", "45:2026-10-29T00:00:00.3000000Z" }),
            ("DescendantImages", new object[] { retainedImage, "", "LiveName" }),
            ("DescendantExitedAtUtc", new object[] { "2026-10-29T00:00:01.5000000Z", "", "" }));
        void Publish(string image, string attempt)
        {
            var verdict = NightlyChunkRunner.ObservationVerdict(OwnerRun(image), true, true);
            TestPhaseManifests.WriteExecuted(verdict, work, Path.Combine(work, "missing.trx"), "antiphon", "all-group-1", "run-1", attempt, 1, null, scope);
        }
        Publish("OwnedChild.exe", "1");
        Publish("Other.exe", "2");
        var firstPath = Path.Combine(work, "timing", "owner-antiphon-all-group-1-run-1-1.json");
        var secondPath = Path.Combine(work, "timing", "owner-antiphon-all-group-1-run-1-2.json");
        File.Exists(firstPath).ShouldBeTrue("root exit and descendant image were not persisted");
        var raw = File.ReadAllText(firstPath);
        using var first = JsonDocument.Parse(raw);
        var doc = first.RootElement;
        string Image(JsonElement owner, int pid) =>
            owner.TryGetProperty("descendants", out var rows) ? rows.EnumerateArray().Where(r => r.GetProperty("pid").GetInt32() == pid)
                .Select(r => r.TryGetProperty("image", out var i) ? i.GetString() ?? "" : "").FirstOrDefault() ?? "" : "";
        var missed = doc.GetProperty("missedDescendantPids").EnumerateArray().Select(p => p.GetInt32()).ToList();
        Image(doc, 43).ShouldBe("OwnedChild.exe");
        Image(doc, 44).ShouldBe("unknown");
        Image(doc, 45).ShouldBe("unknown", "missed pid was left as a live image");
        Image(doc, 46).ShouldBe("unknown", "pid that exited before capture was dropped");
        missed.ShouldContain(45);
        missed.ShouldNotContain(44, "blank image is not a missed pid");
        doc.GetProperty("jobEmpty").GetBoolean().ShouldBeTrue();
        doc.GetProperty("drainMilliseconds").GetDouble().ShouldBe(7);
        doc.GetProperty("executionMilliseconds").GetDouble().ShouldBe(20);
        doc.GetProperty("launchMilliseconds").GetDouble().ShouldBe(10);
        doc.GetProperty("cleanupMilliseconds").GetDouble().ShouldBe(4);
        doc.GetProperty("childrenExited").GetBoolean().ShouldBeTrue();
        doc.GetProperty("outputDrained").GetBoolean().ShouldBeTrue();
        doc.GetProperty("remainingProcessCount").GetInt32().ShouldBe(0);
        System.Text.RegularExpressions.Regex.Match(raw, "\"rootExitedAtUtc\"\\s*:\\s*\"([^\"]*)\"").Groups[1].Value.ShouldBe("2026-10-29T00:00:02.0000000Z");
        System.Text.RegularExpressions.Regex.Match(raw, "\"rootStartedAtUtc\"\\s*:\\s*\"([^\"]*)\"").Groups[1].Value.ShouldBe("2026-10-29T00:00:00.0000000Z");
        doc.GetProperty("runId").GetString().ShouldBe("run-1");
        doc.GetProperty("attempt").GetString().ShouldBe("1");
        File.Exists(secondPath).ShouldBeTrue("rerun overwrote the first owner observation");
        using var second = JsonDocument.Parse(File.ReadAllText(secondPath));
        Image(second.RootElement, 43).ShouldBe("Other.exe", "rerun overwrote the first owner observation");
        Directory.Delete(work, recursive: true);
        return Task.CompletedTask;
    }

    // W2-nightly S09: the planned manifests, launch completion and the run summary are the production C#
    // (TestPhaseManifests, TestPhaseRunSummary), called in-process.
    [Test]
    [Timeout(60_000)]
    public Task PlansRetainEverySuite(CancellationToken cancellationToken)
    {
        var work = FreshRoot();
        var timing = Path.Combine(work, "timing");
        var scope = JournalFixtures.Scope();
        var costPath = Path.Combine(C1514Support.RepoRoot(), "scripts", "lib", "nightly-class-costs.json");
        void Plan(string suite, string assembly, string className, string uid)
        {
            var type = className.Split('.')[^1];
            object node = new PlainObject(("ClassName", className), ("Type", type), ("Method", "One"), ("Uid", uid), ("State", "NotStarted"),
                ("Categories", Array.Empty<object>()), ("Excluded", false));
            object group = new PlainObject(("id", "all-group-1"), ("classes", new object[] { type }), ("seconds", 3), ("exactFilter", ""));
            TestPhaseManifests.WritePlanned(new TestPhasePlannedRequest
            {
                LogRoot = work, RunId = "run-1", Attempt = "1", Suite = suite, SourceDigest = "src", AssemblyDigest = "asm", PolicyDigest = "pol",
                CostFileDigest = "cost", AssemblyId = assembly, Groups = new[] { group }, DiscoveryNodes = new[] { node }, ProfileExclusions = Array.Empty<object>(),
                ClassCostPath = costPath,
            }, scope);
        }
        Plan("antiphon", "Antiphon.Tests", "Antiphon.Tests.Scripts.RcAntiphonSampleTests", "uid-antiphon");
        Plan("e2e", "Antiphon.E2E", "Antiphon.Tests.E2E.RcE2ESampleTests", "uid-e2e");
        void Case(string suite, string id)
        {
            var assembly = suite == "e2e" ? "Antiphon.E2E" : "Antiphon.Tests";
            File.WriteAllText(Path.Combine(timing, "cases-" + suite + "-n1.jsonl"), Header(suite, assembly) + "\n" + CompleteLine(id, "Passed", 1) + "\n");
            var trx = Path.Combine(work, suite + ".trx");
            File.WriteAllText(trx, Trx(TrxRow(id, "Passed", "C1514_NATIVE body " + id)));
            var verdict = new PlainObject(("ExitCode", 0), ("coverageComplete", true), ("testsPassed", true));
            TestPhaseManifests.CompleteLaunch(verdict, work, trx, suite, "all-group-1", "run-1", "1", 1, null, scope).ShouldBeSameAs(verdict);
        }
        Case("antiphon", "antiphon-case");
        Case("e2e", "e2e-case");
        TestPhaseRunSummary.Run(work, 20, scope);

        string[] Planned(string name)
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(timing, name)));
            return doc.RootElement.GetProperty("groups").EnumerateArray().SelectMany(g => g.GetProperty("plannedTestIds").EnumerateArray()).Select(i => i.GetString() ?? "").ToArray();
        }
        Planned("groups-planned-antiphon.json").ShouldBe(new[] { "uid-antiphon" });
        Planned("groups-planned-e2e.json").ShouldBe(new[] { "uid-e2e" });
        using (var index = JsonDocument.Parse(File.ReadAllText(Path.Combine(timing, "groups-planned-index.json"))))
        {
            var suites = index.RootElement.GetProperty("suites").EnumerateArray().ToList();
            suites.Count.ShouldBe(2, "old singleton overwrite");
            foreach (var (suite, assembly) in new[] { ("antiphon", "Antiphon.Tests"), ("e2e", "Antiphon.E2E") })
            {
                var row = suites.Single(r => r.GetProperty("suite").GetString() == suite);
                row.GetProperty("assembly").GetString().ShouldBe(assembly);
                row.GetProperty("sourceDigest").GetString().ShouldBe("src", "index dropped source digest");
            }
        }
        using var summary = JsonDocument.Parse(File.ReadAllText(Path.Combine(timing, "summary.json")));
        summary.RootElement.GetProperty("quality").EnumerateArray().Select(q => q.GetString()).ShouldNotContain("stale-input-not-joined");
        var tests = summary.RootElement.GetProperty("tests").EnumerateArray().ToList();
        foreach (var (id, suite) in new[] { ("antiphon-case", "antiphon"), ("e2e-case", "e2e") })
        {
            var row = tests.Single(t => t.GetProperty("testId").GetString() == id);
            row.GetProperty("suite").GetString().ShouldBe(suite);
            row.GetProperty("joined").GetBoolean().ShouldBeTrue("cross-suite plan was stale");
        }
        return Task.CompletedTask;
    }

    // W2-nightly S03: the host-stats library is the compiled QualificationHostStats (still not wired), called in-process.
    [Test]
    [Timeout(60_000)]
    public Task HostSamplesPreserveUnknownAndStop(CancellationToken cancellationToken)
    {
        var stopped = false;
        var reads = 0;
        var readsAfterStop = 0;
        QualificationHostSample Probe()
        {
            if (stopped) { readsAfterStop++; return null!; }
            reads++;
            return reads == 1
                ? new QualificationHostSample("2026-10-29T00:00:00.0000000Z", null, 4, 10, null, null)
                : new QualificationHostSample("2026-10-29T00:00:30.0000000Z", 5, 4, null, null, null);
        }
        var sampled = QualificationHostStats.Sample(Probe, 2, () => stopped = true);
        sampled.Samples.Count.ShouldBe(2);
        sampled.Samples[0].CpuPercent.ShouldBeNull();
        sampled.Samples[1].CpuPercent.ShouldBe(5);
        sampled.Samples[1].MemoryAvailableBytes.ShouldBeNull();
        readsAfterStop.ShouldBe(0);

        (string Decision, bool LoaderCalled, bool Admission, bool Diagnostic) Matrix(string edition, string framework)
        {
            var called = false;
            var gate = QualificationHostStats.TestRuntime(edition, framework, () => { called = true; return "loaded"; });
            return (gate.Decision, called, gate.MeasurementAdmission, gate.DiagnosticComplete);
        }
        var desktop = Matrix("Desktop", ".NET Framework 4.8.1");
        var net8 = Matrix("Core", ".NET 8.0.11");
        var net9 = Matrix("Core", ".NET 9.0.8");
        desktop.Decision.ShouldBe("incompatible-runtime");
        net8.Decision.ShouldBe("incompatible-runtime");
        net9.Decision.ShouldBe("compatible");
        desktop.LoaderCalled.ShouldBeFalse();
        net8.LoaderCalled.ShouldBeFalse();
        desktop.Admission.ShouldBeFalse();
        net8.Admission.ShouldBeFalse();
        desktop.Diagnostic.ShouldBeTrue();

        var work = FreshRoot();
        QualificationHostStatsProvider? blown = null;
        Should.NotThrow(() => blown = QualificationHostStats.NewProvider(Path.Combine(work, "probe.dll"),
            () => throw new InvalidOperationException("loader-fault"), "Core", ".NET 9.0.8"), "loader fault escaped");
        (blown!.LoadReason == "loader-failed" && !blown.MeasurementAdmission).ShouldBeTrue("loader fault escaped");

        // No production file calls the compiled library, and the nightly executor loads no probe.
        var repo = C1514Support.RepoRoot();
        var own = Path.GetFullPath(Path.Combine(repo, "scripts", "lib", "Antiphon.Nightly", "QualificationHostStats.cs"));
        var callers = ProductionSources(repo)
            .Where(path => !string.Equals(Path.GetFullPath(path), own, StringComparison.OrdinalIgnoreCase))
            .Where(path => File.ReadAllText(path).Contains("QualificationHostStats", StringComparison.Ordinal))
            .Select(path => Path.GetRelativePath(repo, path))
            .ToArray();
        callers.ShouldBeEmpty("production still loads the host-stats probe");
        var implText = File.ReadAllText(Path.Combine(repo, "scripts", "lib", "nightly-tests-impl.ps1"));
        var productionUnwired = !implText.Contains("function Start-NightlyHostSampling", StringComparison.OrdinalIgnoreCase)
            && !implText.Contains("qualification-host-stats", StringComparison.Ordinal)
            && !implText.Contains("Antiphon.SessionRunner.dll", StringComparison.Ordinal)
            && !implText.Contains("Add-Type", StringComparison.Ordinal);
        productionUnwired.ShouldBeTrue("production still loads the host-stats probe");
        Directory.Delete(work, recursive: true);
        return Task.CompletedTask;
    }

    // Production sources that could call a type: the scripts (not their fixtures or test drivers), the server, src
    // and tools, without build output; and the repository root's scripts.
    private static IEnumerable<string> ProductionSources(string repo)
    {
        var extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".cs", ".ps1", ".psm1", ".cmd", ".sh" };
        var skipped = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "bin", "obj", "node_modules", ".git", "fixtures" };
        foreach (var file in Directory.EnumerateFiles(repo, "*.ps1"))
            yield return file;
        var pending = new Stack<string>(new[] { "scripts", "server", "src", "tools" }.Select(top => Path.Combine(repo, top)).Where(Directory.Exists));
        while (pending.Count > 0)
        {
            var directory = pending.Pop();
            foreach (var child in Directory.EnumerateDirectories(directory))
            {
                var name = Path.GetFileName(child);
                if (skipped.Contains(name) || name.StartsWith("bin-", StringComparison.OrdinalIgnoreCase)) continue;
                pending.Push(child);
            }
            foreach (var file in Directory.EnumerateFiles(directory))
            {
                if (!extensions.Contains(Path.GetExtension(file))) continue;
                if (Path.GetFileName(file).StartsWith("test-", StringComparison.OrdinalIgnoreCase)) continue;
                yield return file;
            }
        }
    }

    [Test]
    [Timeout(120_000)]
    public async Task ProductionCallSitesPersistDiscoveryAndSetup(CancellationToken cancellationToken)
    {
        var root = FreshRoot();
        var observed = await Run(CallSiteScript, cancellationToken, root);
        observed.GetProperty("exitCode").GetInt32().ShouldBe(0, observed.GetProperty("text").GetString());
        observed.GetProperty("discoveryNull").GetBoolean().ShouldBeTrue("call site copied the suite constant");
        observed.GetProperty("setupNpmCi").GetBoolean().ShouldBeTrue("npm-ci setup phase was not written");
        observed.GetProperty("setupNpmBuild").GetBoolean().ShouldBeTrue("npm-build setup phase was not written");
        observed.GetProperty("setupLint").GetBoolean().ShouldBeTrue("lint setup phase was not written");
        observed.GetProperty("setupDotnet").GetBoolean().ShouldBeTrue("dotnet-build setup phase was not written");
        observed.GetProperty("planningPresent").GetBoolean().ShouldBeTrue("planning-<suite> was not written");
        observed.GetProperty("planningSeconds").GetDouble().ShouldBeGreaterThan(0, "planning-<suite> seconds were zero");
        observed.GetProperty("successPlanningPresent").GetBoolean().ShouldBeTrue("rc success path did not write planning-<suite>");
        observed.GetProperty("successPlanningSeconds").GetDouble().ShouldBeGreaterThan(0, "rc success path planning seconds were zero");
        observed.GetProperty("supplementalIsMarker").GetBoolean().ShouldBeTrue("supplemental discovery was not the marker");
    }

    // W2-nightly S09: Enable-TestPhaseExecutionEnvironment is TestPhaseEnvironment.Enable, called in-process with the
    // inherited root (this process's ANTIPHON_TEST_PHASE_ROOT in production) as an argument.
    [Test]
    public void PhaseRootAbsentEmptyAndSet()
    {
        var work = FreshRoot();
        var absent = new System.Collections.Hashtable(StringComparer.OrdinalIgnoreCase) { ["ANTIPHON_TEST_PHASE_ROOT"] = "stale" };
        TestPhaseEnvironment.Enable(absent, work, "run-1", "g", "1", "antiphon", "local", "", null!);
        absent.ContainsKey("ANTIPHON_TEST_PHASE_ROOT").ShouldBeFalse("absent phase root was copied");
        var empty = new System.Collections.Hashtable(StringComparer.OrdinalIgnoreCase) { ["ANTIPHON_TEST_PHASE_ROOT"] = "stale" };
        TestPhaseEnvironment.Enable(empty, work, "run-1", "g", "1", "antiphon", "local", "   ", "");
        empty.ContainsKey("ANTIPHON_TEST_PHASE_ROOT").ShouldBeFalse("empty phase root was copied");
        var setRoot = Path.Combine(work, "phase-root");
        var set = new System.Collections.Hashtable(StringComparer.OrdinalIgnoreCase);
        TestPhaseEnvironment.Enable(set, work, "run-1", "g", "1", "antiphon", "local", "", setRoot).ShouldBeSameAs(set);
        set["ANTIPHON_TEST_PHASE_ROOT"].ShouldBe(setRoot, "set phase root was not copied");
        set["ANTIPHON_TEST_PHASE_RUN_ID"].ShouldBe("run-1", "set phase root was not copied");
        var made = TestPhaseEnvironment.Enable(null!, work, "run-1", "g", "1", "antiphon", "rc", "", "");
        made["ANTIPHON_TEST_PHASE_ROOT"].ShouldBe(Path.Combine(work, "timing"), "qualification default root");
    }

    // W2-nightly S11: the production chunk discovery is NightlyTestsPlan.ChunkDiscoverySeconds, called in-process.
    [Test]
    [Timeout(60_000)]
    public Task DiscoveryIsNotCopiedFromSuite(CancellationToken cancellationToken)
    {
        var missing = NightlyTestsPlan.ChunkDiscoverySeconds(21.377, null, false);
        var marked = NightlyTestsPlan.ChunkDiscoverySeconds(21.377, 1.5, false);
        var extra = NightlyTestsPlan.ChunkDiscoverySeconds(21.377, 1.25, true);
        missing.CopiedFromSuite.ShouldBeFalse("baseline copied constant");
        missing.ChunkDiscoverySeconds.ShouldBeNull("ordinary-chunk-copied-suite-constant");
        marked.ChunkDiscoverySeconds.ShouldBe(1.5);
        extra.ChunkDiscoverySeconds.ShouldBe(22.627);
        missing.SuiteDiscoverySeconds.ShouldBe(21.377);
        return Task.CompletedTask;
    }

    // W2-nightly S16: Write-NightlyPrerequisitePhases is NightlyChunkRunner.WritePrerequisitePhases (S12), called
    // in-process. The pwsh child ran once per TZ; a process cannot change its own zone, so the zones are the inputs' offsets
    // instead: the same instants given as UTC text (what the child passed), as a UTC DateTime, and as DateTimeOffsets at
    // each zone's offset.
    [Test]
    [Timeout(90_000)]
    public Task SetupPhasesAreNonoverlapping(CancellationToken cancellationToken)
    {
        var origin = new DateTimeOffset(2026, 10, 29, 0, 0, 0, TimeSpan.Zero);
        var forms = new (string Zone, Func<int, object> At)[]
        {
            ("UTC text", s => origin.AddSeconds(s).UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", System.Globalization.CultureInfo.InvariantCulture)),
            ("UTC DateTime", s => origin.AddSeconds(s).UtcDateTime),
            ("America/New_York", s => origin.AddSeconds(s).ToOffset(TimeSpan.FromHours(-4))),
            ("Asia/Kolkata", s => origin.AddSeconds(s).ToOffset(new TimeSpan(5, 30, 0))),
        };
        foreach (var (zone, at) in forms)
        {
            var work = FreshRoot();
            var timing = TestPhaseManifests.TimingDirectory(work, "");
            NightlyChunkRunner.WritePrerequisitePhases(work, timing, at(0), 1, at(1), 6, at(7), 2, at(9), 0);
            foreach (var chunk in new[] { "a", "b" })
                QualificationTiming.Update(new ChunkTimingUpdate { LogRoot = work, TimingRoot = Path.Combine(work, "timing"), Suite = "antiphon", Chunk = chunk, LastPhase = "run", BuildSeconds = 6, RunId = "run-1", Attempt = "1" }, DateTime.UtcNow);
            var account = QualificationTiming.SetupAccounting(timing);
            var detail = zone + " " + File.ReadAllText(Path.Combine(timing, "setup-phases.json"));
            account.CountedOnce.ShouldBeTrue(detail);
            account.SetupSeconds.ShouldBe(9, detail);
            account.PerChunkBuildSum.ShouldBe(6, detail);
            account.Overlapped.ShouldBeFalse(detail);
            account.FirstStartUtcTicks.ShouldBe(origin.UtcTicks, detail);

            var stampRoot = Path.Combine(work, "stamp-root");
            Directory.CreateDirectory(stampRoot);
            var stampTiming = TestPhaseManifests.TimingDirectory(stampRoot, "");
            QualificationTiming.WriteRunPhase(stampTiming, "npm-ci", "2026-10-30T10:00:00.5698141Z", "2026-10-30T12:00:00.5698141Z", 7200);
            QualificationTiming.WriteRunPhase(stampTiming, "npm-build", "2026-10-30T06:00:00.5698141Z", "2026-10-30T07:00:00.5698141Z", 3600);
            var rawStamp = File.ReadAllText(Path.Combine(stampRoot, "timing", "setup-phases.json"));
            (rawStamp.Contains("\"startedAt\":\"2026-10-30T10:00:00.5698141Z\"", StringComparison.Ordinal)
                && rawStamp.Contains("\"endedAt\":\"2026-10-30T12:00:00.5698141Z\"", StringComparison.Ordinal)).ShouldBeTrue(zone + " stamp-not-iso " + rawStamp);
            var stampAccount = QualificationTiming.SetupAccounting(stampTiming);
            stampAccount.Overlapped.ShouldBeFalse(zone + " stamp-phases-overlapped");
            stampAccount.FirstStartUtcTicks.ShouldBe(DateTimeOffset.Parse("2026-10-30T10:00:00.5698141Z", System.Globalization.CultureInfo.InvariantCulture).UtcTicks, zone + " stamp-ticks-shifted");
            foreach (var chunk in new[] { "a", "b" }) QualificationTiming.Forget(work, "antiphon", chunk);
            Directory.Delete(work, recursive: true);
        }
        return Task.CompletedTask;
    }

    [Test]
    [Timeout(90_000)]
    public Task CountsAndJoinsPreserveEvidence(CancellationToken cancellationToken)
    {
        var work = FreshRoot();
        var timing = Path.Combine(work, "timing");
        Directory.CreateDirectory(timing);
        var scope = JournalFixtures.Scope();
        var sb = new StringBuilder();
        sb.Append(Header("antiphon", "Antiphon.Tests")).Append('\n');
        sb.Append(CompleteLine("early-case", "Passed", 4)).Append('\n');
        sb.Append(CompleteLine("shared-case", "Passed", 4)).Append('\n');
        var pad = CompleteLine("pad-case", "Passed", 4);
        while (sb.Length < 270000) sb.Append(pad).Append('\n');
        sb.Append(CompleteLine("late-case", "Passed", 4)).Append('\n');
        sb.Append("{\"recordKind\":\"comp");
        File.WriteAllText(Path.Combine(timing, "cases-antiphon-n1.jsonl"), sb.ToString());
        File.WriteAllText(Path.Combine(timing, "cases-e2e-n1.jsonl"), Header("e2e", "Antiphon.Tests") + "\n" + CompleteLine("shared-case", "Failed", 4) + "\n");
        File.WriteAllText(Path.Combine(work, "antiphon.trx"), Trx(TrxRow("early-guid", "Passed", "C1514_NATIVE body early-case") + TrxRow("shared-guid", "Passed", "C1514_NATIVE body shared-case")
            + TrxRow("late-guid", "Passed", "C1514_NATIVE body late-case") + TrxRow("dup-a", "Passed", "C1514_NATIVE body shared-case")
            + TrxRow("dup-b", "Failed", "C1514_NATIVE one shared-case\nC1514_NATIVE two shared-case")));
        File.WriteAllText(Path.Combine(work, "e2e.trx"), Trx(TrxRow("e2e-guid", "Failed", "C1514_NATIVE body shared-case")));
        File.WriteAllText(Path.Combine(work, "orphan.trx"), Trx(TrxRow("orphan-guid", "Passed", "no-phase")));
        foreach (var (suite, trx, exit, passed) in new[] { ("antiphon", "antiphon.trx", 0, true), ("e2e", "e2e.trx", 1, false), ("scripts", "orphan.trx", 0, true) })
            TestPhaseManifests.WriteExecuted(new PlainObject(("ExitCode", exit), ("coverageComplete", true), ("testsPassed", passed)), work, Path.Combine(work, trx),
                suite, "all-group-1", "run-1", "1", 1, null, scope).ShouldBeTrue("publish " + suite);
        File.WriteAllText(Path.Combine(timing, "groups-planned.json"), "{\"version\":1,\"runId\":\"run-1\",\"attempt\":\"1\",\"suite\":\"antiphon\",\"assembly\":\"Antiphon.Tests\",\"groups\":[]}");
        TestPhaseRunSummary.Run(work, 20, scope);

        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(timing, "summary.json")));
        var summary = doc.RootElement;
        var tests = summary.GetProperty("tests").EnumerateArray().ToList();
        var ids = tests.Select(t => t.GetProperty("testId").GetString()).ToList();
        ids.ShouldContain("early-case", "completed rows retained");
        ids.ShouldContain("late-case", "completed rows past the inline bound were dropped");
        var quality = summary.GetProperty("quality").EnumerateArray().Select(q => q.GetString()).ToList();
        (quality.Contains("torn-tail") || summary.GetProperty("tornLines").GetInt32() > 0).ShouldBeTrue("torn input incomplete");
        summary.GetProperty("complete").GetBoolean().ShouldBeFalse("torn input incomplete");
        var shared = tests.Where(t => t.GetProperty("testId").GetString() == "shared-case").ToList();
        shared.Count.ShouldBe(2, "same chunk id across suites collapsed");
        shared.Select(t => t.GetProperty("suite").GetString()).Order(StringComparer.Ordinal).ShouldBe(new[] { "antiphon", "e2e" });
        shared.Any(t => t.GetProperty("outcome").GetString() == "Passed" && !t.GetProperty("incomplete").GetBoolean()).ShouldBeFalse("duplicate native markers");
        var orphan = tests.Single(t => t.GetProperty("testId").GetString() == "orphan-guid");
        orphan.GetProperty("outcome").GetString().ShouldBe("unknown", "missing is unknown");
        orphan.GetProperty("incomplete").GetBoolean().ShouldBeTrue("no false pass");
        orphan.GetProperty("countsKnown").GetBoolean().ShouldBeFalse("no false zero");
        summary.GetProperty("summaryAggregationBudgetMilliseconds").GetInt32().ShouldBe(5000, "aggregate re-read budget");
        var spent = summary.GetProperty("summaryAggregationMilliseconds").GetInt32();
        spent.ShouldBeGreaterThanOrEqualTo(0);
        spent.ShouldBeLessThanOrEqualTo(5000);
        return Task.CompletedTask;
    }

    [Test]
    [Timeout(90_000)]
    public Task SummaryReReadSharesOneBudget(CancellationToken cancellationToken)
    {
        var work = FreshRoot();
        var timing = Path.Combine(work, "timing");
        Directory.CreateDirectory(timing);
        void OverBudgetShard(string name, string suite, string earlyId, string lateId)
        {
            var sb = new StringBuilder();
            sb.Append(Header(suite, "Antiphon.Tests")).Append('\n');
            sb.Append(CompleteLine(earlyId, "Passed", 1)).Append('\n');
            var pad = CompleteLine("pad-case", "Passed", 1);
            while (sb.Length < 270000) sb.Append(pad).Append('\n');
            sb.Append(CompleteLine(lateId, "Passed", 1)).Append('\n');
            File.WriteAllText(Path.Combine(timing, name), sb.ToString());
        }
        OverBudgetShard("cases-a-budget.jsonl", "antiphon", "early-a", "late-a");
        OverBudgetShard("cases-b-budget.jsonl", "e2e", "early-b", "late-b");
        File.WriteAllText(Path.Combine(timing, "groups-planned.json"), "{\"version\":1,\"runId\":\"run-1\",\"attempt\":\"1\",\"suite\":\"antiphon\",\"assembly\":\"Antiphon.Tests\",\"groups\":[]}");
        File.WriteAllText(Path.Combine(timing, "groups-planned-e2e.json"), "{\"version\":1,\"runId\":\"run-1\",\"attempt\":\"1\",\"suite\":\"e2e\",\"assembly\":\"Antiphon.E2E\",\"groups\":[]}");
        // The summary's re-read budget runs on this clock: a full re-read spends all of it.
        var scope = JournalFixtures.Scope();
        scope.SummaryReReadWatch = new SpentOnFullRead();
        TestPhaseRunSummary.Run(work, 20, scope);

        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(timing, "summary.json")));
        var ids = doc.RootElement.GetProperty("tests").EnumerateArray().Select(t => t.GetProperty("testId").GetString()).ToList();
        ids.ShouldContain("late-a", "first over-budget shard was not re-read");
        ids.ShouldNotContain("late-b", "second shard was re-read after the shared budget was exhausted");
        doc.RootElement.GetProperty("summaryAggregationBudgetMilliseconds").GetInt32().ShouldBe(5000, "aggregate re-read budget");
        var spent = doc.RootElement.GetProperty("summaryAggregationMilliseconds").GetInt32();
        spent.ShouldBeGreaterThan(0, "aggregate re-read recorded no time");
        spent.ShouldBeLessThanOrEqualTo(5000, "aggregate re-read exceeded the shared budget");
        return Task.CompletedTask;
    }

    // A read-budget clock that a full re-read spends at once.
    private sealed class SpentOnFullRead : ITestPhaseWatch
    {
        public long ElapsedMilliseconds { get; private set; }
        public bool IsRunning { get; private set; }
        public void Start() => IsRunning = true;
        public void Stop() => IsRunning = false;
        public void OnFullRead() => ElapsedMilliseconds = 5000;
    }

    private static string Header(string suite, string assembly) =>
        "{\"recordKind\":\"header\",\"version\":1,\"pid\":1,\"nonce\":\"n1\",\"frequencyHz\":10000000,\"epochUtc\":\"2026-10-29T00:00:00.0000000Z\",\"runId\":\"run-1\",\"groupId\":\"all-group-1\",\"attempt\":\"1\",\"suite\":\""
        + suite + "\",\"assembly\":\"" + assembly + "\"}";

    private static string CompleteLine(string id, string outcome, int db) =>
        "{\"recordKind\":\"complete\",\"testId\":\"" + id + "\",\"className\":\"C\",\"method\":\"M\",\"args\":[],\"startOffsetMs\":0,\"endOffsetMs\":1,\"outcome\":\"" + outcome
        + "\",\"phases\":[],\"processes\":[],\"exclusive\":{\"db\":" + db.ToString(System.Globalization.CultureInfo.InvariantCulture)
        + ",\"host\":0,\"process\":0,\"fs\":0,\"network\":0,\"fixture\":0},\"overlapMs\":0,\"remainderMs\":0,\"childUnionMs\":0,\"droppedPhaseCount\":0,\"shared\":false,\"incomplete\":false}";

    private static string Trx(string body) => "<?xml version=\"1.0\" encoding=\"utf-8\"?><TestRun><Results>" + body + "</Results></TestRun>";

    private static string TrxRow(string id, string outcome, string stdout) =>
        "<UnitTestResult testId=\"" + id + "\" testName=\"" + id + "\" outcome=\"" + outcome + "\"><Output><StdOut>" + stdout + "</StdOut></Output></UnitTestResult>";

    // W2-nightly S04: the witness (OwnerExitWitness) and C1039_DescendantExit are C# in NightlyNativeOwnershipTests.cs (the
    // driver scripts/test-nightly-native.ps1 and test-rc-speed-os-witness.ps1 are gone); their shape is read with Roslyn.
    [Test]
    public void NativeOwnerExitWitnessIsUnconditional()
    {
        var path = Path.Combine(C1514Support.RepoRoot(), "tests", "Antiphon.Tests", "Scripts", "NightlyNativeOwnershipTests.cs");
        var tree = CSharpSyntaxTree.ParseText(File.ReadAllText(path));
        tree.GetDiagnostics().Where(item => item.Severity == DiagnosticSeverity.Error).Select(item => item.ToString()).ShouldBeEmpty("witness-parse-failed");
        var root = tree.GetRoot();
        var witness = root.DescendantNodes().OfType<MethodDeclarationSyntax>().SingleOrDefault(method => method.Identifier.Text == "OwnerExitWitness");
        witness.ShouldNotBeNull("OwnerExitWitness was not declared");
        var start = witness.DescendantNodes().OfType<InvocationExpressionSyntax>().FirstOrDefault(call =>
            call.Expression is MemberAccessExpressionSyntax member && member.Name.Identifier.Text == "Start" && member.Expression.ToString() == "NativeProcessOwner");
        (start is not null && Reachable(start, witness)).ShouldBeTrue("OwnerExitWitness was not declared");
        var throws = witness.DescendantNodes().Where(node => node is ThrowStatementSyntax or ThrowExpressionSyntax).ToArray();
        var members = witness.DescendantNodes().OfType<MemberAccessExpressionSyntax>().ToArray();
        bool Asserts(string needle, string member) =>
            throws.Any(item => item.ToString().Contains(needle, StringComparison.Ordinal) && Reachable(item, witness))
            && members.Any(item => item.Name.Identifier.Text == member && Reachable(item, witness));
        Asserts("descendant image survives process exit", "DescendantImages").ShouldBeTrue("DescendantImages was not asserted");
        Asserts("root exit time missing", "RootExitedAtUtc").ShouldBeTrue("RootExitedAtUtc was not asserted");
        Asserts("missed descendant pids missing", "MissedDescendantPids").ShouldBeTrue("MissedDescendantPids was not asserted");
        Asserts("job empty not observed", "JobEmptyObserved").ShouldBeTrue("JobEmptyObserved was not asserted");
        var testCase = root.DescendantNodes().OfType<MethodDeclarationSyntax>().SingleOrDefault(method => method.Identifier.Text == "C1039_DescendantExit");
        testCase.ShouldNotBeNull("C1039_DescendantExit does not call the witness");
        var calls = testCase.DescendantNodes().OfType<InvocationExpressionSyntax>()
            .Where(call => call.Expression is IdentifierNameSyntax name && name.Identifier.Text == "OwnerExitWitness").ToArray();
        calls.ShouldNotBeEmpty("C1039_DescendantExit does not call the witness");
        calls.Any(call => Unconditional(call, testCase)).ShouldBeTrue("owner witness call is nested under a conditional");
    }

    // Test-ReachableAst: not inside the body of an if whose condition is false or false && ... before stop.
    private static bool Reachable(SyntaxNode node, SyntaxNode stop)
    {
        for (var cursor = node.Parent; cursor is not null && cursor != stop; cursor = cursor.Parent)
        {
            if (cursor is IfStatementSyntax branch && branch.Statement.Span.Contains(node.Span) && Disabled(branch.Condition)) return false;
        }
        return true;
    }

    private static bool Disabled(ExpressionSyntax condition)
    {
        while (condition is ParenthesizedExpressionSyntax parenthesized) condition = parenthesized.Expression;
        if (condition.IsKind(SyntaxKind.FalseLiteralExpression)) return true;
        return condition is BinaryExpressionSyntax binary && binary.IsKind(SyntaxKind.LogicalAndExpression) && Disabled(binary.Left);
    }

    // Test-DirectCaseCall: nothing between the call and its test method decides whether it runs.
    private static bool Unconditional(SyntaxNode node, SyntaxNode stop)
    {
        for (var cursor = node.Parent; cursor is not null && cursor != stop; cursor = cursor.Parent)
        {
            if (cursor is IfStatementSyntax or ElseClauseSyntax or ConditionalExpressionSyntax or SwitchStatementSyntax or SwitchExpressionSyntax
                or WhileStatementSyntax or ForStatementSyntax or ForEachStatementSyntax or DoStatementSyntax or CatchClauseSyntax) return false;
            if (cursor is BinaryExpressionSyntax binary && (binary.IsKind(SyntaxKind.LogicalAndExpression) || binary.IsKind(SyntaxKind.LogicalOrExpression)
                || binary.IsKind(SyntaxKind.CoalesceExpression))) return false;
        }
        return true;
    }

    private static string FreshRoot()
    {
        var path = Path.Combine(Path.GetTempPath(), "rcspeed-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static async Task<JsonElement> Run(string body, CancellationToken cancellationToken, string root)
    {
        var script = Path.Combine(Path.GetTempPath(), "rcspeed-" + Guid.NewGuid().ToString("N") + ".ps1");
        await File.WriteAllTextAsync(script, body, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), cancellationToken);
        var start = new ProcessStartInfo("pwsh");
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-File");
        start.ArgumentList.Add(script);
        start.ArgumentList.Add(C1514Support.RepoRoot());
        start.ArgumentList.Add(root);
        var ran = await C1514Support.RunProcess(start, cancellationToken);
        var resultPath = Path.Combine(root, "observation.json");
        if (!File.Exists(resultPath))
            throw new InvalidOperationException("observation missing\n" + ran.Text);
        using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(resultPath, cancellationToken));
        var copy = doc.RootElement.Clone();
        return copy;
    }

    private const string CallSiteScript = """
        $ErrorActionPreference = 'Stop'
        $repo = $args[0]
        $work = $args[1]
        $bin = Join-Path $work 'bin'
        New-Item -ItemType Directory -Path $bin -Force | Out-Null
        [IO.File]::WriteAllText((Join-Path $bin 'npm'), "#!/bin/sh`nexit 0`n")
        $env:PATH = $bin + [IO.Path]::PathSeparator + $env:PATH
        $lib = Join-Path $repo 'scripts/lib'
        . (Join-Path $lib 'nightly-common.ps1')
        . (Join-Path $lib 'nightly-coverage.ps1')
        . (Join-Path $lib 'release-gate.ps1')
        . (Join-Path $lib 'release-selection.ps1')
        . (Join-Path $lib 'nightly-tests-impl.ps1')
        $log = Join-Path $work 'run'
        $timing = Join-Path $work 'timing'
        New-Item -ItemType Directory -Path $log -Force | Out-Null
        New-Item -ItemType Directory -Path $timing -Force | Out-Null
        $seamPath = Join-Path $work 'seams.ps1'
        $seam = @'
        $NightlySeams = @{
            DockerInfo = { return @{ exitCode = 0; version = 'test'; detail = 'seam' } }
            DiskFree = { return @{ exitCode = 0; freeBytes = 50GB; minimumFreeBytes = 10GB } }
            StartProcess = {
                param($FilePath, $ArgumentList, $WorkingDirectory, $TimeoutMilliseconds, $Environment)
                return @{ ExitCode = 0; TimedOut = $false; ChildrenExited = $true; CleanupComplete = $true; Pid = 1; LogText = '' }
            }
        }
        '@
        [IO.File]::WriteAllText($seamPath, $seam)
        $planningSeamPath = Join-Path $work 'planning-seams.ps1'
        $planningSeam = @'
        $NightlySeams = @{
            DockerInfo = { return @{ exitCode = 0; version = 'test'; detail = 'seam' } }
            DiskFree = { return @{ exitCode = 0; freeBytes = 50GB; minimumFreeBytes = 10GB } }
            HostPlatform = { return 'linux' }
            StartProcess = {
                param($FilePath, $ArgumentList, $WorkingDirectory, $TimeoutMilliseconds, $Environment)
                $argv = @($ArgumentList)
                if ($argv -contains '--list-tests') {
                    Start-Sleep -Milliseconds 250
                    return @{ ExitCode = 1; TimedOut = $true; ChildrenExited = $true; CleanupComplete = $true; Pid = 1; LogText = '' }
                }
                return @{ ExitCode = 0; TimedOut = $false; ChildrenExited = $true; CleanupComplete = $true; Pid = 1; LogText = '' }
            }
        }
        '@
        [IO.File]::WriteAllText($planningSeamPath, $planningSeam)
        $supplementalSeamPath = Join-Path $work 'supplemental-seams.ps1'
        $supplementalSeam = @'
        $NightlySeams = @{
            DockerInfo = { return @{ exitCode = 0; version = 'test'; detail = 'seam' } }
            DiskFree = { return @{ exitCode = 0; freeBytes = 50GB; minimumFreeBytes = 10GB } }
            HostPlatform = { return 'linux' }
            StartProcess = {
                param($FilePath, $ArgumentList, $WorkingDirectory, $TimeoutMilliseconds, $Environment)
                $argv = @($ArgumentList)
                if ($argv -contains '--list-tests') {
                    Start-Sleep -Milliseconds 400
                    $diagIndex = [array]::IndexOf($argv, '--diagnostic-output-directory')
                    if ($diagIndex -lt 0 -or ($diagIndex + 1) -ge $argv.Count) { throw 'seam-missing-diagnostic-directory' }
                    $diagDir = [string]$argv[$diagIndex + 1]
                    New-Item -ItemType Directory -Path $diagDir -Force | Out-Null
                    $diag = '{"format":"antiphon-tunit-discovery-v1","tunitVersion":"1.44.0","mtpVersion":"2.2.2","assemblyHash":"seam-timing-hash","nodes":[{"uid":"uid-seam","assembly":"Antiphon.Tests","namespace":"Antiphon.Tests.Scripts","type":"SeamTimingProbeTests","method":"Probe","signature":"","state":"Discovered","excluded":false,"categories":[]}]}'
                    [IO.File]::WriteAllText((Join-Path $diagDir 'log.diag'), $diag)
                    return @{ ExitCode = 0; TimedOut = $false; ChildrenExited = $true; CleanupComplete = $true; Pid = 1; LogText = '' }
                }
                if ($argv -contains '--report-trx') {
                    $nameIndex = [array]::IndexOf($argv, '--report-trx-filename')
                    $dirIndex = [array]::IndexOf($argv, '--results-directory')
                    if ($nameIndex -lt 0 -or $dirIndex -lt 0) { throw 'seam-missing-trx' }
                    $trxPath = Join-Path ([string]$argv[$dirIndex + 1]) ([string]$argv[$nameIndex + 1])
                    $trxDir = Split-Path -Parent $trxPath
                    if ($trxDir -and -not (Test-Path -LiteralPath $trxDir)) { New-Item -ItemType Directory -Path $trxDir -Force | Out-Null }
                    $trx = '<?xml version="1.0" encoding="utf-8"?><TestRun><TestDefinitions><UnitTest id="uid-seam" name="Antiphon.Tests.Scripts.SeamTimingProbeTests.Probe"><TestMethod className="Antiphon.Tests.Scripts.SeamTimingProbeTests" name="Probe" /></UnitTest></TestDefinitions><Results><UnitTestResult testId="uid-seam" testName="Antiphon.Tests.Scripts.SeamTimingProbeTests.Probe" outcome="Passed" /></Results></TestRun>'
                    [IO.File]::WriteAllText($trxPath, $trx)
                    return @{ ExitCode = 0; TimedOut = $false; ChildrenExited = $true; CleanupComplete = $true; Pid = 1; LogText = '' }
                }
                return @{ ExitCode = 0; TimedOut = $false; ChildrenExited = $true; CleanupComplete = $true; Pid = 1; LogText = '' }
            }
        }
        '@
        [IO.File]::WriteAllText($supplementalSeamPath, $supplementalSeam)
        $planningLog = Join-Path $log 'planning'
        $supplementalLog = Join-Path $log 'supplemental'
        New-Item -ItemType Directory -Path $planningLog -Force | Out-Null
        New-Item -ItemType Directory -Path $supplementalLog -Force | Out-Null
        $problems = New-Object System.Collections.Generic.List[string]
        $discoveryNull = $false
        $setupNpmCi = $false
        $setupNpmBuild = $false
        $setupLint = $false
        $setupDotnet = $false
        $planningPresent = $false
        $planningSeconds = 0.0
        $successPlanningPresent = $false
        $successPlanningSeconds = 0.0
        $supplementalIsMarker = $false
        try {
            $null = Invoke-AntiphonNightlyTests -RepoRoot $repo -LogRoot $log -TimingRoot $timing -Suites @('scripts') -Profile 'nightly' -Sha '9f00008facfc48e75555c08df59887019b3f8dfe' -RunId 'call-site' -Trigger 'manual' -SeamsPath $seamPath -AllowSharedTree
        } catch {
            $problems.Add('scripts-threw')
        }
        try {
            $null = Invoke-AntiphonNightlyTests -RepoRoot $repo -LogRoot $planningLog -TimingRoot $timing -Suites @('antiphon') -Profile 'rc' -Sha '9f00008facfc48e75555c08df59887019b3f8dfe' -RunId 'planning-site' -Trigger 'manual' -SeamsPath $planningSeamPath -AllowSharedTree
        } catch {
            $problems.Add('planning-threw')
        }
        try {
            $null = Invoke-AntiphonNightlyTests -RepoRoot $repo -LogRoot $supplementalLog -TimingRoot $timing -Suites @('antiphon') -Profile 'nightly' -Sha '9f00008facfc48e75555c08df59887019b3f8dfe' -RunId 'supplemental-site' -Trigger 'manual' -SeamsPath $supplementalSeamPath -AllowSharedTree
        } catch {
            $problems.Add('supplemental-threw')
        }
        $successLog = Join-Path $log 'planning-success'
        $successTiming = Join-Path $work 'timing-success'
        New-Item -ItemType Directory -Path $successLog -Force | Out-Null
        New-Item -ItemType Directory -Path $successTiming -Force | Out-Null
        try {
            $null = Invoke-AntiphonNightlyTests -RepoRoot $repo -LogRoot $successLog -TimingRoot $successTiming -Suites @('antiphon') -Profile 'rc' -Sha '9f00008facfc48e75555c08df59887019b3f8dfe' -RunId 'planning-success' -Trigger 'manual' -SeamsPath $supplementalSeamPath -AllowSharedTree
        } catch {
            $problems.Add('planning-success-threw')
        }
        $setupPath = Join-Path $timing 'setup-phases.json'
        if (Test-Path -LiteralPath $setupPath) {
            $setup = [IO.File]::ReadAllText($setupPath) | ConvertFrom-Json
            $names = @()
            if ($null -ne $setup.phases) {
                foreach ($row in @($setup.phases)) {
                    $phaseName = [string]$row.name
                    $names += $phaseName
                    if ($phaseName -eq 'planning-antiphon') {
                        $planningPresent = $true
                        if ($null -ne $row.seconds) { $planningSeconds = [double]$row.seconds }
                    }
                }
            }
            $setupNpmCi = $names -contains 'npm-ci'
            $setupNpmBuild = $names -contains 'npm-build'
            $setupLint = $names -contains 'lint'
            $setupDotnet = $names -contains 'dotnet-build'
        }
        $supplementalPath = Join-Path (Join-Path $log 'supplemental') 'antiphon-all.timing.json'
        if (Test-Path -LiteralPath $supplementalPath) {
            $supplementalRaw = [IO.File]::ReadAllText($supplementalPath)
            $supplementalMatch = [regex]::Match($supplementalRaw, '"discoverySeconds":([^,}]+)')
            $supplementalToken = ''
            if ($supplementalMatch.Success) { $supplementalToken = $supplementalMatch.Groups[1].Value.Trim() }
            $supplementalValue = 0.0
            $supplementalParsed = $false
            if ($supplementalToken -ne '' -and $supplementalToken -ne 'null') {
                $supplementalParsed = [double]::TryParse($supplementalToken, [Globalization.NumberStyles]::Float, [Globalization.CultureInfo]::InvariantCulture, [ref]$supplementalValue)
            }
            if ($supplementalParsed -and $supplementalValue -gt 0.2) { $supplementalIsMarker = $true }
        }
        $timingPath = Join-Path $log 'scripts-all.timing.json'
        $token = ''
        if (Test-Path -LiteralPath $timingPath) {
            $raw = [IO.File]::ReadAllText($timingPath)
            $match = [regex]::Match($raw, '"discoverySeconds":([^,}]+)')
            if ($match.Success) { $token = $match.Groups[1].Value.Trim() }
        }
        $discoveryNull = ($token -eq 'null')
        if (-not $discoveryNull) { $problems.Add('discovery-not-null') }
        if (-not $setupNpmCi) { $problems.Add('missing-npm-ci') }
        if (-not $setupNpmBuild) { $problems.Add('missing-npm-build') }
        if (-not $setupLint) { $problems.Add('missing-lint') }
        if (-not $setupDotnet) { $problems.Add('missing-dotnet-build') }
        $successSetupPath = Join-Path $successTiming 'setup-phases.json'
        if (Test-Path -LiteralPath $successSetupPath) {
            $successSetup = [IO.File]::ReadAllText($successSetupPath) | ConvertFrom-Json
            if ($null -ne $successSetup.phases) {
                foreach ($row in @($successSetup.phases)) {
                    if ([string]$row.name -eq 'planning-antiphon') {
                        $successPlanningPresent = $true
                        if ($null -ne $row.seconds) { $successPlanningSeconds = [double]$row.seconds }
                    }
                }
            }
        }
        if (-not $planningPresent) { $problems.Add('planning-suite-missing') }
        if ($planningSeconds -le 0) { $problems.Add('planning-seconds-zero') }
        if (-not $successPlanningPresent) { $problems.Add('planning-success-missing') }
        if ($successPlanningSeconds -le 0) { $problems.Add('planning-success-seconds-zero') }
        if (-not $supplementalIsMarker) { $problems.Add('supplemental-not-marker') }
        $exit = 0
        $text = ''
        if ($problems.Count -gt 0) { $exit = 1; $text = ($problems -join '; ') }
        $out = [ordered]@{
            exitCode = [int]$exit
            text = [string]$text
            discoveryNull = [bool]$discoveryNull
            setupNpmCi = [bool]$setupNpmCi
            setupNpmBuild = [bool]$setupNpmBuild
            setupLint = [bool]$setupLint
            setupDotnet = [bool]$setupDotnet
            planningPresent = [bool]$planningPresent
            planningSeconds = [double]$planningSeconds
            successPlanningPresent = [bool]$successPlanningPresent
            successPlanningSeconds = [double]$successPlanningSeconds
            supplementalIsMarker = [bool]$supplementalIsMarker
        }
        [IO.File]::WriteAllText((Join-Path $work 'observation.json'), ($out | ConvertTo-Json -Compress -Depth 5))
        """;
}
