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
        unknown.Stderr.ShouldStartWith("usage: antiphon-scripts live-lane-evidence");

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

    // ---- W2-nightly S20: scripts/nightly-run.ps1, the launcher Windmill's nightly job and qualification-launch run ----

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

        // -PassThru returns the run-core names instead of exiting.
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
    /// The launcher's -PassThru contract: in-process `&amp;` with a splat and -PassThru under pwsh, through the real nightly-run
    /// command (the staged build), as release-cut.ps1 called it until unit 15 of the 2026-11-22 plan (the release cut now runs
    /// NightlyRun in its own process). The run refuses (an unknown profile, before anything is written); the caller reads
    /// .ExitCode and .Refusal, keeps running, and the completion record reached stdout.
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

    // ---- unit 15: scripts/release-cut.ps1, the launcher Windmill's antiphon_release_candidates job runs (pwsh -File) ----

    /// <summary>A temporary checkout root holding scripts/release-cut.ps1 beside scripts/lib/antiphon-cli.ps1.</summary>
    private static NightlyFixture CutCheckout()
    {
        var fixture = Checkout();
        File.Copy(Path.Combine(DelegateScriptRunner.RepoRoot, "scripts", "release-cut.ps1"), fixture.PathOf("scripts", "release-cut.ps1"));
        return fixture;
    }

    /// <summary>release-cut.ps1's whole parameter block, each value distinct, and the switches (-SeamsPath and -PassThru are the launcher's own).</summary>
    private static readonly (string Name, string? Value)[] EveryCutParameter =
    [
        ("-ReleaseRoot", @"C:\fixture root\releases"), ("-CoordinationRoot", @"C:\fixture root\coord"), ("-RemoteUrl", "https://example.invalid/fixture.git"),
        ("-Repository", "fixture/repository"), ("-RunId", "u15-launcher"), ("-ScheduleSlot", "u/lndcobra/antiphon_release_candidates"), ("-JobId", "job-15"),
        ("-Ref", "rc/2026-11-30"), ("-SelectionMode", "Shadow"), ("-BaselineSha", new string('b', 40)), ("-BaselineReceiptPath", @"C:\fixture root\receipt.json"),
        ("-QualificationPath", @"C:\fixture root\claim.json"), ("-TimingRoot", @"C:\fixture root\timing"), ("-TrainManifestPath", @"C:\fixture root\train.json"),
        ("-ReleaseIntentPath", @"C:\fixture root\intent.json"), ("-SelectionAttemptPath", @"C:\fixture root\attempt.json"), ("-SelectionAttemptDigest", new string('c', 64)),
        ("-SelectionPath", @"C:\fixture root\selection.json"), ("-SelectionDigest", new string('d', 64)), ("-Attempt", "3"), ("-SkipPublish", null), ("-WhatIf", null),
    ];

    private static readonly Dictionary<string, string> CutEcho = new() { ["CLI_ECHO_COMMAND"] = "release-cut" };

    [Test]
    public async Task Release_cut_launcher_passes_every_parameter_as_its_flag_and_keeps_the_defaults()
    {
        using var fixture = CutCheckout();
        var entry = fixture.PathOf("scripts", "release-cut.ps1");

        var every = await NightlyRunHops.LauncherEcho(entry, EveryCutParameter.SelectMany(p => p.Value == null ? new[] { p.Name } : new[] { p.Name, p.Value }), fixture.Root,
            pwsh: true, environment: CutEcho);
        every.ExitCode.ShouldBe(0, every.Stderr);
        var received = ReleaseCutOptions.Parse(every.Arguments.ShouldNotBeNull(every.Stdout + every.Stderr), "", _ => null);
        // The options the same flags parse to directly: the launcher's translation, value for value (the result file is its own).
        var expected = ReleaseCutOptions.Parse(
            EveryCutParameter.SelectMany(p => p.Value == null ? new[] { NightlyRunHops.Kebab(p.Name) } : new[] { NightlyRunHops.Kebab(p.Name), p.Value }).ToArray(), "", _ => null);
        received.ResultFile.ShouldNotBeNullOrWhiteSpace("the launcher always asks for the record");
        received.ResultFile = "";
        JsonSerializer.Serialize(received).ShouldBe(JsonSerializer.Serialize(expected));
        (expected.SelectionMode, expected.Attempt, expected.SkipPublish, expected.WhatIf).ShouldBe(("Shadow", 3, true, true));

        // Windmill's command line (-Ref, -ScheduleSlot, -JobId): every other value is the parameter block's default, which is the command's.
        var windmill = await NightlyRunHops.LauncherEcho(entry, ["-Ref", "rc/2026-10-18", "-ScheduleSlot", "manual", "-JobId", "job-1"], fixture.Root, pwsh: true,
            environment: CutEcho);
        windmill.ExitCode.ShouldBe(0, windmill.Stderr);
        var bound = ReleaseCutOptions.Parse(windmill.Arguments.ShouldNotBeNull(windmill.Stdout + windmill.Stderr), "", _ => null);
        bound.ResultFile = "";
        var defaults = ReleaseCutOptions.Parse(["--ref", "rc/2026-10-18", "--schedule-slot", "manual", "--job-id", "job-1"], "", _ => null);
        JsonSerializer.Serialize(bound).ShouldBe(JsonSerializer.Serialize(defaults));
        (bound.ReleaseRoot, bound.CoordinationRoot, bound.RemoteUrl, bound.Repository, bound.SelectionMode, bound.Attempt, bound.RunId, bound.SkipPublish)
            .ShouldBe((@"C:\Antiphon\releases", @"C:\Antiphon\verification", "https://github.com/michal-ciechan/Antiphon", "michal-ciechan/Antiphon", "Full", 1, "", false));

        // The 5.1 header, the parameter block as Windmill knows it, and no library but the CLI launcher.
        var text = File.ReadAllText(Path.Combine(DelegateScriptRunner.RepoRoot, "scripts", "release-cut.ps1"));
        text.ShouldStartWith("#requires -Version 5.1");
        text.ShouldContain("    [ValidateSet('Full', 'Shadow', 'ChangeAware')][string]$SelectionMode = 'Full',\n");
        text.ShouldContain("    [string]$ReleaseRoot = 'C:\\Antiphon\\releases',\n");
        text.ShouldContain("    [string]$SeamsPath = '',\n");
        text.Split('\n').Where(line => System.Text.RegularExpressions.Regex.IsMatch(line, @"^\s*\.\s")).ToArray()
            .ShouldBe([". (Join-Path (Join-Path $PSScriptRoot 'lib') 'antiphon-cli.ps1')"]);
        text.ShouldNotContain("[Antiphon.Nightly.");
        text.ShouldNotContain("nightly-run.ps1");
        File.ReadAllBytes(Path.Combine(DelegateScriptRunner.RepoRoot, "scripts", "release-cut.ps1")).All(b => b < 128).ShouldBeTrue("ASCII only");
    }

    [Test]
    [Arguments("-SeamsPath", "seams.ps1", 3, "seams-path-unsupported")]
    [Arguments("-ReleaseRoot", "", 2, "blank-release-root")]
    [Arguments("-CoordinationRoot", "", 2, "blank-coordination-root")]
    [Arguments("-RemoteUrl", "", 2, "blank-remote-url")]
    [Arguments("-Repository", " ", 2, "blank-repository")]
    public async Task Release_cut_launcher_refuses_seams_and_blank_production_roots_with_a_record(string parameter, string value, int code, string refusal)
    {
        // Seams never cross a process; a blank value would leave with its flag and the CLI would read the production default.
        using var fixture = CutCheckout();
        var entry = fixture.PathOf("scripts", "release-cut.ps1");
        var expected = new ReleaseCutResult { ScheduleSlot = "manual", WindmillJobId = "job-1", Refusal = refusal, ExitCode = code }.ToJson();

        var refused = await NightlyRunHops.LauncherEcho(entry, ["-Ref", "rc/2026-10-18", "-ScheduleSlot", "manual", "-JobId", "job-1", parameter, value], fixture.Root,
            pwsh: true, environment: CutEcho);
        refused.ExitCode.ShouldBe(code, refused.Stderr);
        refused.Arguments.ShouldBeNull("the CLI never ran");
        refused.Stdout.Replace("\r\n", "\n").TrimEnd('\n').Split('\n')[^1].ShouldBe(expected);
        refused.Stderr.Trim().ShouldBe("release-cut: " + refusal);
    }

    [Test]
    public async Task Release_cut_launcher_runs_the_command_and_passthru_returns_its_record()
    {
        using var fixture = CutCheckout();
        var entry = fixture.PathOf("scripts", "release-cut.ps1");
        var releases = fixture.PathOf("releases");
        var expected = new ReleaseCutResult { ScheduleSlot = "manual", WindmillJobId = "job-1", Refusal = "ref-shape", ExitCode = 3 }.ToJson();

        // The staged CLI's release-cut: a ref that is not rc/YYYY-MM-DD refuses before anything is read or written.
        var start = new ProcessStartInfo("pwsh") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = fixture.Root };
        foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-File", entry, "-Ref", "master", "-ReleaseRoot", releases, "-ScheduleSlot", "manual", "-JobId", "job-1" })
            start.ArgumentList.Add(argument);
        start.Environment["ANTIPHON_SCRIPTS_DLL"] = StagedCli;
        var direct = await Finish(Process.Start(start) ?? throw new InvalidOperationException("pwsh did not start"));
        direct.Exit.ShouldBe(3, direct.Stderr);
        var lines = direct.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        lines.ShouldBe(["[release-cut] candidate cut refused (ref-shape).", expected]);
        Directory.Exists(releases).ShouldBeFalse("refused before anything is written");

        // -PassThru (a pwsh caller): the record read back from the CLI's result file, and the caller keeps running.
        File.WriteAllText(fixture.PathOf("cut.ps1"), """
            param([string]$Entry, [string]$Releases)
            $r = & $Entry -Ref master -ReleaseRoot $Releases -ScheduleSlot manual -JobId job-1 -PassThru
            [Console]::Out.WriteLine('after')
            [Console]::Out.WriteLine((@($r.PSObject.Properties | ForEach-Object { $_.Name }) -join ','))
            [Console]::Out.WriteLine([string]$r.exitCode + '|' + [string]$r.refusal + '|' + [string]$r.tested)
            """);
        var caller = new ProcessStartInfo("pwsh") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = fixture.Root };
        foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-File", fixture.PathOf("cut.ps1"), "-Entry", entry, "-Releases", releases }) caller.ArgumentList.Add(argument);
        caller.Environment["ANTIPHON_SCRIPTS_DLL"] = StagedCli;
        var passThru = await Finish(Process.Start(caller) ?? throw new InvalidOperationException("pwsh did not start"));
        passThru.Exit.ShouldBe(0, passThru.Stderr);
        passThru.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ShouldBe(["[release-cut] candidate cut refused (ref-shape).", expected, "after",
                "candidateId,ref,sha,scheduleSlot,windmillJobId,tested,published,tag,refusal,exitCode", "3|ref-shape|False"]);
    }

    [Test]
    public async Task Release_cut_launcher_that_cannot_start_the_cli_exits_9_with_a_record()
    {
        using var fixture = CutCheckout();
        var start = new ProcessStartInfo("pwsh") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = fixture.Root };
        foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-File", fixture.PathOf("scripts", "release-cut.ps1"), "-Ref", "rc/2026-10-18", "-JobId", "job-1" })
            start.ArgumentList.Add(argument);
        start.Environment["ANTIPHON_SCRIPTS_DLL"] = fixture.PathOf("missing", "Antiphon.Scripts.dll");
        var run = await Finish(Process.Start(start) ?? throw new InvalidOperationException("pwsh did not start"));
        run.Exit.ShouldBe(9, run.Stderr);
        run.Stderr.Trim().ShouldStartWith("antiphon-cli: ANTIPHON_SCRIPTS_DLL names no file");
        run.Stdout.Trim().ShouldBe(new ReleaseCutResult { WindmillJobId = "job-1", Refusal = "release-cut-cli-unavailable", ExitCode = 9 }.ToJson());
    }

    // ---- unit 21: scripts/cleanup-build-junk.ps1 and scripts/cleanup-codex-test-residue.ps1, the launchers Windmill's
    // antiphon_build_junk_cleanup and antiphon_codex_residue_cleanup start by path (Windows PowerShell 5.1 -File) ----

    /// <summary>A temporary checkout root holding scripts/&lt;entry&gt; beside scripts/lib/antiphon-cli.ps1.</summary>
    private static NightlyFixture CleanupCheckout(string entry)
    {
        var fixture = Checkout();
        File.Copy(Path.Combine(DelegateScriptRunner.RepoRoot, "scripts", entry), fixture.PathOf("scripts", entry));
        return fixture;
    }

    private static void AssertThinLauncher(string entry, params string[] parameterLines)
    {
        var text = File.ReadAllText(Path.Combine(DelegateScriptRunner.RepoRoot, "scripts", entry)).Replace("\r\n", "\n");
        text.ShouldStartWith("#requires -Version 5.1");
        foreach (var line in parameterLines) text.ShouldContain(line + "\n");
        text.Split('\n').Where(line => System.Text.RegularExpressions.Regex.IsMatch(line, @"^\s*\.\s")).ToArray()
            .ShouldBe([". (Join-Path (Join-Path $PSScriptRoot 'lib') 'antiphon-cli.ps1')"]);
        text.ShouldNotContain("[Antiphon.");
        File.ReadAllBytes(Path.Combine(DelegateScriptRunner.RepoRoot, "scripts", entry)).All(b => b < 128).ShouldBeTrue("ASCII only");
    }

    [Test]
    public async Task Build_junk_launcher_passes_its_parameters_keeps_the_defaults_and_refuses_a_cleanup_context()
    {
        using var fixture = CleanupCheckout("cleanup-build-junk.ps1");
        var entry = fixture.PathOf("scripts", "cleanup-build-junk.ps1");
        var echo = new Dictionary<string, string> { ["CLI_ECHO_COMMAND"] = "cleanup-build-junk" };

        // Windmill's command line (no parameters): the checkout above scripts/ and 60 minutes.
        var windmill = await NightlyRunHops.LauncherEcho(entry, [], fixture.Root, environment: echo);
        windmill.ExitCode.ShouldBe(0, windmill.Stderr);
        var defaults = BuildJunkCleanupOptions.Parse(windmill.Arguments.ShouldNotBeNull(windmill.Stdout + windmill.Stderr), "unused");
        defaults.RepoRoot.TrimEnd('\\', '/').ShouldBe(fixture.Root.TrimEnd('\\', '/'), StringCompareShould.IgnoreCase);
        defaults.SkipIfModifiedWithinMinutes.ShouldBe(60);

        var every = await NightlyRunHops.LauncherEcho(entry, ["-RepoRoot", @"C:\fixture root\repo", "-SkipIfModifiedWithinMinutes", "5"], fixture.Root, environment: echo);
        every.ExitCode.ShouldBe(0, every.Stderr);
        every.Arguments.ShouldBe(["--repo-root", @"C:\fixture root\repo", "--skip-if-modified-within-minutes", "5"]);

        // A cleanup context is in-memory custody: it never crosses a process.
        var refused = await NightlyRunHops.LauncherEcho(entry, ["-CleanupContext", "context"], fixture.Root, environment: echo);
        refused.ExitCode.ShouldBe(3, refused.Stderr);
        refused.Arguments.ShouldBeNull("the CLI never ran");
        refused.Stderr.Trim().ShouldBe("cleanup-build-junk: cleanup-context-unsupported");

        AssertThinLauncher("cleanup-build-junk.ps1", "    [string]$RepoRoot = (Split-Path $PSScriptRoot -Parent),", "    [int]$SkipIfModifiedWithinMinutes = 60,",
            "    [object]$CleanupContext = $null");
    }

    [Test]
    public async Task Build_junk_launcher_runs_the_command_and_leaves_every_output_without_creation_records()
    {
        using var fixture = CleanupCheckout("cleanup-build-junk.ps1");
        var old = Directory.CreateDirectory(fixture.PathOf("repo", "bin-old", "nested"));
        File.WriteAllText(Path.Combine(old.FullName, "keep.txt"), "kept");
        Directory.SetLastWriteTime(fixture.PathOf("repo", "bin-old"), DateTime.Now.AddDays(-10));
        var start = new ProcessStartInfo(OperatingSystem.IsWindows() ? "powershell.exe" : "pwsh")
        {
            UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = fixture.Root,
        };
        foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", fixture.PathOf("scripts", "cleanup-build-junk.ps1"),
                     "-RepoRoot", fixture.PathOf("repo"), "-SkipIfModifiedWithinMinutes", "0" })
            start.ArgumentList.Add(argument);
        start.Environment["ANTIPHON_SCRIPTS_DLL"] = StagedCli;
        var run = await Finish(Process.Start(start) ?? throw new InvalidOperationException("the launcher shell did not start"));
        run.Exit.ShouldBe(0, run.Stderr);
        run.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ShouldBe(["skip: producer creation records unavailable; outputs left in place", "cleanup-build-junk: 0 dir(s) removed."]);
        File.ReadAllText(Path.Combine(old.FullName, "keep.txt")).ShouldBe("kept");
    }

    [Test]
    public async Task Codex_residue_launcher_passes_every_parameter_as_its_flag_and_keeps_the_defaults()
    {
        using var fixture = CleanupCheckout("cleanup-codex-test-residue.ps1");
        var entry = fixture.PathOf("scripts", "cleanup-codex-test-residue.ps1");
        var echo = new Dictionary<string, string> { ["CLI_ECHO_COMMAND"] = "cleanup-codex-test-residue" };

        // Windmill's report-only command line: the parameter block's defaults; blank values leave with their flags.
        var windmill = await NightlyRunHops.LauncherEcho(entry, [], fixture.Root, environment: echo);
        windmill.ExitCode.ShouldBe(0, windmill.Stderr);
        windmill.Arguments.ShouldBe(["--older-than-hours", "24", "--max-per-run", "10", "--codex-exe", "codex"]);

        string[] every = ["-OlderThanHours", "48", "-Execute", "-MaxPerRun", "3", "-ReportPath", @"C:\fixture root\reports", "-InputJson", @"C:\fixture root\threads.json",
            "-CodexHome", @"C:\fixture root\codex", "-StateDbPath", @"C:\fixture root\state_5.sqlite", "-CodexExe", "codex-fixture", "-LiveCwdsJson", @"C:\fixture root\live.json",
            "-LockIdsJson", @"C:\fixture root\locks.json", "-Now", "2026-08-26T14:00:00Z"];
        var all = await NightlyRunHops.LauncherEcho(entry, every, fixture.Root, environment: echo);
        all.ExitCode.ShouldBe(0, all.Stderr);
        var received = CodexTestResidueOptions.Parse(all.Arguments.ShouldNotBeNull(all.Stdout + all.Stderr), null, "unused", "unused", "unused");
        var expected = CodexTestResidueOptions.Parse(["--older-than-hours", "48", "--execute", "--max-per-run", "3", "--report-path", @"C:\fixture root\reports",
            "--input-json", @"C:\fixture root\threads.json", "--codex-home", @"C:\fixture root\codex", "--state-db-path", @"C:\fixture root\state_5.sqlite",
            "--codex-exe", "codex-fixture", "--live-cwds-json", @"C:\fixture root\live.json", "--lock-ids-json", @"C:\fixture root\locks.json",
            "--now", "2026-08-26T14:00:00Z"], null, "unused", "unused", "unused");
        JsonSerializer.Serialize(received).ShouldBe(JsonSerializer.Serialize(expected));
        (expected.Execute, expected.OlderThanHours, expected.MaxPerRun).ShouldBe((true, 48, 3));

        AssertThinLauncher("cleanup-codex-test-residue.ps1", "    [int]$OlderThanHours = 24,", "    [switch]$Execute,", "    [int]$MaxPerRun = 10,",
            "    [string]$CodexExe = 'codex',", "    [string]$Now = ''");
    }

    // ---- unit 22: scripts/cleanup-claude-sessions.ps1, the launcher Windmill's u/lndcobra/claude_session_cleanup starts by path
    // (Windows PowerShell 5.1 -File) ----

    [Test]
    public async Task Claude_sessions_launcher_passes_every_parameter_as_its_flag_and_keeps_the_defaults()
    {
        using var fixture = CleanupCheckout("cleanup-claude-sessions.ps1");
        var entry = fixture.PathOf("scripts", "cleanup-claude-sessions.ps1");
        var echo = new Dictionary<string, string> { ["CLI_ECHO_COMMAND"] = "cleanup-claude-sessions" };

        // Windmill's report-only command line: the parameter block's defaults; blank values leave with their flags.
        var windmill = await NightlyRunHops.LauncherEcho(entry, [], fixture.Root, environment: echo);
        windmill.ExitCode.ShouldBe(0, windmill.Stderr);
        windmill.Arguments.ShouldBe(["--older-than-days", "7", "--action", "Archive", "--max-per-run", "10", "--require-disconnected", "true"]);

        // The promoted command line (-Execute -Action Archive), then every parameter; ValidateSet binds -Action ignoring case.
        var promoted = await NightlyRunHops.LauncherEcho(entry, ["-Execute", "-Action", "Archive"], fixture.Root, environment: echo);
        promoted.ExitCode.ShouldBe(0, promoted.Stderr);
        promoted.Arguments.ShouldBe(["--older-than-days", "7", "--action", "Archive", "--max-per-run", "10", "--require-disconnected", "true", "--execute"]);
        string[] every = ["-OlderThanDays", "14", "-Action", "delete", "-Execute", "-MaxPerRun", "3", "-SkipUnread", "-ReportPath", @"C:\fixture root\reports",
            "-InputJson", @"C:\fixture root\list.json", "-LiveBridgesJson", @"C:\fixture root\bridges.json", "-SessionsDir", @"C:\fixture root\sessions",
            "-CredentialsPath", @"C:\fixture root\credentials.json", "-Now", "2026-08-22T14:14:00Z"];
        var all = await NightlyRunHops.LauncherEcho(entry, every, fixture.Root, environment: echo);
        all.ExitCode.ShouldBe(0, all.Stderr);
        var received = ClaudeSessionCleanupOptions.Parse(all.Arguments.ShouldNotBeNull(all.Stdout + all.Stderr), "unused", "unused");
        var expected = ClaudeSessionCleanupOptions.Parse(["--older-than-days", "14", "--action", "Delete", "--execute", "--max-per-run", "3", "--skip-unread",
            "--report-path", @"C:\fixture root\reports", "--input-json", @"C:\fixture root\list.json", "--live-bridges-json", @"C:\fixture root\bridges.json",
            "--sessions-dir", @"C:\fixture root\sessions", "--credentials-path", @"C:\fixture root\credentials.json", "--now", "2026-08-22T14:14:00Z"], "unused", "unused");
        JsonSerializer.Serialize(received).ShouldBe(JsonSerializer.Serialize(expected));
        (expected.Execute, expected.Action, expected.SkipUnread, expected.OlderThanDays, expected.MaxPerRun).ShouldBe((true, "Delete", true, 14, 3));

        // -RequireDisconnected:$false: a [bool] binds only from a script call (-Command), never from -File.
        var start = new ProcessStartInfo(OperatingSystem.IsWindows() ? "powershell.exe" : "pwsh")
        {
            UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = fixture.Root,
        };
        foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-Command",
                     "& '" + entry.Replace("'", "''") + "' -RequireDisconnected:$false; exit $LASTEXITCODE" })
            start.ArgumentList.Add(argument);
        start.Environment["ANTIPHON_SCRIPTS_DLL"] = StagedEcho;
        start.Environment["CLI_ECHO_COMMAND"] = "cleanup-claude-sessions";
        var connected = await Finish(Process.Start(start) ?? throw new InvalidOperationException("the launcher shell did not start"));
        connected.Exit.ShouldBe(0, connected.Stderr);
        connected.Echo().GetProperty("arguments").EnumerateArray().Select(argument => argument.GetString()).ToArray()
            .ShouldBe(["--older-than-days", "7", "--action", "Archive", "--max-per-run", "10", "--require-disconnected", "false"]);

        AssertThinLauncher("cleanup-claude-sessions.ps1", "    [int]$OlderThanDays = 7,", "    [string]$Action = 'Archive',", "    [switch]$Execute,",
            "    [int]$MaxPerRun = 10,", "    [switch]$SkipUnread,", "    [bool]$RequireDisconnected = $true,", "    [string]$Now = ''");
        var launcher = File.ReadAllText(Path.Combine(DelegateScriptRunner.RepoRoot, "scripts", "cleanup-claude-sessions.ps1"));
        launcher.ShouldNotContain("[scriptblock]$HttpShim", Case.Sensitive, "the fixture seam is gone");
        launcher.ShouldNotContain("[switch]$PassThru", Case.Sensitive, "the fixture seam is gone");
    }

    // ---- unit 25: scripts/github-sync.ps1, the launcher Windmill's u/lndcobra/antiphon_github_sync starts by path with -Notify ----

    [Test]
    public async Task Github_sync_launcher_passes_every_parameter_as_its_flag_and_keeps_the_defaults()
    {
        using var fixture = CleanupCheckout("github-sync.ps1");
        var entry = fixture.PathOf("scripts", "github-sync.ps1");
        var echo = new Dictionary<string, string> { ["CLI_ECHO_COMMAND"] = "github-sync" };

        // Windmill's command line: -Notify, with the default base URL.
        var windmill = await NightlyRunHops.LauncherEcho(entry, ["-Notify"], fixture.Root, environment: echo);
        windmill.ExitCode.ShouldBe(0, windmill.Stderr);
        windmill.Arguments.ShouldBe(["--base-url", "http://localhost:17202", "--notify"]);

        var all = await NightlyRunHops.LauncherEcho(entry, ["-BoardId", "8988CA03-7414-47AD-B0B6-51556C701703", "-BaseUrl", "http://127.0.0.1:5000"], fixture.Root,
            environment: echo);
        all.ExitCode.ShouldBe(0, all.Stderr);
        all.Arguments.ShouldBe(["--board-id", "8988ca03-7414-47ad-b0b6-51556c701703", "--base-url", "http://127.0.0.1:5000"]);
        var received = GithubSyncOptions.Parse(all.Arguments.ShouldNotBeNull(all.Stdout + all.Stderr));
        (received.BoardId, received.BaseUrl, received.Notify).ShouldBe(((Guid?)Guid.Parse("8988ca03-7414-47ad-b0b6-51556c701703"), "http://127.0.0.1:5000", false));

        AssertThinLauncher("github-sync.ps1", "    [Guid]$BoardId,", "    [string]$BaseUrl = 'http://localhost:17202',", "    [switch]$Notify");
    }

    [Test]
    public async Task Github_sync_launcher_that_cannot_start_the_cli_exits_9()
    {
        using var fixture = CleanupCheckout("github-sync.ps1");
        var start = new ProcessStartInfo(OperatingSystem.IsWindows() ? "powershell.exe" : "pwsh")
        {
            UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = fixture.Root,
        };
        foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", fixture.PathOf("scripts", "github-sync.ps1"), "-Notify" })
            start.ArgumentList.Add(argument);
        start.Environment["ANTIPHON_SCRIPTS_DLL"] = fixture.PathOf("missing", "Antiphon.Scripts.dll");
        var run = await Finish(Process.Start(start) ?? throw new InvalidOperationException("the launcher shell did not start"));
        run.Exit.ShouldBe(9, run.Stderr);
        run.Stderr.Trim().ShouldStartWith("antiphon-cli: ANTIPHON_SCRIPTS_DLL names no file");
    }

    [Test]
    public async Task Claude_sessions_launcher_runs_the_command_on_a_saved_list_without_credentials_or_network()
    {
        using var fixture = CleanupCheckout("cleanup-claude-sessions.ps1");
        var reports = fixture.PathOf("reports");
        var start = new ProcessStartInfo(OperatingSystem.IsWindows() ? "powershell.exe" : "pwsh")
        {
            UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = fixture.Root,
        };
        // A saved list and named live bridges: no credentials file, session file, process or network is read.
        foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", fixture.PathOf("scripts", "cleanup-claude-sessions.ps1"),
                     "-InputJson", Path.Combine(DelegateScriptRunner.RepoRoot, "scripts", "fixtures", "claude-sessions-2026-08-22.json"),
                     "-LiveBridgesJson", Path.Combine(DelegateScriptRunner.RepoRoot, "scripts", "fixtures", "claude-sessions-live-bridges.json"),
                     "-Now", "2026-08-22T14:14:00Z", "-ReportPath", reports, "-Execute" })
            start.ArgumentList.Add(argument);
        start.Environment["ANTIPHON_SCRIPTS_DLL"] = StagedCli;
        start.Environment["USERPROFILE"] = fixture.PathOf("profile");
        var run = await Finish(Process.Start(start) ?? throw new InvalidOperationException("the launcher shell did not start"));
        run.Exit.ShouldBe(0, run.Stdout + run.Stderr);
        run.Stdout.ShouldContain("--input-json is set; --execute is ignored (dry run only).");
        run.Stdout.ShouldContain("=== CANDIDATES (9) ===");
        run.Stdout.ShouldContain("candidates=9 skipped=9 liveBridges=7 execute=False action=Archive requireDisconnected=True");
        var report = Directory.GetFiles(reports, "*.json").ShouldHaveSingleItem();
        using var document = JsonDocument.Parse(File.ReadAllText(report));
        (document.RootElement.GetProperty("candidateCount").GetInt32(), document.RootElement.GetProperty("exitCode").GetInt32()).ShouldBe((9, 0));
    }

    // ---- unit 30: scripts/run-daemon.ps1, the launcher the AppHost (DaemonProcessService: a detached pwsh -File) and the
    // logon task's autostart-session-runner.ps1 start by path (scripts/check-daemon-build.ps1, which dev-aspire.ps1 ran by
    // path, went when unit 37 ported dev-aspire.ps1; unit 43 deleted the repository root's restart-session-runner.ps1
    // forwarder) ----

    [Test]
    public async Task Run_daemon_launcher_passes_the_apphost_command_line_and_names_itself_the_supervisor()
    {
        using var fixture = CleanupCheckout("run-daemon.ps1");
        var entry = fixture.PathOf("scripts", "run-daemon.ps1");
        // DaemonProcessService.BuildArgs: ExeArgs is one value with a leading space, so -File never reads it as a parameter name.
        var start = new ProcessStartInfo(OperatingSystem.IsWindows() ? "powershell.exe" : "pwsh")
        {
            UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = fixture.Root,
        };
        foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", entry,
                     "-Name", "session-runner", "-WorkDir", @"C:\fixture root\runner", "-Exe", @"C:\fixture root\runner\bin\Antiphon.SessionRunner.exe",
                     "-ExeArgs", " --urls http://localhost:17204", "-LogFile", @"C:\fixture root\logs\session-runner.log",
                     "-ServicePidFile", @"C:\fixture root\logs\session-runner.service.pid", "-StateFile", @"C:\fixture root\logs\session-runner.state",
                     "-BuildProjectDir", @"C:\fixture root\runner" })
            start.ArgumentList.Add(argument);
        start.Environment["ANTIPHON_SCRIPTS_DLL"] = StagedEcho;
        start.Environment["CLI_ECHO_COMMAND"] = "run-daemon";
        var process = Process.Start(start) ?? throw new InvalidOperationException("the launcher shell did not start");
        var launcher = process.Id;
        var run = await Finish(process);
        run.Exit.ShouldBe(0, run.Stderr);
        var echo = run.Echo();
        Arguments(echo).ShouldBe(["--name", "session-runner", "--work-dir", @"C:\fixture root\runner", "--exe", @"C:\fixture root\runner\bin\Antiphon.SessionRunner.exe",
            "--exe-args", " --urls http://localhost:17204", "--log-file", @"C:\fixture root\logs\session-runner.log",
            "--service-pid-file", @"C:\fixture root\logs\session-runner.service.pid", "--state-file", @"C:\fixture root\logs\session-runner.state",
            "--build-project-dir", @"C:\fixture root\runner", "--log-max-mb", "20", "--log-retain-days", "5", "--log-retain-count", "10",
            "--supervisor-pid", launcher.ToString()]);
        echo.GetProperty("pid").GetInt32().ShouldNotBe(launcher, "the command runs in a child of the launcher, which stays the recorded supervisor");
        var options = DaemonSupervisorOptions.Parse(Arguments(echo));
        options.ExeArgList.ShouldBe(["--urls", "http://localhost:17204"]);
        options.SupervisorPid.ShouldBe(launcher);

        // A blank -BuildProjectDir (no build) leaves with its flag; the numbers pass as given.
        var plain = await NightlyRunHops.LauncherEcho(entry, ["-Name", "fake-gateway", "-WorkDir", "w", "-Exe", "e", "-LogFile", "l", "-ServicePidFile", "p",
            "-StateFile", "s", "-LogMaxMb", "0", "-LogRetainDays", "2", "-LogRetainCount", "3"], fixture.Root,
            environment: new Dictionary<string, string> { ["CLI_ECHO_COMMAND"] = "run-daemon" });
        plain.ExitCode.ShouldBe(0, plain.Stderr);
        var received = DaemonSupervisorOptions.Parse(plain.Arguments.ShouldNotBeNull(plain.Stdout + plain.Stderr));
        (received.Name, received.BuildProjectDir, received.ExeArgs, received.LogMaxMb, received.LogRetainDays, received.LogRetainCount).ShouldBe(("fake-gateway", "", "", 0, 2, 3));

        AssertThinLauncher("run-daemon.ps1", "    [string]$Name,", "    [string]$ExeArgs,", "    [string]$BuildProjectDir = '',", "    [int]$LogMaxMb = 20,",
            "    [int]$LogRetainDays = 5,", "    [int]$LogRetainCount = 10");
    }

    // ---- unit 36: scripts/autostart-session-runner.ps1, the launcher the logon task "Antiphon Session Runner" (and the phase 1
    // clone, by hand) starts by path ----

    [Test]
    public async Task Autostart_session_runner_launcher_names_itself_the_supervisor_and_passes_an_explicit_config_only()
    {
        using var fixture = CleanupCheckout("autostart-session-runner.ps1");
        var entry = fixture.PathOf("scripts", "autostart-session-runner.ps1");
        const string phase1 = @"C:\Antiphon\runner\phone-home.phase1.json";
        // The task's action: -NonInteractive -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File <launcher>, no parameters.
        async Task<(LauncherRun Run, int Launcher)> Start(params string[] parameters)
        {
            var start = new ProcessStartInfo(OperatingSystem.IsWindows() ? "powershell.exe" : "pwsh")
            {
                UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = fixture.Root,
            };
            foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", entry }.Concat(parameters))
                start.ArgumentList.Add(argument);
            start.Environment["ANTIPHON_SCRIPTS_DLL"] = StagedEcho;
            start.Environment["CLI_ECHO_COMMAND"] = "autostart-session-runner";
            var process = Process.Start(start) ?? throw new InvalidOperationException("the launcher shell did not start");
            var launcher = process.Id;
            return (await Finish(process), launcher);
        }
        var (logon, logonPid) = await Start();
        logon.Exit.ShouldBe(0, logon.Stderr);
        Arguments(logon.Echo()).ShouldBe(["--supervisor-pid", logonPid.ToString()], "the default path is not passed: it is not explicit");
        logon.Echo().GetProperty("pid").GetInt32().ShouldNotBe(logonPid, "the command runs in a child of the launcher, which stays the recorded supervisor");
        var options = SessionRunnerAutostartOptions.Parse(Arguments(logon.Echo()), "root");
        (options.SupervisorPid, options.ExplicitConfigPath, options.ConfigPath).ShouldBe((logonPid, false, WindowsPhoneHome.DefaultConfigPath));

        var (explicitRun, explicitPid) = await Start("-PhoneHomeConfigPath", phase1);
        explicitRun.Exit.ShouldBe(0, explicitRun.Stderr);
        Arguments(explicitRun.Echo()).ShouldBe(["--supervisor-pid", explicitPid.ToString(), "--phone-home-config-path", phase1]);
        var explicitOptions = SessionRunnerAutostartOptions.Parse(Arguments(explicitRun.Echo()), "root");
        (explicitOptions.ExplicitConfigPath, explicitOptions.ConfigPath).ShouldBe((true, phase1));

        var missing = await NightlyRunHops.LauncherEcho(entry, [], fixture.Root,
            environment: new Dictionary<string, string> { ["ANTIPHON_SCRIPTS_DLL"] = fixture.PathOf("missing", "Antiphon.Scripts.dll") });
        missing.ExitCode.ShouldBe(9, missing.Stderr);
        AssertThinLauncher("autostart-session-runner.ps1", @"    [string]$PhoneHomeConfigPath = 'C:\Antiphon\runner\phone-home.json'");
    }

    [Test]
    public async Task Run_daemon_launcher_that_cannot_start_the_cli_exits_9()
    {
        using var fixture = CleanupCheckout("run-daemon.ps1");
        var run = await NightlyRunHops.LauncherEcho(fixture.PathOf("scripts", "run-daemon.ps1"), ["-Name", "n"], fixture.Root,
            environment: new Dictionary<string, string> { ["ANTIPHON_SCRIPTS_DLL"] = fixture.PathOf("missing", "Antiphon.Scripts.dll") });
        run.ExitCode.ShouldBe(9, run.Stderr);
        run.Stderr.Trim().ShouldStartWith("antiphon-cli: ANTIPHON_SCRIPTS_DLL names no file");
    }

    // ---- unit 32: scripts/test-client.ps1, which scripts/c590-remote.sh (the CARD-0590 nested-lane client case) runs by path ----

    [Test]
    public async Task Test_client_launcher_maps_its_switches_and_forwards_every_vitest_argument()
    {
        using var fixture = CleanupCheckout("test-client.ps1");
        var entry = fixture.PathOf("scripts", "test-client.ps1");
        var echo = new Dictionary<string, string> { ["CLI_ECHO_COMMAND"] = "test-client" };

        // c590-remote.sh's command line (pwsh -NoProfile -File scripts/test-client.ps1 -JsonResultPath '<case>/client.json').
        var c590 = await NightlyRunHops.LauncherEcho(entry, ["-JsonResultPath", "/work/test-evidence/run/client-tests/client.json"], fixture.Root, pwsh: true, environment: echo);
        c590.ExitCode.ShouldBe(0, c590.Stderr);
        c590.Arguments.ShouldBe(["--json-result-path", "/work/test-evidence/run/client-tests/client.json"]);

        var all = await NightlyRunHops.LauncherEcho(entry, ["-TypecheckProject", "client/tsconfig.node.json", "-LintPath", "client/src/a.test.ts", "attentionVisuals.test",
            "-t", "keeps a name with spaces"], fixture.Root, environment: echo);
        all.ExitCode.ShouldBe(0, all.Stderr);
        all.Arguments.ShouldBe(["--typecheck-project", "client/tsconfig.node.json", "--lint-path", "client/src/a.test.ts", "attentionVisuals.test", "-t",
            "keeps a name with spaces"]);
        var options = ClientTestsOptions.Parse(all.Arguments!, "root");
        (options.TypecheckProject, options.LintPath, options.JsonResultPath).ShouldBe(("client/tsconfig.node.json", "client/src/a.test.ts", ""));
        options.Forward.ToArray().ShouldBe(["attentionVisuals.test", "-t", "keeps a name with spaces"]);

        AssertThinLauncher("test-client.ps1");
        File.Exists(Path.Combine(DelegateScriptRunner.RepoRoot, "scripts", "lint-client.ps1")).ShouldBeFalse();
        File.Exists(Path.Combine(DelegateScriptRunner.RepoRoot, "scripts", "typecheck-client.ps1")).ShouldBeFalse();
        File.Exists(Path.Combine(DelegateScriptRunner.RepoRoot, "scripts", "client-mode.ps1")).ShouldBeFalse();
        File.Exists(Path.Combine(DelegateScriptRunner.RepoRoot, "scripts", "lib", "client-npm-install.ps1")).ShouldBeFalse();
    }

    [Test]
    public async Task Test_client_launcher_that_cannot_start_the_cli_exits_9()
    {
        using var fixture = CleanupCheckout("test-client.ps1");
        var run = await NightlyRunHops.LauncherEcho(fixture.PathOf("scripts", "test-client.ps1"), ["BoardPage"], fixture.Root,
            environment: new Dictionary<string, string> { ["ANTIPHON_SCRIPTS_DLL"] = fixture.PathOf("missing", "Antiphon.Scripts.dll") });
        run.ExitCode.ShouldBe(9, run.Stderr);
        run.Stderr.Trim().ShouldStartWith("antiphon-cli: ANTIPHON_SCRIPTS_DLL names no file");
    }

    // Unit 43: nothing starts the runner restart's root forwarder by path, so it is gone; operators run the command. Without
    // --timeout-sec the command waits its own 180 s (the forwarder passed -TimeoutSec only when it was bound).
    [Test]
    public void Runner_restart_has_no_powershell_entry_left()
    {
        foreach (var gone in new[] { "restart-session-runner.ps1", Path.Combine("scripts", "restart-session-runner.ps1"), Path.Combine("scripts", "session-runner-restart-health.ps1") })
            File.Exists(Path.Combine(DelegateScriptRunner.RepoRoot, gone)).ShouldBeFalse(gone);
        RunnerRestartOptions.Parse([], "root").TimeoutSec.ShouldBe(180);
        var options = RunnerRestartOptions.Parse(["--hard", "--kill-sessions", "--timeout-sec", "60"], "root");
        (options.Hard, options.KillSessions, options.TimeoutSec).ShouldBe((true, true, 60));
    }

    // ---- unit 31: the AppHost chain's launchers. scripts/restart-apphost.ps1 and scripts/set-apphost-maintenance.ps1
    // (scripts/cutover-postgres-server2.ps1 and scripts/deploy-local.ps1 start them by path), scripts/watchdog-apphost.ps1,
    // scripts/apphost-watchdog-state-observer.ps1 and scripts/autostart-apphost.ps1 (the Scheduled Tasks the install-autostart command
    // registers start them by path) and the repository root's verify-dev-stack.ps1 (deploy-local.ps1 runs it by path; the
    // bootstrap-check command runs the check in process since unit 38) ----

    private static Dictionary<string, string> Echo(string command, string? exit = null)
    {
        var environment = new Dictionary<string, string> { ["CLI_ECHO_COMMAND"] = command };
        if (exit != null) environment["CLI_ECHO_EXIT"] = exit;
        return environment;
    }

    [Test]
    public async Task Restart_apphost_launcher_passes_only_the_parameters_given()
    {
        using var fixture = CleanupCheckout("restart-apphost.ps1");
        var entry = fixture.PathOf("scripts", "restart-apphost.ps1");
        var plain = await NightlyRunHops.LauncherEcho(entry, [], fixture.Root, environment: Echo("restart-apphost"));
        plain.ExitCode.ShouldBe(0, plain.Stderr);
        plain.Arguments.ShouldNotBeNull(plain.Stdout).ShouldBeEmpty("the command's defaults are the script's");
        var sha = new string('a', 40);
        // The cutover's two calls: -StopOnly, and -ExpectedServerSha <sha>.
        (await NightlyRunHops.LauncherEcho(entry, ["-StopOnly"], fixture.Root, environment: Echo("restart-apphost"))).Arguments.ShouldBe(["--stop-only"]);
        (await NightlyRunHops.LauncherEcho(entry, ["-ExpectedServerSha", sha], fixture.Root, environment: Echo("restart-apphost"))).Arguments.ShouldBe(["--expected-server-sha", sha]);
        var all = await NightlyRunHops.LauncherEcho(entry, ["-NoBuild", "-TimeoutSec", "9", "-MaxWaitSec", "700", "-LockMaxAgeMinutes", "5", "-AllowWorktree",
            "-ExpectedServerSha", sha, "-GracefulStopTimeoutSec", "3", "-SkipGracefulStop", "-StopOnly", "-WhatIf"], fixture.Root, environment: Echo("restart-apphost", "5"));
        all.ExitCode.ShouldBe(5, "the command's exit is the launcher's: " + all.Stderr);
        all.Arguments.ShouldBe(["--no-build", "--timeout-sec", "9", "--max-wait-sec", "700", "--lock-max-age-minutes", "5", "--allow-worktree", "--expected-server-sha", sha,
            "--graceful-stop-timeout-sec", "3", "--skip-graceful-stop", "--stop-only", "--what-if"]);
        var options = AppHostRestartOptions.Parse(all.Arguments!, "root");
        (options.NoBuild, options.TimeoutSec, options.MaxWaitSec, options.LockMaxAgeMinutes, options.AllowWorktree, options.ExpectedServerSha, options.GracefulStopTimeoutSec,
            options.SkipGracefulStop, options.StopOnly, options.WhatIf).ShouldBe((true, 9, 700, 5, true, sha, 3, true, true, true));
        AssertThinLauncher("restart-apphost.ps1", "    [int]$TimeoutSec = 150,", "    [int]$MaxWaitSec = 600,", "    [string]$ExpectedServerSha,", "    [switch]$StopOnly,",
            "    [switch]$WhatIf");

        var missing = await NightlyRunHops.LauncherEcho(entry, ["-StopOnly"], fixture.Root,
            environment: new Dictionary<string, string> { ["ANTIPHON_SCRIPTS_DLL"] = fixture.PathOf("missing", "Antiphon.Scripts.dll") });
        missing.ExitCode.ShouldBe(9, missing.Stderr);
    }

    [Test]
    public async Task Maintenance_launcher_passes_its_switches_and_returns_exit_2()
    {
        using var fixture = CleanupCheckout("set-apphost-maintenance.ps1");
        var entry = fixture.PathOf("scripts", "set-apphost-maintenance.ps1");
        var enter = await NightlyRunHops.LauncherEcho(entry, ["-EnterIfAbsent"], fixture.Root, environment: Echo("set-apphost-maintenance", "2"));
        enter.ExitCode.ShouldBe(2, "the cutover reads exit 2 as a marker that already existed: " + enter.Stderr);
        enter.Arguments.ShouldBe(["--enter-if-absent"]);
        var clear = await NightlyRunHops.LauncherEcho(entry, ["-Clear", "-Root", @"C:\fixture root", "-WatchdogTaskName", "Fixture Watchdog"], fixture.Root,
            environment: Echo("set-apphost-maintenance"));
        clear.Arguments.ShouldBe(["--clear", "--root", @"C:\fixture root", "--watchdog-task-name", "Fixture Watchdog"]);
        var options = AppHostMaintenanceOptions.Parse(clear.Arguments!, "checkout");
        (options.Clear, options.EnterIfAbsent, options.Root, options.WatchdogTaskName).ShouldBe((true, false, @"C:\fixture root", "Fixture Watchdog"));
        AssertThinLauncher("set-apphost-maintenance.ps1", "    [switch]$Clear,", "    [switch]$EnterIfAbsent,", "    [string]$Root,",
            "    [string]$WatchdogTaskName = 'Antiphon AppHost Watchdog'");
    }

    [Test]
    public async Task Watchdog_observer_and_autostart_launchers_pass_their_parameters()
    {
        using var fixture = CleanupCheckout("watchdog-apphost.ps1");
        foreach (var other in new[] { "apphost-watchdog-state-observer.ps1", "autostart-apphost.ps1" })
            File.Copy(Path.Combine(DelegateScriptRunner.RepoRoot, "scripts", other), fixture.PathOf("scripts", other));
        var watchdog = fixture.PathOf("scripts", "watchdog-apphost.ps1");
        (await NightlyRunHops.LauncherEcho(watchdog, [], fixture.Root, environment: Echo("watchdog-apphost"))).Arguments.ShouldNotBeNull().ShouldBeEmpty();
        var probe = await NightlyRunHops.LauncherEcho(watchdog, ["-ProbeOnly", "-IntervalSeconds", "5", "-HealthUrl", "http://127.0.0.1:9/health"], fixture.Root,
            environment: Echo("watchdog-apphost"));
        probe.Arguments.ShouldBe(["--probe-only", "--interval-seconds", "5", "--health-url", "http://127.0.0.1:9/health"]);
        AppHostWatchdogOptions.Parse(probe.Arguments!, "root").IntervalSeconds.ShouldBe(5);
        var stub = await NightlyRunHops.LauncherEcho(watchdog, ["-RestartScript", "stub.ps1"], fixture.Root, environment: Echo("watchdog-apphost"));
        stub.ExitCode.ShouldBe(2);
        stub.Stderr.ShouldContain("-RestartScript is not supported");
        stub.Arguments.ShouldBeNull("nothing reached the command");

        var observer = await NightlyRunHops.LauncherEcho(fixture.PathOf("scripts", "apphost-watchdog-state-observer.ps1"), ["-TaskName", "T", "-Root", "R", "-StateFile", "S"],
            fixture.Root, environment: Echo("apphost-watchdog-state-observer"));
        observer.Arguments.ShouldBe(["--task-name", "T", "--root", "R", "--state-file", "S"]);
        var autostart = await NightlyRunHops.LauncherEcho(fixture.PathOf("scripts", "autostart-apphost.ps1"), ["-NoBuild", "-DockerTimeoutSec", "60"], fixture.Root,
            environment: Echo("autostart-apphost"));
        autostart.Arguments.ShouldBe(["--no-build", "--docker-timeout-sec", "60"]);
        AppHostAutostartOptions.Parse(autostart.Arguments!, "root").DockerTimeoutSec.ShouldBe(60);
        AssertThinLauncher("watchdog-apphost.ps1", "    [switch]$ProbeOnly,", "    [string]$RestartScript");
        AssertThinLauncher("apphost-watchdog-state-observer.ps1", "    [string]$TaskName = 'Antiphon AppHost Watchdog',", "    [string]$LogFile");
        AssertThinLauncher("autostart-apphost.ps1", "    [switch]$NoBuild,", "    [int]$HealthTimeoutSec = 180");
    }

    [Test]
    public async Task Root_verify_dev_stack_launcher_passes_the_deploy_flags()
    {
        using var fixture = Checkout();
        File.Copy(Path.Combine(DelegateScriptRunner.RepoRoot, "verify-dev-stack.ps1"), fixture.PathOf("verify-dev-stack.ps1"));
        var entry = fixture.PathOf("verify-dev-stack.ps1");
        // scripts/deploy-local.ps1 runs verify-dev-stack.ps1 -SkipBrowser -TimeoutSec 30.
        var deploy = await NightlyRunHops.LauncherEcho(entry, ["-SkipBrowser", "-TimeoutSec", "30"], fixture.Root, environment: Echo("verify-dev-stack", "1"));
        deploy.ExitCode.ShouldBe(1, deploy.Stderr);
        deploy.Arguments.ShouldBe(["--skip-browser", "--timeout-sec", "30"]);
        (await NightlyRunHops.LauncherEcho(entry, ["-SimpleMode", "-ProbeListeningPorts"], fixture.Root, environment: Echo("verify-dev-stack"))).Arguments
            .ShouldBe(["--simple-mode", "--probe-listening-ports"]);
        var text = File.ReadAllText(Path.Combine(DelegateScriptRunner.RepoRoot, "verify-dev-stack.ps1")).Replace("\r\n", "\n");
        text.ShouldStartWith("#requires -Version 5.1");
        text.Split('\n').Where(line => System.Text.RegularExpressions.Regex.IsMatch(line, @"^\s*\.\s")).ToArray()
            .ShouldBe([". (Join-Path (Join-Path (Join-Path $PSScriptRoot 'scripts') 'lib') 'antiphon-cli.ps1')"]);
        File.ReadAllBytes(Path.Combine(DelegateScriptRunner.RepoRoot, "verify-dev-stack.ps1")).All(b => b < 128).ShouldBeTrue("ASCII only");
        File.Exists(Path.Combine(DelegateScriptRunner.RepoRoot, "scripts", "start-apphost.ps1")).ShouldBeFalse();
    }

    // ---- unit 37: the repository root's dev stack launchers. Unit 43 deleted dev-start.ps1, dev-stop.ps1, dev-fresh.ps1,
    // dev-backup.ps1, dev-restore.ps1, dev-upgrade.ps1, restart.ps1, restart-server.ps1, stop-server.ps1 and
    // restart-session-runner.ps1, and unit 44 dev-aspire.ps1: nothing starts them by path, so operators run
    // scripts\antiphon.cmd <command> ----

    private static readonly string[] GoneRootLaunchers =
        ["dev-aspire.ps1", "dev-start.ps1", "dev-stop.ps1", "dev-fresh.ps1", "dev-backup.ps1", "dev-restore.ps1", "dev-upgrade.ps1", "restart.ps1", "restart-server.ps1",
            "stop-server.ps1", "restart-session-runner.ps1"];

    [Test]
    public void Root_dev_stack_launchers_are_gone_and_the_commands_take_their_flags()
    {
        // The flags dev-aspire.ps1 passed (-NoBuild, -NoBrowser, -AllowWorktree) are the command's own.
        var aspire = DevAspireOptions.Parse(["--no-build", "--no-browser", "--allow-worktree"], "root");
        (aspire.NoBuild, aspire.NoBrowser, aspire.AllowWorktree).ShouldBe((true, true, true));
        foreach (var gone in GoneRootLaunchers.Concat(new[] { "restart-client.ps1", "assert-dev-ports-clear.ps1", Path.Combine("scripts", "check-daemon-build.ps1"),
                     Path.Combine("scripts", "lib", "dev-postgres-compose.ps1"), Path.Combine("scripts", "lib", "live-startup-timing.ps1"), Path.Combine("scripts", "test-live-startup-timing.ps1") }))
            File.Exists(Path.Combine(DelegateScriptRunner.RepoRoot, gone)).ShouldBeFalse(gone);
    }

    // The 5.1 and pwsh parse of the kept launchers (it was the deleted
    // RunnerRestartScriptCompatibilityTests.Touched_scripts_parse_as_ascii_on_both_shells): -Command parses, nothing runs.
    [Test]
    [Arguments("powershell.exe")]
    [Arguments("pwsh")]
    public async Task Kept_launchers_and_restart_entries_parse_and_are_ascii(string shell)
    {
        if (shell == "powershell.exe" && !OperatingSystem.IsWindows())
            Skip.Test("CARD-1669: Windows PowerShell 5.1 (powershell.exe) exists only on Windows");
        string[] files = ["scripts/run-daemon.ps1", "scripts/test-client.ps1", "scripts/autostart-session-runner.ps1", "scripts/lib/antiphon-cli.ps1", "scripts/lib/windows-phone-home-env.ps1",
            "scripts/restart-apphost.ps1", "scripts/set-apphost-maintenance.ps1",
            "scripts/watchdog-apphost.ps1", "scripts/apphost-watchdog-state-observer.ps1", "scripts/autostart-apphost.ps1", "scripts/apphost-common.ps1", "verify-dev-stack.ps1"];
        foreach (var file in files)
            File.ReadAllBytes(Path.Combine(DelegateScriptRunner.RepoRoot, file)).All(b => b < 128).ShouldBeTrue(file + " is ASCII");
        var list = string.Join(",", files.Select(file => "'" + Path.Combine(DelegateScriptRunner.RepoRoot, file).Replace("'", "''") + "'"));
        var command = "$failed = 0; foreach ($f in @(" + list + ")) { $e = $null; $t = $null; "
            + "$null = [System.Management.Automation.Language.Parser]::ParseFile($f, [ref]$t, [ref]$e); "
            + "if ($e.Count) { Write-Output $f; $e | ForEach-Object { Write-Output $_.ToString() }; $failed++ } }; exit $failed";
        var start = new ProcessStartInfo(shell) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-Command", command }) start.ArgumentList.Add(argument);
        var run = await Finish(Process.Start(start) ?? throw new InvalidOperationException(shell + " did not start"));
        run.Exit.ShouldBe(0, run.Stdout + run.Stderr);
    }
}