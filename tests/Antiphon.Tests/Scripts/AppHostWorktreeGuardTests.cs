using Antiphon.Nightly;
using Antiphon.Tests.Application;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// CARD-0273, unit 31 of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md: a linked worktree cannot control the
/// local AppHost by default. The rows were scripts/test-apphost-main-worktree-guard.ps1's. Real git on a one-commit fixture
/// repository and a linked worktree of it (the driver linked a worktree of this checkout, so it needed core.longpaths and the
/// path arithmetic LongPathsCheckoutScriptTests checked; a fixture repository needs neither, so those LP rows are dropped).
/// The restart, dev-aspire (unit 37) and deploy-local (unit 49, which deleted deploy-local.ps1 and the PowerShell guard
/// scripts/apphost-common.ps1) run in process on AppHostWorld with real git.
/// </summary>
[Category("Integration")]
[ParallelLimiter<GitProcessSpawnLimit>]
public sealed class AppHostWorktreeGuardTests
{
    private sealed class Repos : IDisposable
    {
        public Repos()
        {
            Root = Path.Combine(Path.GetTempPath(), "apphost-worktree-guard-" + Guid.NewGuid().ToString("N"));
            Main = Path.Combine(Root, "main");
            Linked = Path.Combine(Root, "linked");
            NotGit = Path.Combine(Root, "not-a-repository");
            Directory.CreateDirectory(Main);
            Directory.CreateDirectory(NotGit);
            Git(Main, "init", "-q", "-b", "master");
            File.WriteAllText(Path.Combine(Main, "README.md"), "seed\n");
            Git(Main, "add", "README.md");
            Git(Main, "-c", "user.name=c273", "-c", "user.email=c273@example.invalid", "-c", "commit.gpgsign=false", "commit", "-q", "-m", "seed");
            Git(Main, "worktree", "add", "--detach", Linked, "HEAD");
        }

        public string Root { get; }

        public string Main { get; }

        public string Linked { get; }

        public string NotGit { get; }

        public void Dispose() => ReadOnlyDirectory.Delete(Root);

        // Real git in the directory; a nonzero exit throws with its output.
        private static void Git(string directory, params string[] arguments)
        {
            var start = new System.Diagnostics.ProcessStartInfo("git") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
            start.ArgumentList.Add("-C");
            start.ArgumentList.Add(directory);
            foreach (var argument in arguments) start.ArgumentList.Add(argument);
            using var process = System.Diagnostics.Process.Start(start) ?? throw new InvalidOperationException("git did not start");
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            process.WaitForExit();
            if (process.ExitCode != 0) throw new InvalidOperationException("git " + string.Join(" ", arguments) + " exited " + process.ExitCode + ": " + stdout.Result + stderr.Result);
        }
    }

    private static NightlyHost RealGit(AppHostWorld world)
    {
        var host = world.Host();
        return new NightlyHost
        {
            Clock = host.Clock, Git = NightlyNativeGit.Run, Launcher = host.Launcher, Processes = host.Processes, Probe = host.Probe, Out = host.Out,
            Error = host.Error, Trace = host.Trace, Sleep = host.Sleep, Variables = host.Variables,
        };
    }

    [Test]
    public void The_classifier_tells_the_main_worktree_from_a_linked_one()
    {
        using var repos = new Repos();
        using var world = new AppHostWorld();
        var host = RealGit(world);
        var main = AppHostWorktree.Classify(host, world.Files, repos.Main);
        main.Verified.ShouldBeTrue(main.Failure);
        main.IsMainWorktree.ShouldBeTrue("root=" + main.ScriptWorktreeRoot + " main=" + main.MainWorktreeRoot);
        var linked = AppHostWorktree.Classify(host, world.Files, repos.Linked);
        linked.Verified.ShouldBeTrue(linked.Failure);
        linked.IsMainWorktree.ShouldBeFalse();
        linked.MainWorktreeRoot.ShouldBe(main.MainWorktreeRoot, StringCompareShould.IgnoreCase);
        linked.ScriptWorktreeRoot.ShouldBe(AppHostWorktree.Canonical(world.Files, repos.Linked), StringCompareShould.IgnoreCase);

        var notGit = AppHostWorktree.Classify(host, world.Files, repos.NotGit);
        notGit.Verified.ShouldBeFalse();
        notGit.Failure.ShouldStartWith("git -C \"" + repos.NotGit + "\" rev-parse --show-toplevel failed (exit ");
        notGit.GuardMessage(false)[0].ShouldBe("REFUSED: this AppHost command could not verify its Git worktree root.");
    }

