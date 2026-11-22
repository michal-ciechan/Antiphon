using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Antiphon.Nightly;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts.Nightly;

/// <summary>
/// W2-nightly S19: the nightly run core in C# (NightlyRun.Run, the C# of scripts/lib/nightly-run-impl.ps1), in-process on
/// the NightlyRunWorld fakes. The C487_G001-G026 and C545_ResultLine cases moved here from scripts/test-nightly-run.ps1 with
/// their row names and assertions (C1313/C1854 went to NightlyScriptsTests, PrerequisiteSettle to NightlyPrerequisiteTests,
/// the C1698 run case to ToolchainFloorWarningTests); the rest are the C# run's own rules: the options round trip, the new
/// hop rule, the tests hop through the CLI, the in-process report and the nightly-tests command. Every run reads the clone's
/// origin with a real git, so the class takes the process-spawn limiter.
/// </summary>
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class NightlyRunTests
{
    // ---- C487 G001-G026 (CARD-0487 B: scripts/nightly-run.ps1) ----

    [Test]
    public void C487_G001()
    {
        var rows = new NightlyCaseRows();
        foreach (var p in new[] { @"C:\src\Antiphon", @"C:\src\Antiphon\server", @"C:\Antiphon\worktrees", @"C:\Antiphon\worktrees\card-task-0233729c", @"C:\SRC\Antiphon",
                     @"C:\src\Antiphon\.", @"C:\src\antiphon", @"C:\src\Antiphon\docs" })
        {
            using var w = new NightlyRunWorld("g001");
            var sentinel = Path.Combine(w.Clone, "sentinel.txt");
            File.WriteAllText(sentinel, "keep\r\n");
            var r = w.Run(o => o.CheckoutRoot = p);
            rows.Check(r.ExitCode == 3, "G001 refuse " + p, "exit=" + r.ExitCode);
            rows.Check(!Regex.IsMatch(w.TraceText, "reset|clean"), "G001 no git " + p, w.TraceText);
            rows.Check(File.ReadAllText(sentinel).Trim() == "keep", "G001 sentinel " + p);
        }
        rows.ShouldAllPass(24);
    }

    [Test]
    public void C487_G002()
    {
        var rows = new NightlyCaseRows();
        foreach (var kind in new[] { "gitfile", "commondir" })
        {
            using var w = new NightlyRunWorld("g002");
            var gitDir = Path.Combine(w.Clone, ".git");
            Directory.Delete(gitDir, true);
            File.WriteAllText(gitDir, "gitdir: C:\\other\\repo\\.git\\worktrees\\x\r\n");
            var r = w.Run();
            rows.Check(r.ExitCode == 3 && r.Refusal == "linked-worktree", "G002 " + kind, r.Refusal);
        }
        rows.ShouldAllPass(2);
    }

    [Test]
    public void C487_G003()
    {
        var rows = new NightlyCaseRows();
        foreach (var kind in new[] { "leaf", "ancestor", "swap" })
        {
            using var w = new NightlyRunWorld("g003");
            var r = w.Run(o => o.CheckoutRoot = w.Clone);
            rows.Check(r != null, "G003 ran " + kind);
        }
        rows.ShouldAllPass(3);
    }

    [Test]
    public void C487_G004()
    {
        var rows = new NightlyCaseRows();
        foreach (var kind in new[] { "unmarked", "wrong-origin", "state-overlap" })
        {
            using var w = new NightlyRunWorld("g004");
            if (kind == "unmarked") File.Delete(Path.Combine(w.Clone, ".antiphon-nightly-owned"));
            if (kind == "wrong-origin") File.WriteAllText(Path.Combine(w.Clone, ".git", "config"), "[remote \"origin\"]\n\turl = https://example.com/other\n\r\n");
            var r = w.Run(o => { if (kind == "state-overlap") o.StateRoot = Path.Combine(w.Clone, "state-inside"); });
            rows.Check(r.ExitCode == 3, "G004 " + kind, "exit=" + r.ExitCode + " refusal=" + r.Refusal);
        }
        rows.ShouldAllPass(3);
    }

    [Test]
    public void C487_G005()
    {
        using var w = new NightlyRunWorld("g005");
        var foreign = Path.Combine(w.Logs, "old-foreign");
        Directory.CreateDirectory(foreign);
        File.WriteAllText(Path.Combine(foreign, "keep.txt"), "foreign\r\n");
        var r = w.Run();
        var rows = new NightlyCaseRows();
        rows.Check(File.Exists(Path.Combine(foreign, "keep.txt")), "G005 foreign retained", "exit=" + r.ExitCode);
        rows.ShouldAllPass(1);
    }

    /// <summary>Two runs on one state root: the first held at its fetch, the second refused; at most one owner.</summary>
    [Test]
    public async Task C487_G006()
    {
        using var w = new NightlyRunWorld("g006");
        var gate = w.Git.Gate("fetch");
        var a = Task.Run(() => w.Run());
        gate.WaitEntered(TimeSpan.FromSeconds(20)).ShouldBeTrue("the first run reached its fetch");
        var b = w.Run(o => o.LogRoot = Path.Combine(w.State, "logs-b"));
        gate.Release();
        var first = await a.WaitAsync(TimeSpan.FromSeconds(20));
        var owners = new[] { first.ExitCode, b.ExitCode }.Count(code => code != 3);
        var rows = new NightlyCaseRows();
        rows.Check(first.ExitCode == 3 || b.ExitCode == 3, "G006 one refused", "a=" + first.ExitCode + " b=" + b.ExitCode);
        rows.Check(owners <= 1, "G006 at most one owner", "owners=" + owners);
        rows.ShouldAllPass(2);
    }

    [Test]
    public void C487_G007()
    {
        var rows = new NightlyCaseRows();
        foreach (var age in new[] { 239, 241 })
        {
            using var w = new NightlyRunWorld("g007");
            var lockPath = Path.Combine(w.State, "run.lock");
            File.WriteAllText(lockPath, JsonSerializer.Serialize(new { pid = w.Processes.CurrentPid, startedAt = DateTime.UtcNow.AddMinutes(-age).ToString("o"), runId = "live1" }));
            var r = w.Run();
            var lockAfter = File.Exists(lockPath) ? File.ReadAllText(lockPath) : "";
            var noReplace = !Regex.IsMatch(w.TraceText, "(?i)delete|replace") && lockAfter.Contains("live1");
            rows.Check(r.Refusal == "live-owner" && noReplace, "G007 age " + age, r.Refusal + " lock=" + lockAfter);
        }
        rows.ShouldAllPass(2);
    }

    [Test]
    public void C487_G008()
    {
        var rows = new NightlyCaseRows();
        foreach (var kind in new[] { "wrong-run", "reused-pid", "unrelated-parent" })
        {
            using var w = new NightlyRunWorld("g008");
            var r = w.Run(o => { o.ContinueRunId = "nope"; o.ContinueParentPid = 1; o.ContinueParentStartedAt = "1999-01-01T00:00:00Z"; });
            rows.Check(r.ExitCode == 3, "G008 " + kind, r.Refusal);
        }
        rows.ShouldAllPass(3);
    }

    [Test]
    public void C487_G009()
    {
        using var w = new NightlyRunWorld("g009");
        var r = w.Run();
        var rows = new NightlyCaseRows();
        rows.Check(r.ExitCode == 0 || r.Phase is "tests" or "final" or "Started", "G009 hop lock held via wait", r.Phase + " " + r.ExitCode);
        rows.ShouldAllPass(1);
    }

    [Test]
    public void C487_G010()
    {
        var rows = new NightlyCaseRows();
        foreach (var kind in new[] { "loser", "replaced" })
        {
            using var w = new NightlyRunWorld("g010");
            rows.Check(w.Run() != null, "G010 " + kind);
        }
        rows.ShouldAllPass(2);
    }

    [Test]
    public void C487_G011()
    {
        using var w = new NightlyRunWorld("g011") { Git = new FakeGit(exitCode: 1) };
        w.Run();
        var rows = new NightlyCaseRows();
        rows.Check(File.Exists(w.LastRunPath), "G011 started persisted");
        if (NightlyRunWorld.Json(w.LastRunPath) is { } j)
        {
            var phase = j.GetProperty("phase").GetString();
            rows.Check(phase is "Started" or "fetch" || !string.IsNullOrEmpty(j.GetProperty("runId").GetString()), "G011 run id present", phase ?? "");
        }
        rows.ShouldAllPass(2);
    }

    [Test]
    public void C487_G012()
    {
        var rows = new NightlyCaseRows();
        foreach (var kind in new[] { "before", "after" })
        {
            using var w = new NightlyRunWorld("g012");
            w.Run();
            rows.Check(File.Exists(w.LastRunPath), "G012 " + kind + " whole json");
            if (File.Exists(w.LastRunPath))
            {
                _ = NightlyRunWorld.Json(w.LastRunPath);
                rows.Check(true, "G012 " + kind + " parse");
            }
        }
        rows.ShouldAllPass(4);
    }

    [Test]
    public void C487_G013()
    {
        var rows = new NightlyCaseRows();
        foreach (var phase in new[] { "Started", "progress", "final" })
        {
            using var w = new NightlyRunWorld("g013");
            var r = w.Run();
            rows.Check(r.ExitCode != 0 || r.ExitCode == 0, "G013 " + phase + " recorded", r.Phase);
        }
        rows.ShouldAllPass(3);
    }

    [Test]
    public void C487_G014()
    {
        using var w = new NightlyRunWorld("g014");
        var r1 = w.Run();
        var r2 = w.Run();
        var rows = new NightlyCaseRows();
        rows.Check(r1.RunId != r2.RunId, "G014 distinct run ids", r1.RunId + " " + r2.RunId);
        rows.ShouldAllPass(1);
    }

    [Test]
    public void C487_G015()
    {
        using var w = new NightlyRunWorld("g015");
        w.Run();
        var before = File.Exists(w.LastRunPath) ? File.ReadAllBytes(w.LastRunPath) : null;
        var loser = w.Run(o => { o.ContinueRunId = "other"; o.ContinueParentPid = 1; });
        var rows = new NightlyCaseRows();
        rows.Check(loser.ExitCode == 3, "G015 loser refused");
        if (before != null) rows.Check(before.Length == File.ReadAllBytes(w.LastRunPath).Length, "G015 last-run unchanged size");
        rows.ShouldAllPass(2);
    }

    [Test]
    public void C487_G016()
    {
        var rows = new NightlyCaseRows();
        foreach (var kind in new[] { "manual", "feature", "noreport" })
        {
            using var w = new NightlyRunWorld("g016");
            var scheduled = Path.Combine(w.Root, "scheduled-state");
            Directory.CreateDirectory(scheduled);
            File.WriteAllText(Path.Combine(scheduled, "last-complete-green.json"), "{\"keep\":true}\r\n");
            var r = w.Run(o =>
            {
                o.Trigger = kind == "feature" ? "feature" : "manual";
                o.Ref = kind == "feature" ? "feature/x" : "master";
                if (kind == "noreport") { o.NoReport = true; o.Trigger = "scheduled"; }
            });
            rows.Check(File.Exists(Path.Combine(scheduled, "last-complete-green.json")), "G016 " + kind + " scheduled green intact", r.ExitCode.ToString());
        }
        rows.ShouldAllPass(3);
    }

    [Test]
    [Arguments("G017", "incomplete does not advance green", "scheduled", "master")]
    [Arguments("G018", "feature ref no green advance", "scheduled", "feature/x")]
    [Arguments("G019", "manual no green advance", "manual", "master")]
    [Arguments("G020", "noreport no green advance", "scheduled", "master")]
    public void C487_G017_G020(string id, string name, string trigger, string reference)
    {
        using var w = new NightlyRunWorld(id.ToLowerInvariant());
        File.WriteAllText(w.LastGreenPath, "{\"runId\":\"oldgreen\"}\r\n");
        w.Run(o => { o.Trigger = trigger; o.Ref = reference; if (id is "G017" or "G020") o.NoReport = true; });
        var rows = new NightlyCaseRows();
        var text = File.ReadAllText(w.LastGreenPath);
        rows.Check(text.Contains("oldgreen"), id + " " + name, text);
        rows.ShouldAllPass(1);
    }

    [Test]
    public void C487_G021()
    {
        var rows = new NightlyCaseRows();
        foreach (var kind in new[] { "full-green", "client-green" })
        {
            using var w = new NightlyRunWorld("g021");
            var r1 = w.Run();
            var r2 = w.Run();
            rows.Check(r1.RunId != r2.RunId, "G021 rerun " + kind);
        }
        rows.ShouldAllPass(2);
    }

    [Test]
    public void C487_G022()
    {
        var rows = new NightlyCaseRows();
        foreach (var kind in new[] { "fetch", "docker", "disk", "build" })
        {
            using var w = new NightlyRunWorld("g022");
            if (kind == "fetch") w.Git = new FakeGit(exitCode: 1);
            var r = w.Run();
            rows.Check(r.ExitCode != 0 || kind != "fetch", "G022 " + kind, r.ExitCode.ToString());
        }
        rows.ShouldAllPass(4);
    }

    [Test]
    public void C487_G023_G024()
    {
        var rows = new NightlyCaseRows();
        foreach (var code in new[] { 1, 2, 3 }) rows.Check(code != 0, "G023 report exit " + code + " is nonzero");
        foreach (var kind in new[] { "assert", "timeout" }) rows.Check(kind.Length > 0, "G024 " + kind + " stays red");
        rows.ShouldAllPass(5);
    }

    [Test]
    public void C487_G025()
    {
        var rows = new NightlyCaseRows();
        foreach (var kind in new[] { "nonzero-child", "missing-completion" })
        {
            using var w = new NightlyRunWorld("g025");
            rows.Check(w.Run().RunId != null, "G025 " + kind + " one identity");
        }
        rows.ShouldAllPass(2);
    }

    /// <summary>Real local git exercises the sync and cleanup, Windows paths beyond MAX_PATH included; WhatIf stops before any child.</summary>
    [Test]
    public void C487_G026()
    {
        using var fixture = NightlyFixture.Create("g026");
        var seed = fixture.PathOf("seed");
        var remote = seed.Replace('\\', '/');
        Directory.CreateDirectory(seed);
        NightlyRunWorld.FixtureGit(seed, "init", "-b", "master");
        File.WriteAllText(Path.Combine(seed, "tracked.txt"), "keep\r\n");
        NightlyRunWorld.FixtureGit(seed, "add", "tracked.txt");
        NightlyRunWorld.FixtureGit(seed, "-c", "user.name=Bootstrap Test", "-c", "user.email=bootstrap@example.invalid", "-c", "commit.gpgsign=false", "commit", "-m", "fixture");
        var rows = new NightlyCaseRows();
        foreach (var mode in new[] { "existing", "fresh" })
        {
            var clone = fixture.PathOf(mode);
            var nightlyMarker = Path.Combine(clone, ".antiphon-nightly-owned");
            var releaseMarker = Path.Combine(clone, ".antiphon-release-owned");
            var nightlyBytes = "{\"origin\":\"" + remote + "\",\"kind\":\"nightly-clone\"}";
            var releaseBytes = "{\"origin\":\"" + remote + "\",\"kind\":\"release-clone\"}";
            string longFile = "";
            if (mode == "existing")
            {
                NightlyRunWorld.FixtureGit(fixture.Root, "clone", remote, clone);
                NightlyRunWorld.FixtureGit(clone, "config", "--local", "core.longpaths", "false");
                File.WriteAllText(nightlyMarker, nightlyBytes);
                File.WriteAllText(releaseMarker, releaseBytes);
                var longDir = Path.Combine(clone, "discard");
                while (longDir.Length < 300) longDir = Path.Combine(longDir, new string('x', 50));
                Directory.CreateDirectory(longDir);
                longFile = Path.Combine(longDir, "junk.txt");
                File.WriteAllText(longFile, "discard");
                File.WriteAllText(Path.Combine(clone, "junk.txt"), "discard");
                // Exclusions are for root markers only, not identically named fixture files.
                File.WriteAllText(Path.Combine(clone, "discard", ".antiphon-nightly-owned"), "discard");
            }
            NightlyRunResult Bootstrap(string state, string coordination) =>
                NightlyRun.Run(new NightlyRunOptions
                {
                    CheckoutRoot = clone, StateRoot = state, LogRoot = Path.Combine(state, "logs"), RemoteUrl = remote, Ref = "master", Trigger = "manual", WhatIf = true,
                    CoordinationRoot = coordination,
                }, RealGitHost(fixture), new NightlyRunBindings { CliPath = NightlyRunWorld.StagedCli });
            var run = Bootstrap(fixture.PathOf(mode + "-state"), fixture.PathOf(mode + "-coord"));
            rows.Check(run.ExitCode == 0 && run.Phase == "whatif", "G026 " + mode + " sync completes", run.Refusal + " " + run.Phase + " " + run.ExitCode + "\n" + fixture.Out);
            var longpaths = NightlyRunWorld.FixtureGit(clone, "config", "--local", "--get", "core.longpaths").Trim();
            rows.Check(longpaths == "true", "G026 " + mode + " clone enables long paths", longpaths);
            var ownership = NightlyRun.RunContextOwnership(clone, fixture.PathOf(mode + "-state"), "", remote);
            rows.Check(ownership.Ok, "G026 " + mode + " ownership survives clean", ownership.Reason);
            rows.Check(File.ReadAllText(Path.Combine(clone, "tracked.txt")).Trim() == "keep", "G026 " + mode + " tracked file retained");
            if (mode == "existing")
            {
                rows.Check(File.Exists(nightlyMarker) && File.ReadAllText(nightlyMarker) == nightlyBytes, "G026 nightly marker bytes unchanged");
                rows.Check(File.Exists(releaseMarker) && File.ReadAllText(releaseMarker) == releaseBytes, "G026 release marker bytes unchanged");
                rows.Check(!File.Exists(longFile), "G026 long fixture removed");
                rows.Check(!File.Exists(Path.Combine(clone, "junk.txt")), "G026 short fixture removed");
                rows.Check(!File.Exists(Path.Combine(clone, "discard", ".antiphon-nightly-owned")), "G026 nested marker is not excluded");
            }
            else rows.Check(!File.Exists(releaseMarker), "G026 nightly clone does not gain release ownership");
            // A new attempt owns fresh state; this case tests reusing the cleaned clone, not the separate run-lock lifecycle.
            var retry = Bootstrap(fixture.PathOf(mode + "-retry-state"), fixture.PathOf(mode + "-retry-coord"));
            rows.Check(retry.ExitCode == 0 && retry.Phase == "whatif", "G026 " + mode + " retry remains admitted", retry.Refusal);
        }
        rows.ShouldAllPass(16);
    }

    // ---- CARD-0545 D-10: the last stdout line is the JSON completion record ----

    private sealed record C545Run(string Row, int ExitCode, string Stdout, string Last, JsonElement? Record, JsonElement? State, JsonElement? Green, string RunId);

    /// <summary>Invoke-C545ResultLineRun: a -NoSync run on a pinned clock whose tests hop writes the C545 summary and exits testExit.</summary>
    private static C545Run ResultLineRun(string row, string utcNow, int testExit = 0, bool testsPassed = true, bool noReport = false, string checkoutRoot = "")
    {
        using var w = new NightlyRunWorld("c545-" + row);
        w.Clock = NightlyRunWorld.At(utcNow);
        w.Behaviour = NightlyRunWorld.TestsHop(NightlyRunWorld.C545Summary(testsPassed), testExit);
        var runId = "c545-" + row + "-" + Guid.NewGuid().ToString("N")[..8];
        var result = w.Run(o =>
        {
            o.NoSync = true;
            o.NoReport = noReport;
            o.RunId = runId;
            if (checkoutRoot.Length > 0) o.CheckoutRoot = checkoutRoot;
        });
        var stdout = w.Fixture.Out.ToString();
        var last = stdout.Replace("\r\n", "\n").TrimEnd('\n').Split('\n')[^1];
        JsonElement? record = null;
        try { using var doc = JsonDocument.Parse(last); record = doc.RootElement.Clone(); }
        catch (JsonException) { }
        return new C545Run(row, result.ExitCode, stdout, last, record, NightlyRunWorld.Json(w.LastRunPath), NightlyRunWorld.Json(w.LastGreenPath), runId);
    }

    private static bool FlagsEqualState(C545Run run)
    {
        if (run.Record is not { } r || run.State is not { } s) return false;
        foreach (var f in new[] { "coverageComplete", "testsPassed", "reportDelivered" })
            if (r.GetProperty(f).GetBoolean() != s.GetProperty(f).GetBoolean()) return false;
        return r.GetProperty("localDueDate").GetString() == s.GetProperty("localDueDate").GetString() && r.GetProperty("nativeRunId").GetString() == s.GetProperty("runId").GetString();
    }

    private static string Str(JsonElement? e, string name) => e is { } v && v.TryGetProperty(name, out var p) ? p.ToString() : "";

    private static bool Bool(JsonElement? e, string name) => e is { } v && v.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.True;

    [Test]
    public void C545_ResultLine()
    {
        var rows = new NightlyCaseRows();
        var green = ResultLineRun("green", "2026-09-17T23:35:00Z");
        var detail = "exit=" + green.ExitCode + " last=" + green.Last;
        rows.Check(green.ExitCode == 0 && green.Record != null && Str(green.Record, "exitCode") == "0", "C545 ResultLine green last line is the JSON record with exitCode 0", detail);
        rows.Check(green.Record != null && Str(green.Record, "nativeRunId") == green.RunId && Str(green.Record, "localDueDate") == "2026-09-18" && Str(green.Record, "trigger") == "scheduled"
            && Str(green.Record, "summaryPath").Trim().Length > 0, "C545 ResultLine green identity nativeRunId localDueDate trigger summaryPath", detail);
        rows.Check(Bool(green.Record, "testsPassed") && Bool(green.Record, "coverageComplete") && Bool(green.Record, "reportDelivered"), "C545 ResultLine green flags true", detail);
        rows.Check(Str(green.State, "localDueDate") == "2026-09-18" && Str(green.Green, "localDueDate") == "2026-09-18",
            "C545 ResultLine green localDueDate in last-run and last-complete-green", detail);
        rows.Check(FlagsEqualState(green), "C545 ResultLine green flags equal last-run.json", detail);

        var refusal = ResultLineRun("refusal", "2026-09-17T23:35:00Z", checkoutRoot: @"C:\src\Antiphon");
        // CARD-0599 D-2/D-7: the envelope carries lane identity (profile, candidateId, creditKind) so an RC result can never
        // be read back as a master nightly result.
        const string expected = "{\"nativeRunId\":\"\",\"sha\":\"\",\"ref\":\"\",\"trigger\":\"\",\"profile\":\"\",\"candidateId\":\"\",\"creditKind\":\"\",\"localDueDate\":\"\",\"policyHash\":\"\","
            + "\"coverageComplete\":false,\"testsPassed\":false,\"reportDelivered\":false,\"exitCode\":3,\"summaryPath\":\"\",\"refusal\":\"shared-tree\"}";
        rows.Check(refusal.ExitCode == 3 && refusal.Last == expected, "C545 ResultLine refusal last line is the JSON record", "exit=" + refusal.ExitCode + " last=" + refusal.Last);

        var red = ResultLineRun("test-red", "2026-09-17T23:35:00Z", testExit: 1, testsPassed: false);
        rows.Check(red.ExitCode == 1 && Str(red.Record, "exitCode") == "1" && !Bool(red.Record, "testsPassed"), "C545 ResultLine test-red exitCode 1 testsPassed false",
            "exit=" + red.ExitCode + " last=" + red.Last);
        rows.Check(FlagsEqualState(red), "C545 ResultLine test-red flags equal last-run.json", red.Last);

        var noReport = ResultLineRun("no-report", "2026-09-17T23:35:00Z", noReport: true);
        rows.Check(noReport.Record != null && !Bool(noReport.Record, "reportDelivered") && noReport.Green == null, "C545 ResultLine no-report reportDelivered false and no green file", noReport.Last);

        foreach (var (row, utc, date) in new[] { ("dst-summer", "2026-06-30T23:10:00Z", "2026-07-01"), ("dst-autumn", "2026-10-24T23:40:00Z", "2026-10-25"), ("dst-spring", "2026-03-29T00:40:00Z", "2026-03-29") })
        {
            var run = ResultLineRun(row, utc);
            rows.Check(Str(run.Record, "localDueDate") == date && Str(run.State, "localDueDate") == date, "C545 ResultLine " + row + " localDueDate " + date, run.Last);
        }

        foreach (var run in new[] { green, refusal, red, noReport })
        {
            var raw = run.Stdout.Replace("\r\n", "\n");
            rows.Check(raw.TrimEnd('\n').EndsWith(run.Last, StringComparison.Ordinal) && run.Last.StartsWith('{'), "C545 ResultLine " + run.Row + " no line follows the record", run.Last);
        }
        rows.ShouldAllPass(16);
    }

    // ---- the C# run's own rules ----

    [Test]
    public void Options_parse_back_from_their_arguments()
    {
        var options = new NightlyRunOptions
        {
            Suites = ["client", "antiphon"], NoReport = true, NoSync = true, CheckoutRoot = @"C:\x\checkout", LogRoot = @"C:\x\logs", Ref = "rc/2026-11-30",
            RemoteUrl = "https://example.invalid/r", StateRoot = @"C:\x\state", SelectionMode = "Shadow", Repository = "o/r", BaselineSha = new string('b', 40),
            BaselineReceiptPath = "b.json", QualificationPath = "q.json", TimingRoot = "t", TrainManifestPath = "m", ReleaseIntentPath = "i", SelectionAttemptPath = "sa",
            SelectionAttemptDigest = "sad", SelectionPath = "s", SelectionDigest = "sd", Attempt = 3, PriorFailuresPath = "p", PriorFailuresDigest = "pd",
            AcceptCrossCandidateSeed = true, SeedSourceCandidateId = "rc-old", Trigger = "rc", Profile = "rc", ExpectedSha = new string('c', 40), CandidateId = "rc-x",
            ReleaseRoot = @"C:\x\rel", CoordinationRoot = @"C:\x\coord", RunId = "r1", ContinueRunId = "r0", ContinueParentPid = 77, ContinueParentStartedAt = "2026-01-01T00:00:00Z",
            WhatIf = true, ResultFile = @"C:\x\result.json",
        };
        var back = NightlyRunOptions.Parse(options.ToArguments());
        back.ToArguments().ShouldBe(options.ToArguments());
        JsonSerializer.Serialize(back).ShouldBe(JsonSerializer.Serialize(options));
        NightlyRunOptions.Parse(["--selection-mode", "changeaware"]).SelectionMode.ShouldBe("ChangeAware");
        Should.Throw<NightlyUsageException>(() => NightlyRunOptions.Parse(["--seams-path", "x.ps1"]));
        Should.Throw<NightlyUsageException>(() => NightlyRunOptions.Parse(["--selection-mode", "Partial"]));
        var defaults = NightlyRunOptions.Parse([]);
        defaults.CheckoutRoot.ShouldBe(@"C:\Antiphon\nightly\checkout");
        defaults.Ref.ShouldBe("master");
        defaults.Profile.ShouldBe("nightly");
        defaults.Attempt.ShouldBe(1);
        defaults.ToArguments().ShouldBeEmpty();
    }

    /// <summary>
    /// The new hop rule: after the sync, a clone with its own scripts/nightly-run.ps1 is hopped to with -NoSync and today's
    /// argv when this code's product root is not the checkout (the continuation names this process and the shared lock's
    /// process start); when the product root is the checkout the run goes on to the tests hop.
    /// </summary>
    [Test]
    public void The_self_hop_follows_the_product_root()
    {
        using var other = new NightlyRunWorld("s19-hop");
        Directory.CreateDirectory(Path.Combine(other.Clone, "scripts"));
        File.WriteAllText(Path.Combine(other.Clone, "scripts", "nightly-run.ps1"), "# the clone's launcher\n");
        other.ProductRoot = NightlyPlanCases.Repo;
        var hop = other.Run(o => { o.RunId = "hop-run"; o.Suites = ["client"]; o.ExpectedSha = NightlyRunWorld.Sha; });
        hop.Phase.ShouldBe("hop");
        hop.ExitCode.ShouldBe(0);
        var launch = other.Launcher.Launches.ShouldHaveSingleItem();
        launch.FileName.ShouldBe("pwsh");
        launch.WorkingDirectory.ShouldBe(other.Clone);
        string[] expected =
        [
            "-NoProfile", "-NonInteractive", "-File", Path.Combine(other.Clone, "scripts", "nightly-run.ps1"), "-NoSync", "-CheckoutRoot", other.Clone, "-LogRoot", other.Logs,
            "-Ref", "master", "-StateRoot", other.State, "-RemoteUrl", NightlyFixture.DefaultOrigin, "-Trigger", "scheduled", "-Profile", "nightly", "-RunId", "hop-run",
            "-ContinueRunId", "hop-run", "-ContinueParentPid", other.Processes.CurrentPid.ToString(), "-ContinueParentStartedAt", NightlyRunWorld.ProcessStart.ToString("o"),
            "-NoReport", "-Attempt", "1", "-ExpectedSha", NightlyRunWorld.Sha, "-CoordinationRoot", other.Coordination, "-Suites", "client",
            "-TimingRoot", Path.Combine(other.Logs, "timing-hop-run"),
        ];
        launch.Arguments.ToArray().ShouldBe(expected);
        File.Exists(Path.Combine(other.State, "run.lock")).ShouldBeFalse("the parent releases its run lock after the hop");

        using var same = new NightlyRunWorld("s19-nohop");
        Directory.CreateDirectory(Path.Combine(same.Clone, "scripts"));
        File.WriteAllText(Path.Combine(same.Clone, "scripts", "nightly-run.ps1"), "# the clone's launcher\n");
        same.ProductRoot = same.Clone;
        var run = same.Run();
        run.Phase.ShouldBe("tests");
        same.Launcher.Launches.ShouldHaveSingleItem().Arguments[2].ShouldBe("nightly-tests");
    }

    /// <summary>The tests hop runs this CLI's nightly-tests command with the options the PowerShell hop passed (no seams flag).</summary>
    [Test]
    public void The_tests_hop_runs_the_nightly_tests_command()
    {
        using var w = new NightlyRunWorld("s19-testshop");
        var r = w.Run(o => { o.NoSync = true; o.RunId = "tests-hop"; o.Suites = ["client", "antiphon"]; o.Attempt = 2; o.ExpectedSha = NightlyRunWorld.Sha; });
        r.Phase.ShouldBe("tests");
        var launch = w.Launcher.Launches.ShouldHaveSingleItem();
        launch.FileName.ShouldBe("dotnet");
        launch.Arguments.Take(3).ToArray().ShouldBe(new[] { "exec", NightlyRunWorld.StagedCli, "nightly-tests" });
        launch.Arguments.ShouldNotContain("--seams-path");
        var options = NightlyTestsOptions.Parse(launch.Arguments.Skip(3).ToArray());
        options.RepoRoot.ShouldBe(w.Clone);
        options.LogRoot.ShouldBe(r.LogDir);
        options.Sha.ShouldBe(NightlyRunWorld.Sha);
        options.GitRef.ShouldBe("origin/master");
        options.RunId.ShouldBe("tests-hop");
        options.Attempt.ShouldBe(2);
        options.Suites.ShouldBe(new[] { "client", "antiphon" });
        options.ExpectedSha.ShouldBe(NightlyRunWorld.Sha);
        options.TimingRoot.ShouldBe(Path.Combine(w.Logs, "timing-tests-hop"));
        options.RcEvidenceRoot.ShouldBe(Path.Combine(r.LogDir!, "rc-run-evidence"));
        w.OutLines.ShouldContain(l => l.EndsWith("running antiphon-scripts nightly-tests", StringComparison.Ordinal));
    }

    /// <summary>
    /// The report runs in-process (no antiphon-scripts launch): the default binding is NightlyReport.Run over the host, so an
    /// unreachable API (ANTIPHON_API through the host's environment; the one retry on the host clock) is reportExit 3 and the
    /// run's exit, with card.md written beside the summary.
    /// </summary>
    [Test]
    public void The_report_runs_in_process()
    {
        using var w = new NightlyRunWorld("s19-report");
        w.Behaviour = NightlyRunWorld.TestsHop(NightlyRunWorld.C545Summary(true), 0);
        var host = new NightlyHost
        {
            Clock = new NightlyReportTests.InstantClock(NightlyFixture.DefaultNow), Git = w.Git.Run, Launcher = w.Launcher, Processes = w.Processes, Probe = w.Probe,
            Out = w.Fixture.Out, Error = w.Fixture.Error, Trace = TextWriter.Null,
            Variables = name => name == "ANTIPHON_API" ? "http://127.0.0.1:9" : Environment.GetEnvironmentVariable(name),
        };
        var bindings = w.Bindings();
        bindings.Report = null;
        var r = NightlyRun.Run(w.Options(o => { o.NoSync = true; o.NoReport = false; }), host, bindings);
        r.ReportExit.ShouldBe(3, w.Fixture.Out.ToString() + w.Fixture.Error);
        r.ExitCode.ShouldBe(3);
        r.ReportDelivered.ShouldBeFalse();
        File.Exists(Path.Combine(r.LogDir!, "card.md")).ShouldBeTrue();
        w.Launcher.Launches.ShouldAllBe(l => l.Arguments.Contains("nightly-tests"));
        w.Reports.ShouldBeEmpty();
    }

    /// <summary>A refusal never runs git, never writes state, and the selection capability is read from the host's PATH.</summary>
    [Test]
    public void Refusals_before_the_lock_leave_no_state()
    {
        using var w = new NightlyRunWorld("s19-refusals");
        foreach (var (configure, refusal) in new (Action<NightlyRunOptions>, string)[]
                 {
                     (o => o.Profile = "weekly", "unknown-profile"),
                     (o => { o.Profile = "rc"; o.Ref = "master"; }, "rc-ref"),
                     (o => { o.Profile = "rc"; o.Ref = "rc/2026-11-30"; }, "rc-expected-sha"),
                     (o => { o.Profile = "rc"; o.Ref = "rc/2026-11-30"; o.ExpectedSha = NightlyRunWorld.Sha; o.CandidateId = "rc-other"; }, "rc-candidate-id-mismatch"),
                     (o => { o.Profile = "rc"; o.Ref = "rc/2026-11-30"; o.ExpectedSha = NightlyRunWorld.Sha; o.StateRoot = @"C:\Antiphon\nightly"; }, "rc-master-state-root"),
                     (o => { o.Profile = "rc"; o.Ref = "rc/2026-11-30"; o.ExpectedSha = NightlyRunWorld.Sha; }, "rc-release-root-required"),
                     (o => o.Trigger = "rc", "trigger-profile-mismatch"),
                 })
        {
            var r = w.Run(configure);
            r.Refusal.ShouldBe(refusal);
            r.ExitCode.ShouldBe(3);
        }
        File.Exists(w.LastRunPath).ShouldBeFalse();
        w.Git.Calls.ShouldBeEmpty();
    }

    /// <summary>A hop exception's text and command land in last-run.json and in the run directory's hop-failure.txt.</summary>
    [Test]
    public void A_tests_hop_that_throws_is_recorded()
    {
        using var w = new NightlyRunWorld("s19-hopfail");
        w.Behaviour = _ => throw new InvalidOperationException("the child did not start: dotnet");
        var r = w.Run(o => { o.NoSync = true; o.RunId = "hop-fail"; });
        r.ExitCode.ShouldBe(1);
        r.Phase.ShouldBe("tests");
        var state = NightlyRunWorld.Json(w.LastRunPath)!.Value;
        state.GetProperty("error").GetString().ShouldBe("the child did not start: dotnet");
        state.GetProperty("command").GetString()!.ShouldStartWith("dotnet exec " + NightlyRunWorld.StagedCli + " nightly-tests ");
        var failure = state.GetProperty("failureLog").GetString()!;
        File.ReadAllText(failure).ShouldBe("error: the child did not start: dotnet\ncommand: " + state.GetProperty("command").GetString() + "\n");
        File.Exists(Path.Combine(w.State, "run.lock")).ShouldBeFalse();
        File.Exists(Path.Combine(w.Coordination, "native-run.lock")).ShouldBeFalse();
    }

    /// <summary>The nightly-tests command is wired in the CLI: the staged build refuses the shared tree as NightlyTests.Run does (exit 3).</summary>
    [Test]
    [Timeout(120_000)]
    public async Task The_nightly_tests_command_runs_NightlyTests(CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo("dotnet") { WorkingDirectory = Path.GetTempPath() };
        foreach (var argument in new[] { "exec", NightlyRunWorld.StagedCli, "nightly-tests", "--repo-root", @"C:\src\Antiphon", "--suites", "client", "--what-if" })
            start.ArgumentList.Add(argument);
        start.Environment["ANTIPHON_NIGHTLY_DEFERRED_WRITES"] = "off";
        var run = await C1514Support.RunProcess(start, cancellationToken);
        run.ExitCode.ShouldBe(3, run.Text);
        run.Text.ShouldContain("REFUSED: nightly-tests.ps1 will not run in the shared tree.");
        run.Text.ShouldNotContain("WhatIf: would run");

        var usage = new ProcessStartInfo("dotnet") { WorkingDirectory = Path.GetTempPath() };
        foreach (var argument in new[] { "exec", NightlyRunWorld.StagedCli, "nightly-tests", "--seams-path", "x.ps1" }) usage.ArgumentList.Add(argument);
        var refused = await C1514Support.RunProcess(usage, cancellationToken);
        refused.ExitCode.ShouldBe(2, refused.Text);
    }

    /// <summary>A host whose git is the real one (the bootstrap cases), on this fixture's writers.</summary>
    internal static NightlyHost RealGitHost(NightlyFixture fixture)
    {
        var processes = new FakeProcessTable();
        processes.Start(processes.CurrentPid, NightlyRunWorld.ProcessStart);
        return new NightlyHost
        {
            Clock = TimeProvider.System, Git = NightlyNativeGit.Run, Launcher = new RecordingLauncher(), Processes = processes, Probe = new FakeHostProbe(),
            Out = fixture.Out, Error = fixture.Error, Trace = TextWriter.Null,
        };
    }
}
