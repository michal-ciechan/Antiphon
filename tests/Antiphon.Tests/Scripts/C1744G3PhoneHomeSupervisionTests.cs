using System.Diagnostics;
using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using Antiphon.Nightly;
using Antiphon.SessionRunner;
using Antiphon.Tests.Application;
using Antiphon.Tests.Scripts.Nightly;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// CARD-1744 S4. Phone-home env, autostart config refusal, and the AppHost launch guard. Since unit 36 of
/// docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md the rules are Antiphon.Nightly.WindowsPhoneHome and the
/// logon start is the autostart-session-runner command (SessionRunnerAutostart; scripts/autostart-session-runner.ps1 is its
/// thin launcher, AntiphonCliLauncherTests), so the rows of the deleted driver scripts/test-windows-phone-home-runner.ps1 run in
/// process with their names: the fail-closed autostart rows on a mock file system with the supervisor's build slot and service
/// start recorded (nothing is built or started). The same cases run on Windows and Linux. Dropped: autostart-helper-load (the
/// dot-source of the library could fail; the rules are compiled in), env-table.start-calls-helper and
/// start-no-inline-table (AST checks of the deleted start script; Env_table_matches_the_start_command runs the start command),
/// and the 5.1 AST parse of the library (AntiphonCliLauncherTests parses the kept launcher and library).
/// </summary>
[Category("Unit")]
public sealed class C1744G3PhoneHomeSupervisionTests
{
    private const string Origin = "https://antiphon.desktop.codeperf.net";
    private const string Pwsh = @"C:\Program Files\PowerShell\7\pwsh.exe";
    private const int Launcher = 5150;

    private static readonly IReadOnlyList<KeyValuePair<string, string>> NoExecutables = [];

    private static string PhoneJson(string runnerId = "desktop-win", string origin = Origin, string capacity = "1", string? root = null, string? port = null,
        string? extraName = null, string extraValue = "")
    {
        var parts = new List<string> { "\"runnerId\":\"" + runnerId + "\"", "\"serverOrigin\":\"" + origin + "\"", "\"capacity\":" + capacity };
        if (root != null) parts.Add("\"root\":\"" + root.Replace("\\", "\\\\") + "\"");
        if (port != null) parts.Add("\"port\":" + port);
        if (extraName != null) parts.Add("\"" + extraName + "\":\"" + extraValue + "\"");
        return "{" + string.Join(",", parts) + "}";
    }

    private static SessionRunnerAutostartPlan Plan(string? text, bool explicitPath = false, bool secret = true, string repo = @"C:\Antiphon\repo") =>
        WindowsPhoneHome.AutostartPlan(text, explicitPath, secret, @"C:\Users\operator", Pwsh, NoExecutables, repo);

    private static string? Env(SessionRunnerAutostartPlan plan, string key) =>
        plan.Environment.Where(pair => pair.Key == key).Select(pair => pair.Value).FirstOrDefault();

    // ---- env-table ----

    private static readonly string[] ExpectedKeys =
    [
        "ASPNETCORE_URLS", "PhoneHome__Enabled", "PhoneHome__RunnerId", "PhoneHome__ServerOrigin", "PhoneHome__SecretPath", "PhoneHome__StoreIdPath",
        "PhoneHome__CapacityStatePath", "PhoneHome__AllowedCwd", "PhoneHome__RunnerRepository", "PhoneHome__Capacity", "PhoneHome__ClaudeHome",
        "PhoneHome__CodexHome", "PhoneHome__GrokHome", "PhoneHome__RawExeAllowList__0", "SessionRunner__SessionLogPath", "SessionRunner__PtyHostDir",
        "SessionRunner__Herdr__Enabled", "SessionRunner__HostStats__HistoryEnabled", "SessionRunner__HostStats__Volumes__0", "Serilog__LogPath",
        "PhoneHome__Executables__claude", "PhoneHome__Executables__grok",
    ];

