using System.Collections;
using System.Diagnostics;
using System.Text.Json;
using Antiphon.Nightly;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts.Nightly;

/// <summary>
/// W2-nightly S01: the production pieces of NightlyHost on this machine. WatchedLauncher (Start-NightlyProcess's
/// semantics, the environment swap included) starts the cli-echo mode of the script harness host, which reports its
/// argv, directory and chosen environment values. NightlyNativeGit runs the real git with Invoke-NightlyGit's options.
/// NightlyHost.Production observes this process.
/// </summary>
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class NightlyHostTests
{
    private static string EchoHost => Path.Combine(AppContext.BaseDirectory, "script-harness-host", "Antiphon.ScriptHarnessHost.dll");

    private static readonly StringComparison PathComparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private static Dictionary<string, string> CurrentEnvironment() =>
        Environment.GetEnvironmentVariables().Cast<DictionaryEntry>()
            .ToDictionary(entry => (string)entry.Key, entry => (string?)entry.Value ?? "", OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

    private static JsonElement Echo(string resultFile)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(resultFile));
        return document.RootElement.Clone();
    }

    [Test]
    public void Watched_launch_passes_the_exit_code_argv_directory_and_exactly_the_given_environment()
    {
        using var fixture = NightlyFixture.Create("nightly-host");
        var starts = 0;
        var launcher = new WatchedLauncher(() => starts++);
        var environment = CurrentEnvironment();
        environment.Remove("PATH");
        environment["NIGHTLY_KEPT"] = "kept value";
        environment["NIGHTLY_EMPTY"] = "";
        environment["CLI_ECHO_ENV"] = "PATH,NIGHTLY_KEPT,NIGHTLY_EMPTY";
        environment["CLI_ECHO_EXIT"] = "7";
        var swapped = fixture.PathOf("swapped.json");

        var run = launcher.Run(new NightlyLaunch("dotnet", ["exec", EchoHost, "cli-echo", "a b", "", @"c\", "--result-file", swapped],
            fixture.Root, Environment: environment));

        run.ExitCode.ShouldBe(7);
        run.TimedOut.ShouldBeFalse();
        run.Pid.ShouldBeGreaterThan(0);
        starts.ShouldBe(1);
        var echo = Echo(swapped);
        echo.GetProperty("arguments").EnumerateArray().Select(a => a.GetString()).ShouldBe(new[] { "a b", "", @"c\", "--result-file", swapped });
        echo.GetProperty("workingDirectory").GetString()!.TrimEnd('\\', '/').Equals(fixture.Root.TrimEnd('\\', '/'), PathComparison).ShouldBeTrue();
        var seen = echo.GetProperty("environment");
        seen.GetProperty("PATH").ValueKind.ShouldBe(JsonValueKind.Null);
        seen.GetProperty("NIGHTLY_KEPT").GetString().ShouldBe("kept value");
        seen.GetProperty("NIGHTLY_EMPTY").ValueKind.ShouldBe(JsonValueKind.Null);

        // Without a map the child inherits this process's environment (CLI_ECHO_ENV is unset here: nothing is reported).
        var inherited = fixture.PathOf("inherited.json");
        launcher.Run(new NightlyLaunch("dotnet", ["exec", EchoHost, "cli-echo", "--result-file", inherited], fixture.Root)).ExitCode.ShouldBe(0);
        Echo(inherited).GetProperty("environment").EnumerateObject().Count().ShouldBe(0);
        starts.ShouldBe(2);
        Environment.GetEnvironmentVariable("NIGHTLY_KEPT").ShouldBeNull();
    }

    [Test]
    public void Watched_launch_kills_the_child_when_its_timeout_expires()
    {
        using var fixture = NightlyFixture.Create("nightly-host");
        var environment = CurrentEnvironment();
        environment["CLI_ECHO_HOLD"] = fixture.PathOf("never-released");
        var watch = Stopwatch.StartNew();

        var run = new WatchedLauncher().Run(new NightlyLaunch("dotnet", ["exec", EchoHost, "cli-echo", "--result-file", fixture.PathOf("held.json")],
            fixture.Root, TimeoutMilliseconds: 3000, Environment: environment));

        run.TimedOut.ShouldBeTrue();
        run.ExitCode.ShouldBe(1);
        watch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(30));
        new NightlyProcessTable().IsAlive(run.Pid).ShouldBeFalse();
    }

    // Unit 10 of 2026-11-22-remaining-ps1-to-csharp.md: the watchdog deploy reads ssh, scp and dotnet output without printing it.
    [Test]
    public void Watched_launch_with_capture_reads_stdout_and_stderr_into_the_log_text()
    {
        using var fixture = NightlyFixture.Create("nightly-host");
        var repository = fixture.PathOf("repo");
        Directory.CreateDirectory(repository);
        NightlyNativeGit.Run(repository, ["init", "-q"]).ExitCode.ShouldBe(0);
        var launcher = new WatchedLauncher();

        var version = launcher.Run(new NightlyLaunch("git", ["--version"], repository, CaptureOutput: true));
        version.ExitCode.ShouldBe(0);
        version.LogText.ShouldStartWith("git version ");
        version.LogText.ShouldEndWith("\n");
        version.LogText.ShouldNotContain("\r");

        var failed = launcher.Run(new NightlyLaunch("git", ["rev-parse", "--verify", "refs/heads/no-such-branch"], repository, CaptureOutput: true));
        failed.ExitCode.ShouldNotBe(0);
        failed.LogText.ShouldContain("fatal:");

        // Without CaptureOutput the child shares this process's streams: nothing is read.
        launcher.Run(new NightlyLaunch("git", ["--version"], repository)).LogText.ShouldBe("");
    }

    [Test]
    public void Watched_launcher_refuses_what_it_cannot_honour()
    {
        var launcher = new WatchedLauncher();
        Should.Throw<NotSupportedException>(() => launcher.Run(new NightlyLaunch("dotnet", ["--info"], "", Custody: NightlyCustody.Owned)));
        Should.Throw<NotSupportedException>(() => launcher.Run(new NightlyLaunch("dotnet", ["--info"], "", LogPath: "run.log")));
        launcher.WaitCleanup(new NightlyLaunchResult()).ShouldBeTrue();
        launcher.NativeActiveCount.ShouldBe(0);
    }

    [Test]
    public void Native_git_passes_the_invoke_nightly_git_options_and_merges_stderr()
    {
        using var fixture = NightlyFixture.Create("nightly-git");
        var repository = fixture.PathOf("repo");
        Directory.CreateDirectory(repository);

        NightlyNativeGit.CommandLine(repository, ["status"]).ShouldBe(new[]
            { "-c", "core.fsmonitor=false", "-c", "gc.auto=0", "-c", "maintenance.auto=false", "-C", repository, "status" });
        NightlyNativeGit.Run(repository, ["init", "-q"]).ExitCode.ShouldBe(0);
        NightlyNativeGit.Run(repository, ["config", "--get", "gc.auto"]).Output.ShouldBe("0" + Environment.NewLine);
        NightlyNativeGit.Run(repository, ["config", "--get", "core.fsmonitor"]).Output.ShouldBe("false" + Environment.NewLine);
        NightlyNativeGit.Run(repository, ["config", "--get", "maintenance.auto"]).Output.ShouldBe("false" + Environment.NewLine);
        var top = NightlyNativeGit.Run(repository, ["rev-parse", "--show-toplevel"]);
        Path.GetFullPath(top.Output.Trim()).Equals(Path.GetFullPath(repository), PathComparison).ShouldBeTrue(top.Output);
        var missing = NightlyNativeGit.Run(repository, ["rev-parse", "--verify", "refs/heads/no-such-branch"]);
        missing.ExitCode.ShouldNotBe(0);
        missing.Output.ShouldContain("fatal:");
    }

    [Test]
    public void Production_host_observes_this_process_and_machine()
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var host = NightlyHost.Production(output, error);

        host.Clock.ShouldBeSameAs(TimeProvider.System);
        // W2-nightly S17: both custodies (a hop is Watched, a build or test host Owned); an owned launch never runs on the watched launcher.
        host.Launcher.ShouldBeOfType<CustodyLauncher>();
        host.Launcher.NativeActiveCount.ShouldBe(0);
        host.Out.ShouldBeSameAs(output);
        host.Error.ShouldBeSameAs(error);
        host.Trace.ShouldBeSameAs(TextWriter.Null);
        (host.UtcNow - DateTime.UtcNow).Duration().ShouldBeLessThan(TimeSpan.FromMinutes(1));

        var processes = host.Processes;
        processes.CurrentPid.ShouldBe(Environment.ProcessId);
        processes.IsAlive(Environment.ProcessId).ShouldBeTrue();
        processes.IsAlive(0).ShouldBeFalse();
        using var self = Process.GetCurrentProcess();
        processes.StartTimeUtc(Environment.ProcessId).ShouldBe(self.StartTime.ToUniversalTime());
        processes.Parent(Environment.ProcessId).Id.ShouldBeGreaterThan(0);
        // The boot id is always there (registry, /proc); a minimal Linux host may have no machine-id file.
        if (OperatingSystem.IsWindows() || OperatingSystem.IsLinux()) processes.BootId().ShouldNotBeNullOrWhiteSpace();
        if (OperatingSystem.IsWindows()) processes.HostId().ShouldNotBeNullOrWhiteSpace();

        host.Probe.Platform().ShouldBe(OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsLinux() ? "linux" : "");
        host.Probe.Docker().Version.ShouldBe("unprobed-default");
        host.Probe.DiskFree().MinimumFreeBytes.ShouldBe(10L * 1024 * 1024 * 1024);
        host.Probe.ToolVersion("git").ShouldStartWith("git version");
        host.Probe.ToolVersion("node").ShouldBe("");
        host.Git(Environment.CurrentDirectory, ["--version"]).Output.ShouldStartWith("git version");
    }
}
