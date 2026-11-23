using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Antiphon.Nightly;
using Antiphon.Tests.Application;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts.Nightly;

/// <summary>
/// W2-nightly S01: scripts/lib/antiphon-cli.ps1, the launcher the Windows PowerShell 5.1 entries run antiphon-scripts
/// through. It is production PowerShell, so these tests start it: powershell.exe (5.1) on Windows, pwsh elsewhere, from
/// a copy under a temporary checkout root (its bin-launcher stays there). ANTIPHON_SCRIPTS_DLL names the staged
/// Antiphon.Scripts build (antiphon-scripts/) or the cli-echo mode of the script harness host, so nothing runs msbuild.
/// </summary>
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class AntiphonCliLauncherTests
{
    private const string Driver = """
        param([string]$Request)
        $ErrorActionPreference = 'Stop'
        $r = [IO.File]::ReadAllText($Request) | ConvertFrom-Json
        . (Join-Path $PSScriptRoot 'scripts/lib/antiphon-cli.ps1')
        $arguments = New-Object System.Collections.Generic.List[string]
        foreach ($a in @($r.arguments)) { $arguments.Add([string]$a) }
        $code = Invoke-AntiphonCli -Command ([string]$r.command) -Arguments $arguments.ToArray() -ResultFile ([string]$r.resultFile)
        exit $code
        """;

    private static string StagedCli => Path.Combine(AppContext.BaseDirectory, "antiphon-scripts", "Antiphon.Scripts.dll");

    private static string StagedEcho => Path.Combine(AppContext.BaseDirectory, "script-harness-host", "Antiphon.ScriptHarnessHost.dll");

    private sealed record LauncherRun(int Exit, string Stdout, string Stderr)
    {
        public JsonElement Echo()
        {
            var line = Stdout.Split('\n').Select(text => text.Trim()).First(text => text.StartsWith('{'));
            using var document = JsonDocument.Parse(line);
            return document.RootElement.Clone();
        }
    }

    /// <summary>A temporary checkout root holding scripts/lib/antiphon-cli.ps1 and the driver.</summary>
    private static NightlyFixture Checkout()
    {
        var fixture = NightlyFixture.Create("antiphon-cli");
        Directory.CreateDirectory(fixture.PathOf("scripts", "lib"));
        File.Copy(Path.Combine(DelegateScriptRunner.RepoRoot, "scripts", "lib", "antiphon-cli.ps1"), fixture.PathOf("scripts", "lib", "antiphon-cli.ps1"));
        File.WriteAllText(fixture.PathOf("drive.ps1"), Driver);
        return fixture;
    }

    private static Process Start(NightlyFixture fixture, string dll, string command, string[] arguments, string resultFile = "",
        IReadOnlyDictionary<string, string>? environment = null)
    {
        var request = fixture.PathOf("request-" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(request, JsonSerializer.Serialize(new { command, arguments, resultFile }));
        var start = new ProcessStartInfo(OperatingSystem.IsWindows() ? "powershell.exe" : "pwsh")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = fixture.Root,
        };
        foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", fixture.PathOf("drive.ps1"), "-Request", request })
            start.ArgumentList.Add(argument);
        start.Environment["ANTIPHON_SCRIPTS_DLL"] = dll;
        foreach (var pair in environment ?? new Dictionary<string, string>()) start.Environment[pair.Key] = pair.Value;
        return Process.Start(start) ?? throw new InvalidOperationException("the launcher shell did not start");
    }

    private static async Task<LauncherRun> Finish(Process process)
    {
        using (process)
        {
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(120));
            await process.WaitForExitAsync(timeout.Token);
            return new LauncherRun(process.ExitCode, await stdout, await stderr);
        }
    }

    private static Task<LauncherRun> Run(NightlyFixture fixture, string dll, string command, string[] arguments, string resultFile = "",
        IReadOnlyDictionary<string, string>? environment = null) =>
        Finish(Start(fixture, dll, command, arguments, resultFile, environment));

    private static string[] Arguments(JsonElement echo) =>
        echo.GetProperty("arguments").EnumerateArray().Select(argument => argument.GetString()!).ToArray();

    [Test]
    public async Task Exit_codes_and_streams_pass_through()
    {
        using var fixture = Checkout();

        var status = await Run(fixture, StagedCli, "rc-ledger-status", []);
        status.Exit.ShouldBe(2, status.Stderr);
        status.Stderr.ShouldContain("usage: antiphon-scripts rc-ledger-import");
        status.Stdout.Trim().ShouldBeEmpty();

        var unknown = await Run(fixture, StagedCli, "no-such-command", []);
        unknown.Exit.ShouldBe(2, unknown.Stderr);
        unknown.Stderr.ShouldStartWith("usage: antiphon-scripts select-release");

        var echoed = await Run(fixture, StagedEcho, "cli-echo", ["--flag", "value"], environment: new Dictionary<string, string> { ["CLI_ECHO_EXIT"] = "37" });
        echoed.Exit.ShouldBe(37, echoed.Stderr);
        Arguments(echoed.Echo()).ShouldBe(new[] { "--flag", "value" });
        echoed.Echo().GetProperty("workingDirectory").GetString()!.TrimEnd('\\', '/').ShouldBe(fixture.Root.TrimEnd('\\', '/'), StringCompareShould.IgnoreCase);
    }

    [Test]
    public async Task A_missing_dll_exits_9_with_one_stderr_line()
    {
        using var fixture = Checkout();

        var run = await Run(fixture, fixture.PathOf("missing", "Antiphon.Scripts.dll"), "rc-ledger-status", []);

        run.Exit.ShouldBe(9);
        run.Stdout.Trim().ShouldBeEmpty();
        var lines = run.Stderr.Split('\n').Select(line => line.Trim()).Where(line => line.Length > 0).ToArray();
        lines.Length.ShouldBe(1, run.Stderr);
        lines[0].ShouldStartWith("antiphon-cli: ANTIPHON_SCRIPTS_DLL names no file");
    }

    [Test]
    public async Task An_empty_value_is_dropped_with_its_flag()
    {
        using var fixture = Checkout();

        var run = await Run(fixture, StagedEcho, "cli-echo", ["--a", "", "--b", "x", "--c", "   ", "--what-if", "--d", "y", ""]);

        run.Exit.ShouldBe(0, run.Stderr);
        Arguments(run.Echo()).ShouldBe(new[] { "--b", "x", "--what-if", "--d", "y" });
    }

    [Test]
    public async Task A_trailing_backslash_is_trimmed_and_quoting_survives()
    {
        using var fixture = Checkout();
        var spaced = fixture.PathOf("with space");

        var run = await Run(fixture, StagedEcho, "cli-echo",
            ["--root", spaced + "\\", "--next", "v", "--drive", @"C:\", "--quote", "say \"hi\"", "--many", @"x\\", "--inner", @"a\b c"]);

        run.Exit.ShouldBe(0, run.Stderr);
        Arguments(run.Echo()).ShouldBe(new[] { "--root", spaced, "--next", "v", "--drive", @"C:\", "--quote", "say \"hi\"", "--many", "x", "--inner", @"a\b c" });
    }

    [Test]
    public async Task The_result_file_is_passed_after_a_stale_one_is_removed()
    {
        using var fixture = Checkout();
        var result = fixture.PathOf("out dir", "result.json");
        Directory.CreateDirectory(Path.GetDirectoryName(result)!);
        File.WriteAllText(result, "stale");

        var run = await Run(fixture, StagedEcho, "cli-echo", ["--x", "1"], result);

        run.Exit.ShouldBe(0, run.Stderr);
        var echo = run.Echo();
        Arguments(echo).ShouldBe(new[] { "--x", "1", "--result-file", result });
        echo.GetProperty("resultFileExisted").GetBoolean().ShouldBeFalse();
        using var written = JsonDocument.Parse(File.ReadAllText(result));
        Arguments(written.RootElement).ShouldBe(new[] { "--x", "1", "--result-file", result });
    }

    [Test]
    public async Task The_cli_runs_from_a_copy_and_the_source_dll_stays_unlocked()
    {
        using var fixture = Checkout();
        var source = fixture.PathOf("source");
        Directory.CreateDirectory(source);
        foreach (var file in Directory.EnumerateFiles(Path.GetDirectoryName(StagedEcho)!))
            File.Copy(file, Path.Combine(source, Path.GetFileName(file)));
        var dll = Path.Combine(source, Path.GetFileName(StagedEcho));
        var release = fixture.PathOf("release");
        var launcherRoot = fixture.PathOf("bin-launcher");

        var process = Start(fixture, dll, "cli-echo", [], environment: new Dictionary<string, string> { ["CLI_ECHO_HOLD"] = release });
        try
        {
            var first = await process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(120));
            first.ShouldNotBeNull("the held CLI printed nothing");
            using var echo = JsonDocument.Parse(first);
            var running = echo.RootElement.GetProperty("baseDirectory").GetString()!;
            running.ShouldStartWith(Path.Combine(launcherRoot, "run-"), Case.Insensitive);
            if (OperatingSystem.IsWindows())
            {
                // While the CLI runs, the source can be rewritten (a build) but the loaded copy cannot.
                using (new FileStream(dll, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
                Should.Throw<IOException>(() => new FileStream(Path.Combine(running, Path.GetFileName(dll)), FileMode.Open, FileAccess.ReadWrite, FileShare.None).Dispose());
            }
        }
        finally
        {
            File.WriteAllText(release, "go");
        }
        var held = await Finish(process);
        held.Exit.ShouldBe(0, held.Stderr);

        // The same output reuses its copy; a changed output gets a new one.
        (await Run(fixture, dll, "cli-echo", [])).Exit.ShouldBe(0);
        Directory.GetDirectories(launcherRoot, "run-*").Length.ShouldBe(1);
        File.WriteAllText(Path.Combine(source, "extra.txt"), "changed");
        var changed = await Run(fixture, dll, "cli-echo", []);
        changed.Exit.ShouldBe(0, changed.Stderr);
        Directory.GetDirectories(launcherRoot, "run-*").Length.ShouldBe(2);
        File.Exists(Path.Combine(changed.Echo().GetProperty("baseDirectory").GetString()!, "extra.txt")).ShouldBeTrue();
    }

    // ---- W2-nightly S05: scripts/nightly-health.ps1, the launcher Windmill's readiness job runs ----

    private static readonly string[] HealthEnvironment =
    [
        "WINDMILL_BASE_URL", "WINDMILL_TOKEN", "WINDMILL_TOKEN_FILE", "WINDMILL_WORKSPACE", "ANTIPHON_NIGHTLY_WINDMILL_SCRIPT",
        "ANTIPHON_NIGHTLY_REPOSITORY_PATH", "ANTIPHON_NIGHTLY_PROJECT_ID",
    ];

    /// <summary>A temporary checkout root holding scripts/nightly-health.ps1 beside scripts/lib/antiphon-cli.ps1.</summary>
    private static NightlyFixture HealthCheckout()
    {
        var fixture = Checkout();
        File.Copy(Path.Combine(DelegateScriptRunner.RepoRoot, "scripts", "nightly-health.ps1"), fixture.PathOf("scripts", "nightly-health.ps1"));
        return fixture;
    }

    /// <summary>Runs a script with -File as Windmill does, with the staged CLI and no machine Windmill settings.</summary>
    private static Task<LauncherRun> RunScript(NightlyFixture fixture, string script, string[] arguments, IReadOnlyDictionary<string, string> environment)
    {
        var start = new ProcessStartInfo(OperatingSystem.IsWindows() ? "powershell.exe" : "pwsh")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = fixture.Root,
        };
        foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", script }.Concat(arguments))
            start.ArgumentList.Add(argument);
        foreach (var name in HealthEnvironment) start.Environment.Remove(name);
        start.Environment["ANTIPHON_SCRIPTS_DLL"] = StagedCli;
        foreach (var pair in environment) start.Environment[pair.Key] = pair.Value;
        return Finish(Process.Start(start) ?? throw new InvalidOperationException("the launcher shell did not start"));
    }

    /// <summary>A state root with today's scheduled run (green or red) and a readiness config naming the watchdog.</summary>
    private static string HealthState(NightlyFixture fixture, string name, string today, bool green, string watchdogUrl, string repository)
    {
        var root = fixture.PathOf(name);
        var completed = DateTime.UtcNow.AddMinutes(-5).ToString("o");
        NightlyFixture.WriteJson(Path.Combine(root, "last-run.json"), new
        {
            runId = "launcher-run", sha = "sha-launcher", @ref = "master", trigger = "scheduled", phase = "final", policyHash = "p", localDueDate = today,
            coverageComplete = true, testsPassed = green, reportDelivered = true, completedAt = completed, lastActivityAt = completed,
        });
        NightlyFixture.WriteJson(Path.Combine(root, "readiness-config.json"), new
        {
            expectedScriptHash = "a", expectedPolicyHash = "p", watchdogSnapshotUrl = watchdogUrl, watchdogInstanceId = "wd-launcher",
            repositoryPath = repository, projectId = "c5450000-0000-4000-8000-0000000000bb",
        });
        return root;
    }

    /// <summary>last-monitor.json without RecordedAt (each run records its own instant).</summary>
    private static string Monitor(string root)
    {
        var record = JsonNode.Parse(File.ReadAllText(Path.Combine(root, "last-monitor.json")))!.AsObject();
        record.Remove("RecordedAt").ShouldBeTrue();
        return record.ToJsonString();
    }

    [Test]
    public async Task Nightly_health_launcher_matches_the_evaluator_for_a_healthy_and_an_unhealthy_state()
    {
        using var fixture = HealthCheckout();
        const string schedule = "u/lndcobra/antiphon_nightly_tests";
        var today = NightlyLondon.DateText(NightlyLondon.ToLocal(DateTime.UtcNow));
        var list = new JsonArray { new JsonObject { ["id"] = "job-today", ["type"] = "CompletedJob", ["success"] = true, ["schedule_path"] = schedule } };
        using var windmill = LoopbackHttpStub.Start(
            ("scripts/get/p/" + schedule, """{"hash":"a","tag":"desktop","path":"u/lndcobra/antiphon_nightly_tests"}"""),
            ("schedules/get/" + schedule, """{"enabled":true,"paused":false,"tag":"desktop","args":{}}"""),
            ("jobs/list", list.ToJsonString()),
            ("get_result/job-today", new JsonObject { ["nativeRunId"] = "launcher-run", ["sha"] = "sha-launcher", ["localDueDate"] = today }.ToJsonString()));
        var snapshot = new JsonObject
        {
            ["schemaVersion"] = 1, ["instanceId"] = "wd-launcher", ["namespace"] = "mc", ["heartbeatAt"] = DateTime.UtcNow.ToString("o"), ["openOutages"] = new JsonArray(),
        };
        using var watchdog = LoopbackHttpStub.Start(("snapshot.json", snapshot.ToJsonString()));
        var environment = new Dictionary<string, string> { ["WINDMILL_BASE_URL"] = windmill.BaseUrl, ["WINDMILL_TOKEN"] = "launcher-token" };
        var repository = fixture.PathOf("repo");
        var entry = fixture.PathOf("scripts", "nightly-health.ps1");

        foreach (var (name, green, exit) in new[] { ("healthy", true, 0), ("unhealthy", false, 1) })
        {
            var launched = HealthState(fixture, name + "-launched", today, green, watchdog.BaseUrl + "/snapshot.json", repository);
            var inProcess = HealthState(fixture, name + "-in-process", today, green, watchdog.BaseUrl + "/snapshot.json", repository);

            // The Windmill command line: -RepositoryPath and -ReadinessConfigPath (here also -StateRoot, a fixture).
            var run = await RunScript(fixture, entry, ["-StateRoot", launched, "-RepositoryPath", repository, "-ReadinessConfigPath", Path.Combine(launched, "readiness-config.json")],
                environment);
            var error = new StringWriter();
            var expected = NightlyHealth.Run(
                new NightlyHealthOptions { StateRoot = inProcess, RepositoryPath = repository, ReadinessConfigPath = Path.Combine(inProcess, "readiness-config.json") },
                NightlyHealthSeams.Production(variable => environment.GetValueOrDefault(variable)!, error));

            expected.ExitCode.ShouldBe(exit, string.Join(",", expected.Health.Reasons));
            run.Exit.ShouldBe(exit, run.Stderr);
            run.Stdout.ShouldBeEmpty("the evaluator prints nothing to stdout");
            run.Stderr.Trim().ShouldBe(error.ToString().Trim());
            Monitor(launched).ShouldBe(Monitor(inProcess));
        }
    }

    [Test]
    public async Task Nightly_health_launcher_passthru_reads_the_record_back_and_a_seams_path_refuses()
    {
        using var fixture = HealthCheckout();
        var entry = fixture.PathOf("scripts", "nightly-health.ps1");
        var state = fixture.PathOf("state");
        File.WriteAllText(fixture.PathOf("passthru.ps1"), """
            param([string]$Entry, [string]$StateRoot)
            $r = & $Entry -StateRoot $StateRoot -PassThru
            [Console]::Out.WriteLine((@($r.PSObject.Properties | ForEach-Object { $_.Name }) -join ','))
            [Console]::Out.WriteLine([string]$r.ExitCode + '|' + [string]$r.Health.Healthy + '|' + (@($r.Health.Reasons) -join ',') + '|' + [string]$r.Receipt + '|' + [string]$r.Identity.ScriptHash)
            $s = & $Entry -StateRoot $StateRoot -SeamsPath 'seams.ps1' -PassThru
            [Console]::Out.WriteLine([string]$s.ExitCode + '|' + [string]$s.Refusal)
            """);

        var passThru = await RunScript(fixture, fixture.PathOf("passthru.ps1"), ["-Entry", entry, "-StateRoot", state], new Dictionary<string, string>());

        passThru.Exit.ShouldBe(0, passThru.Stderr);
        var lines = passThru.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        lines.Length.ShouldBe(3, passThru.Stdout);
        lines[0].ShouldBe("ExitCode,Health,Notification,Receipt,RecordedAt,Identity");
        // No Windmill credentials and no watchdog URL: unhealthy, nothing sent, the reasons in the evaluator's order.
        lines[1].ShouldStartWith("1|False|missing-script,missing-schedule,");
        lines[1].ShouldEndWith(",unauthorized-destination,watchdog-unreachable|False|");
        lines[2].ShouldBe("3|seams-path-unsupported");
        File.Exists(Path.Combine(state, "last-monitor.json")).ShouldBeTrue();

        var refusedRoot = fixture.PathOf("refused");
        var refused = await RunScript(fixture, entry, ["-StateRoot", refusedRoot, "-SeamsPath", "seams.ps1"], new Dictionary<string, string>());
        refused.Exit.ShouldBe(3, refused.Stderr);
        refused.Stdout.Trim().ShouldBeEmpty();
        refused.Stderr.Trim().ShouldBe("nightly-health: seams-path-unsupported");
        Directory.Exists(refusedRoot).ShouldBeFalse("a refusal evaluates nothing and writes nothing");
    }

    // ---- W2-nightly S20: scripts/nightly-run.ps1, the launcher Windmill's nightly job, release-cut and qualification-launch run ----

    /// <summary>A temporary checkout root holding scripts/nightly-run.ps1 beside scripts/lib/antiphon-cli.ps1 (its bin-launcher stays there).</summary>
    private static NightlyFixture RunCheckout()
    {
        var fixture = Checkout();
        File.Copy(Path.Combine(DelegateScriptRunner.RepoRoot, "scripts", "nightly-run.ps1"), fixture.PathOf("scripts", "nightly-run.ps1"));
        return fixture;
    }

    /// <summary>The whole parameter block as Windmill's 5.1 binds it, each value distinct, and the switches.</summary>
    private static readonly (string Name, string? Value)[] EveryParameter =
    [
        ("-Suites", "client,scripts"), ("-NoReport", null), ("-NoSync", null), ("-CheckoutRoot", @"C:\fixture root\checkout"), ("-LogRoot", @"C:\fixture root\logs"),
        ("-Ref", "rc/2026-11-30"), ("-RemoteUrl", "https://example.invalid/fixture.git"), ("-StateRoot", @"C:\fixture root\state"), ("-SelectionMode", "Shadow"),
        ("-Repository", "fixture/repository"), ("-BaselineSha", new string('b', 40)), ("-BaselineReceiptPath", @"C:\fixture root\receipt.json"),
        ("-QualificationPath", @"C:\fixture root\claim.json"), ("-TimingRoot", @"C:\fixture root\timing"), ("-TrainManifestPath", @"C:\fixture root\train.json"),
        ("-ReleaseIntentPath", @"C:\fixture root\intent.json"), ("-SelectionAttemptPath", @"C:\fixture root\attempt.json"), ("-SelectionAttemptDigest", new string('c', 64)),
        ("-SelectionPath", @"C:\fixture root\selection.json"), ("-SelectionDigest", new string('d', 64)), ("-Attempt", "3"), ("-PriorFailuresPath", @"C:\fixture root\seed.json"),
        ("-PriorFailuresDigest", new string('e', 64)), ("-AcceptCrossCandidateSeed", null), ("-SeedSourceCandidateId", "rc-2026-10-27-cde8def41638"), ("-Trigger", "rc"),
        ("-Profile", "rc"), ("-ExpectedSha", new string('a', 40)), ("-CandidateId", "rc-2026-11-30-aaaaaaaaaaaa"), ("-ReleaseRoot", @"C:\fixture root\releases"),
        ("-CoordinationRoot", @"C:\fixture root\coord"), ("-RunId", "s20-launcher"), ("-ContinueRunId", "s20-parent"), ("-ContinueParentPid", "4242"),
        ("-ContinueParentStartedAt", "2026-09-01T06:00:00.0000000Z"), ("-WhatIf", null),
    ];

    [Test]
    public async Task Nightly_run_launcher_passes_every_parameter_as_its_flag_and_keeps_the_defaults()
    {
        using var fixture = RunCheckout();
        var entry = fixture.PathOf("scripts", "nightly-run.ps1");

        var every = await NightlyRunHops.LauncherEcho(entry, EveryParameter.SelectMany(p => p.Value == null ? new[] { p.Name } : new[] { p.Name, p.Value }), fixture.Root);
        every.ExitCode.ShouldBe(0, every.Stderr);
        var received = every.Options.ShouldNotBeNull(every.Stdout + every.Stderr);
        // The options the same flags parse to directly: the launcher's translation, value for value (the result file is its own).
        var expected = NightlyRunOptions.Parse(EveryParameter.SelectMany(p => p.Value == null ? new[] { NightlyRunHops.Kebab(p.Name) } : new[] { NightlyRunHops.Kebab(p.Name), p.Value }).ToArray());
        received.ResultFile.ShouldNotBeNullOrWhiteSpace("the launcher always asks for the result record");
        received.ResultFile = "";
        JsonSerializer.Serialize(received).ShouldBe(JsonSerializer.Serialize(expected));
        expected.Suites.ShouldBe(["client", "scripts"]);

        var defaults = await NightlyRunHops.LauncherEcho(entry, [], fixture.Root);
        defaults.ExitCode.ShouldBe(0, defaults.Stderr);
        var bound = defaults.Options.ShouldNotBeNull(defaults.Stdout + defaults.Stderr);
        bound.CheckoutRoot.ShouldBe(@"C:\Antiphon\nightly\checkout");
        bound.StateRoot.ShouldBe(@"C:\Antiphon\nightly");
        (bound.Ref, bound.RemoteUrl, bound.Repository, bound.SelectionMode, bound.Trigger, bound.Profile, bound.Attempt)
            .ShouldBe(("master", "https://github.com/michal-ciechan/Antiphon", "michal-ciechan/Antiphon", "Full", "scheduled", "nightly", 1));
        (bound.NoSync, bound.NoReport, bound.WhatIf, bound.AcceptCrossCandidateSeed, bound.ContinueParentPid, bound.Suites).ShouldBe((false, false, false, false, 0, (string[]?)null));

        // The parameter block, the 5.1 header and the help text stay as Windmill and the operators know them.
        var text = File.ReadAllText(Path.Combine(DelegateScriptRunner.RepoRoot, "scripts", "nightly-run.ps1"));
        text.ShouldStartWith("#requires -Version 5.1");
        text.ShouldContain("    [ValidateSet('Full', 'Shadow', 'ChangeAware')][string]$SelectionMode = 'Full',\n");
        text.ShouldContain("    [string]$CheckoutRoot = 'C:\\Antiphon\\nightly\\checkout',\n");
        text.ShouldContain("origin/master");
        File.ReadAllBytes(Path.Combine(DelegateScriptRunner.RepoRoot, "scripts", "nightly-run.ps1")).All(b => b < 128).ShouldBeTrue("ASCII only");
    }

    [Test]
    public async Task Nightly_run_launcher_refuses_a_seams_path_and_still_prints_the_record()
    {
        using var fixture = RunCheckout();
        var entry = fixture.PathOf("scripts", "nightly-run.ps1");
        var state = fixture.PathOf("state");
        var expected = NightlyRun.RecordLine(new NightlyRunResult { ExitCode = 3, Refusal = "seams-path-unsupported" });

        var refused = await RunScript(fixture, entry, ["-StateRoot", state, "-SeamsPath", "seams.ps1"], new Dictionary<string, string>());
        refused.Exit.ShouldBe(3, refused.Stderr);
        refused.Stdout.Replace("\r\n", "\n").TrimEnd('\n').Split('\n')[^1].ShouldBe(expected);
        refused.Stderr.Trim().ShouldBe("nightly-run: seams-path-unsupported");
        Directory.Exists(state).ShouldBeFalse("a refusal runs nothing and writes nothing");

        // -PassThru (release-cut's call) returns the run-core names instead of exiting.
        File.WriteAllText(fixture.PathOf("passthru.ps1"), """
            param([string]$Entry, [string]$StateRoot)
            $r = & $Entry -StateRoot $StateRoot -SeamsPath 'seams.ps1' -PassThru
            [Console]::Out.WriteLine('after')
            [Console]::Out.WriteLine((@($r.PSObject.Properties | ForEach-Object { $_.Name }) -join ','))
            [Console]::Out.WriteLine([string]$r.ExitCode + '|' + [string]$r.Refusal)
            """);
        var passThru = await RunScript(fixture, fixture.PathOf("passthru.ps1"), ["-Entry", entry, "-StateRoot", state], new Dictionary<string, string>());
        passThru.Exit.ShouldBe(0, passThru.Stderr);
        var lines = passThru.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        lines.ShouldBe([expected, "after", string.Join(",", new NightlyRunResult().ToRecord().Keys.Cast<string>()), "3|seams-path-unsupported"]);
    }

    [Test]
    [Arguments("-Trigger", "blank-trigger")]
    [Arguments("-CheckoutRoot", "blank-checkout-root")]
    public async Task Nightly_run_launcher_refuses_an_explicitly_blank_value(string parameter, string refusal)
    {
        // 5.1 drops an empty native argument, so the CLI would read a blank -Trigger as scheduled (credit) and a blank
        // -CheckoutRoot as the production clone; the launcher refuses them as the PowerShell run core did.
        using var fixture = RunCheckout();
        var entry = fixture.PathOf("scripts", "nightly-run.ps1");
        var state = fixture.PathOf("state");
        var expected = NightlyRun.RecordLine(new NightlyRunResult { ExitCode = 2, Refusal = refusal });

        var refused = await RunScript(fixture, entry, ["-StateRoot", state, parameter, ""], new Dictionary<string, string>());
        refused.Exit.ShouldBe(2, refused.Stderr);
        refused.Stdout.Replace("\r\n", "\n").TrimEnd('\n').Split('\n')[^1].ShouldBe(expected);
        refused.Stderr.Trim().ShouldBe("nightly-run: " + refusal);
        Directory.Exists(state).ShouldBeFalse("a refusal runs nothing and writes nothing");
    }

    [Test]
    public async Task Nightly_run_launcher_that_cannot_start_the_cli_exits_9_with_a_record()
    {
        using var fixture = RunCheckout();
        var run = await RunScript(fixture, fixture.PathOf("scripts", "nightly-run.ps1"), ["-StateRoot", fixture.PathOf("state")],
            new Dictionary<string, string> { ["ANTIPHON_SCRIPTS_DLL"] = fixture.PathOf("missing", "Antiphon.Scripts.dll") });
        run.Exit.ShouldBe(9, run.Stderr);
        run.Stderr.Trim().ShouldStartWith("antiphon-cli: ANTIPHON_SCRIPTS_DLL names no file");
        run.Stdout.Trim().ShouldBe(NightlyRun.RecordLine(new NightlyRunResult { ExitCode = 9, Refusal = "nightly-run-cli-unavailable" }));
    }

    /// <summary>
    /// release-cut.ps1's call: in-process `&amp;` with its splat and -PassThru under pwsh, through the real nightly-run command (the
    /// staged build). The run refuses (an unknown profile, before anything is written); the caller reads .ExitCode and .Refusal,
    /// keeps running, and the completion record reached stdout.
    /// </summary>
    [Test]
    public async Task Nightly_run_launcher_passthru_serves_release_cut()
    {
        using var fixture = RunCheckout();
        var entry = fixture.PathOf("scripts", "nightly-run.ps1");
        var state = fixture.PathOf("state");
        File.WriteAllText(fixture.PathOf("cut.ps1"), """
            param([string]$Entry, [string]$StateRoot)
            $runArgs = @{ RunId = 's20-cut'; CheckoutRoot = (Join-Path $StateRoot 'checkout'); StateRoot = $StateRoot; Profile = 'turbo'; Trigger = 'rc'; SeamsPath = ''; PassThru = $true; WhatIf = $false }
            $run = & $Entry @runArgs
            [Console]::Out.WriteLine('tested=' + ([int]$run.ExitCode -eq 0) + '|' + [int]$run.ExitCode + '|' + [string]$run.Refusal + '|' + [string]$run.RunId)
            """);
        var start = new ProcessStartInfo("pwsh") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = fixture.Root };
        foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-File", fixture.PathOf("cut.ps1"), "-Entry", entry, "-StateRoot", state }) start.ArgumentList.Add(argument);
        start.Environment["ANTIPHON_SCRIPTS_DLL"] = StagedCli;
        var run = await Finish(Process.Start(start) ?? throw new InvalidOperationException("pwsh did not start"));
        run.Exit.ShouldBe(0, run.Stderr);
        var lines = run.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        lines[^1].ShouldBe("tested=False|3|unknown-profile|s20-cut");
        using var record = JsonDocument.Parse(lines[^2]);
        record.RootElement.GetProperty("refusal").GetString().ShouldBe("unknown-profile");
        record.RootElement.GetProperty("exitCode").GetInt32().ShouldBe(3);
        File.Exists(Path.Combine(state, "last-run.json")).ShouldBeFalse("refused before any state");
    }
}