    private static readonly Dictionary<string, string> ExpectedValues = new()
    {
        ["ASPNETCORE_URLS"] = "http://localhost:17214",
        ["PhoneHome__Enabled"] = "true",
        ["PhoneHome__RunnerId"] = "laptop",
        ["PhoneHome__ServerOrigin"] = Origin,
        ["PhoneHome__SecretPath"] = @"C:\Antiphon\runner\secrets\phone-home",
        ["PhoneHome__StoreIdPath"] = "C:/Antiphon/runner/state/runner-store-id",
        ["PhoneHome__CapacityStatePath"] = "C:/Antiphon/runner/state/runner-capacity",
        ["PhoneHome__AllowedCwd"] = "C:/Antiphon/runner/work",
        ["PhoneHome__RunnerRepository"] = "C:/Antiphon/runner/work/repos/antiphon",
        ["PhoneHome__Capacity"] = "2",
        ["PhoneHome__ClaudeHome"] = @"C:\Users\operator\.claude",
        ["PhoneHome__CodexHome"] = @"C:\Users\operator\.codex",
        ["PhoneHome__GrokHome"] = @"C:\Users\operator\.grok",
        ["PhoneHome__RawExeAllowList__0"] = Pwsh,
        ["SessionRunner__SessionLogPath"] = "C:/Antiphon/runner/state/session-runner",
        ["SessionRunner__PtyHostDir"] = "C:/Antiphon/runner/state/pty-hosts",
        ["SessionRunner__Herdr__Enabled"] = "false",
        ["SessionRunner__HostStats__HistoryEnabled"] = "false",
        ["SessionRunner__HostStats__Volumes__0"] = "C:/Antiphon/runner/work",
        ["Serilog__LogPath"] = "C:/Antiphon/runner/state/logs",
        ["PhoneHome__Executables__claude"] = @"C:\Tools\claude.exe",
        ["PhoneHome__Executables__grok"] = @"C:\Tools\grok.exe",
    };

    [Test]
    public void Env_table_matches_the_start_command()
    {
        KeyValuePair<string, string>[] executables = [new("claude", @"C:\Tools\claude.exe"), new("grok", @"C:\Tools\grok.exe")];
        var envs = WindowsPhoneHome.RunnerEnvironment(new WindowsPhoneHomeRunner("laptop", @"C:\Antiphon\runner", 17214, 2, Origin, @"C:\Users\operator\.claude",
            @"C:\Users\operator\.codex", @"C:\Users\operator\.grok", Pwsh, executables));
        // env-table.keys, env-table.values, env-table.executables
        envs.Select(pair => pair.Key).ToArray().ShouldBe(ExpectedKeys);
        foreach (var pair in envs) pair.Value.ShouldBe(ExpectedValues[pair.Key], pair.Key);
        envs.Select(pair => pair.Key).ShouldNotContain("PhoneHome__Executables__codex");
        Env(Plan(PhoneJson()), "SessionRunner__HistoryEnabled").ShouldBeNull();
        Env(Plan(PhoneJson()), "SessionRunner__HostStats__HistoryEnabled").ShouldBe("false", "phase2-history");

        // The start command hands the runner exactly this table (the start script called the helper, not an inline table).
        using var stage = new StartStage();
        var run = stage.Run("--runner-id", "laptop", "--root", stage.RunnerRoot, "--no-build");
        run.Exit.ShouldBe(0, run.Text);
        var expected = WindowsPhoneHome.RunnerEnvironment(new WindowsPhoneHomeRunner("laptop", stage.RunnerRoot, 17214, 2, Origin,
            Path.Combine(stage.Home, ".claude"), Path.Combine(stage.Home, ".codex"), Path.Combine(stage.Home, ".grok"), stage.PwshPath,
            [new("claude", stage.ClaudePath)]));
        stage.Started.ShouldHaveSingleItem().Environment.ShouldBe(expected);
    }

    // ---- autostart-direct, autostart-phone-home, autostart-refusals ----

    [Test]
    public void Autostart_without_config_is_the_direct_runner()
    {
        var plan = WindowsPhoneHome.AutostartPlan(null, false, false, @"C:\Users\operator", Pwsh, NoExecutables, "");
        plan.ModeName.ShouldBe("direct", "autostart-direct.mode");
        plan.Environment.ShouldBeEmpty("autostart-direct.env");
        plan.ExeArgs.ShouldBe("--urls http://localhost:17204", "autostart-direct.exe-args");
    }

