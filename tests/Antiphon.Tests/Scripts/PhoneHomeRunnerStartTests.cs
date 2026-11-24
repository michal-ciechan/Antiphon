using Antiphon.Nightly;
using Shouldly;
using TUnit.Core;
using Stage = Antiphon.Tests.Scripts.C1744G3PhoneHomeSupervisionTests.StartStage;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// The start-windows-phone-home-runner command (Antiphon.Nightly.PhoneHomeRunnerStart; unit 36 of
/// docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md deleted scripts/start-windows-phone-home-runner.ps1) in
/// process: the stamped build under a build slot (CARD-0179 R3: a deployed runner is built with -p:SourceRevisionId=&lt;HEAD&gt;,
/// an unreadable HEAD refuses), the refusals, the desktop entry it prints and the homes it hands the runner. Nothing is built
/// or started: the build slot and the foreground run are recorded.
/// </summary>
[Category("Unit")]
public sealed class PhoneHomeRunnerStartTests
{
    private const string Head = "0123456789abcdef0123456789abcdef01234567";

    [Test]
    public void Build_is_stamped_with_head_under_a_build_slot_and_the_runner_runs_in_the_foreground()
    {
        using var stage = new Stage { Exit = 7 };
        var run = stage.Run("--runner-id", "laptop", "--root", stage.RunnerRoot, "--port", "17214", "--capacity", "1");
        run.Exit.ShouldBe(7, "the runner's exit code is the command's: " + run.Text);
        var build = stage.Builds.ShouldHaveSingleItem();
        build.Label.ShouldBe("phone-home-runner-laptop");
        build.Command.ShouldBe(["dotnet", "build", Path.Combine(stage.Checkout, "src", "Antiphon.SessionRunner"), "-nologo", "-v:q", "-p:SourceRevisionId=" + Head]);
        stage.Git.Lines.ShouldBe(["GIT rev-parse HEAD"]);
        var started = stage.Started.ShouldHaveSingleItem();
        started.FileName.ShouldBe(Path.Combine(stage.Checkout, "src", "Antiphon.SessionRunner", "bin", "Debug", "net9.0", "Antiphon.SessionRunner.exe"));
        started.WorkingDirectory.ShouldBe(Path.Combine(stage.Checkout, "src", "Antiphon.SessionRunner"));
        started.Environment.Single(pair => pair.Key == "PhoneHome__Capacity").Value.ShouldBe("1");
        run.Text.ShouldNotContain("WARNING:", Case.Sensitive, "a built runner is stamped");
        var root = WindowsPhoneHome.Slash(Path.GetFullPath(stage.RunnerRoot));
        run.Text.ShouldContain("Phone-home runner 'laptop' -> https://antiphon.desktop.codeperf.net (local http://localhost:17214)");
        run.Text.ShouldContain("Desktop entry PhoneHomeRunner:Runners:laptop must use:");
        run.Text.ShouldContain("  RunnerWorkspace  = " + root + "/work");
        run.Text.ShouldContain("  RunnerRepository = " + root + "/work/repos/antiphon");
        run.Text.ShouldContain("  RawExeAllowList  = " + stage.PwshPath);
        run.Text.ShouldContain("  executable claude = " + stage.ClaudePath);
        foreach (var directory in new[] { "work/repos", "state/session-runner", "state/logs", "state/pty-hosts" })
            Directory.Exists(Path.Combine(stage.RunnerRoot, directory)).ShouldBeTrue(directory);
    }

    [Test]
    public void Configured_homes_win_over_the_profile()
    {
        using var stage = new Stage();
        stage.Vars["CLAUDE_CONFIG_DIR"] = @"D:\claude-home";
        stage.Vars["GROK_HOME"] = @"D:\grok-home";
        stage.Run("--runner-id", "laptop", "--root", stage.RunnerRoot, "--no-build").Exit.ShouldBe(0);
        var environment = stage.Started.ShouldHaveSingleItem().Environment.ToDictionary(pair => pair.Key, pair => pair.Value);
        (environment["PhoneHome__ClaudeHome"], environment["PhoneHome__CodexHome"], environment["PhoneHome__GrokHome"])
            .ShouldBe((@"D:\claude-home", Path.Combine(stage.Home, ".codex"), @"D:\grok-home"));
        stage.Builds.ShouldBeEmpty("--no-build");
    }

