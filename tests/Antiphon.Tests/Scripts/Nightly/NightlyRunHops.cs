using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Antiphon.Nightly;

namespace Antiphon.Tests.Scripts.Nightly;

/// <summary>
/// W2-nightly S20: the run's two hops read back in-process (plan section 2 rule 3). The self-update hop is the pwsh argv of
/// NightlyRun.SelfHopArguments (the clone's scripts/nightly-run.ps1 -NoSync ...); the clone's launcher binds it and passes
/// each -PascalName parameter as the nightly-run command's --kebab-name flag (a switch bare), so its options are
/// NightlyRunOptions.Parse of that translation. The tests hop is `dotnet exec &lt;cli&gt; nightly-tests &lt;flags&gt;`.
/// </summary>
public static class NightlyRunHops
{
    /// <summary>The script a pwsh launch names after -File ("" when there is none).</summary>
    public static string ScriptOf(NightlyLaunch launch)
    {
        var args = launch.Arguments ?? [];
        for (var i = 0; i + 1 < args.Count; i++)
            if (string.Equals(args[i], "-File", StringComparison.OrdinalIgnoreCase)) return args[i + 1];
        return "";
    }

    /// <summary>The self-update hop: pwsh running some checkout's scripts/nightly-run.ps1.</summary>
    public static bool IsSelfHop(NightlyLaunch launch) =>
        string.Equals(launch.FileName, "pwsh", StringComparison.OrdinalIgnoreCase)
        && ScriptOf(launch).EndsWith("nightly-run.ps1", StringComparison.OrdinalIgnoreCase);

    /// <summary>The tests hop: dotnet exec &lt;cli&gt; nightly-tests ...</summary>
    public static bool IsTestsHop(NightlyLaunch launch) =>
        string.Equals(launch.FileName, "dotnet", StringComparison.Ordinal) && launch.Arguments is { Count: >= 3 } a && a[0] == "exec" && a[2] == "nightly-tests";

    /// <summary>The tests hop's options (NightlyTestsOptions.Parse of the flags after the command name).</summary>
    public static NightlyTestsOptions TestsOptions(NightlyLaunch launch) => NightlyTestsOptions.Parse(launch.Arguments.Skip(3).ToArray());

    /// <summary>The flags the clone's launcher passes for a self-hop argv: everything after -File &lt;script&gt;, translated.</summary>
    public static string[] LauncherFlags(IReadOnlyList<string> pwshArguments)
    {
        var start = 0;
        for (var i = 0; i + 1 < pwshArguments.Count; i++)
            if (string.Equals(pwshArguments[i], "-File", StringComparison.OrdinalIgnoreCase)) { start = i + 2; break; }
        var flags = new List<string>();
        for (var i = start; i < pwshArguments.Count; i++)
        {
            var token = pwshArguments[i];
            flags.Add(IsParameterName(token) ? Kebab(token) : token);
        }
        return flags.ToArray();
    }

    /// <summary>The child's options of a self-update hop launch.</summary>
    public static NightlyRunOptions SelfHopOptions(NightlyLaunch launch) => NightlyRunOptions.Parse(LauncherFlags(launch.Arguments));

    /// <summary>-ContinueParentStartedAt to --continue-parent-started-at.</summary>
    public static string Kebab(string parameter)
    {
        var name = parameter.TrimStart('-');
        var text = new StringBuilder("--");
        for (var i = 0; i < name.Length; i++)
        {
            if (char.IsUpper(name[i]) && i > 0) text.Append('-');
            text.Append(char.ToLowerInvariant(name[i]));
        }
        return text.ToString();
    }

    private static bool IsParameterName(string token) =>
        token.Length > 2 && token[0] == '-' && char.IsUpper(token[1]) && token.Skip(1).All(char.IsLetter);

    /// <summary>The result of one launcher run against the echo CLI: the exit, both streams, and the argv the CLI received.</summary>
    public sealed record LauncherEchoRun(int ExitCode, string Stdout, string Stderr, string[]? Arguments)
    {
        /// <summary>The command's options of the received argv (null when nothing was echoed).</summary>
        public NightlyRunOptions? Options => Arguments == null ? null : NightlyRunOptions.Parse(Arguments);
    }