    [Test]
    public void Autostart_with_config_runs_desktop_win()
    {
        var plan = Plan(PhoneJson());
        plan.ModeName.ShouldBe("phone-home");
        Env(plan, "PhoneHome__RunnerId").ShouldBe("desktop-win", "autostart-phone-home.runner-id");
        Env(plan, "PhoneHome__Capacity").ShouldBe("1", "autostart-phone-home.capacity");
        (Env(plan, "ASPNETCORE_URLS"), plan.ExeArgs).ShouldBe(("http://localhost:17204", "--urls http://localhost:17204"), "autostart-phone-home.port");
        var explicitPlan = Plan(PhoneJson(port: "17214"), explicitPath: true);
        (explicitPlan.ModeName, Env(explicitPlan, "ASPNETCORE_URLS"), explicitPlan.ExeArgs)
            .ShouldBe(("phone-home", "http://localhost:17214", "--urls http://localhost:17214"), "autostart-phone-home.explicit-port");
        Env(plan, "PhoneHome__ClaudeHome").ShouldBe(@"C:\Users\operator\.claude");
        // autostart-phone-home.helper-source: the rules stay pure (the callers read files and resolve executables).
        var source = File.ReadAllText(Path.Combine(DelegateScriptRunner.RepoRoot, "scripts", "lib", "Antiphon.Nightly", "WindowsPhoneHome.cs"));
        foreach (var banned in new[] { "File.", "ReadAll", "Process", "HttpClient", "GetEnvironmentVariable" })
            source.ShouldNotContain(banned, Case.Sensitive, "autostart-phone-home.helper-source");
    }

    [Test]
    [Arguments("local", "{\"runnerId\":\"local\",\"serverOrigin\":\"https://antiphon.desktop.codeperf.net\",\"capacity\":1}", "runner-id-reserved")]
    [Arguments("desktop", "{\"runnerId\":\"Desktop\",\"serverOrigin\":\"https://antiphon.desktop.codeperf.net\",\"capacity\":1}", "runner-id-reserved")]
    [Arguments("bad-id", "{\"runnerId\":\"bad_id\",\"serverOrigin\":\"https://antiphon.desktop.codeperf.net\",\"capacity\":1}", "runner-id-invalid")]
    [Arguments("http-origin", "{\"runnerId\":\"desktop-win\",\"serverOrigin\":\"http://antiphon.desktop.codeperf.net\",\"capacity\":1}", "origin-not-https")]
    [Arguments("origin-path", "{\"runnerId\":\"desktop-win\",\"serverOrigin\":\"https://antiphon.desktop.codeperf.net/extra\",\"capacity\":1}", "origin-path")]
    [Arguments("capacity-0", "{\"runnerId\":\"desktop-win\",\"serverOrigin\":\"https://antiphon.desktop.codeperf.net\",\"capacity\":0}", "capacity-out-of-range")]
    [Arguments("capacity-3", "{\"runnerId\":\"desktop-win\",\"serverOrigin\":\"https://antiphon.desktop.codeperf.net\",\"capacity\":3}", "capacity-out-of-range")]
    [Arguments("port-17299", "{\"runnerId\":\"desktop-win\",\"serverOrigin\":\"https://antiphon.desktop.codeperf.net\",\"capacity\":1,\"port\":17299}", "port-invalid")]
    [Arguments("port-not-broker", "{\"runnerId\":\"desktop-win\",\"serverOrigin\":\"https://antiphon.desktop.codeperf.net\",\"capacity\":1,\"port\":17214}", "port-not-broker")]
    [Arguments("unknown-key", "{\"runnerId\":\"desktop-win\",\"serverOrigin\":\"https://antiphon.desktop.codeperf.net\",\"capacity\":1,\"sharedSecret\":\"nope\"}", "unknown-key:sharedSecret")]
    [Arguments("malformed", "{", "config-malformed")]
    [Arguments("unc-root", "{\"runnerId\":\"desktop-win\",\"serverOrigin\":\"https://antiphon.desktop.codeperf.net\",\"capacity\":1,\"root\":\"\\\\\\\\server\\\\share\"}", "root-unc")]
    [Arguments("device-root", "{\"runnerId\":\"desktop-win\",\"serverOrigin\":\"https://antiphon.desktop.codeperf.net\",\"capacity\":1,\"root\":\"\\\\\\\\?\\\\C:\\\\x\"}", "root-device")]
    [Arguments("relative-root", "{\"runnerId\":\"desktop-win\",\"serverOrigin\":\"https://antiphon.desktop.codeperf.net\",\"capacity\":1,\"root\":\"runner\"}", "root-not-drive-rooted")]
    [Arguments("missing-capacity", "{\"runnerId\":\"desktop-win\",\"serverOrigin\":\"https://antiphon.desktop.codeperf.net\"}", "missing-key:capacity")]
    [Arguments("case-variant-key", "{\"runnerId\":\"desktop-win\",\"RunnerId\":\"x\",\"serverOrigin\":\"https://antiphon.desktop.codeperf.net\",\"capacity\":1}", "config-malformed")]
    [Arguments("wrong-case-key", "{\"RunnerId\":\"desktop-win\",\"serverOrigin\":\"https://antiphon.desktop.codeperf.net\",\"capacity\":1}", "unknown-key:RunnerId")]
    [Arguments("non-ascii", "{\"runnerId\":\"desktop-win\",\"serverOrigin\":\"https://antiphon.desktop.codeperf.net\",\"capacity\":1,\"root\":\"C:\\\\caf\u00e9\"}", "config-malformed")]
    [Arguments("array", "[1]", "config-malformed")]
    [Arguments("capacity-double", "{\"runnerId\":\"desktop-win\",\"serverOrigin\":\"https://antiphon.desktop.codeperf.net\",\"capacity\":1.0}", "capacity-invalid")]
    [Arguments("capacity-string", "{\"runnerId\":\"desktop-win\",\"serverOrigin\":\"https://antiphon.desktop.codeperf.net\",\"capacity\":\"1\"}", "capacity-invalid")]
    [Arguments("origin-number", "{\"runnerId\":\"desktop-win\",\"serverOrigin\":5,\"capacity\":1}", "origin-invalid")]
    [Arguments("origin-userinfo", "{\"runnerId\":\"desktop-win\",\"serverOrigin\":\"https://u:p@antiphon.desktop.codeperf.net\",\"capacity\":1}", "origin-userinfo")]
    [Arguments("origin-query", "{\"runnerId\":\"desktop-win\",\"serverOrigin\":\"https://antiphon.desktop.codeperf.net/?a=1\",\"capacity\":1}", "origin-query")]
    [Arguments("origin-fragment", "{\"runnerId\":\"desktop-win\",\"serverOrigin\":\"https://antiphon.desktop.codeperf.net/#f\",\"capacity\":1}", "origin-fragment")]
    public void Autostart_refuses_bad_config_without_fallback(string row, string text, string reason)
    {
        var plan = Plan(text);
        (plan.ModeName, plan.Reason, plan.Environment.Count).ShouldBe(("refused", reason, 0), "autostart-refusals." + row);
    }

