using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Antiphon.Checkpoints;
using Antiphon.LiveLane;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// W2 slice S22: the portable S1c checkpoint commissions, the C# successors of scripts/test-live-lane-isolation.ps1 and the
/// RunPortableRows case of scripts/test-live-lane-evidence.ps1 (both deleted; nothing tested them but the budget lint and
/// the wrapper's pass-through cases). Here they run against a fake checkpoint tool on a fake host: the real plans are
/// imported (PlanTableImporter, as `dotnet tool import` does), the rewritten manifest must load (ManifestLoader), and the
/// command order, arguments, child environment, context bindings, printed lines and exits are the scripts'. No process is
/// started.
/// </summary>
[Category("Unit")]
public sealed class LiveLaneCommissionTests
{
    private static readonly string Source = new('a', 40);

    /// <summary>A fake machine: git answers the source, the tool imports the real plan and starts and waits fake runs.</summary>
    private sealed class FakeHost(string repository) : ILiveLaneCommissionHost
    {
        public List<LiveLaneProcessStart> Calls { get; } = [];
        public StringWriter Output { get; } = new();
        public StringWriter Errors { get; } = new();
        public TextWriter Out => Output;
        public TextWriter Error => Errors;
        public int ImportExit { get; set; }
        public Queue<int> StartExits { get; } = new();
        public Queue<int> WaitExits { get; } = new();
        public bool PrintRun { get; set; } = true;
        public bool GitFails { get; set; }
        public int ExtraRunDirectories { get; set; }
        public bool WrongStateRun { get; set; }
        private int _runs;

        public void Sleep(TimeSpan delay) { }

        public LiveLaneProcessResult Run(LiveLaneProcessStart start)
        {
            Calls.Add(start);
            if (start.FileName == "git") return GitFails ? new LiveLaneProcessResult(128, "") : new LiveLaneProcessResult(0, Source + "\n");
            var verb = start.Arguments[1];
            switch (verb)
            {
                case "import":
                {
                    var plan = start.Arguments[3];
                    var real = Path.Combine(LiveLaneTestSources.RepoRoot, Path.GetRelativePath(repository, plan));
                    var manifest = PlanTableImporter.ImportFile(real).Manifest.ShouldNotBeNull();
                    File.WriteAllText(start.Arguments[5], ManifestLoader.ToYaml(manifest));
                    return new LiveLaneProcessResult(ImportExit, "");
                }
                case "start":
                {
                    var exit = StartExits.Count > 0 ? StartExits.Dequeue() : 0;
                    var run = "run-" + (++_runs).ToString(System.Globalization.CultureInfo.InvariantCulture);
                    var key = Regex.Match(File.ReadAllText(start.Arguments[2]), "bin-((?:f727-|s1c-ev-g)[0-9a-f]+)-pty").Groups[1].Value;
                    var checkpoints = Path.Combine(repository, ".antiphon", "checkpoints");
                    if (exit == 0)
                    {
                        Directory.CreateDirectory(Path.Combine(checkpoints, run));
                        File.WriteAllText(Path.Combine(checkpoints, run, "report.json"), "{\"run\":\"" + run + "\"}");
                        File.WriteAllText(Path.Combine(checkpoints, run, "state.json"), "{\"runId\":\"" + (WrongStateRun ? "other" : run) + "\"}");
                        for (var i = 0; i < ExtraRunDirectories; i++) Directory.CreateDirectory(Path.Combine(checkpoints, run + "-extra-" + i));
                        var build = Path.Combine(repository, "tests", "Antiphon.Agents.Pty.Tests", "bin-" + key + "-pty");
                        Directory.CreateDirectory(build);
                        File.WriteAllText(Path.Combine(build, "Antiphon.Agents.Pty.Tests.dll"), "fake producer build " + key);
                    }
                    return new LiveLaneProcessResult(exit, PrintRun ? "CHECKPOINT start\nRUN " + run + " started\n" : "CHECKPOINT start\n");
                }
                case "wait":
                    return new LiveLaneProcessResult(WaitExits.Count > 0 ? WaitExits.Dequeue() : 0, "WAIT " + start.Arguments[3] + "\n");
                default:
                    throw new InvalidOperationException("unexpected tool verb " + verb);
            }
        }

        public List<LiveLaneProcessStart> Tool(string verb) => Calls.Where(c => c.FileName == "dotnet" && c.Arguments[1] == verb).ToList();
    }

    private static string Value(LiveLaneProcessStart start, string flag) => start.Arguments[start.Arguments.ToList().IndexOf(flag) + 1];