    /// <summary>
    /// The production launcher scripts/nightly-run.ps1 (-File, as Windmill and launch-rc start it) under Windows PowerShell 5.1
    /// (pwsh elsewhere, or when asked), with ANTIPHON_SCRIPTS_DLL naming the script harness host's echo mode for the nightly-run
    /// command (CLI_ECHO_COMMAND): nothing is built and nothing runs; the CLI prints the argv the launcher handed it.
    /// </summary>
    public static async Task<LauncherEchoRun> LauncherEcho(string entry, IEnumerable<string> parameters, string workingDirectory, bool pwsh = false,
        IReadOnlyDictionary<string, string>? environment = null)
    {
        var shell = pwsh || !OperatingSystem.IsWindows() ? "pwsh" : "powershell.exe";
        var start = new ProcessStartInfo(shell) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = workingDirectory };
        foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", entry }.Concat(parameters)) start.ArgumentList.Add(argument);
        start.Environment["ANTIPHON_SCRIPTS_DLL"] = Path.Combine(AppContext.BaseDirectory, "script-harness-host", "Antiphon.ScriptHarnessHost.dll");
        start.Environment["CLI_ECHO_COMMAND"] = "nightly-run";
        foreach (var pair in environment ?? new Dictionary<string, string>()) start.Environment[pair.Key] = pair.Value;
        using var process = Process.Start(start) ?? throw new InvalidOperationException(shell + " did not start");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        await process.WaitForExitAsync(timeout.Token);
        var text = await stdout;
        var line = text.Split('\n').Select(l => l.Trim()).LastOrDefault(l => l.StartsWith("{\"arguments\"", StringComparison.Ordinal));
        string[]? arguments = null;
        if (line != null)
        {
            using var echo = JsonDocument.Parse(line);
            arguments = echo.RootElement.GetProperty("arguments").EnumerateArray().Select(a => a.GetString() ?? "").ToArray();
        }
        return new LauncherEchoRun(process.ExitCode, text, await stderr, arguments);
    }
}

/// <summary>
/// W2-nightly S20: an rc run chain in-process, the C# twin of the fixtures that ran the PowerShell run core on seams
/// (New-C599RunFx -Rc, New-Fx of test-change-aware-release.ps1, New-C1336Candidate): a seed repository on the candidate
/// branch carrying the committed selection inputs (global.json, both policies, select-test-rosters.ps1,
/// scripts/lib/test-selection.ps1), the catalog's source inputs as stubs, every policy project, its own
/// scripts/nightly-run.ps1 (so the self-update hop fires) and both ownership markers; its bare origin; and a clone of
/// it with long paths enabled. Every launch is answered in this process: the self-update hop runs NightlyRun.Run of the
/// child's options with the clone as its product root (the clone's own code, so it hops no further), the tests hop runs
/// NightlyTests.Run, and each test host is the native-pass fake (one passing Fixture.Fixture.One row). Git is real and
/// traced ("GIT ..."), the platform windows, the E2E prerequisites met, the report a stand-in that records its call.
/// </summary>
public sealed class NightlyRcChain : IDisposable
{
    private static readonly string[] SelectionInputs =
        ["global.json", "tests/test-execution-policy.json", "tests/test-selection-policy.json", "scripts/select-test-rosters.ps1", "scripts/lib/test-selection.ps1"];

    private readonly object _gate = new();
    private readonly StringBuilder _trace = new();

    /// <summary>A chain whose seed has one commit on candidateRef (more can be added with Commit before Publish).</summary>
    public NightlyRcChain(string prefix, string candidateRef = "rc/2026-10-18")
    {
        Fixture = NightlyFixture.Create(prefix);
        Ref = candidateRef;
        Seed = Fixture.PathOf("seed");
        Directory.CreateDirectory(Seed);
        Git(Seed, "init", "-q", "-b", candidateRef);
        Git(Seed, "config", "core.autocrlf", "false");
        var repo = NightlyPlanCases.Repo;
        foreach (var file in SelectionInputs) Write(file, File.ReadAllText(Path.Combine(repo, file.Replace('/', Path.DirectorySeparatorChar))));
        using (var catalog = JsonDocument.Parse(File.ReadAllText(Path.Combine(repo, "tests", "test-selection-policy.json"))))
        {
            var sets = catalog.RootElement.GetProperty("sets");
            var inputs = new SortedSet<string>(StringComparer.Ordinal);
            if (sets.TryGetProperty("sourceInputs", out var sourceInputs))
                foreach (var item in sourceInputs.EnumerateArray()) if (item.GetString() is { Length: > 0 } path) inputs.Add(path);
            if (sets.TryGetProperty("members", out var members))
                foreach (var member in members.EnumerateArray())
                    if (member.TryGetProperty("source", out var source) && source.GetString() is { Length: > 0 } path) inputs.Add(path);
            foreach (var path in inputs)
                if (!File.Exists(Path.Combine(Seed, path.Replace('/', Path.DirectorySeparatorChar)))) Write(path, "fixture source");
        }
        NightlyTestsWorld.CopyPolicyProjects(Seed);
        Write("scripts/nightly-run.ps1", "# the clone's launcher (the self-update hop's target)\n");
        Write(".antiphon-nightly-owned", "{\"kind\":\"nightly-clone\"}");
        Write(".antiphon-release-owned", "{\"kind\":\"release-clone\"}");
        Write("fixture.txt", "A");
        Sha = Commit("candidate A");
        Launcher = new RecordingLauncher(Answer);
        Processes.Start(Processes.CurrentPid, NightlyRunWorld.ProcessStart);
    }