    [Test]
    public void Autostart_refuses_a_missing_secret_or_checkout_and_reads_the_lenient_json_forms()
    {
        var secret = Plan(PhoneJson(), secret: false);
        (secret.ModeName, secret.Reason).ShouldBe(("refused", "secret-missing"), "autostart-refusals.secret-missing");
        var repo = Plan(PhoneJson(), repo: " ");
        (repo.ModeName, repo.Reason).ShouldBe(("refused", "repo-root-missing"));
        // pwsh 7's ConvertFrom-Json read comments and trailing commas, and kept the last of two equal keys.
        Plan("{\"runnerId\":\"desktop-win\", // phase 2\n\"serverOrigin\":\"" + Origin + "\",\"capacity\":1,}").ModeName.ShouldBe("phone-home");
        Env(Plan("{\"runnerId\":\"first\",\"runnerId\":\"desktop-win\",\"serverOrigin\":\"" + Origin + "\",\"capacity\":1}"), "PhoneHome__RunnerId").ShouldBe("desktop-win");
        var rooted = WindowsPhoneHome.ParseConfig(PhoneJson(root: @"D:/runner/../other", port: "17214"));
        (rooted.Ok, rooted.Root, rooted.Port).ShouldBe((true, "D:/runner/../other", 17214));
        WindowsPhoneHome.RunnerEnvironment(new WindowsPhoneHomeRunner("x", rooted.Root, 17214, 1, Origin, "", "", "", "", NoExecutables))
            .Single(pair => pair.Key == "PhoneHome__AllowedCwd").Value.ShouldBe("D:/other/work");
    }