    [Test]
    public void Isolation_runs_the_producer_then_the_consumers_under_one_owned_context()
    {
        using var scratch = new LaneScratch("c1545-isolation-");
        var repository = scratch.Path("repo");
        Directory.CreateDirectory(repository);
        var host = new FakeHost(repository);
        host.WaitExits.Enqueue(75);
        host.WaitExits.Enqueue(0);
        host.WaitExits.Enqueue(0);
        var tool = scratch.Path("tool", "Antiphon.Checkpoints.dll");
        new LiveLaneIsolationRows(repository, tool, controls: false, host, scratch.Root).Run().ShouldBe(0, host.Errors.ToString());

        host.Calls[0].FileName.ShouldBe("git");
        host.Calls[0].Arguments.ShouldBe(["-C", repository, "rev-parse", "HEAD"]);
        var import = host.Tool("import").Single();
        import.Arguments.ShouldBe([tool, "import", "--plan", Path.Combine(repository, LiveLaneIsolationRows.Plan), "--out", import.Arguments[5]]);
        import.Environment.ContainsKey("TEMP").ShouldBeFalse("the import ran before the groups' TEMP");
        var starts = host.Tool("start");
        starts.Select(s => Value(s, "--rows")).ShouldBe(["CP-18", "CP-24,CP-24-PARITY"]);
        foreach (var start in starts)
        {
            start.Capture.ShouldBeTrue();
            Value(start, "--expected-source-sha").ShouldBe(Source);
            Value(start, "--total-timeout").ShouldBe("26m");
            Value(start, "--total-timeout-override-reason").ShouldBe(LiveLaneIsolationRows.TotalOverrideReason);
            Value(start, "--hard-wall-timeout").ShouldBe("29m");
            start.Arguments.ShouldContain("--serial");
            start.Arguments.ShouldContain("--keep-outputs");
            start.Environment["TEMP"].ShouldBe("/tmp");
            start.WorkingDirectory.ShouldBe(repository);
        }
        host.Tool("wait").Select(w => w.Arguments[3]).ShouldBe(["run-1", "run-1", "run-2"], "75 waits again");
        host.Tool("wait").ShouldAllBe(w => w.Arguments.Contains("50s") && w.Arguments.Contains(Source));

        // The manifest: the plan rewritten for this run, loadable, with the parity row on the consumer build.
        var manifestPath = import.Arguments[5];
        Path.GetDirectoryName(manifestPath).ShouldBe(Path.Combine(repository, ".antiphon"));
        var text = File.ReadAllText(manifestPath);
        var key = Path.GetFileNameWithoutExtension(manifestPath);
        key.ShouldMatch("^f727-[0-9a-f]{8}$");
        text.ShouldContain("UseAppHost: false");
        text.ShouldNotContain("bin-live-pty-isolation");
        text.ShouldContain("bin-" + key + "-pty");
        var contextPath = Regex.Match(text, "ANTIPHON_LIVE_PORTABLE_CONTEXT: '([^']+)'").Groups[1].Value;
        File.Exists(contextPath).ShouldBeTrue();
        var loaded = ManifestLoader.LoadYaml(text, LiveLaneTestSources.RepoRoot);
        var parity = loaded.Checkpoints.Single(row => row.Id == "CP-24-PARITY");
        parity.Build.ShouldBe("bin-" + key + "-c680");
        parity.Filter.ShouldBe("/*/Antiphon.Tests.Scripts/LiveLaneIsolationTests/StartupArgvMatchesProductionApplyOff");
        loaded.Checkpoints.Select(row => row.Id).ShouldContain("CP-18");
        loaded.Checkpoints.ShouldNotContain(row => row.Id.StartsWith("PC-V", StringComparison.Ordinal));

        // The context: bound to the source and run, then to the producer's receipt and build.
        var context = JsonNode.Parse(File.ReadAllText(contextPath))!;
        ((string)context["source"]!).ShouldBe(Source);
        ((string)context["run"]!).ShouldBe(key);
        File.ReadAllText(Path.Combine((string)context["root"]!, "inert-executable")).ShouldBe("inert attested portable executable");
        var receipt = Path.Combine(repository, ".antiphon", "checkpoints", "run-1", "report.json");
        ((string)context["producerReceipt"]!).ShouldBe(receipt);
        ((string)context["producerReceiptHash"]!).ShouldBe(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(receipt))));
        ((string)context["producerBuild"]!).ShouldMatch("^[0-9A-F]{64}$");
        host.Output.ToString().ShouldContain("RUN run-1 started");
        host.Output.ToString().TrimEnd().ShouldEndWith("PROFILE COMPLETE context=" + contextPath + " producer=" + receipt + " consumer=run-2");
    }

    [Test]
    public void Isolation_controls_split_at_V12()
    {
        using var scratch = new LaneScratch("c1545-isolation-controls-");
        var repository = scratch.Path("repo");
        Directory.CreateDirectory(repository);
        var host = new FakeHost(repository);
        new LiveLaneIsolationRows(repository, "tool.dll", controls: true, host, scratch.Root).Run().ShouldBe(0, host.Errors.ToString());
        host.Tool("start").Select(s => Value(s, "--rows")).ShouldBe(
            ["CP-18,PC-V7,PC-V8,PC-V9,PC-V10,PC-V11,PC-V12", "CP-24,CP-24-PARITY,PC-V13,PC-V14,PC-V15,PC-V16,PC-V17"]);
        var text = File.ReadAllText(host.Tool("import").Single().Arguments[5]);
        var loaded = ManifestLoader.LoadYaml(text, LiveLaneTestSources.RepoRoot);
        var v12 = loaded.Checkpoints.Single(row => row.Id == "PC-V12");
        v12.Filter.ShouldBe("/*/Antiphon.Agents.Pty.Tests/LiveClaudeIsolationWiringTests/ModelSelectionComesFromContext");
        v12.TimeoutMinutes.ShouldBe(2);
        var v17 = loaded.Checkpoints.Single(row => row.Id == "PC-V17");
        v17.Filter.ShouldBe("/*/Antiphon.Tests.Scripts/LiveLaneIsolationTests/StartupArgvMatchesProductionApplyOff");
        v17.TimeoutMinutes.ShouldBe(4);
        text.ShouldContain("C1545_PROFILE_CONTROLS: V-17");
    }

    [Test]
    public void Isolation_failures_stop_with_the_scripts_reasons()
    {
        string Fail(Action<FakeHost> setup, int expected)
        {
            using var scratch = new LaneScratch("c1545-isolation-fail-");
            var repository = scratch.Path("repo");
            Directory.CreateDirectory(repository);
            var host = new FakeHost(repository);
            setup(host);
            new LiveLaneIsolationRows(repository, "tool.dll", false, host, scratch.Root).Run().ShouldBe(expected);
            return host.Errors.ToString();
        }
        Fail(h => h.GitFails = true, 1).ShouldContain("Cannot read source SHA");
        Fail(h => h.ImportExit = 5, 5).ShouldBeEmpty();
        Fail(h => h.StartExits.Enqueue(3), 1).ShouldContain("Checkpoint start refused (3)");
        Fail(h => h.PrintRun = false, 1).ShouldContain("Missing owned run identity");
        Fail(h => h.WaitExits.Enqueue(1), 1).ShouldMatch(@"Checkpoint group failed \(1\), run=run-1, context=.+context\.json");
    }

    [Test]
    public void Evidence_runs_the_tests_then_the_scan_then_publishes()
    {
        using var scratch = new LaneScratch("c1545-evidence-");
        var repository = scratch.Path("repo");
        Directory.CreateDirectory(repository);
        var tool = scratch.Path("Antiphon.Checkpoints.dll");
        File.WriteAllText(tool, "fake");
        var host = new FakeHost(repository);
        host.WaitExits.Enqueue(75);
        host.WaitExits.Enqueue(0);
        host.WaitExits.Enqueue(0);
        string? published = null;
        var roster = new[] { "control-success EvidenceIsSanitized CP-20", "c680-composer C680EvidenceIsSanitized CP-26" };
        new LiveLaneEvidenceRows(repository, tool, host, () => roster, context => { published = context; return 0; }, windows: false).Run().ShouldBe(0, host.Errors.ToString());

        var starts = host.Tool("start");
        starts.Select(s => Value(s, "--rows") + " " + Value(s, "--total-timeout") + " " + Value(s, "--hard-wall-timeout")).ShouldBe(["CP-20,CP-26 70m 90m", "CP-29 20m 25m"]);
        starts.ShouldAllBe(s => !s.Capture && s.Timeout == TimeSpan.FromMinutes(3) && !s.Arguments.Contains("--total-timeout-override-reason"));
        host.Tool("wait").Select(w => w.Arguments[3]).ShouldBe(["run-1", "run-1", "run-2"]);
        published.ShouldNotBeNull();
        foreach (var child in host.Calls.Where(c => c.FileName == "dotnet" && c.Arguments[1] != "import"))
        {
            child.Environment["TEMP"].ShouldBe("/tmp");
            child.Environment["TMPDIR"].ShouldBe("/tmp");
            child.Environment[LiveLaneEvidenceRows.ContextVariable].ShouldBe(published);
        }
        var context = JsonNode.Parse(File.ReadAllText(published!))!;
        ((string)context["checkpointRun"]!).ShouldBe("run-1");
        ((string)context["source"]!).ShouldBe(Source);
        ((string)context["runToken"]!).ShouldBe("portable");
        ((bool)context["requirePublic"]!).ShouldBeTrue();
        context["cases"]!.AsArray().Select(c => (string)c!["case"]! + "/" + (string)c["test"]! + "/" + (string)c["cp"]!)
            .ShouldBe(["control-success/EvidenceIsSanitized/CP-20", "c680-composer/C680EvidenceIsSanitized/CP-26"]);
        var key = Path.GetFileName(Path.GetDirectoryName(published))!;
        key.ShouldMatch("^s1c-ev-g[0-9a-f]{6}$");
        context["outputs"]!.AsArray().Select(o => (string)o!["id"]! + "=" + (string)o["path"]!)
            .ShouldBe(["CP-20=tests/Antiphon.Agents.Pty.Tests/bin-" + key + "-pty", "CP-26=tests/Antiphon.Tests/bin-" + key + "-c680"]);
        var output = host.Output.ToString();
        foreach (var line in new[] { "start-exit=0 rows=CP-20,CP-26", "tests-run=run-1", "wait-exit=75 run=run-1", "wait-exit=0 run=run-1", "start-exit=0 rows=CP-29",
                     "scan-run=run-2", "wait-exit=0 run=run-2" })
            output.ShouldContain(line);
        Directory.GetFiles(Path.GetDirectoryName(published)!, "wait-run-*.log").Length.ShouldBe(3);

        // The manifest: the plan rewritten for this run and loadable; CP-29 is the CLI command (the cmd launcher on Windows).
        var manifestText = File.ReadAllText(Path.Combine(Path.GetDirectoryName(published)!, "manifest.yaml"));
        manifestText.ShouldContain("bin-" + key + "-c680");
        manifestText.ShouldNotContain("bin-live-evidence");
        ManifestLoader.LoadYaml(manifestText, LiveLaneTestSources.RepoRoot).Checkpoints.Single(row => row.Id == "CP-29").Command.ShouldBe(LiveLaneEvidenceRows.ScanCommand);
        var windows = LiveLaneEvidenceRows.Rewrite(manifestText, key, windows: true);
        ManifestLoader.LoadYaml(windows, LiveLaneTestSources.RepoRoot).Checkpoints.Single(row => row.Id == "CP-29").Command.ShouldBe(LiveLaneEvidenceRows.WindowsScanCommand);
    }

    [Test]
    public void Evidence_failures_exit_as_the_script_did()
    {
        int Run(Action<FakeHost> setup, Func<string, int>? publish = null, bool tool = true)
        {
            using var scratch = new LaneScratch("c1545-evidence-fail-");
            var repository = scratch.Path("repo");
            Directory.CreateDirectory(repository);
            var path = scratch.Path("Antiphon.Checkpoints.dll");
            if (tool) File.WriteAllText(path, "fake");
            var host = new FakeHost(repository);
            setup(host);
            var exit = new LiveLaneEvidenceRows(repository, path, host, () => ["case test CP-20"], publish ?? (_ => 0), false).Run();
            if (!tool)
            {
                host.Output.ToString().Trim().ShouldBe("INVALID/evidence-sanitization reason=context-absent");
                host.Calls.ShouldBeEmpty();
            }
            return exit;
        }
        Run(_ => { }, tool: false).ShouldBe(2);
        Run(h => h.StartExits.Enqueue(4)).ShouldBe(4);
        Run(h => h.WaitExits.Enqueue(1)).ShouldBe(1);
        Run(h => { h.WaitExits.Enqueue(0); h.StartExits.Enqueue(0); h.StartExits.Enqueue(6); }).ShouldBe(6);
        Run(_ => { }, _ => 2).ShouldBe(2);
        Run(h => h.ExtraRunDirectories = 1).ShouldBe(1);
        Run(h => h.WrongStateRun = true).ShouldBe(1);
        Run(h => h.GitFails = true).ShouldBe(1);
    }
}
