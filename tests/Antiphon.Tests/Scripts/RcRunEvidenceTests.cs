using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Antiphon.Nightly;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// CARD-1836 S6b shadow run evidence, in-process (moved from test-rc-run-evidence.ps1): RcRunEvidence's source,
/// build, discovery, launch and chunk revision seals through RcRunEvidenceRecorder, and RcChunkOutcome. Fixture git
/// and output files only; git is the only process started, except OneEvidenceRootPerRun, which runs the production
/// entry scripts/nightly-run.ps1 (still PowerShell) three times on one log root.
/// </summary>
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class RcRunEvidenceTests
{
    private readonly List<string> errors = [];

    private void Note(bool ok, string label)
    {
        if (!ok) errors.Add(label);
    }

    private void Contract(string name) =>
        errors.ShouldBeEmpty("RE " + name + ".contract: " + string.Join("; ", errors));

    private void Refuses(Func<object?> body, string reason)
    {
        try
        {
            body();
            Note(false, "accepted:" + reason);
        }
        catch (Exception ex)
        {
            Note(ex.Message.Contains(reason, StringComparison.Ordinal), "wrong-refusal:" + reason + ":" + ex.Message);
        }
    }

    // -Strict, ANTIPHON_RC_RUN_EVIDENCE unset, no seams.
    private static RcRunEvidenceRecorder Strict() => new(null, false, true, null);

    private static RcRunEvidenceRecorder Off() => new("off", false, true, null);

    private static string ReadSource(string path) => RcInputFixtureRepository.ReadSource(path);

    private static int CountFiles(string directory) =>
        Directory.Exists(directory) ? Directory.GetFiles(directory, "*", SearchOption.AllDirectories).Length : 0;

    private static string NewRepo(RcInputFixtureRepository fixture)
    {
        fixture.Init();
        fixture.Put("src/input.txt", "input");
        return fixture.Commit();
    }

    private static void NewOutputs(string directory, string entry, string helper)
    {
        Directory.CreateDirectory(directory);
        File.WriteAllBytes(Path.Combine(directory, entry), [1, 2, 3, 4]);
        File.WriteAllBytes(Path.Combine(directory, helper), [5, 6, 7, 8]);
        File.WriteAllText(Path.Combine(directory, "entry.deps.json"), "{\"helper\":true}");
    }

    private static string Dir(RcInputFixtureRepository fixture, string name)
    {
        var path = Path.Combine(fixture.Scratch, name);
        Directory.CreateDirectory(path);
        return path;
    }

    private const string Build = "dotnet build server.csproj -c Debug";

    [Test, Timeout(60_000)]
    public void BuildSourceAndLaunchOutputMustJoin()
    {
        using var fixture = new RcInputFixtureRepository("re-");
        var repo = fixture.Root;
        var ev = Dir(fixture, "evidence");
        var @out = Path.Combine(fixture.Scratch, "out");
        var foreign = Path.Combine(fixture.Scratch, "foreign");
        var entryOnly = Dir(fixture, "entry-only");
        var sha = NewRepo(fixture);
        NewOutputs(@out, "entry.dll", "helper.dll");
        File.Copy(Path.Combine(@out, "entry.dll"), Path.Combine(entryOnly, "entry.dll"));
        NewOutputs(foreign, "entry.dll", "helper.dll");
        var rec = Strict();
        var snap = rec.SourceSnapshot(ev, "", repo, ["src/input.txt"], sha);
        Note(snap.EffectiveMode == "Full" && !snap.GateCredit && !snap.CoverageComplete && snap.BindingState == "diagnostic-only" && !snap.Publish, "shadow-authority");
        var build = rec.Build(ev, "", "server", Build, @out, "entry.dll", snap.Digest, "", "");
        var names = build.Outputs.Select(o => o.RelativePath).ToArray();
        Note(names.Contains("entry.dll") && names.Contains("helper.dll") && names.Contains("entry.deps.json"), "build-covers-helpers");
        Note(build.EffectiveMode == "Full" && !build.GateCredit && !build.Publish, "build-grants-no-credit");
        var launch = rec.Launch(ev, "", "server", "c1", @out, ["test"], build.Digest, repo, sha);
        Note(launch.Joined && launch.EffectiveMode == "Full" && !launch.GateCredit && !launch.Publish, "launch-joins-build");
        Refuses(() => rec.Launch(ev, "", "server", "c2", entryOnly, ["test"], build.Digest, repo, sha), "output-not-joined");
        Refuses(() => rec.Launch(ev, "", "server", "c3", foreign, ["--no-build"], build.Digest, repo, sha), "foreign-output");
        var before = CountFiles(ev);
        var off = Off().Launch(ev, "", "server", "c4", @out, ["test"], build.Digest, "", "");
        Note(off.Recording == "off" && off.EffectiveMode == "Full" && !off.GateCredit && !off.Publish, "off-switch-stays-full");
        Note(CountFiles(ev) == before, "off-switch-writes-nothing");
        Contract(nameof(BuildSourceAndLaunchOutputMustJoin));
    }

    [Test, Timeout(60_000)]
    public void ChangedInputsDuringRunRefuse()
    {
        using var fixture = new RcInputFixtureRepository("re-");
        var repo = fixture.Root;
        var ev = Dir(fixture, "evidence");
        var @out = Path.Combine(fixture.Scratch, "out");
        var sha = NewRepo(fixture);
        NewOutputs(@out, "entry.dll", "helper.dll");
        var rec = Strict();
        var snap = rec.SourceSnapshot(ev, "", repo, ["src/input.txt"], sha);
        var build = rec.Build(ev, "", "server", Build, @out, "entry.dll", snap.Digest, "", "");
        File.WriteAllText(Path.Combine(repo, "src", "input.txt"), "changed");
        Refuses(() => rec.AssertInputs(ev, "", repo, sha), "inputs-changed");
        Refuses(() => rec.Launch(ev, "", "server", "c1", @out, ["test"], build.Digest, repo, sha), "inputs-changed");
        Note(snap.EffectiveMode == "Full" && !snap.GateCredit && !snap.Publish, "changed-input-still-no-credit");
        Contract(nameof(ChangedInputsDuringRunRefuse));
    }

    [Test, Timeout(60_000)]
    public void InterruptedWriterLeavesNotRun()
    {
        using var fixture = new RcInputFixtureRepository("re-");
        var repo = fixture.Root;
        var ev = Dir(fixture, "evidence");
        var @out = Path.Combine(fixture.Scratch, "out");
        var sha = NewRepo(fixture);
        NewOutputs(@out, "entry.dll", "helper.dll");
        var rec = Strict();
        var snap = rec.SourceSnapshot(ev, "", repo, ["src/input.txt"], sha);
        rec.Build(ev, "", "server", Build, @out, "entry.dll", snap.Digest, "", "");
        var open = rec.ChunkRevision(ev, "", "server", "a", "Passed", "", false);
        var read = rec.Read(ev, "");
        Note(open.State == "open" && read.Sequence == 1 && read.State == "open" && read.Pending == "none" && read.EffectiveMode == "Full" && !read.GateCredit && !read.Publish, "open-revision-readable");
        Refuses(() => rec.ChunkRevision(ev, "", "server", "b", "", "", true), "writer-interrupted");
        read = rec.Read(ev, "");
        Note(read.Sequence == 1 && read.State == "open" && read.Pending == "not-run" && !read.GateCredit && read.EffectiveMode == "Full", "interrupt-leaves-not-run");
        var index = Path.Combine(ev, "index.json");
        var forged = JsonNode.Parse(File.ReadAllText(index))!.AsObject();
        forged["state"] = "complete";
        forged["gateCredit"] = true;
        File.WriteAllText(index, forged.ToJsonString());
        Refuses(() => rec.Read(ev, ""), "closed-green-refused");
        Contract(nameof(InterruptedWriterLeavesNotRun));
    }

    // The call sites: the nightly tests run (W2-nightly S17: NightlyTests.cs; nightly-tests-impl.ps1 is deleted), the run core
    // (W2-nightly S20: NightlyRun.cs; nightly-run-impl.ps1 is deleted), the chunk runner (W2-nightly S12: the launch, the chunk record and its shadow parse) and the suite
    // runner (W2-nightly S13: the suite's discovery record moved there with the suite preparation).
    private const string TestsImpl = "scripts/lib/Antiphon.Nightly/NightlyTests.cs";
    private const string RunImpl = "scripts/lib/Antiphon.Nightly/NightlyRun.cs";
    private const string ChunkRunner = "scripts/lib/Antiphon.Nightly/NightlyChunkRunner.cs";
    private const string SuiteRunner = "scripts/lib/Antiphon.Nightly/NightlySuiteRunner.cs";
    private const string BuildCall = "rcEvidence.Build(";
    private const string DiscoveryCall = "s.RcEvidence?.DiscoveryOnce(";
    private const string LaunchCall = "c.RcEvidence?.Launch(";
    private const string RevisionCall = "c.RcEvidence.ChunkRevision(";
    private const string ReadCall = "RcChunkOutcome.Read(";
    private const string BindCall = "RcChunkOutcome.Bind(";

    [Test, Timeout(60_000)]
    public void OneBuildAndDiscoveryPerSuite()
    {
        using var fixture = new RcInputFixtureRepository("re-");
        var repo = fixture.Root;
        var ev = Dir(fixture, "evidence");
        var @out = Path.Combine(fixture.Scratch, "out");
        var client = Path.Combine(fixture.Scratch, "client-out");
        var sha = NewRepo(fixture);
        NewOutputs(@out, "entry.dll", "helper.dll");
        NewOutputs(client, "client.dll", "helper.dll");
        var rec = Strict();
        var snap = rec.SourceSnapshot(ev, "", repo, ["src/input.txt"], sha);
        rec.Build(ev, "", "server", Build, @out, "entry.dll", snap.Digest, "", "");
        Refuses(() => rec.Build(ev, "", "server", Build, @out, "entry.dll", snap.Digest, "", ""), "build-already-recorded");
        var discovered = rec.Discovery(ev, "", "server", "", true, false);
        Note(!discovered.Skipped && discovered.EffectiveMode == "Full" && !discovered.GateCredit && !discovered.Publish, "carry-still-discovers");
        Refuses(() => rec.Discovery(ev, "", "server", "", true, true), "carry-cannot-skip-discovery");
        Refuses(() => rec.Discovery(ev, "", "server", "", false, false), "discovery-already-recorded");
        var other = rec.Build(ev, "", "client", "dotnet build client.csproj -c Debug", client, "client.dll", snap.Digest, "", "");
        Note(other.Suite == "client" && other.EffectiveMode == "Full" && !other.GateCredit, "other-suite-one-build");
        var testsImpl = ReadSource(TestsImpl);
        var runImpl = ReadSource(RunImpl);
        var chunkRunner = ReadSource(ChunkRunner);
        var suiteRunner = ReadSource(SuiteRunner);
        Note(testsImpl.Contains(BuildCall, StringComparison.Ordinal) && suiteRunner.Contains(DiscoveryCall, StringComparison.Ordinal)
            && chunkRunner.Contains(LaunchCall, StringComparison.Ordinal) && chunkRunner.Contains(RevisionCall, StringComparison.Ordinal), "tests-impl-wires-evidence");
        Note(runImpl.Contains("rcEvidence.SourceSnapshot(", StringComparison.Ordinal) && runImpl.Contains("rcEvidence.AssertInputs(", StringComparison.Ordinal), "run-impl-wires-checkout");
        Note(!testsImpl.Contains("publish-release.ps1", StringComparison.OrdinalIgnoreCase) && !runImpl.Contains("Incremental", StringComparison.OrdinalIgnoreCase), "wiring-adds-no-mode-or-publish");
        Contract(nameof(OneBuildAndDiscoveryPerSuite));
    }

    // ---- CARD-1836 S6b follow-up: the run path, three times, on one log root ------------------------------------

    /// <summary>
    /// The run core (NightlyRun.Run, in-process since W2-nightly S20; it ran the PowerShell entry on seams) records its shadow
    /// evidence with ANTIPHON_RC_RUN_EVIDENCE=record: one evidence root per run on a shared log root, the checkout's
    /// changed-input snapshot, the tests run's build, discovery, launch and chunk revision records joined Full and without
    /// gate credit. The tests hop runs NightlyTests.Run in this process with its own recorder; the test host is faked from
    /// the c487 probe's diagnostics and TRX; the host's git answers the fixture sha. A third run whose tests child sees a
    /// repository where every real git call fails (its copy with a broken .git: the GIT_CONFIG_PARAMETERS fault of the
    /// PowerShell case, which a process-wide variable would leak into other tests) keeps the first run's verdict and warns once.
    /// </summary>
    [Test, Timeout(420_000)]
    public void OneEvidenceRootPerRun(CancellationToken token)
    {
        using var fixture = new RcInputFixtureRepository("re-");
        var root = Dir(fixture, "e2e");
        var repo = Path.Combine(root, "checkout");
        var state = Dir(fixture, Path.Combine("e2e", "state"));
        var logs = Dir(fixture, Path.Combine("e2e", "logs"));
        var coord = Dir(fixture, Path.Combine("e2e", "coord"));
        var source = RcInputFixtureRepository.SourceRoot;
        Directory.CreateDirectory(repo);
        Git(repo, "init", "-q", "-b", "main");
        Git(repo, "config", "user.name", "Fixture");
        Git(repo, "config", "user.email", "fixture@example.invalid");
        Write(repo, "src/input.txt", "input");
        Git(repo, "add", "src/input.txt");
        Git(repo, "commit", "-qm", "fixture");
        Write(repo, "tests/Probe/Probe.csproj", "<Project><PropertyGroup><IsTestProject>true</IsTestProject></PropertyGroup></Project>\n");
        var policy = new JsonObject
        {
            ["schemaVersion"] = 1,
            ["policyHash"] = "pending",
            ["defaultSuites"] = new JsonArray("probe"),
            ["suites"] = new JsonObject
            {
                ["probe"] = new JsonObject { ["name"] = "Probe", ["mode"] = "native", ["assembly"] = "Probe", ["project"] = "tests/Probe/Probe.csproj" },
            },
            ["projects"] = new JsonArray("tests/Probe/Probe.csproj"),
        };
        var indented = new JsonSerializerOptions { WriteIndented = true };
        policy["policyHash"] = NightlyPolicy.HashText(policy.ToJsonString(indented));
        var policyPath = Write(repo, "tests/test-execution-policy.json", policy.ToJsonString(indented));
        var reread = JsonNode.Parse(File.ReadAllText(policyPath))!;
        Note(NightlyPolicy.HashText(File.ReadAllText(policyPath)) == (string?)reread["policyHash"], "policy-hash");
        Git(repo, "add", "tests/Probe/Probe.csproj", "tests/test-execution-policy.json");
        Git(repo, "commit", "-qm", "probe");
        var sha = Git(repo, "rev-parse", "HEAD");
        Git(repo, "remote", "add", "origin", "https://github.com/michal-ciechan/Antiphon");
        Git(repo, "config", "core.longpaths", "true");
        NewOutputs(Path.Combine(repo, "tests", "Probe", "bin", "Debug", "net9.0"), "Probe.dll", "helper.dll");
        File.WriteAllText(Path.Combine(repo, ".antiphon-nightly-owned"), "{\"origin\":\"https://github.com/michal-ciechan/Antiphon\",\"kind\":\"nightly-clone\"}");
        var probe = Path.Combine(source, "scripts", "fixtures", "nightly", "c487-probe");
        // The fault run's tests child: the same files, a .git that names no repository.
        var faulted = Path.Combine(root, "faulted-checkout");
        foreach (var file in Directory.EnumerateFiles(repo, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(repo, file);
            if (relative.StartsWith(".git" + Path.DirectorySeparatorChar, StringComparison.Ordinal)) continue;
            var target = Path.Combine(faulted, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, true);
        }
        File.WriteAllText(Path.Combine(faulted, ".git"), "gitdir: " + Path.Combine(root, "no-such-git-dir") + "\n");

        var childLines = new List<string>();
        var faultChild = false;
        var processes = new Nightly.FakeProcessTable();
        processes.Start(processes.CurrentPid, Nightly.NightlyRunWorld.ProcessStart);
        var output = new StringWriter();
        NightlyHost host = null!;
        NightlyLaunchResult Answer(NightlyLaunch launch)
        {
            if (Nightly.NightlyRunHops.IsTestsHop(launch))
            {
                var options = Nightly.NightlyRunHops.TestsOptions(launch);
                if (faultChild) options.RepoRoot = faulted;
                Directory.CreateDirectory(options.LogRoot);
                var recorder = new RcRunEvidenceRecorder("record", false, false, line => { lock (childLines) childLines.Add(line); });
                var tests = NightlyTests.Run(options, host, new NightlyTestsBindings { RcEvidence = recorder });
                return Nightly.NightlyTestsWorld.Result(tests.ProcessExitCode, "");
            }
            var args = launch.Arguments.ToList();
            string Take(string name) => args.IndexOf(name) is var i and >= 0 && i + 1 < args.Count ? args[i + 1] : "";
            if (args.Contains("--list-tests") && Take("--diagnostic-output-directory") is { Length: > 0 } discovery)
            {
                Directory.CreateDirectory(discovery);
                File.Copy(Path.Combine(probe, "discovery.diag"), Path.Combine(discovery, "discovery.diag"), true);
            }
            else if (args.Any(a => a.StartsWith("--report-trx", StringComparison.Ordinal)))
            {
                if (Take("--diagnostic-output-directory") is { Length: > 0 } diag)
                {
                    Directory.CreateDirectory(diag);
                    File.Copy(Path.Combine(probe, "execution.diag"), Path.Combine(diag, "execution.diag"), true);
                }
                if (Take("--results-directory") is { Length: > 0 } results && Take("--report-trx-filename") is { Length: > 0 } trx)
                {
                    Directory.CreateDirectory(results);
                    File.Copy(Path.Combine(probe, "all.trx"), Path.Combine(results, trx), true);
                }
            }
            return Nightly.NightlyTestsWorld.Result(0);
        }
        host = new NightlyHost
        {
            Clock = TimeProvider.System,
            Git = (_, arguments) =>
            {
                if (arguments.Contains("--get") && arguments.Contains("core.longpaths")) return new NightlyGitResult(0, "true");
                return arguments.Contains("rev-parse") ? new NightlyGitResult(0, sha) : new NightlyGitResult(0, "");
            },
            Launcher = new Nightly.RecordingLauncher(Answer),
            Processes = processes,
            Probe = new Nightly.FakeHostProbe(),
            Out = output,
            Error = output,
            Trace = TextWriter.Null,
        };
        EntryRun Run(string runId)
        {
            var result = NightlyRun.Run(new NightlyRunOptions
            {
                NoSync = true, NoReport = true, CheckoutRoot = repo, StateRoot = state, LogRoot = logs, CoordinationRoot = coord, RemoteUrl = "https://github.com/michal-ciechan/Antiphon",
                Ref = "master", Trigger = "scheduled", Profile = "nightly", SelectionMode = "Full", Suites = ["probe"], Repository = "michal-ciechan/Antiphon", RunId = runId,
            }, host, new NightlyRunBindings
            {
                CliPath = Nightly.NightlyRunWorld.StagedCli, RcEvidence = new RcRunEvidenceRecorder("record", false, false, line => { lock (childLines) childLines.Add(line); }),
            });
            var record = NightlyRun.ResultRecord(result);
            var last = JsonNode.Parse(File.ReadAllText(Path.Combine(state, "last-run.json")));
            var phase = (string?)last?["runId"] == runId ? (string?)last?["phase"] ?? "" : "";
            var logDir = (string?)last?["runId"] == runId ? (string?)last?["logDir"] ?? "" : "";
            return new EntryRun(runId, result.ExitCode, phase, logDir, PsValue.Str(record["nativeRunId"]), output.ToString());
        }
        var first = Run("s6bfol01");
        var second = Run("s6bfol02");
        var detail = $"first={first.Phase}/{first.ExitCode} second={second.Phase}/{second.ExitCode}";
        Console.WriteLine(string.Join(Environment.NewLine, childLines.TakeLast(40)));
        Console.WriteLine(detail);
        Note(first.LogDir.Length > 0, "run-dir:" + detail + " " + first.Output);
        var one = first.LogDir.Length > 0 ? ReadRunEvidence(first.LogDir) : null;
        var two = second.LogDir.Length > 0 ? ReadRunEvidence(second.LogDir) : null;
        Note(one?.Join is not null && one.Join["joined"]?.GetValue<bool>() == true, "missing-join-record");
        var inputs = one?.Source?["inputs"]?.AsArray();
        Note(inputs is { Count: >= 1 } && (string?)inputs[0]!["path"] == "tests/test-execution-policy.json", "changed-input-snapshot");
        Note(one is { Revisions: >= 1 }, "chunk-revision");
        Note(one is { Discovery: >= 1 }, "discovery-revision");
        Note(one is not null && two is not null && one.Evidence != two.Evidence, "second-run-own-root");
        Note(second.NativeRunId == second.RunId && two?.Source is not null, "second-run-refused");
        if (one?.Join is not null)
            Note((string?)one.Join["effectiveMode"] == "Full" && one.Join["gateCredit"]?.GetValue<bool>() == false, "shadow-stays-full");
        var offRoot = Path.Combine(root, "off-evidence");
        var before = CountFiles(offRoot);
        var off = new RcRunEvidenceRecorder("off", false, false, null).SourceSnapshot(offRoot, "", repo, null!, sha);
        Note(off.Recording == "off" && off.EffectiveMode == "Full" && !off.GateCredit, "off-switch-stays-full");
        Note(CountFiles(offRoot) == before, "off-switch-writes-nothing");
        // Every real git call of the tests child fails; the verdict stays and one warning line is printed.
        const string warning = "rc-run-evidence: git-failed";
        int Warnings() { lock (childLines) return childLines.Count(l => l.Contains(warning, StringComparison.Ordinal)); }
        var warningsBefore = Warnings();
        faultChild = true;
        var fault = Run("s6bfol03");
        faultChild = false;
        var warnings = Warnings() - warningsBefore;
        Console.WriteLine($"fault={fault.Phase}/{fault.ExitCode} warnings={warnings}");
        Note(fault.Phase == first.Phase && fault.ExitCode == first.ExitCode, "fault-verdict");
        Note(warnings == 1, "fault-one-warning");
        var runText = ReadSource(RunImpl);
        var testsText = ReadSource(TestsImpl);
        Note(!runText.Contains("Incremental", StringComparison.OrdinalIgnoreCase) && !testsText.Contains("publish-release.ps1", StringComparison.OrdinalIgnoreCase), "run-path-adds-no-mode-or-publish");
        Contract(nameof(OneEvidenceRootPerRun));
    }

    private sealed record EntryRun(string RunId, int ExitCode, string Phase, string LogDir, string NativeRunId, string Output);
    private sealed record RunEvidence(string Evidence, JsonNode? Join, JsonNode? Source, int Revisions, int Discovery);

    private static RunEvidence ReadRunEvidence(string runDirectory)
    {
        var evidence = Path.Combine(runDirectory, "rc-run-evidence");
        JsonNode? join = null;
        var launches = Path.Combine(evidence, "launches");
        if (Directory.Exists(launches))
        {
            var file = Directory.GetFiles(launches, "*.json", SearchOption.AllDirectories).FirstOrDefault();
            if (file is not null) join = JsonNode.Parse(File.ReadAllText(file));
        }
        var sourcePath = Path.Combine(evidence, "source.json");
        var source = File.Exists(sourcePath) ? JsonNode.Parse(File.ReadAllText(sourcePath)) : null;
        var revisions = Path.Combine(evidence, "revisions");
        var discovery = Path.Combine(evidence, "discovery");
        return new RunEvidence(evidence, join, source,
            Directory.Exists(revisions) ? Directory.GetFiles(revisions, "revision.json", SearchOption.AllDirectories).Length : 0,
            Directory.Exists(discovery) ? Directory.GetFiles(discovery, "*.json").Length : 0);
    }

    private static int Occurrences(string path, string text)
    {
        if (!File.Exists(path)) return 0;
        var all = File.ReadAllText(path);
        var count = 0;
        for (var at = all.IndexOf(text, StringComparison.Ordinal); at >= 0; at = all.IndexOf(text, at + text.Length, StringComparison.Ordinal)) count++;
        return count;
    }

    private static string Git(string root, params string[] args)
    {
        var start = new ProcessStartInfo("git")
        {
            WorkingDirectory = root, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
        };
        start.Environment["GIT_TERMINAL_PROMPT"] = "0";
        foreach (var arg in args) start.ArgumentList.Add(arg);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("git did not start");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(60_000))
        {
            try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            throw new TimeoutException("git " + string.Join(' ', args) + " did not exit within 60 s");
        }
        process.WaitForExit();
        if (process.ExitCode != 0) throw new InvalidOperationException("git " + string.Join(' ', args) + " exited " + process.ExitCode + ": " + error.Result.Trim());
        return output.Result.Trim();
    }

    private static string Write(string root, string relative, string text)
    {
        var path = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
        return path;
    }

    private static void CopyTree(string from, string to, bool top)
    {
        Directory.CreateDirectory(to);
        foreach (var file in Directory.GetFiles(from)) File.Copy(file, Path.Combine(to, Path.GetFileName(file)), true);
        foreach (var directory in Directory.GetDirectories(from))
        {
            var name = Path.GetFileName(directory);
            if (top && (name == "bin" || name == "obj")) continue;
            CopyTree(directory, Path.Combine(to, name), false);
        }
    }

    // ---- chunk outcomes ----------------------------------------------------------------------------------------

    private sealed record TrxRow(string Id, string ClassName, string MethodName, string Outcome, string DebugTrace = "");

    // Write-RcOutcomeTrx.
    private static void WriteTrx(string path, string summaryOutcome, int total, int executed, int passed, int failed, int notExecuted, params TrxRow[] rows)
    {
        var defs = new StringBuilder("<TestDefinitions>");
        var results = new StringBuilder("<Results>");
        foreach (var r in rows)
        {
            defs.Append($"<UnitTest id=\"{r.Id}\" name=\"{r.ClassName}.{r.MethodName}\"><TestMethod className=\"{r.ClassName}\" name=\"{r.MethodName}\" /></UnitTest>");
            results.Append(string.IsNullOrWhiteSpace(r.DebugTrace)
                ? $"<UnitTestResult testId=\"{r.Id}\" testName=\"{r.ClassName}.{r.MethodName}\" outcome=\"{r.Outcome}\" />"
                : $"<UnitTestResult testId=\"{r.Id}\" testName=\"{r.ClassName}.{r.MethodName}\" outcome=\"{r.Outcome}\"><Output><DebugTrace>{r.DebugTrace}</DebugTrace></Output></UnitTestResult>");
        }
        defs.Append("</TestDefinitions>");
        results.Append("</Results>");
        var summary = $"<ResultSummary outcome=\"{summaryOutcome}\"><Counters total=\"{total}\" executed=\"{executed}\" passed=\"{passed}\" failed=\"{failed}\" notExecuted=\"{notExecuted}\" /></ResultSummary>";
        File.WriteAllText(path, "<?xml version=\"1.0\" encoding=\"utf-8\"?><TestRun>" + defs + results + summary + "</TestRun>");
    }

    private static RcChunkOutcome Shadow(string path, RcSkipEntry[]? roster = null, string platform = "") =>
        RcChunkOutcome.Read(0, false, path, roster!, platform);

    [Test, Timeout(60_000)]
    public void ChunkOutcomeNeedsResults()
    {
        using var fixture = new RcInputFixtureRepository("re-");
        var results = Dir(fixture, "results");
        var passedFile = Path.Combine(results, "passed.trx");
        var jsonFile = Path.Combine(results, "passed.json");
        var badFile = Path.Combine(results, "bad.txt");
        WriteTrx(passedFile, "Completed", 1, 1, 1, 0, 0, new TrxRow("1", "Ns.Sample", "Method", "Passed"));
        File.WriteAllText(Path.Combine(results, "weak.trx"), "<TestRun id=\"1\"><Results /></TestRun>");
        File.WriteAllText(jsonFile, "{\"ok\":true}");
        File.WriteAllText(badFile, "not-json");
        var passed = Shadow(passedFile);
        var weak = Shadow(Path.Combine(results, "weak.trx"));
        var jsonPassed = Shadow(jsonFile);
        var drained = RcChunkOutcome.Read(0, true, passedFile, null!, "");
        var missing = Shadow(Path.Combine(results, "missing.trx"));
        var unparsed = Shadow(badFile);
        var failed = RcChunkOutcome.Read(2, false, passedFile, null!, "");
        Note(passed.Outcome == "Passed" && string.IsNullOrWhiteSpace(passed.Reason), "passed-needs-results");
        Note(weak.Outcome == "NotRun" && weak.Reason == "results-summary-unreadable", "weak-trx-not-passed");
        Note(jsonPassed.Outcome == "NotRun" && jsonPassed.Reason == "results-summary-unreadable", "json-without-rows-not-passed");
        Note(drained.Outcome == "NotRun" && drained.Reason == "drain-timeout", "drain-timeout-not-run");
        Note(missing.Outcome == "NotRun" && missing.Reason == "results-missing", "missing-results-not-run");
        Note(unparsed.Outcome == "NotRun" && unparsed.Reason == "results-unparseable", "unparseable-results-not-run");
        Note(failed.Outcome == "Failed" && failed.Reason == "exit-2", "nonzero-exit-failed");
        var repo = fixture.Root;
        var ev = Dir(fixture, "evidence");
        var sha = NewRepo(fixture);
        var rec = Strict();
        rec.SourceSnapshot(ev, "", repo, ["src/input.txt"], sha);
        var stored = rec.ChunkRevision(ev, "", "server", "c1", failed.Outcome, failed.Reason, false);
        var body = JsonNode.Parse(File.ReadAllText(Path.Combine(ev, "revisions", "1", "revision.json")))!;
        Note(stored.Sequence == 1 && (string?)body["outcome"] == "Failed" && (string?)body["reason"] == "exit-2" && body["gateCredit"]?.GetValue<bool>() == false, "reason-is-stored");
        Note(ReadSource(ChunkRunner).Contains(ReadCall, StringComparison.Ordinal), "chunk-outcome-is-wired");
        Contract(nameof(ChunkOutcomeNeedsResults));
    }

    // New-RcNativeCheckFixture: a chunk's TRX and its invocation-stamped log, written now (or the TRX three hours
    // ago). The native suite verdict's discovery and execution diagnostic are not written: that verdict is
    // PowerShell-typed (NightlyChunkVerdict.NativeSuiteVerdict) and is not called here, so each case states the
    // verdict it returns for the fixture.
    private sealed record NativeFixture(string Directory, string Trx, string Log, string Invocation, DateTime NotBefore);

    private static NativeFixture NewNativeFixture(string directory, TrxRow[] rows, bool staleTrx = false, string failedAttribute = "")
    {
        System.IO.Directory.CreateDirectory(directory);
        var trx = Path.Combine(directory, "chunk.trx");
        var failedRows = rows.Count(r => r.Outcome == "Failed");
        WriteTrx(trx, "Completed", rows.Length, rows.Length, rows.Length - failedRows, failedRows, 0, rows);
        if (failedAttribute.Length > 0) File.WriteAllText(trx, File.ReadAllText(trx).Replace("failed=\"0\"", "failed=\"" + failedAttribute + "\"", StringComparison.Ordinal));
        var log = Path.Combine(directory, "chunk.log");
        const string invocation = "rc-native-invocation";
        File.WriteAllText(log, "C487_INVOCATION=" + invocation + Environment.NewLine);
        var now = DateTime.UtcNow;
        File.SetLastWriteTimeUtc(trx, staleTrx ? now.AddHours(-3) : now);
        File.SetLastWriteTimeUtc(log, now);
        return new NativeFixture(directory, trx, log, invocation, staleTrx ? now.AddMinutes(-30) : now.AddMinutes(-5));
    }

    private static NightlyFreshness LogFresh(NativeFixture fx) => NightlyChunkVerdict.Freshness(fx.Log, fx.Directory, fx.NotBefore, fx.Invocation);

    // The native verdict's first gate: the TRX is fresh in the run directory.
    private static NightlyFreshness TrxFresh(NativeFixture fx) => NightlyChunkVerdict.Freshness(fx.Trx, fx.Directory, fx.NotBefore, "");

    private static readonly TrxRow ClassA = new("1", "Ns.ClassA", "MethodA", "Passed");

    [Test, Timeout(90_000)]
    public void ShadowPassedIsAtLeastTheRealCheck()
    {
        using var fixture = new RcInputFixtureRepository("re-");
        var results = Dir(fixture, "results");
        void NotWeaker(string label, string path)
        {
            // The native check needs the suite's discovery, which none of these launches has: it fails them all.
            var shadow = Shadow(path);
            Note(shadow.Outcome != "Passed", label + " shadow=Passed");
        }
        var bare = Path.Combine(results, "bare.trx");
        File.WriteAllText(bare, "<TestRun id=\"1\"><Results /></TestRun>");
        NotWeaker("summary-unreadable", bare);
        var identity = Path.Combine(results, "identity.trx");
        File.WriteAllText(identity, "<?xml version=\"1.0\" encoding=\"utf-8\"?><TestRun><ResultSummary outcome=\"Completed\"><Counters total=\"1\" executed=\"1\" passed=\"1\" failed=\"0\" notExecuted=\"0\" /></ResultSummary><Results><UnitTestResult testId=\"missing\" testName=\"Ns.Sample.Method\" outcome=\"Passed\" /></Results></TestRun>");
        NotWeaker("identity-unreadable", identity);
        var failed = Path.Combine(results, "failed.trx");
        WriteTrx(failed, "Completed", 1, 1, 0, 1, 0, new TrxRow("1", "Ns.Sample", "Method", "Failed"));
        NotWeaker("failed-count", failed);
        var executed = Path.Combine(results, "executed.trx");
        WriteTrx(executed, "Completed", 1, 4, 1, 0, 0, new TrxRow("1", "Ns.Sample", "Method", "Passed"));
        NotWeaker("executed-count", executed);
        var skip = Path.Combine(results, "skip.trx");
        WriteTrx(skip, "Completed", 1, 0, 0, 0, 1, new TrxRow("1", "Ns.Sample", "Method", "NotExecuted", "Skipped: not-on-this-host"));
        NotWeaker("expected-skip-roster", skip);
        var coverage = Path.Combine(results, "coverage.trx");
        WriteTrx(coverage, "Completed", 1, 0, 0, 0, 1, new TrxRow("1", "Ns.Sample", "Method", "Skipped"));
        NotWeaker("coverage-incomplete", coverage);
        var json = Path.Combine(results, "weak.json");
        File.WriteAllText(json, "{\"ok\":true}");
        NotWeaker("json-summary-unreadable", json);
        var passFx = NewNativeFixture(Path.Combine(results, "real-pass"), [ClassA]);
        var passShadow = Shadow(passFx.Trx);
        Note(TrxFresh(passFx).Ok && passShadow.Outcome == "Passed", "real-pass-stays-passed shadow=" + passShadow.Outcome);
        var missingFailed = Path.Combine(results, "missing-failed.trx");
        File.WriteAllText(missingFailed, "<?xml version=\"1.0\" encoding=\"utf-8\"?><TestRun><TestDefinitions><UnitTest id=\"1\" name=\"Ns.Sample.Method\"><TestMethod className=\"Ns.Sample\" name=\"Method\" /></UnitTest></TestDefinitions><Results><UnitTestResult testId=\"1\" testName=\"Ns.Sample.Method\" outcome=\"Passed\" /></Results><ResultSummary outcome=\"Completed\"><Counters total=\"1\" executed=\"1\" passed=\"1\" notExecuted=\"0\" /></ResultSummary></TestRun>");
        NotWeaker("missing-failed-attribute", missingFailed);
        var otherOutcome = Path.Combine(results, "other-outcome.trx");
        WriteTrx(otherOutcome, "Failed", 1, 1, 1, 0, 0, new TrxRow("1", "Ns.Sample", "Method", "Passed"));
        NotWeaker("summary-not-completed", otherOutcome);
        RcSkipEntry[] roster = [new RcSkipEntry("Ns.Sample.Method", "linux", "not-on-this-host", "platform")];
        var listed = Path.Combine(results, "listed-skip.trx");
        WriteTrx(listed, "Completed", 1, 0, 0, 0, 1, new TrxRow("1", "Ns.Sample", "Method", "NotExecuted", "Skipped: not-on-this-host"));
        var withRoster = Shadow(listed, roster, "linux");
        var withoutRoster = Shadow(listed);
        Note(withRoster.Outcome == "Passed" && string.IsNullOrWhiteSpace(withRoster.Reason), "roster-skip-passes");
        Note(withoutRoster.Outcome != "Passed", "roster-skip-needs-roster");
        var goodJson = Path.Combine(results, "good.json");
        File.WriteAllText(goodJson, "{\"executed\":1,\"failed\":0,\"coverageComplete\":true,\"rows\":[{\"Outcome\":\"Passed\",\"Identity\":\"Ns.Sample.Method\"}]}");
        Note(Shadow(goodJson).Outcome == "Passed", "json-real-pass");
        var falseCover = Path.Combine(results, "false-cover.json");
        File.WriteAllText(falseCover, "{\"executed\":1,\"failed\":0,\"coverageComplete\":false,\"rows\":[{\"Outcome\":\"Passed\",\"Identity\":\"Ns.Sample.Method\"}]}");
        Note(Shadow(falseCover).Outcome != "Passed", "json-coverage-false");
        var overrideFile = Path.Combine(results, "override.json");
        File.WriteAllText(overrideFile, "{\"executed\":1,\"failed\":1,\"coverageComplete\":true,\"rows\":[{\"Outcome\":\"Failed\",\"Identity\":\"Ns.Sample.Method\"}]}");
        Note(Shadow(overrideFile).Outcome != "Passed", "json-coverage-true-does-not-override");
        Contract(nameof(ShadowPassedIsAtLeastTheRealCheck));
    }

    // CARD-2008. The recorded shadow outcome goes through the native suite verdict (NightlyChunkVerdict) and log freshness.
    [Test, Timeout(120_000)]
    public void ShadowPassedMatchesNativeBranch()
    {
        using var fixture = new RcInputFixtureRepository("re-");
        var results = Dir(fixture, "results");
        var passFx = NewNativeFixture(Path.Combine(results, "bound-pass"), [ClassA]);
        var passFresh = LogFresh(passFx);
        var passBound = RcChunkOutcome.Bind(Shadow(passFx.Trx), true, true, [], passFresh.Ok, passFresh.Reason);
        Note(TrxFresh(passFx).Ok && passFresh.Ok && passBound.Outcome == "Passed", "real-pass-stays-passed bound=" + passBound.Outcome + " fresh=" + passFresh.Reason);
        // The shadow parse passes each of these; the native verdict fails it (testsPassed, coverageComplete, reason).
        void NativeGap(string label, NativeFixture fx, bool testsPassed, bool coverageComplete, string reason)
        {
            var fresh = LogFresh(fx);
            var shadow = Shadow(fx.Trx);
            var bound = RcChunkOutcome.Bind(shadow, testsPassed, coverageComplete, [reason], fresh.Ok, fresh.Reason);
            Note(shadow.Outcome == "Passed", label + " shadow-parse=" + shadow.Outcome);
            Note(bound.Outcome != "Passed" && bound.Reason == reason,
                label + " shadow=" + shadow.Outcome + " real=failure bound=" + bound.Outcome + " reason=" + bound.Reason);
        }
        var staleFx = NewNativeFixture(Path.Combine(results, "stale-trx"), [ClassA], staleTrx: true);
        var stale = TrxFresh(staleFx);
        Note(stale.Reason.Contains("stale-timestamp", StringComparison.Ordinal), "stale-trx real-reason " + stale.Reason);
        Note(!stale.Ok, "stale-trx real-did-not-fail fresh=" + stale.Reason);
        NativeGap("stale-trx", staleFx, false, false, stale.Reason);
        NativeGap("missing-required-class", NewNativeFixture(Path.Combine(results, "missing-class"), [ClassA]), false, false, "missing-required-class ClassB");
        NativeGap("trx-diagnostic-mismatch", NewNativeFixture(Path.Combine(results, "mismatch"), [ClassA]), true, false, "trx-diagnostic-mismatch classa.methoda");
        NativeGap("missing-discovery", NewNativeFixture(Path.Combine(results, "missing-discovery"), [ClassA]), true, false, "missing-discovery");
        foreach (var badFailed in new[] { "x", "+0" })
        {
            var badFx = NewNativeFixture(Path.Combine(results, "failed-" + badFailed.Replace("+", "plus", StringComparison.Ordinal)), [ClassA], failedAttribute: badFailed);
            var bad = Shadow(badFx.Trx);
            Note(bad.Outcome == "NotRun" && bad.Reason == "results-summary-unreadable", "non-numeric-failed " + badFailed + " shadow=" + bad.Outcome + " reason=" + bad.Reason);
        }
        var closed = RcChunkOutcome.Bind(Shadow(passFx.Trx), null, null, null!, null, null!);
        Note(closed.Outcome == "NotRun" && closed.Reason == "results-unreadable", "unreadable-real shadow=" + closed.Outcome);
        var half = RcChunkOutcome.Bind(Shadow(passFx.Trx), true, true, [], null, null!);
        Note(half.Outcome == "NotRun", "unreadable-fresh shadow=" + half.Outcome);
        var chunkRunner = ReadSource(ChunkRunner);
        var verdictAt = chunkRunner.IndexOf("NightlyChunkVerdict.NativeSuiteVerdict(", StringComparison.Ordinal);
        var freshAt = chunkRunner.IndexOf("NightlyChunkVerdict.Freshness(", StringComparison.Ordinal);
        var bindAt = chunkRunner.LastIndexOf(BindCall, StringComparison.Ordinal);
        Note(verdictAt >= 0 && freshAt >= 0 && bindAt > verdictAt && bindAt > freshAt, "recorded-after-real-check bind=" + bindAt);
        Note(Count(chunkRunner, ".ChunkRevision(") == 1 && chunkRunner.Contains(RevisionCall, StringComparison.Ordinal), "one-chunk-record");
        var evidence = ReadSource("scripts/lib/Antiphon.Nightly/RcRunEvidence.cs");
        var idle = Off().Read("", "");
        Note(evidence.Contains("public string EffectiveMode => \"Full\";", StringComparison.Ordinal) && evidence.Contains("public bool GateCredit => false;", StringComparison.Ordinal)
            && evidence.Contains("public string BindingState => \"diagnostic-only\";", StringComparison.Ordinal) && evidence.Contains("public bool Publish => false;", StringComparison.Ordinal)
            && idle.EffectiveMode == "Full" && !idle.GateCredit && idle.BindingState == "diagnostic-only" && !idle.Publish, "shadow-guarantees-unchanged");
        Contract(nameof(ShadowPassedMatchesNativeBranch));
    }

    private static int Count(string text, string part)
    {
        var count = 0;
        for (var at = text.IndexOf(part, StringComparison.Ordinal); at >= 0; at = text.IndexOf(part, at + part.Length, StringComparison.Ordinal)) count++;
        return count;
    }

    [Test, Timeout(60_000)]
    public void TornRevisionStaysNotRun()
    {
        using var fixture = new RcInputFixtureRepository("re-");
        var repo = fixture.Root;
        var ev = Dir(fixture, "evidence");
        var @out = Path.Combine(fixture.Scratch, "out");
        var sha = NewRepo(fixture);
        NewOutputs(@out, "entry.dll", "helper.dll");
        var rec = Strict();
        var snap = rec.SourceSnapshot(ev, "", repo, ["src/input.txt"], sha);
        rec.Build(ev, "", "server", Build, @out, "entry.dll", snap.Digest, "", "");
        rec.ChunkRevision(ev, "", "server", "a", "Passed", "", false);
        var tornDir = Path.Combine(ev, "revisions", "2");
        Directory.CreateDirectory(tornDir);
        var tornPath = Path.Combine(tornDir, "revision.json");
        File.WriteAllText(tornPath, "{");
        var read = rec.Read(ev, "");
        Note(read.Sequence == 1 && read.Pending == "not-run" && read.EffectiveMode == "Full" && !read.GateCredit, "torn-is-not-run");
        var next = rec.ChunkRevision(ev, "", "server", "b", "Failed", "exit-1", false);
        read = rec.Read(ev, "");
        Note(next.Sequence == 3 && read.Sequence == 3 && read.Pending == "not-run", "torn-does-not-block");
        Note(File.ReadAllText(tornPath) == "{", "torn-file-kept");
        var only = Path.Combine(fixture.Scratch, "torn-only", "revisions", "1");
        Directory.CreateDirectory(only);
        File.WriteAllText(Path.Combine(only, "revision.json"), "{");
        var orphan = rec.Read(Path.Combine(fixture.Scratch, "torn-only"), "");
        Note(orphan.Sequence == 0 && orphan.Pending == "not-run" && !orphan.GateCredit, "orphan-root-is-not-run");
        Contract(nameof(TornRevisionStaysNotRun));
    }

    [Test, Timeout(90_000)]
    public void LaunchStampIsCheaper()
    {
        using var fixture = new RcInputFixtureRepository("re-");
        var repo = fixture.Root;
        var ev = Dir(fixture, "evidence");
        var @out = Path.Combine(fixture.Scratch, "out");
        var sample = Dir(fixture, "sample");
        var sha = NewRepo(fixture);
        NewOutputs(@out, "entry.dll", "helper.dll");
        var chunk = RandomNumberGenerator.GetBytes(4 * 1024 * 1024);
        var sampleBytes = 0L;
        for (var index = 1; index <= 4; index++)
        {
            File.WriteAllBytes(Path.Combine(sample, "part" + index + ".dll"), chunk);
            sampleBytes += chunk.Length;
        }
        var rec = Strict();
        var snap = rec.SourceSnapshot(ev, "", repo, ["src/input.txt"], sha);
        var build = rec.Build(ev, "", "server", Build, @out, "entry.dll", snap.Digest, "", "");
        var fixtureWatch = Stopwatch.StartNew();
        var launch = rec.Launch(ev, "", "server", "c1", @out, ["test"], build.Digest, repo, sha);
        fixtureWatch.Stop();
        var buildText = File.ReadAllText(Path.Combine(ev, "builds", "server.json"));
        Note(launch.Joined && buildText.Contains("lastWriteUtcTicks", StringComparison.Ordinal), "stamp-joins-build");
        File.WriteAllBytes(Path.Combine(@out, "helper.dll"), [9, 9, 9, 9]);
        Refuses(() => rec.Launch(ev, "", "server", "c2", @out, ["test"], build.Digest, repo, sha), "output-not-joined");
        // The full read (a build seal) against the stamp check (a launch join), neither with the source check's git.
        var bulkBuildWatch = Stopwatch.StartNew();
        var bulkBuild = rec.Build(ev, "", "bulk", "dotnet build bulk.csproj -c Debug", sample, "part1.dll", snap.Digest, "", "");
        bulkBuildWatch.Stop();
        var bulkLaunchWatch = Stopwatch.StartNew();
        var bulkLaunch = rec.Launch(ev, "", "bulk", "c1", sample, ["test"], bulkBuild.Digest, "", "");
        bulkLaunchWatch.Stop();
        var wide = Dir(fixture, "wide");
        File.WriteAllBytes(Path.Combine(wide, "entry.dll"), [1, 2, 3, 4]);
        var tiny = new byte[32];
        for (var index = 1; index <= 279; index++) File.WriteAllBytes(Path.Combine(wide, "h" + index.ToString("000", System.Globalization.CultureInfo.InvariantCulture) + ".dll"), tiny);
        rec.Build(ev, "", "wide", "dotnet build wide.csproj -c Debug", wide, "entry.dll", snap.Digest, "", "");
        var wideWatch = Stopwatch.StartNew();
        var wideLaunch = rec.Launch(ev, "", "wide", "c1", wide, ["test"], "", repo, sha);
        wideWatch.Stop();
        const double productionBytes = 174587904.0;
        const double launches = 1289.0;
        var scale = productionBytes / sampleBytes;
        var fullMs = Math.Round(bulkBuildWatch.Elapsed.TotalMilliseconds, 3);
        var stampMs = Math.Round(bulkLaunchWatch.Elapsed.TotalMilliseconds, 3);
        var fixtureMs = Math.Round(fixtureWatch.Elapsed.TotalMilliseconds, 3);
        var wideMs = Math.Round(wideWatch.Elapsed.TotalMilliseconds, 3);
        Console.WriteLine(FormattableString.Invariant(
            $"A6 fixtureLaunchMs={fixtureMs} sampleFiles=4 sampleBytes={sampleBytes} fullMs={fullMs} stampMs={stampMs} wideFiles=280 wideLaunchMs={wideMs} extrapolatedFull1289={Math.Round(fullMs * scale * launches, 1)} extrapolatedStamp1289={Math.Round(wideMs * launches, 1)}"));
        Note(bulkLaunch.Joined && wideLaunch.Joined && stampMs < fullMs, "stamp-cheaper-than-full-read");
        var chunkRunner = ReadSource(ChunkRunner);
        var launchAt = chunkRunner.IndexOf(LaunchCall, StringComparison.Ordinal);
        var watchAt = chunkRunner.IndexOf("var phaseStart = host.Clock.GetTimestamp();", StringComparison.Ordinal);
        Note(launchAt >= 0 && watchAt > launchAt, "launch-evidence-outside-phase");
        Contract(nameof(LaunchStampIsCheaper));
    }
}