    [Test]
    [Arguments("C:/Users/operator", ".claude", @"C:/Users/operator\.claude")]
    [Arguments(@"C:\Users\operator\", "/.codex", @"C:\Users\operator\.codex")]
    [Arguments("/home/operator", ".grok", "/home/operator/.grok")]
    [Arguments("", @"\secrets\phone-home", @"secrets\phone-home")]
    [Arguments(@"C:\Antiphon\runner", @"secrets\phone-home", @"C:\Antiphon\runner\secrets\phone-home")]
    public void Child_paths_keep_the_left_separator(string left, string right, string expected) =>
        WindowsPhoneHome.ChildPath(left, right).ShouldBe(expected);

    // ---- the autostart command (autostart-unreadable, autostart-refused-script, autostart-script-direct) ----

    /// <summary>One logon start on a mock file system: the checkout C:\repo, the supervisor's build slot and service start recorded.</summary>
    private sealed class AutostartWorld
    {
        public const string Repo = @"C:\repo";

        public MockFileSystem Files { get; } = new();
        public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 11, 24, 8, 30, 0, TimeSpan.Zero));
        public FakeProcessTable Processes { get; } = new FakeProcessTable().Start(Launcher, new DateTime(2026, 11, 24, 8, 29, 0, DateTimeKind.Utc));
        public List<BuildSlotOptions> Builds { get; } = [];
        public List<DaemonServiceStart> Starts { get; } = [];
        public StringWriter Out { get; } = new();

        public string Log => Files.Path.Combine(Repo, "logs", "session-runner.log");
        public string LogText => Files.File.Exists(Log) ? Files.File.ReadAllText(Log) : "";
        public string State => Files.Path.Combine(Repo, "logs", "session-runner.state");

        public int Run(params string[] arguments)
        {
            var host = new NightlyHost
            {
                Clock = Clock, Git = new FakeGit().Run, Launcher = new RecordingLauncher(), Processes = Processes, Probe = new FakeHostProbe(),
                Out = Out, Error = Out, Trace = TextWriter.Null, Sleep = Clock.Advance,
            };
            var machine = new SessionRunnerAutostartMachine(Files, name => name == "pwsh" ? Pwsh : name == "claude" ? @"C:\Tools\claude.exe" : null,
                @"C:\Users\operator", Start, (options, capture) =>
                {
                    Builds.Add(options);
                    return 0;
                });
            return SessionRunnerAutostart.Run(SessionRunnerAutostartOptions.Parse(["--supervisor-pid", Launcher.ToString(), .. arguments], Repo), host, machine);
        }

        // The service runs once: its exit sets the desired state to stopped, so supervision ends.
        private IDaemonService Start(DaemonServiceStart start)
        {
            Starts.Add(start);
            return new Service(() =>
            {
                Files.File.WriteAllText(State, "stopped");
                return 0;
            });
        }

        private sealed class Service(Func<int> run) : IDaemonService
        {
            public int Pid => 7001;
            public DateTime? StartTimeUtc => null;
            public int WaitForExit() => run();
            public void Dispose() { }
        }
    }

    private static void AssertClosed(AutostartWorld world, int exit, string row, string reason)
    {
        exit.ShouldBe(2, row + ".exit");
        world.LogText.ShouldContain("2026-11-24 08:30:00 [autostart] phone-home config refused: " + reason, Case.Sensitive, row + ".log");
        (world.Builds.Count, world.Starts.Count).ShouldBe((0, 0), row + ".stub: nothing was built or started");
        world.Files.File.Exists(world.State).ShouldBeFalse("the desired state is untouched");
    }

    [Test]
    public void Autostart_unreadable_config_exits_before_run_daemon()
    {
        var world = new AutostartWorld();
        world.Files.Directory.CreateDirectory(@"C:\cfg\phone-home.json");
        AssertClosed(world, world.Run("--phone-home-config-path", @"C:\cfg\phone-home.json"), "autostart-unreadable", "config-unreadable");
    }