    [Test]
    public void Restart_from_a_linked_worktree_refuses_before_any_lock_or_stop()
    {
        using var repos = new Repos();
        using var world = new AppHostWorld();
        var host = RealGit(world);
        var exit = AppHostRestart.Run(AppHostRestartOptions.Parse(["--no-build", "--timeout-sec", "1"], repos.Linked), host, world);
        var text = world.Out.ToString();
        exit.ShouldBe(3, text);
        text.ShouldContain("REFUSED: this AppHost command is rooted in a linked Git worktree.");
        var linked = AppHostWorktree.Classify(host, world.Files, repos.Linked);
        text.ShouldContain(linked.ScriptWorktreeRoot);
        text.ShouldContain(linked.MainWorktreeRoot);
        text.ShouldContain("re-run with --allow-worktree.");
        text.ShouldNotContain("Restarting Antiphon AppHost");
        File.Exists(Path.Combine(repos.Linked, "logs", "apphost.restart.lock")).ShouldBeFalse();
        (world.Count("graceful"), world.Count("stop-tree"), world.Count("launch")).ShouldBe((0, 0, 0));

        var warning = string.Join("\n", linked.GuardMessage(true));
        warning.ShouldStartWith("WARNING: this AppHost command is rooted in a linked Git worktree.");
        warning.ShouldContain(linked.ScriptWorktreeRoot);
        warning.ShouldContain(linked.MainWorktreeRoot);
        warning.ShouldContain("shared local ports are not isolated");
        warning.ShouldContain("--allow-worktree explicitly permits this worktree to affect the shared stack.");
    }

    [Test]
    public void Dev_aspire_from_a_linked_worktree_refuses_before_any_lock_or_docker()
    {
        using var repos = new Repos();
        using var world = new AppHostWorld();
        var host = RealGit(world);
        var exit = DevAspire.Run(new DevAspireOptions { RepoRoot = repos.Linked, WorktreeRoot = world.PathOf("worktrees") }, host, world, world);
        var text = world.Out.ToString();
        exit.ShouldBe(3, text);
        text.ShouldContain("REFUSED: this AppHost command is rooted in a linked Git worktree.");
        text.ShouldContain("re-run with --allow-worktree.");
        File.Exists(Path.Combine(repos.Linked, "logs", "apphost.launch.lock")).ShouldBeFalse();
        world.Trace.ShouldNotContain(row => row.StartsWith("run docker", StringComparison.Ordinal));
        (world.Count("launch"), world.Count("slot"), world.Count("tool")).ShouldBe((0, 0, 0));
        Directory.Exists(world.PathOf("worktrees")).ShouldBeFalse();
    }

    // deploy-local (DeployLocal; unit 49 replaced deploy-local.ps1 and its PowerShell guard, scripts/apphost-common.ps1): the
    // verdict line carries the guard's lines, and nothing reaches Docker, the restart or the verification. The guard's messages
    // name --allow-worktree, the command's flag (the script's -AllowWorktree). (dev-aspire is a C# command since unit 37 and unit
    // 44 deleted its launcher: Dev_aspire_from_a_linked_worktree_refuses_before_any_lock_or_docker.)
    [Test]
    public void Deploy_local_from_a_linked_worktree_refuses_before_postgres_or_the_restart()
    {
        var root = DelegateScriptRunner.RepoRoot;
        foreach (var gone in new[] { "dev-aspire.ps1", Path.Combine("scripts", "deploy-local.ps1"), Path.Combine("scripts", "apphost-common.ps1") })
            File.Exists(Path.Combine(root, gone)).ShouldBeFalse(gone);

        using var repos = new Repos();
        using var world = new AppHostWorld();
        var launcher = new Antiphon.Tests.Scripts.Nightly.RecordingLauncher();
        var real = RealGit(world);
        // No real user secret: APPDATA is unset, so a resolution that ran would fail instead of reading one.
        var host = new NightlyHost
        {
            Clock = real.Clock, Git = real.Git, Launcher = launcher, Processes = real.Processes, Probe = real.Probe, Out = real.Out, Error = real.Error,
            Trace = real.Trace, Sleep = real.Sleep, Variables = _ => null,
        };
        var steps = new List<string>();
        var bound = new DeployLocalSteps
        {
            RestartAppHost = (_, _) => { steps.Add("restart"); return 0; },
            VerifyDevStack = (_, _) => { steps.Add("verify"); return 0; },
        };

        var exit = DeployLocal.Run(DeployLocalOptions.Parse(["--no-build"], repos.Linked), host, world.Files, bound);
        var text = world.Out.ToString();
        exit.ShouldBe(DeployLocal.Refused, text);
        var linked = AppHostWorktree.Classify(host, world.Files, repos.Linked);
        text.ReplaceLineEndings("\n").ShouldBe("DEPLOY VERDICT: refused " + string.Join(" ", linked.GuardMessage(false)) + "\n");
        text.ShouldContain("REFUSED: this AppHost command is rooted in a linked Git worktree.");
        text.ShouldContain(linked.ScriptWorktreeRoot);
        text.ShouldContain(linked.MainWorktreeRoot);
        text.ShouldContain("re-run with --allow-worktree.");
        launcher.Launches.ShouldBeEmpty("no Docker call before the guard");
        steps.ShouldBeEmpty("no restart or verification before the guard");
        File.Exists(Path.Combine(repos.Linked, "logs", "apphost.restart.lock")).ShouldBeFalse();

        var before = world.Out.ToString().Length;
        DeployLocal.Run(DeployLocalOptions.Parse([], repos.NotGit), host, world.Files, bound).ShouldBe(DeployLocal.Refused);
        world.Out.ToString()[before..].ShouldStartWith("DEPLOY VERDICT: refused REFUSED: this AppHost command could not verify its Git worktree root.");
        (launcher.Launches.Count, steps.Count).ShouldBe((0, 0));
    }
}
