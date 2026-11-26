using Shouldly;
using TUnit.Core;

namespace Antiphon.SessionRunner.Tests.Invariants;

/// <summary>
/// Guards the pty-host survival fix (docs/superpowers/specs/2026-07-19-pty-host-split.md,
/// "Deployment gotcha & fix"): the session-runner must be launched as the BUILT exe, never via
/// `dotnet run`. `dotnet run` wraps the app in a kill-on-close Job Object that captures the
/// detached pty-hosts and kills them on restart. If either launch path reverts to `dotnet run`,
/// sessions silently stop surviving runner restarts - so pin both.
/// </summary>
[Category("RepositoryInvariant")]
public class PtyHostDeploymentGuardTests
{
    private static string RepoRoot => FindRepoRoot();

    // Unit 36 (2026-11-22 plan): scripts/autostart-session-runner.ps1 is the logon task's launcher of the
    // autostart-session-runner command (Antiphon.Nightly.SessionRunnerAutostart), which supervises the BUILT executable and
    // rebuilds its project before each launch; Antiphon.Tests C1744G3PhoneHomeSupervisionTests run it.
    [Test]
    public async Task Autostart_script_launches_the_built_exe_not_dotnet_run()
    {
        var script = await File.ReadAllTextAsync(
            Path.Combine(RepoRoot, "scripts", "autostart-session-runner.ps1"));
        var autostart = await File.ReadAllTextAsync(
            Path.Combine(RepoRoot, "scripts", "lib", "Antiphon.Nightly", "SessionRunnerAutostart.cs"));

        foreach (var text in new[] { script, autostart })
            text.ShouldNotContain(
                "run --urls",
                customMessage: "autostart must NOT use 'dotnet run' - it creates the kill-on-close job that kills pty-hosts.");
        script.ShouldContain("Invoke-AntiphonCli -Command 'autostart-session-runner'");
        autostart.ShouldContain("\"Antiphon.SessionRunner.exe\"");
        autostart.ShouldContain("BuildProjectDir = runnerDir,");
    }

    [Test]
    public async Task AppHost_launches_the_built_exe_not_dotnet_run()
    {
        var program = await File.ReadAllTextAsync(
            Path.Combine(RepoRoot, "Antiphon.AppHost", "Program.cs"));

        // The session-runner daemon config must not shell out to `dotnet run`.
        program.ShouldNotContain(
            "\"run\", \"--urls\"",
            customMessage: "AppHost must NOT launch the session-runner via 'dotnet run' (kill-on-close job).");
        program.ShouldContain("Antiphon.SessionRunner.exe");
        program.ShouldContain("BuildProjectDir:");
    }

    // Unit 30 (2026-11-22 plan): scripts/run-daemon.ps1 is the launcher of the run-daemon command, whose supervisor
    // (Antiphon.Nightly.DaemonSupervisor) builds before each launch; Antiphon.Tests DaemonSupervisorTests run it.
    [Test]
    public async Task Run_daemon_supports_build_before_launch()
    {
        var runDaemon = await File.ReadAllTextAsync(
            Path.Combine(RepoRoot, "scripts", "run-daemon.ps1"));

        runDaemon.ShouldContain("$BuildProjectDir");
        runDaemon.ShouldContain("'--build-project-dir', $BuildProjectDir");
        var supervisor = await File.ReadAllTextAsync(
            Path.Combine(RepoRoot, "scripts", "lib", "Antiphon.Nightly", "DaemonSupervisor.cs"));
        supervisor.ShouldContain("new List<string> { \"build\", _options.BuildProjectDir, \"-c\", \"Debug\", \"--nologo\" }");
        supervisor.ShouldContain("var command = new List<string> { \"dotnet\" };");
        supervisor.ShouldNotContain("\"run\"");
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Antiphon.sln")))
            dir = dir.Parent;

        return dir?.FullName
            ?? throw new DirectoryNotFoundException("Could not locate repo root (Antiphon.sln) from test base dir.");
    }
}