    [Test]
    public void Autostart_refused_config_exits_before_run_daemon()
    {
        var world = new AutostartWorld();
        world.Files.AddFile(@"C:\cfg\phone-home.json", new MockFileData("{"));
        AssertClosed(world, world.Run("--phone-home-config-path", @"C:\cfg\phone-home.json"), "autostart-refused-script", "config-malformed");
        var secretless = new AutostartWorld();
        secretless.Files.AddFile(WindowsPhoneHome.DefaultConfigPath, new MockFileData(PhoneJson()));
        AssertClosed(secretless, secretless.Run(), "autostart-secret-missing", "secret-missing");
        var phase1AtDefault = new AutostartWorld();
        phase1AtDefault.Files.AddFile(WindowsPhoneHome.DefaultConfigPath, new MockFileData(PhoneJson(port: "17214")));
        AssertClosed(phase1AtDefault, phase1AtDefault.Run(), "autostart-port-not-broker", "port-not-broker");
    }

    [Test]
    public void Autostart_absent_config_reaches_run_daemon_direct()
    {
        var world = new AutostartWorld();
        world.Files.AddFile(world.State, new MockFileData("stopped"));
        world.Run().ShouldBe(0, "autostart-script-direct.exit");
        var start = world.Starts.ShouldHaveSingleItem("autostart-script-direct.stub");
        start.FileName.ShouldBe("cmd.exe");
        start.Arguments.ShouldBe("/s /c \"" + world.Files.Path.Combine(AutostartWorld.Repo, "src", "Antiphon.SessionRunner", "bin", "Debug", "net9.0", "Antiphon.SessionRunner.exe")
            + " --urls http://localhost:17204 >> \"" + world.Log + "\" 2>&1\"");
        start.WorkingDirectory.ShouldBe(world.Files.Path.Combine(AutostartWorld.Repo, "src", "Antiphon.SessionRunner"));
        start.Environment.Keys.ShouldNotContain(key => key.StartsWith("PhoneHome__", StringComparison.Ordinal));
        start.Environment["ANTIPHON_STARTUP_SUPERVISOR_PID"].ShouldBe(Launcher.ToString(), "the launcher is the supervisor the records name");
        var build = world.Builds.ShouldHaveSingleItem();
        (build.Label, build.Command[1], build.Command[2]).ShouldBe(("daemon-build", "build", world.Files.Path.Combine(AutostartWorld.Repo, "src", "Antiphon.SessionRunner")));
        world.Files.File.ReadAllText(world.Files.Path.Combine(AutostartWorld.Repo, "logs", "session-runner.supervisor.pid")).Trim().ShouldBe(Launcher.ToString());
        world.LogText.ShouldContain("2026-11-24 08:30:00 [autostart] logon supervisor starting (PID " + Launcher + ")");
        world.LogText.ShouldNotContain("phone-home config refused", Case.Sensitive, "autostart-script-direct.log");
        world.LogText.ShouldContain("[session-runner] Supervisor started (PID " + Launcher + ", run-daemon PID 4242)");
    }

    [Test]
    public void Autostart_phone_home_config_hands_the_variables_to_the_runner_only()
    {
        var world = new AutostartWorld();
        world.Files.AddFile(WindowsPhoneHome.DefaultConfigPath, new MockFileData(PhoneJson()));
        world.Files.AddFile(WindowsPhoneHome.ChildPath(WindowsPhoneHome.DefaultRoot, WindowsPhoneHome.SecretChild), new MockFileData("secret"));
        world.Run().ShouldBe(0);
        var start = world.Starts.ShouldHaveSingleItem();
        start.Arguments.ShouldContain(" --urls http://localhost:17204 >> ");
        var plan = WindowsPhoneHome.AutostartPlan(PhoneJson(), false, true, @"C:\Users\operator", Pwsh, [new("claude", @"C:\Tools\claude.exe")], AutostartWorld.Repo);
        foreach (var pair in plan.Environment) start.Environment[pair.Key].ShouldBe(pair.Value, pair.Key);
        start.Environment["PhoneHome__Executables__claude"].ShouldBe(@"C:\Tools\claude.exe");
        start.Environment.ShouldContainKey("ANTIPHON_STARTUP_ATTEMPT");
        world.Builds.ShouldHaveSingleItem().Environment.ShouldBeNull("the build does not see the runner's variables");
    }

    // ---- apphost-guard ----