    [Test]
    [Arguments("local")]
    [Arguments("Desktop")]
    public void Reserved_runner_ids_are_refused(string id)
    {
        using var stage = new Stage();
        var run = stage.Run("--runner-id", id, "--root", stage.RunnerRoot);
        run.Exit.ShouldBe(1);
        run.Text.Trim().ShouldBe("start-windows-phone-home-runner: RunnerId '" + id + "' is reserved for the desktop runner.");
        (stage.Builds.Count, stage.Started.Count).ShouldBe((0, 0));
    }

    [Test]
    public void Refusals_start_nothing()
    {
        using (var linux = new Stage { Windows = false })
        {
            var run = linux.Run("--runner-id", "laptop", "--root", linux.RunnerRoot);
            (run.Exit, linux.Started.Count).ShouldBe((1, 0));
            run.Text.ShouldContain("use docker-compose.server2-runner.yml on Linux");
        }
        using (var secretless = new Stage())
        {
            File.Delete(Path.Combine(secretless.RunnerRoot, "secrets", "phone-home"));
            var run = secretless.Run("--runner-id", "laptop", "--root", secretless.RunnerRoot);
            (run.Exit, secretless.Builds.Count).ShouldBe((1, 0));
            run.Text.ShouldContain("Missing shared secret file " + Path.Combine(secretless.RunnerRoot, "secrets", "phone-home")
                + ". Write the value of PhoneHomeRunner:Runners:laptop:SharedSecret into it.");
        }
        using (var unstamped = new Stage())
        {
            unstamped.Git.Reply("rev-parse", new NightlyGitResult(128, "fatal: not a git repository"));
            var run = unstamped.Run("--runner-id", "laptop", "--root", unstamped.RunnerRoot);
            (run.Exit, unstamped.Builds.Count, unstamped.Started.Count).ShouldBe((1, 0, 0));
            run.Text.ShouldContain("git rev-parse HEAD failed; refusing an unstamped runner build.");
        }
        using (var failed = new Stage { BuildExit = 4 })
        {
            var run = failed.Run("--runner-id", "laptop", "--root", failed.RunnerRoot);
            (run.Exit, failed.Started.Count).ShouldBe((1, 0));
            run.Text.ShouldContain("Runner build failed (4).");
        }
        using (var unbuilt = new Stage())
        {
            var exe = Path.Combine(unbuilt.Checkout, "src", "Antiphon.SessionRunner", "bin", "Debug", "net9.0", "Antiphon.SessionRunner.exe");
            File.Delete(exe);
            var run = unbuilt.Run("--runner-id", "laptop", "--root", unbuilt.RunnerRoot, "--no-build");
            (run.Exit, unbuilt.Started.Count).ShouldBe((1, 0));
            run.Text.ShouldContain("Runner not built: " + exe);
        }
    }

    [Test]
    public void Options_keep_the_script_defaults_and_require_a_runner_id()
    {
        var options = PhoneHomeRunnerStartOptions.Parse(["--runner-id", "laptop"], "checkout");
        (options.ServerOrigin, options.Root, options.Port, options.Capacity, options.NoBuild, options.RepoRoot)
            .ShouldBe(("https://antiphon.desktop.codeperf.net", @"C:\Antiphon\runner", 17214, 2, false, "checkout"));
        Should.Throw<NightlyUsageException>(() => PhoneHomeRunnerStartOptions.Parse([], "checkout"));
        Should.Throw<NightlyUsageException>(() => PhoneHomeRunnerStartOptions.Parse(["--runner-id", "x", "--port", "many"], "checkout"));
    }
}