    public NightlyFixture Fixture { get; }
    public string Root => Fixture.Root;
    public string Ref { get; }
    public string Seed { get; }
    public string Origin => Fixture.PathOf("origin.git");
    public string Clone => Fixture.PathOf("checkout");

    /// <summary>The first commit (candidate A).</summary>
    public string Sha { get; }

    /// <summary>The rc candidate id of a pinned sha on this chain's ref.</summary>
    public string CandidateId(string? sha = null) => ReleaseGateCredit.Candidate(Ref, sha ?? Sha).CandidateId;

    public RecordingLauncher Launcher { get; }
    public FakeProcessTable Processes { get; } = new();
    public FakeHostProbe Probe { get; } = new();

    /// <summary>The self-update hops' child options and results, in order.</summary>
    public List<NightlyRunOptions> SelfHops { get; } = [];
    public List<NightlyRunResult> SelfHopResults { get; } = [];

    /// <summary>The tests hops' options and results, in order.</summary>
    public List<NightlyTestsOptions> TestsHops { get; } = [];
    public List<NightlyTestsResult> TestsResults { get; } = [];

    /// <summary>The report stand-in's calls; it writes &lt;summary&gt;.report-receipt and answers ReportExit.</summary>
    public List<NightlyReportOptions> Reports { get; } = [];
    public int ReportExit { get; set; }

    /// <summary>Changes the tests hop's options before the in-process run (a fault the child alone sees).</summary>
    public Func<NightlyTestsOptions, NightlyTestsOptions>? BeforeTests { get; set; }

    /// <summary>Runs after each in-process tests run (C1336's optimistic synthetic reportDelivered field).</summary>
    public Action<NightlyTestsResult>? AfterTests { get; set; }

    /// <summary>Answers the tests hop instead of NightlyTests.Run (the options -> the hop's exit), for a case about the run alone.</summary>
    public Func<NightlyTestsOptions, int>? TestsAnswer { get; set; }

    /// <summary>The evidence recorders of the run and of the tests run (null: suppressed, as under the seams).</summary>
    public Func<RcRunEvidenceRecorder>? RunEvidence { get; set; }
    public Func<RcRunEvidenceRecorder>? TestsEvidence { get; set; }

    /// <summary>The GIT and EXEC lines so far.</summary>
    public string TraceText
    {
        get { lock (_gate) return _trace.ToString(); }
    }

    /// <summary>Writes a file under the seed.</summary>
    public void Write(string relative, string text)
    {
        var path = Path.Combine(Seed, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text, new UTF8Encoding(false));
    }

    /// <summary>Commits everything in the seed; returns the new head.</summary>
    public string Commit(string message)
    {
        Git(Seed, "add", "--all");
        Git(Seed, "-c", "user.name=S20 fixture", "-c", "user.email=s20@example.invalid", "-c", "commit.gpgsign=false", "-c", "core.hooksPath=.git/no-hooks", "commit", "-qm", message);
        return Git(Seed, "rev-parse", "HEAD").Trim();
    }

    /// <summary>The bare origin and the clone (long paths enabled), from the seed's current state.</summary>
    public NightlyRcChain Publish()
    {
        Git(Root, "clone", "-q", "--bare", Seed, Origin);
        Git(Root, "clone", "-q", Origin, Clone);
        Git(Clone, "config", "--local", "core.longpaths", "true");
        return this;
    }

    /// <summary>Invoke-C1336Candidate's options under &lt;root&gt;/&lt;name&gt;: rc, the pin, the clone and its origin, private roots, -NoSync -WhatIf.</summary>
    public NightlyRunOptions Options(string name, Action<NightlyRunOptions>? extra = null)
    {
        var root = Fixture.PathOf(name);
        var options = new NightlyRunOptions
        {
            Profile = "rc", Trigger = "rc", Ref = Ref, ExpectedSha = Sha, CheckoutRoot = Clone, RemoteUrl = Origin, Repository = "fixture/repository",
            StateRoot = Path.Combine(root, "state"), LogRoot = Path.Combine(root, "logs"), ReleaseRoot = Path.Combine(root, "release"),
            CoordinationRoot = Path.Combine(root, "coord"), RunId = name, NoSync = true, WhatIf = true,
        };
        extra?.Invoke(options);
        return options;
    }