    [Test]
    public void AppHost_launchers_refuse_while_phase2_config_exists()
    {
        var present = WindowsPhoneHome.LaunchRefusal(true, @"C:\Antiphon\runner\phone-home.json").ShouldNotBeNull("apphost-guard.present");
        foreach (var part in new[]
                 {
                     "REFUSED: phone-home-config-present", "If the Linux server is serving, this refusal is correct: do NOT delete the file.",
                     "Only during ROLLBACK, after the Linux server is stopped and pg_stat_activity is clear:", @"delete C:\Antiphon\runner\phone-home.json",
                     @"run scripts\antiphon.cmd restart-session-runner --hard, then retry.",
                 })
            present.ShouldContain(part, Case.Sensitive, "apphost-guard.present");
        present.ShouldNotContain("restart-linux-server");
        WindowsPhoneHome.LaunchRefusal(false, @"C:\Antiphon\runner\phone-home.json").ShouldBeNull("apphost-guard.absent");
        WindowsPhoneHome.LaunchRefusal(true, " ").ShouldContain(@"delete C:\Antiphon\runner\phone-home.json");

        // restart-apphost: the worktree guard, then the refusal, then the restart lock (before any stop or kill).
        var launcher = File.ReadAllText(Path.Combine(DelegateScriptRunner.RepoRoot, "scripts", "restart-apphost.ps1"));
        var restart = File.ReadAllText(Path.Combine(DelegateScriptRunner.RepoRoot, "scripts", "lib", "Antiphon.Nightly", "AppHostRestart.cs"));
        launcher.ShouldContain("Invoke-AntiphonCli -Command 'restart-apphost'");
        launcher.ShouldNotContain("Stop-Process");
        var work = restart.IndexOf("AppHostWorktree.Classify(", StringComparison.Ordinal);
        var guard = restart.IndexOf("WindowsPhoneHome.LaunchRefusal(", StringComparison.Ordinal);
        var danger = restart.IndexOf("AppHostLocks.TakeRestart(", StringComparison.Ordinal);
        (work >= 0 && guard > work && danger > guard).ShouldBeTrue($"apphost-guard.restart-order work={work} guard={guard} lock={danger}");

        // dev-aspire (DevAspire.cs since unit 37; dev-aspire.ps1 is its launcher): the worktree classification, then the
        // refusal, then the launch lock write.
        var aspireLauncher = File.ReadAllText(Path.Combine(DelegateScriptRunner.RepoRoot, "dev-aspire.ps1"));
        aspireLauncher.ShouldContain("Invoke-AntiphonCli -Command 'dev-aspire'");
        var aspire = File.ReadAllText(Path.Combine(DelegateScriptRunner.RepoRoot, "scripts", "lib", "Antiphon.Nightly", "DevAspire.cs"));
        var aspireWork = aspire.IndexOf("AppHostWorktree.Classify(", StringComparison.Ordinal);
        var aspireGuard = aspire.IndexOf("WindowsPhoneHome.LaunchRefusal(", StringComparison.Ordinal);
        var aspireWrite = aspire.IndexOf("files.File.WriteAllText(launchLock", StringComparison.Ordinal);
        (aspireWork >= 0 && aspireGuard > aspireWork && aspireWrite > aspireGuard)
            .ShouldBeTrue($"apphost-guard.aspire-order work={aspireWork} guard={aspireGuard} write={aspireWrite}");
        (launcher + aspireLauncher + aspire).ShouldNotContain("restart-linux-server", Case.Sensitive, "apphost-guard.no-linux-name");
    }

    // ---- compat ----

    [Test]
    public void Supervision_scripts_stay_ascii_and_51_compatible()
    {
        string[] files = ["scripts/autostart-session-runner.ps1", "scripts/lib/windows-phone-home-env.ps1", "scripts/restart-apphost.ps1", "dev-aspire.ps1",
            "scripts/lib/Antiphon.Nightly/WindowsPhoneHome.cs", "scripts/lib/Antiphon.Nightly/SessionRunnerAutostart.cs"];
        foreach (var file in files)
        {
            var path = Path.Combine(DelegateScriptRunner.RepoRoot, file);
            File.ReadAllBytes(path).All(b => b < 128).ShouldBeTrue(file + " compat.ascii");
            File.ReadAllText(path).ShouldNotContain("#requires -Version 7", Case.Insensitive, file + " compat.no-requires-7");
        }
        File.ReadAllText(Path.Combine(DelegateScriptRunner.RepoRoot, "scripts", "autostart-session-runner.ps1")).ShouldStartWith("#requires -Version 5.1");
    }

