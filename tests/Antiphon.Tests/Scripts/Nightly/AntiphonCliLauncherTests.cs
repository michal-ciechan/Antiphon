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

    private static Process Start(NightlyFixture fixture, string? dll, string command, string[] arguments, string resultFile = "",
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
        // No dll: the launcher runs the published build bin-launcher/antiphon-scripts/current names.
        if (dll == null) start.Environment.Remove("ANTIPHON_SCRIPTS_DLL");
        else start.Environment["ANTIPHON_SCRIPTS_DLL"] = dll;
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

    private static Task<LauncherRun> Run(NightlyFixture fixture, string? dll, string command, string[] arguments, string resultFile = "",
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

    // Unit 57 (N0, the cached launcher): the published build bin-launcher/antiphon-scripts/current names runs in place, with
    // ANTIPHON_CLI_CHECK set to the checkout so it checks itself before the command (CliLauncherTests own that check); no run
    // copy is made. ANTIPHON_SCRIPTS_DLL (tests) runs its dll in place and unchecked, and nothing is built.
    [Test]
    public async Task The_published_cli_runs_in_place_and_checks_itself_and_an_override_runs_unchecked()
    {
        using var fixture = Checkout();
        var environment = new Dictionary<string, string>
        {
            ["CLI_ECHO_ENV"] = "ANTIPHON_CLI_CHECK,ANTIPHON_SCRIPTS_DLL",
            ["ANTIPHON_CLI_CHECK"] = "inherited",
        };

        var overridden = await Run(fixture, StagedEcho, "cli-echo", ["--x"], environment: environment);
        overridden.Exit.ShouldBe(0, overridden.Stderr);
        var echo = overridden.Echo();
        echo.GetProperty("baseDirectory").GetString()!.TrimEnd('\\', '/').ShouldBe(Path.GetDirectoryName(StagedEcho)!.TrimEnd('\\', '/'), StringCompareShould.IgnoreCase);
        echo.GetProperty("environment").GetProperty("ANTIPHON_CLI_CHECK").ValueKind.ShouldBe(JsonValueKind.Null, "an override is never checked");
        Directory.Exists(fixture.PathOf("bin-launcher")).ShouldBeFalse("nothing is built or copied");

        // The published build: the echo host under the CLI's name, in a folder named by an identity.
        const string name = "0123456789abcdef01234567";
        var published = fixture.PathOf("bin-launcher", "antiphon-scripts", name);
        Directory.CreateDirectory(published);
        foreach (var file in Directory.EnumerateFiles(Path.GetDirectoryName(StagedEcho)!).Where(file => !file.EndsWith(".deps.json", StringComparison.Ordinal)))
            File.Copy(file, Path.Combine(published, Path.GetFileName(file).Replace("Antiphon.ScriptHarnessHost", "Antiphon.Scripts", StringComparison.Ordinal)));
        File.WriteAllText(fixture.PathOf("bin-launcher", "antiphon-scripts", "current"), name);

        var run = await Run(fixture, null, "cli-echo", ["--y", "a b"], environment: environment);
        run.Exit.ShouldBe(0, run.Stderr);
        var current = run.Echo();
        Arguments(current).ShouldBe(new[] { "--y", "a b" });
        current.GetProperty("baseDirectory").GetString()!.TrimEnd('\\', '/').ShouldBe(published, StringCompareShould.IgnoreCase);
        current.GetProperty("environment").GetProperty("ANTIPHON_CLI_CHECK").GetString()!.TrimEnd('\\', '/').ShouldBe(fixture.Root.TrimEnd('\\', '/'), StringCompareShould.IgnoreCase);
        current.GetProperty("environment").GetProperty("ANTIPHON_SCRIPTS_DLL").ValueKind.ShouldBe(JsonValueKind.Null);
        Directory.GetDirectories(fixture.PathOf("bin-launcher")).Select(Path.GetFileName).ShouldBe(new[] { "antiphon-scripts" }, "no run copy");
    }

    // Unit 57: Main checks its build only when a launcher asks (ANTIPHON_CLI_CHECK). The staged CLI is no published build, so
    // asked, it publishes the named checkout's CLI first; this checkout has no CLI project: exit 9 and one stderr line.
    [Test]
    public async Task The_cli_checks_itself_only_when_a_launcher_asks()
    {
        using var fixture = Checkout();

        var plain = await Exec(StagedCli, ["no-such-command"], null);
        plain.Exit.ShouldBe(2, plain.Stderr);
        plain.Stderr.ShouldStartWith("usage: antiphon-scripts");

        var asked = await Exec(StagedCli, ["no-such-command"], fixture.Root);
        asked.Exit.ShouldBe(9, asked.Stderr);
        asked.Stdout.Trim().ShouldBeEmpty();
        asked.Stderr.Trim().ShouldBe("antiphon-cli: " + fixture.Root.TrimEnd('\\', '/') + " has no scripts/lib/Antiphon.Scripts/Antiphon.Scripts.csproj");
    }

    private static Task<LauncherRun> Exec(string dll, string[] arguments, string? check)
    {
        var start = new ProcessStartInfo("dotnet") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in new[] { "exec", dll }.Concat(arguments)) start.ArgumentList.Add(argument);
        if (check == null) start.Environment.Remove("ANTIPHON_CLI_CHECK");
        else start.Environment["ANTIPHON_CLI_CHECK"] = check;
        return Finish(Process.Start(start) ?? throw new InvalidOperationException("dotnet did not start"));
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

    // ---- unit 36: scripts/autostart-session-runner.ps1, the launcher the logon task "Antiphon Session Runner" (as registered
    // before unit 61, whose action runs the command itself) and the phase 1 clone, by hand, start by path ----

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
    // (scripts/cutover-postgres-server2.ps1 starts them by path), scripts/watchdog-apphost.ps1,
    // scripts/apphost-watchdog-state-observer.ps1 and scripts/autostart-apphost.ps1 (the Scheduled Tasks registered before unit 61
    // start them by path; since unit 61 the actions run the commands). Unit 49 deleted the repository root's verify-dev-stack.ps1 with scripts/deploy-local.ps1, its
    // last by-path starter: the deploy-local command runs restart-apphost and verify-dev-stack in process (DeployLocalTests) ----

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

    // Unit 49: the deploy's two steps are the commands in process, with the flags the deleted launchers passed (deploy-local.ps1
    // ran verify-dev-stack.ps1 -SkipBrowser -TimeoutSec 30, and restart-apphost.ps1 -TimeoutSec <n> [-NoBuild] [-AllowWorktree]).
    [Test]
    public void Deploy_local_and_its_root_verify_launcher_are_gone_and_the_steps_take_their_flags()
    {
        foreach (var gone in new[] { "verify-dev-stack.ps1", Path.Combine("scripts", "deploy-local.ps1"), Path.Combine("scripts", "apphost-common.ps1"),
                     Path.Combine("scripts", "lib", "desktop-postgres.ps1"), Path.Combine("scripts", "start-apphost.ps1") })
            File.Exists(Path.Combine(DelegateScriptRunner.RepoRoot, gone)).ShouldBeFalse(gone);
        var verify = DevStackVerifyOptions.Parse(DeployLocal.VerifyArguments, "root");
        (verify.SkipBrowser, verify.TimeoutSec, verify.SimpleMode, verify.ProbeListeningPorts).ShouldBe((true, 30, false, false));
        var restart = AppHostRestartOptions.Parse(DeployLocal.RestartArguments(DeployLocalOptions.Parse(["--no-build", "--timeout-sec", "30", "--allow-worktree"], "root")), "root");
        (restart.NoBuild, restart.TimeoutSec, restart.AllowWorktree, restart.MaxWaitSec).ShouldBe((true, 30, true, 600));
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
        string[] files = ["scripts/run-daemon.ps1", "scripts/test-client.ps1", "scripts/autostart-session-runner.ps1", "scripts/lib/antiphon-cli.ps1",
            "scripts/restart-apphost.ps1", "scripts/set-apphost-maintenance.ps1",
            "scripts/watchdog-apphost.ps1", "scripts/apphost-watchdog-state-observer.ps1", "scripts/autostart-apphost.ps1"];
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

    // ---- unit 47: scripts/verify-docker-stack.ps1, which the owner-gated deploy-server2.ps1, c590-real.ps1, c1609-manual.ps1 and
    // verify-card0849-caches.ps1 start by path (pwsh -File). Its head stays: the manifest checks, the c590-real.ps1 context guards
    // and the live server2 bridge; everything past it is the verify-docker-stack command. No test here runs a routed case without
    // the stub (that is the bridge: RetireTempRunnerTimestampTests drives it with fake ssh and scp) ----

    private static readonly string[] StackVerifierFiles = ["verify-docker-stack.ps1", "c590-command.ps1", "c590-real.ps1", "c1008-host-proof.ps1", "c1609-host-proof.ps1"];

    /// <summary>A temporary checkout root holding the verifier, the scripts its head loads, and scripts/lib/antiphon-cli.ps1.</summary>
    private static NightlyFixture StackVerifierCheckout(out string manifest, string fields = "")
    {
        var fixture = Checkout();
        foreach (var name in StackVerifierFiles)
            File.Copy(Path.Combine(DelegateScriptRunner.RepoRoot, "scripts", name), fixture.PathOf("scripts", name));
        manifest = fixture.PathOf("manifest.json");
        File.WriteAllText(manifest, "{\"evidenceRoot\":" + JsonSerializer.Serialize(fixture.PathOf("evidence")) + fields + "}");
        return fixture;
    }

    [Test]
    public async Task Stack_verifier_launcher_hands_stub_and_unrouted_cases_to_the_command()
    {
        using var fixture = StackVerifierCheckout(out var manifest);
        var entry = fixture.PathOf("scripts", "verify-docker-stack.ps1");
        var echo = new Dictionary<string, string> { ["CLI_ECHO_COMMAND"] = "verify-docker-stack", ["CLI_ECHO_ENV"] = "ANTIPHON_C590_STUB,ANTIPHON_C590_SINK" };

        // Stub mode: every case, a routed one included, is the command's; the stub and sink reach it through the environment.
        var stub = await NightlyRunHops.LauncherEcho(entry, ["-Case", "deploy-parent", "-Manifest", manifest, "-RefreshClaudeToken"], fixture.Root, pwsh: true,
            environment: new Dictionary<string, string>(echo) { ["ANTIPHON_C590_STUB"] = "1", ["ANTIPHON_C590_SINK"] = fixture.PathOf("sink.jsonl") });
        stub.ExitCode.ShouldBe(0, stub.Stderr);
        stub.Arguments.ShouldBe(["--case", "deploy-parent", "--manifest", manifest]);
        stub.Stdout.ShouldContain("\"ANTIPHON_C590_STUB\":\"1\"");

        // Without the stub a case the bridge does not route (CP-5's) reaches the command too.
        var unrouted = await NightlyRunHops.LauncherEcho(entry, ["-Case", "e2e-staged-apphost", "-Manifest", manifest], fixture.Root, pwsh: true, environment: echo);
        unrouted.ExitCode.ShouldBe(0, unrouted.Stderr);
        unrouted.Arguments.ShouldBe(["--case", "e2e-staged-apphost", "--manifest", manifest]);
    }

    [Test]
    public async Task Stack_verifier_launcher_head_still_refuses_before_the_command()
    {
        var echo = new Dictionary<string, string> { ["CLI_ECHO_COMMAND"] = "verify-docker-stack", ["ANTIPHON_C590_STUB"] = "1" };
        using var fixture = StackVerifierCheckout(out _);
        var entry = fixture.PathOf("scripts", "verify-docker-stack.ps1");
        var missing = await NightlyRunHops.LauncherEcho(entry, ["-Case", "version-check", "-Manifest", fixture.PathOf("absent.json")], fixture.Root, pwsh: true, environment: echo);
        missing.ExitCode.ShouldBe(1);
        missing.Arguments.ShouldBeNull("the CLI never ran");
        (missing.Stdout + missing.Stderr).ShouldContain("Manifest is required");

        // A C994 cleanup context on any other case is refused by c590-real.ps1's guard in the head.
        using var guarded = StackVerifierCheckout(out var manifest, ",\"tempContainerCleanup\":{\"version\":1}");
        var refused = await NightlyRunHops.LauncherEcho(guarded.PathOf("scripts", "verify-docker-stack.ps1"), ["-Case", "version-check", "-Manifest", manifest],
            guarded.Root, pwsh: true, environment: echo);
        refused.ExitCode.ShouldBe(1);
        refused.Arguments.ShouldBeNull("the CLI never ran");
        (refused.Stdout + refused.Stderr).ShouldContain("TempContainerContextInvalid");
    }

    [Test]
    public async Task Stack_verifier_launcher_runs_the_command_and_writes_its_result()
    {
        using var fixture = StackVerifierCheckout(out var manifest, ",\"observedRevision\":\"wrong\",\"sourceSha\":\"abc\"");
        var start = new ProcessStartInfo("pwsh") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = fixture.Root };
        foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-File", fixture.PathOf("scripts", "verify-docker-stack.ps1"), "-Case", "version-check", "-Manifest", manifest })
            start.ArgumentList.Add(argument);
        start.Environment["ANTIPHON_SCRIPTS_DLL"] = StagedCli;
        start.Environment["ANTIPHON_C590_STUB"] = "1";
        var run = await Finish(Process.Start(start) ?? throw new InvalidOperationException("pwsh did not start"));
        run.Exit.ShouldBe(2, run.Stderr);
        run.Stdout.Trim().ShouldBe("DIAGNOSIS=RevisionMismatch");
        File.ReadAllText(fixture.PathOf("evidence", "c590-result.json")).ShouldBe("{\"accepted\":false,\"diagnosis\":\"RevisionMismatch\",\"exit\":2}" + Environment.NewLine);
    }

    [Test]
    public void Stack_verifier_launcher_keeps_its_parameters_and_head_and_holds_no_switch()
    {
        var path = Path.Combine(DelegateScriptRunner.RepoRoot, "scripts", "verify-docker-stack.ps1");
        var bytes = File.ReadAllBytes(path);
        bytes.All(b => b < 128 && b != (byte)'\r').ShouldBeTrue("ASCII with LF line ends");
        var text = File.ReadAllText(path);
        text.ShouldStartWith("# CARD-0590 stack verifier. -Case selects one literal checkpoint or guard.\nparam(\n    [Parameter(Mandatory = $true)][string]$Case,\n"
            + "    [Parameter(Mandatory = $true)][string]$Manifest,\n    [switch]$RefreshClaudeToken\n)\n");
        text.Split('\n').Select(line => line.Trim()).Where(line => System.Text.RegularExpressions.Regex.IsMatch(line, @"^\.\s")).ToArray().ShouldBe([
            ". (Join-Path $PSScriptRoot 'c590-command.ps1')",
            ". (Join-Path $PSScriptRoot 'c590-real.ps1')",
            ". (Join-Path $PSScriptRoot 'c590-real.ps1')",
            ". (Join-Path $PSScriptRoot 'c590-real.ps1')",
            ". (Join-Path (Join-Path $PSScriptRoot 'lib') 'antiphon-cli.ps1')"]);
        text.ShouldContain("Invoke-C590LiveCase -Case $Case -Manifest $m -RawManifest $rawManifest");
        text.ShouldContain("exit (Invoke-AntiphonCli -Command 'verify-docker-stack' -Arguments @('--case', $Case, '--manifest', $Manifest))");
        text.ShouldNotContain("switch ($Case)");
        text.ShouldNotContain("Write-C590Result");
        foreach (var gone in new[] { "test-docker.ps1", "test-docker-container.ps1", "verify-card0604-dind-runner.ps1" })
            File.Exists(Path.Combine(DelegateScriptRunner.RepoRoot, "scripts", gone)).ShouldBeFalse(gone);
    }

    // Unit 48: nothing started the CARD-0660 image qualification by path, so the verify-card0660-codex-image command has no
    // launcher, and its W10 fixture went with it (the owner's approval); its checks run in process (CodexRunnerImageContractTests).
    // Unit 51: the W8 operator tools are commands. Nothing started them by path, so none keeps a launcher, and the phone-home
    // library went with its last loader (its Join-PhoneHomeChild is WindowsPhoneHome.ChildPath). The flags are the scripts'
    // parameters in kebab case.
    [Test]
    public void Operator_tools_and_the_phone_home_library_are_gone_and_the_commands_take_their_flags()
    {
        foreach (var gone in new[] { "refresh-server2-github-token.ps1", "desktop-slot-provision.ps1", "desktop-win-phase1-secrets.ps1", Path.Combine("lib", "windows-phone-home-env.ps1") })
            File.Exists(Path.Combine(DelegateScriptRunner.RepoRoot, "scripts", gone)).ShouldBeFalse(gone);
        GithubTokenRefreshOptions.Parse(["--server", "mc@server2"], "home").Server.ShouldBe("mc@server2");
        var slots = DesktopSlotProvisionOptions.Parse(["--root", "C:\\Antiphon\\slots", "--execute", "--what-if", "--remove-empty", "--disk-budget-gib", "32"]);
        (slots.Execute, slots.WhatIf, slots.RemoveEmpty, slots.DiskBudgetGiB).ShouldBe((true, true, true, 32));
        var secrets = DesktopWinPhase1Options.Parse(["--execute", "--rotate", "--what-if", "--sweep-stranded", "--sweep-backups"], "home", "scripts");
        (secrets.Execute, secrets.Rotate, secrets.WhatIf, secrets.SweepStranded, secrets.SweepBackups).ShouldBe((true, true, true, true, true));
    }
    [Test]
    public void Codex_image_verifier_and_its_fixture_are_gone()
    {
        foreach (var gone in new[] { Path.Combine("scripts", "verify-card0660-codex-image.ps1"), Path.Combine("scripts", "fixtures", "c913-image-wrapper.ps1") })
            File.Exists(Path.Combine(DelegateScriptRunner.RepoRoot, gone)).ShouldBeFalse(gone);
    }

    // ---- unit 53: scripts/postgres-snapshot.ps1 (the snapshot job's runner resolves it by path; the cutover runbooks and
    // the deploy-server2-postgres drill plan name it) and scripts/windmill/invoke-postgres-snapshot.ps1 and invoke-postgres-snapshot-stale.ps1
    // (Windmill's u/lndcobra/antiphon_postgres_snapshot and _stale start their runner copies by path) ----

    /// <summary>A temporary checkout root holding the three snapshot launchers beside scripts/lib/antiphon-cli.ps1.</summary>
    private static NightlyFixture SnapshotCheckout()
    {
        var fixture = Checkout();
        Directory.CreateDirectory(fixture.PathOf("scripts", "windmill"));
        foreach (var entry in new[] { "postgres-snapshot.ps1", Path.Combine("windmill", "invoke-postgres-snapshot.ps1"), Path.Combine("windmill", "invoke-postgres-snapshot-stale.ps1") })
            File.Copy(Path.Combine(DelegateScriptRunner.RepoRoot, "scripts", entry), fixture.PathOf("scripts", entry));
        return fixture;
    }

    private static void AssertSnapshotLauncher(string entry, string libLine)
    {
        var path = Path.Combine(DelegateScriptRunner.RepoRoot, "scripts", entry);
        File.ReadAllBytes(path).All(b => b < 128 && b != (byte)'\r').ShouldBeTrue(entry + " is ASCII with LF line ends");
        var text = File.ReadAllText(path);
        text.ShouldStartWith("#requires -Version 5.1\n");
        text.ShouldContain(libLine + "\n");
        // A runner copy has no checkout around it: it runs the main checkout's CLI.
        text.ShouldContain("if (-not (Test-Path -LiteralPath $cli -PathType Leaf)) { $cli = 'C:\\src\\Antiphon\\scripts\\lib\\antiphon-cli.ps1' }\n");
        text.Split('\n').Where(line => System.Text.RegularExpressions.Regex.IsMatch(line, @"^\s*\.\s")).ToArray().ShouldBe([". $cli"]);
        text.ShouldNotContain("[Antiphon.");
        text.ShouldNotContain("Invoke-SnapshotNative");
    }

    [Test]
    public async Task Postgres_snapshot_launcher_passes_every_parameter_and_its_strings_verbatim()
    {
        using var fixture = SnapshotCheckout();
        var entry = fixture.PathOf("scripts", "postgres-snapshot.ps1");
        var echo = new Dictionary<string, string> { ["CLI_ECHO_COMMAND"] = "postgres-snapshot" };

        // No parameters: every default as its flag, the strings verbatim (blank ones included), no --container.
        var defaults = await NightlyRunHops.LauncherEcho(entry, [], fixture.Root, environment: echo);
        defaults.ExitCode.ShouldBe(0, defaults.Stderr);
        defaults.Arguments.ShouldBe(["--one-drive-retain", "7", "--retain", "14", "--minimum-dump-bytes", "1048576", "--stale-run-minutes", "360", "--target", "desktop",
            "--database", "antiphon", "--staging-path", "", "--destination", "", "--sync-root", "", "--recipient-file", "", "--recipient-ref", "", "--identity-file", "",
            "--identity-ref", "", "--scratch-container", "antiphon-postgres-drill", "--scratch-database", "antiphon_drill", "--scratch-volume", "antiphon-postgres-drill-pgdata",
            "--source-volume", "antiphon_pgdata", "--scratch-image", "postgres:16"]);
        JsonSerializer.Serialize(PostgresSnapshotOptions.Parse(defaults.Arguments!)).ShouldBe(JsonSerializer.Serialize(PostgresSnapshotOptions.Parse([])));

        // The scheduled child's command line (PostgresSnapshotJob.ChildArguments).
        var child = await NightlyRunHops.LauncherEcho(entry, ["-Plain", "-Execute", "-OneDriveCopy", "-StagingPath", @"C:\Users\lndco\antiphon-snapshot-staging", "-Target", "server2"],
            fixture.Root, environment: echo);
        child.ExitCode.ShouldBe(0, child.Stderr);
        JsonSerializer.Serialize(PostgresSnapshotOptions.Parse(child.Arguments.ShouldNotBeNull(child.Stdout + child.Stderr))).ShouldBe(JsonSerializer.Serialize(
            PostgresSnapshotOptions.Parse(["--plain", "--execute", "--one-drive-copy", "--staging-path", @"C:\Users\lndco\antiphon-snapshot-staging", "--target", "server2"])));

        // Every parameter; an explicitly blank -Container is bound (server2 refuses it) and a trailing backslash survives.
        string[] every = ["-WhatIf", "-Execute", "-Plain", "-OneDriveCopy", "-OneDriveRetain", "3", "-RestoreDrill", "-Container", "", "-Database", "db", "-StagingPath",
            @"C:\stage dir\", "-Destination", @"C:\dest", "-SyncRoot", @"C:\one drive", "-RecipientFile", "r.txt", "-RecipientRef", "REF", "-IdentityFile", "i.txt",
            "-IdentityRef", "IREF", "-Retain", "2", "-MinimumDumpBytes", "5000000000", "-KeepScratch", "-ScratchContainer", "sc", "-ScratchDatabase", "sd", "-ScratchVolume", "sv",
            "-SourceVolume", "so", "-ScratchImage", "img:1", "-AllowRemoteDocker", "-StaleRunMinutes", "9", "-Target", "desktop"];
        var all = await NightlyRunHops.LauncherEcho(entry, every, fixture.Root, environment: echo);
        all.ExitCode.ShouldBe(0, all.Stderr);
        var received = PostgresSnapshotOptions.Parse(all.Arguments.ShouldNotBeNull(all.Stdout + all.Stderr));
        JsonSerializer.Serialize(received).ShouldBe(JsonSerializer.Serialize(PostgresSnapshotOptions.Parse(["--what-if", "--execute", "--plain", "--one-drive-copy",
            "--one-drive-retain", "3", "--restore-drill", "--container", "", "--database", "db", "--staging-path", @"C:\stage dir\", "--destination", @"C:\dest", "--sync-root",
            @"C:\one drive", "--recipient-file", "r.txt", "--recipient-ref", "REF", "--identity-file", "i.txt", "--identity-ref", "IREF", "--retain", "2", "--minimum-dump-bytes",
            "5000000000", "--keep-scratch", "--scratch-container", "sc", "--scratch-database", "sd", "--scratch-volume", "sv", "--source-volume", "so", "--scratch-image", "img:1",
            "--allow-remote-docker", "--stale-run-minutes", "9", "--target", "desktop"])));
        (received.ContainerGiven, received.Container, received.StagingPath).ShouldBe((true, "", @"C:\stage dir\"));

        // ValidateSet('desktop', 'server2', IgnoreCase = $false) and [int] binding still refuse before the CLI runs.
        foreach (var bad in new[] { new[] { "-Target", "Server2" }, ["-Retain", "x"], ["-Bogus"] })
        {
            var refused = await NightlyRunHops.LauncherEcho(entry, bad, fixture.Root, environment: echo);
            refused.ExitCode.ShouldBe(1, string.Join(" ", bad));
            refused.Arguments.ShouldBeNull("the CLI never ran");
        }

        var exit = await NightlyRunHops.LauncherEcho(entry, ["-WhatIf"], fixture.Root, environment: new Dictionary<string, string> { ["CLI_ECHO_COMMAND"] = "postgres-snapshot", ["CLI_ECHO_EXIT"] = "9" });
        exit.ExitCode.ShouldBe(9, "the command's exit code is returned");
        AssertSnapshotLauncher("postgres-snapshot.ps1", "$cli = Join-Path (Join-Path $PSScriptRoot 'lib') 'antiphon-cli.ps1'");
        var text = File.ReadAllText(Path.Combine(DelegateScriptRunner.RepoRoot, "scripts", "postgres-snapshot.ps1"));
        foreach (var line in new[] { "    [switch]$WhatIf,", "    [int]$OneDriveRetain = 7,", "    [string]$Container = 'antiphon-postgres',", "    [int64]$MinimumDumpBytes = 1048576,",
                     "    [ValidateSet('desktop', 'server2', IgnoreCase = $false)]", "    [string]$Target = 'desktop'" })
            text.ShouldContain(line + "\n");
    }

    [Test]
    public async Task Snapshot_job_and_stale_launchers_run_their_commands_and_keep_set_snapshot_target()
    {
        using var fixture = SnapshotCheckout();
        var windmill = fixture.PathOf("scripts", "windmill");
        var job = await NightlyRunHops.LauncherEcho(Path.Combine(windmill, "invoke-postgres-snapshot.ps1"), [], fixture.Root, environment: Echo("postgres-snapshot-job", "9"));
        job.ExitCode.ShouldBe(9, job.Stderr);
        job.Arguments.ShouldNotBeNull(job.Stdout + job.Stderr).Length.ShouldBe(2);
        job.Arguments![0].ShouldBe("--runner-dir");
        Path.GetFullPath(job.Arguments[1]).TrimEnd('\\', '/').ShouldBe(windmill.TrimEnd('\\', '/'), StringCompareShould.IgnoreCase);
        var stale = await NightlyRunHops.LauncherEcho(Path.Combine(windmill, "invoke-postgres-snapshot-stale.ps1"), [], fixture.Root, environment: Echo("postgres-snapshot-stale", "1"));
        stale.ExitCode.ShouldBe(1, stale.Stderr);
        stale.Arguments.ShouldBe([]);

        // The operator's flip and rollback: dot-sourced, the job does not run; Set-SnapshotTarget answers the word, or '' when refused.
        foreach (var (exitCode, expected) in new[] { ("0", "RETURN=server2"), ("1", "RETURN=") })
        {
            var start = new ProcessStartInfo(OperatingSystem.IsWindows() ? "powershell.exe" : "pwsh")
            {
                UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = fixture.Root,
            };
            foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-Command",
                         ". '" + Path.Combine(windmill, "invoke-postgres-snapshot.ps1").Replace("'", "''") + "'; $r = Set-SnapshotTarget server2; 'RETURN=' + $r" })
                start.ArgumentList.Add(argument);
            start.Environment["ANTIPHON_SCRIPTS_DLL"] = StagedEcho;
            start.Environment["CLI_ECHO_COMMAND"] = "postgres-snapshot-target";
            start.Environment["CLI_ECHO_EXIT"] = exitCode;
            var run = await Finish(Process.Start(start) ?? throw new InvalidOperationException("the launcher shell did not start"));
            run.Exit.ShouldBe(0, run.Stderr);
            var lines = run.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            lines.Count(line => line.StartsWith('{')).ShouldBe(1, "one CLI call: the target, not the job");
            lines[^1].ShouldBe(expected);
            var arguments = Arguments(run.Echo());
            arguments.Take(3).ShouldBe(["--word", "server2", "--runner-dir"]);
            Path.GetFullPath(arguments[3]).TrimEnd('\\', '/').ShouldBe(windmill.TrimEnd('\\', '/'), StringCompareShould.IgnoreCase);
        }

        AssertSnapshotLauncher(Path.Combine("windmill", "invoke-postgres-snapshot.ps1"), "$cli = Join-Path (Join-Path (Split-Path $PSScriptRoot -Parent) 'lib') 'antiphon-cli.ps1'");
        AssertSnapshotLauncher(Path.Combine("windmill", "invoke-postgres-snapshot-stale.ps1"), "$cli = Join-Path (Join-Path (Split-Path $PSScriptRoot -Parent) 'lib') 'antiphon-cli.ps1'");
        File.Exists(Path.Combine(DelegateScriptRunner.RepoRoot, "scripts", "windmill", "job-status.ps1")).ShouldBeFalse("the status library is C# (JobStatus)");
        // The jobs start the runner copies by path; their definitions are unchanged.
        foreach (var (job_, script) in new[] { ("antiphon-postgres-snapshot.json", "invoke-postgres-snapshot.ps1"), ("antiphon-postgres-snapshot-stale.json", "invoke-postgres-snapshot-stale.ps1") })
            JsonDocument.Parse(File.ReadAllText(Path.Combine(DelegateScriptRunner.RepoRoot, "scripts", "windmill", job_))).RootElement.GetProperty("content").GetString()!
                .ShouldContain(@"-File C:\Users\lndco\antiphon-snapshot-runner\" + script);
    }

    [Test]
    public async Task Snapshot_launchers_that_cannot_start_the_cli_exit_9()
    {
        using var fixture = SnapshotCheckout();
        foreach (var entry in new[] { fixture.PathOf("scripts", "postgres-snapshot.ps1"), fixture.PathOf("scripts", "windmill", "invoke-postgres-snapshot.ps1"),
                     fixture.PathOf("scripts", "windmill", "invoke-postgres-snapshot-stale.ps1") })
        {
            var start = new ProcessStartInfo(OperatingSystem.IsWindows() ? "powershell.exe" : "pwsh")
            {
                UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = fixture.Root,
            };
            foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", entry })
                start.ArgumentList.Add(argument);
            start.Environment["ANTIPHON_SCRIPTS_DLL"] = fixture.PathOf("missing", "Antiphon.Scripts.dll");
            var run = await Finish(Process.Start(start) ?? throw new InvalidOperationException("the launcher shell did not start"));
            run.Exit.ShouldBe(9, run.Stderr);
            run.Stderr.Trim().ShouldStartWith("antiphon-cli: ANTIPHON_SCRIPTS_DLL names no file");
            run.Stdout.Trim().ShouldBeEmpty();
        }
    }

    // ---- unit 61 (N3): the actions the install-autostart and install-cleanup-task commands register run the CLI command with no
    // repository .ps1: the hidden PowerShell host runs <checkout>\scripts\antiphon.cmd and exits with the command's exit code (9
    // when the entry cannot start). They run here as the Task Scheduler starts them (one argument string, the registered working
    // directory or the system directory) against a checkout whose path holds a space, a single quote and parentheses and whose
    // published CLI is cli-echo; CreateNoWindow keeps -WindowStyle Hidden away from this console. Nothing is registered: the
    // installers write to a recording Task Scheduler. ----

    private sealed record FiredAction(LauncherRun Run, int Shell);

    private static async Task<FiredAction> Fire(ScheduledTaskDefinition task, string command, string exit)
    {
        var start = new ProcessStartInfo(task.Action.Execute, task.Action.Arguments)
        {
            UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true,
            WorkingDirectory = task.Action.WorkingDirectory ?? Environment.SystemDirectory,
        };
        start.Environment["CLI_ECHO_COMMAND"] = command;
        start.Environment["CLI_ECHO_EXIT"] = exit;
        start.Environment["CLI_ECHO_ENV"] = "ANTIPHON_CLI_CHECK";
        var process = Process.Start(start) ?? throw new InvalidOperationException("the task's PowerShell host did not start");
        var shell = process.Id;
        return new FiredAction(await Finish(process), shell);
    }

    private static string Folder(string? path) => Path.GetFullPath(path.ShouldNotBeNull()).TrimEnd(Path.DirectorySeparatorChar);

    [Test]
    public async Task Registered_task_actions_run_the_cli_command_and_return_its_exit_code()
    {
        if (!OperatingSystem.IsWindows()) Skip.Test("the task actions run scripts\\antiphon.cmd, a Windows entry");
        using var fixture = NightlyFixture.Create("task-action");
        var root = fixture.PathOf("it's a checkout (1)");
        Directory.CreateDirectory(Path.Combine(root, "scripts"));
        File.Copy(Path.Combine(DelegateScriptRunner.RepoRoot, "scripts", "antiphon.cmd"), Path.Combine(root, "scripts", "antiphon.cmd"));
        File.WriteAllText(Path.Combine(root, "docker-compose.dev.yml"), "services: {}\n");
        const string published = "0123456789abcdef01234567";
        var folder = Path.Combine(root, "bin-launcher", "antiphon-scripts", published);
        Directory.CreateDirectory(folder);
        File.Copy(StagedEcho, Path.Combine(folder, "Antiphon.Scripts.dll"));
        File.Copy(Path.Combine(Path.GetDirectoryName(StagedEcho)!, "Antiphon.ScriptHarnessHost.runtimeconfig.json"), Path.Combine(folder, "Antiphon.Scripts.runtimeconfig.json"));
        File.WriteAllText(Path.Combine(root, "bin-launcher", "antiphon-scripts", "current"), published);

        var tasks = new AutostartInstallTests.RecordingTasks();
        static string? OnPath(string name) => (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .SelectMany(directory => new[] { Path.Combine(directory, name), Path.Combine(directory, name + ".exe") }).FirstOrDefault(File.Exists);
        // Docker answers "not running": the compose step is skipped and the tasks are still registered.
        var machine = new AutostartInstallMachine(new System.IO.Abstractions.FileSystem(), tasks, name => Environment.GetEnvironmentVariable(name)!, name => OnPath(name)!,
            (_, _) => 1);
        var output = new StringWriter();
        var host = new NightlyHost
        {
            Clock = new Microsoft.Extensions.Time.Testing.FakeTimeProvider(new DateTimeOffset(2026, 11, 26, 9, 0, 0, TimeSpan.Zero)), Git = new FakeGit().Run,
            Launcher = new RecordingLauncher(), Processes = new FakeProcessTable(), Probe = new FakeHostProbe(), Out = output, Error = output, Trace = TextWriter.Null,
        };
        AutostartInstall.Run(AutostartInstallOptions.Parse([], root), host, machine).ShouldBe(0, output.ToString());
        AutostartInstall.RunCleanupTask(CleanupTaskInstallOptions.Parse([], root), host, machine).ShouldBe(0, output.ToString());
        tasks.Registered.Count.ShouldBe(5);

        (string Name, string Command, int Exit, string[]? Expected)[] cases =
        [
            // Null: the launcher passed its own PID; the action passes the host's ($PID), the task's process, the supervisor.
            ("Antiphon Session Runner", "autostart-session-runner", 37, null),
            ("Antiphon AppHost", "autostart-apphost", 0, []),
            ("Antiphon AppHost Watchdog", "watchdog-apphost", 3, []),
            ("Antiphon AppHost Watchdog State Observer", "apphost-watchdog-state-observer", 0, []),
            ("Antiphon Build Junk Cleanup", "cleanup-build-junk", 0, ["--repo-root", root, "--skip-if-modified-within-minutes", "60"]),
        ];
        foreach (var (name, command, exit, expected) in cases)
        {
            var task = tasks.Registered[name].ShouldNotBeNull(name);
            var fired = await Fire(task, command, exit.ToString(System.Globalization.CultureInfo.InvariantCulture));
            fired.Run.Exit.ShouldBe(exit, name + ": " + fired.Run.Stderr);
            var echo = fired.Run.Echo();
            Arguments(echo).ShouldBe(expected ?? ["--supervisor-pid", fired.Shell.ToString(System.Globalization.CultureInfo.InvariantCulture)], name);
            echo.GetProperty("pid").GetInt32().ShouldNotBe(fired.Shell, name + ": the command runs under cmd.exe, a child of the host");
            Folder(echo.GetProperty("environment").GetProperty("ANTIPHON_CLI_CHECK").GetString()).ShouldBe(Folder(root), StringCompareShould.IgnoreCase);
            Folder(echo.GetProperty("workingDirectory").GetString()).ShouldBe(Folder(task.Action.WorkingDirectory ?? Environment.SystemDirectory), StringCompareShould.IgnoreCase);
        }
        tasks.Registered["Antiphon Build Junk Cleanup"]!.Action.WorkingDirectory.ShouldBeNull("the cleanup task never had one");

        // The entry gone (a moved checkout): the host exits 9, never 0.
        File.Delete(Path.Combine(root, "scripts", "antiphon.cmd"));
        var missing = await Fire(tasks.Registered["Antiphon AppHost Watchdog"]!, "watchdog-apphost", "0");
        missing.Run.Exit.ShouldBe(9, missing.Run.Stderr);
        missing.Run.Stdout.Trim().ShouldBeEmpty();
    }
}