    /// <summary>NightlyRun.Run of the options on this chain (the outer run: this checkout is the product root).</summary>
    public NightlyRunResult Run(NightlyRunOptions options) => NightlyRun.Run(options, Host(), Bindings(null));

    public NightlyHost Host() => new()
    {
        Clock = TimeProvider.System,
        Git = (directory, arguments) =>
        {
            Append("GIT " + string.Join(" ", arguments));
            return NightlyNativeGit.Run(directory, arguments);
        },
        Launcher = Launcher,
        Processes = Processes,
        Probe = Probe,
        Out = Fixture.Out,
        Error = Fixture.Error,
        Trace = TextWriter.Null,
    };

    /// <summary>The run bindings: the staged CLI, the product root (null: this checkout), the report stand-in, the recorder.</summary>
    public NightlyRunBindings Bindings(string? productRoot) => new()
    {
        CliPath = NightlyRunWorld.StagedCli,
        ProductRoot = productRoot ?? NightlyPlanCases.Repo,
        Report = options =>
        {
            lock (_gate) Reports.Add(options);
            File.WriteAllText(options.Summary + ".report-receipt", "inert local report I/O");
            return ReportExit;
        },
        RcEvidence = RunEvidence?.Invoke() ?? Suppressed(),
    };

    public static RcRunEvidenceRecorder Suppressed() => new(Environment.GetEnvironmentVariable("ANTIPHON_RC_RUN_EVIDENCE"), true, false, null);

    /// <summary>A JSON file under the chain, or null.</summary>
    public static JsonNode? Json(string path) => File.Exists(path) ? JsonNode.Parse(File.ReadAllText(path)) : null;

    public void Dispose() => Fixture.Dispose();

    private NightlyLaunchResult Answer(NightlyLaunch launch)
    {
        Append("EXEC " + launch.CommandText);
        if (NightlyRunHops.IsSelfHop(launch))
        {
            var child = NightlyRunHops.SelfHopOptions(launch);
            lock (_gate) SelfHops.Add(child);
            var result = NightlyRun.Run(child, Host(), Bindings(Clone));
            lock (_gate) SelfHopResults.Add(result);
            return NightlyTestsWorld.Result(result.ExitCode);
        }
        if (NightlyRunHops.IsTestsHop(launch))
        {
            var options = NightlyRunHops.TestsOptions(launch);
            if (BeforeTests != null) options = BeforeTests(options);
            lock (_gate) TestsHops.Add(options);
            if (TestsAnswer != null) return NightlyTestsWorld.Result(TestsAnswer(options));
            var bindings = new NightlyTestsBindings
            {
                RcEvidence = TestsEvidence?.Invoke() ?? Suppressed(),
                E2EPrerequisites = (_, _, _, _) => new PlainObject(("Ok", true), ("Rows", new object[0])),
            };
            Directory.CreateDirectory(options.LogRoot);
            var result = NightlyTests.Run(options, Host(), bindings);
            lock (_gate) TestsResults.Add(result);
            AfterTests?.Invoke(result);
            return NightlyTestsWorld.Result(result.ProcessExitCode);
        }
        return NativePass(launch);
    }

    private static readonly Func<NightlyLaunch, NightlyLaunchResult> NativePass = NightlyTestsWorld.C487NativePass(
        [new NightlyTrxRow("fixture-one", "Fixture.Fixture", "One")], [new NightlyTestNode("fixture-one", "Fixture", "One", "Fixture")]);

    private void Append(string line)
    {
        lock (_gate) _trace.AppendLine(line);
    }

    /// <summary>A real git in this directory; a nonzero exit throws with its output.</summary>
    public static string Git(string directory, params string[] arguments)
    {
        var start = new ProcessStartInfo("git") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var name in new[] { "GIT_AUTHOR_DATE", "GIT_COMMITTER_DATE" }) start.Environment[name] = "2026-11-01T00:00:00Z";
        start.ArgumentList.Add("-C");
        start.ArgumentList.Add(directory);
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        var errors = process.StandardError.ReadToEndAsync();
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0) throw new InvalidOperationException("fixture git failed: " + string.Join(" ", arguments) + "\n" + output + errors.Result);
        return output;
    }
}