    // ---- no-build-stamp: the start command with --no-build on an unstamped runner ----

    [Test]
    public void Phone_home_no_build_warns_and_launches()
    {
        var runnerAssembly = typeof(RunnerBuildIdentity).Assembly.Location;
        FileVersionInfo.GetVersionInfo(runnerAssembly).ProductVersion.ShouldNotBeNull().ShouldEndWith("+unknown");
        using var stage = new StartStage { Version = PhoneHomeRunnerStart.ProductVersion(runnerAssembly), Exit = 2 };
        var run = stage.Run("--runner-id", "stamp-fixture", "--root", stage.RunnerRoot, "--server-origin", "http://127.0.0.1:1", "--no-build");
        (run.Text.Contains("Phone-home runner 'stamp-fixture'", StringComparison.Ordinal) && run.Exit == 2).ShouldBeTrue("no-build-stamp.launch: " + run.Text);
        run.Text.ShouldMatch("WARNING:.*--no-build.*unknown", "no-build-stamp.warning");
        stage.Started.ShouldHaveSingleItem().Arguments.ShouldBe(["--urls", "http://localhost:17214"]);

        using var stamped = new StartStage { Version = "1.0.0+" + new string('a', 40) };
        stamped.Run("--runner-id", "stamp-fixture", "--root", stamped.RunnerRoot, "--no-build").Text.ShouldNotContain("WARNING:");
    }

    /// <summary>
    /// A start-windows-phone-home-runner run on a temporary checkout (an empty runner executable) and runner root (the secret
    /// present): Windows, pwsh and claude on PATH, the build slot and the foreground run recorded.
    /// </summary>
    internal sealed class StartStage : IDisposable
    {
        private readonly NightlyFixture _fixture = NightlyFixture.Create("c1744g3-start");

        public StartStage()
        {
            var bin = _fixture.PathOf("checkout", "src", "Antiphon.SessionRunner", "bin", "Debug", "net9.0");
            Directory.CreateDirectory(bin);
            File.WriteAllText(Path.Combine(bin, "Antiphon.SessionRunner.exe"), "");
            Directory.CreateDirectory(Path.Combine(RunnerRoot, "secrets"));
            File.WriteAllText(Path.Combine(RunnerRoot, "secrets", "phone-home"), "offline fixture");
        }

        public string Checkout => _fixture.PathOf("checkout");
        public string RunnerRoot => _fixture.PathOf("runner");
        public string Home => _fixture.PathOf("home");
        public string PwshPath => _fixture.PathOf("tools", "pwsh.exe");
        public string ClaudePath => _fixture.PathOf("tools", "claude.exe");
        public bool Windows { get; init; } = true;
        public string? Version { get; init; } = "1.0.0+unknown";
        public int Exit { get; init; }
        public int BuildExit { get; init; }
        public FakeGit Git { get; } = new FakeGit("0123456789abcdef0123456789abcdef01234567\n");
        public Dictionary<string, string> Vars { get; } = [];
        public List<BuildSlotOptions> Builds { get; } = [];
        public List<PhoneHomeRunnerProcess> Started { get; } = [];

        public (int Exit, string Text) Run(params string[] arguments)
        {
            var output = new StringWriter();
            var host = new NightlyHost
            {
                Clock = new FakeTimeProvider(NightlyFixture.DefaultNow), Git = Git.Run, Launcher = new RecordingLauncher(), Processes = new FakeProcessTable(),
                Probe = new FakeHostProbe(), Out = output, Error = output, Trace = TextWriter.Null, Variables = name => Vars.GetValueOrDefault(name)!,
            };
            var machine = new PhoneHomeRunnerMachine(new FileSystem(), Windows, name => name == "pwsh" ? PwshPath : name == "claude" ? ClaudePath : null, Home,
                (options, capture) =>
                {
                    Builds.Add(options);
                    return BuildExit;
                },
                _ => Version, start =>
                {
                    Started.Add(start);
                    return Exit;
                });
            var exit = PhoneHomeRunnerStart.Run(PhoneHomeRunnerStartOptions.Parse(arguments, Checkout), host, machine);
            return (exit, output.ToString());
        }

        public void Dispose() => _fixture.Dispose();
    }
}
