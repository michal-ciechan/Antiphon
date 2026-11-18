using System.Diagnostics;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.TestHelpers;

/// <summary>
/// CARD-1981. A fixture that leaves its scratch directory fails. The witnesses call the
/// producers' own cleanup and then remove anything still left so the assertion itself
/// does not add a /tmp entry.
/// </summary>
[Category("Unit")]
[Timeout(45_000)]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class OwnedTempLeakTests
{
    private bool _hookOwnsCleanup;
    private string? _leftForHook;

    [Test]
    public void C459_release_removes_the_scratch_directory()
    {
        var root = Antiphon.Tests.Application.TaskWorktreeRetirementTests.RetirementScratch.Create(Guid.NewGuid());
        try
        {
            Antiphon.Tests.Application.TaskWorktreeRetirementTests.RetirementScratch.Release(root);
            Directory.Exists(root).ShouldBeFalse();
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task Bridge_temp_root_is_removed_when_cleanup_throws()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(),
            "antiphon-bridge-queue-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            await Should.ThrowAsync<InvalidOperationException>(() =>
                BridgeQueueHarness.TempRootCleanup.FinishAsync(root, preserveDatabase: false,
                    () => throw new InvalidOperationException("db")));
            Directory.Exists(root).ShouldBeFalse();
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task Bridge_temp_root_stays_when_the_database_is_preserved()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(),
            "antiphon-bridge-queue-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            await BridgeQueueHarness.TempRootCleanup.FinishAsync(root, preserveDatabase: true,
                () => Task.CompletedTask);
            Directory.Exists(root).ShouldBeTrue();
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public void Owned_root_release_removes_the_directory()
    {
        var root = OwnedTempRoot.CreateOwned();
        OwnedTempRoot.Release(root);
        Directory.Exists(root).ShouldBeFalse();
    }

    [Test]
    public void Hook_removes_a_root_the_test_left_behind()
    {
        _hookOwnsCleanup = true;
        _leftForHook = OwnedTempRoot.CreateOwned();
        Directory.Exists(_leftForHook).ShouldBeTrue();
    }

    [Test]
    public void Review_scratch_remove_deletes_an_owned_container()
    {
        var parent = OwnedTempRoot.CreateOwned();
        try
        {
            var (task, owner) = OwnerUnder(parent);
            var container = RunScript(0, "scripts/new-review-scratch.ps1",
                "-TaskId", task.ToString(), "-OwnerWorktree", owner, "-TempRoot", parent).Trim();
            Directory.Exists(container).ShouldBeTrue();
            RunScript(0, "scripts/remove-review-scratch.ps1", "-Root", container, "-TempRoot", parent);
            Directory.Exists(container).ShouldBeFalse();
        }
        finally
        {
            OwnedTempRoot.Release(parent);
        }
    }

    [Test]
    public void Review_scratch_remove_keeps_a_container_without_a_marker()
    {
        var parent = OwnedTempRoot.CreateOwned();
        var fake = Path.Combine(parent, "antiphon-review-0123abcd");
        Directory.CreateDirectory(fake);
        try
        {
            var exit = RunScriptRaw("scripts/remove-review-scratch.ps1", "-Root", fake, "-TempRoot", parent);
            exit.Code.ShouldNotBe(0);
            Directory.Exists(fake).ShouldBeTrue();
        }
        finally
        {
            OwnedTempRoot.Release(parent);
        }
    }

    [Test]
    public void Review_scratch_remove_keeps_the_container_when_worktree_remove_fails()
    {
        var parent = OwnedTempRoot.CreateOwned();
        try
        {
            var (task, owner) = OwnerUnder(parent);
            var container = RunScript(0, "scripts/new-review-scratch.ps1",
                "-TaskId", task.ToString(), "-OwnerWorktree", owner, "-TempRoot", parent).Trim();
            var child = Path.Combine(container, "master");
            Directory.CreateDirectory(child);
            File.WriteAllText(Path.Combine(child, ".git"), "gitdir: /nowhere\n");
            var exit = RunScriptRaw("scripts/remove-review-scratch.ps1", "-Root", container, "-TempRoot", parent);
            exit.Code.ShouldNotBe(0);
            Directory.Exists(container).ShouldBeTrue();
        }
        finally
        {
            OwnedTempRoot.Release(parent);
        }
    }

    [Test]
    public void Review_scratch_remove_refuses_a_symlinked_foreign_worktree()
    {
        var parent = OwnedTempRoot.CreateOwned();
        try
        {
            var repo = InitRepo(parent);
            var outside = Path.Combine(parent, "outside-wt");
            Git(repo, "worktree", "add", "-q", "--detach", outside, "HEAD");
            var readme = Path.Combine(outside, "README");
            var (task, owner) = OwnerUnder(parent);
            var container = RunScript(0, "scripts/new-review-scratch.ps1",
                "-TaskId", task.ToString(), "-OwnerWorktree", owner, "-TempRoot", parent).Trim();
            Directory.CreateSymbolicLink(Path.Combine(container, "link"), outside);
            var exit = RunScriptRaw("scripts/remove-review-scratch.ps1", "-Root", container, "-TempRoot", parent);
            File.Exists(readme).ShouldBeTrue("foreign worktree must stay");
            File.ReadAllText(readme).ShouldBe("seed\n");
            exit.Code.ShouldNotBe(0);
            exit.Text.ShouldContain("Refusing reparse point inside the scratch container");
            Directory.Exists(container).ShouldBeTrue();
            Directory.Exists(outside).ShouldBeTrue();
        }
        finally
        {
            OwnedTempRoot.Release(parent);
        }
    }

    [Test]
    public void Review_scratch_remove_refuses_a_dirty_nested_worktree()
    {
        var parent = OwnedTempRoot.CreateOwned();
        try
        {
            var repo = InitRepo(parent);
            var (task, owner) = OwnerUnder(parent);
            var container = RunScript(0, "scripts/new-review-scratch.ps1",
                "-TaskId", task.ToString(), "-OwnerWorktree", owner, "-TempRoot", parent).Trim();
            var nested = Path.Combine(container, "sub", "wt");
            Git(repo, "worktree", "add", "-q", "--detach", nested, "HEAD");
            var dirty = Path.Combine(nested, "uncommitted.txt");
            File.WriteAllText(dirty, "keep-me\n");
            var exit = RunScriptRaw("scripts/remove-review-scratch.ps1", "-Root", container, "-TempRoot", parent);
            File.Exists(dirty).ShouldBeTrue("uncommitted file must stay");
            File.ReadAllText(dirty).ShouldBe("keep-me\n");
            exit.Code.ShouldNotBe(0);
            exit.Text.ShouldContain("Refusing nested git metadata inside the scratch container");
            Directory.Exists(container).ShouldBeTrue();
            Git(repo, "worktree", "list", "--porcelain").ShouldContain(nested);
        }
        finally
        {
            OwnedTempRoot.Release(parent);
        }
    }

    [After(Test)]
    public void Owned_roots_are_gone()
    {
        if (!_hookOwnsCleanup || _leftForHook is null)
            return;
        Directory.Exists(_leftForHook).ShouldBeTrue();
        OwnedTempRoot.Release(_leftForHook);
        Directory.Exists(_leftForHook).ShouldBeFalse();
    }

    private static (Guid Task, string Owner) OwnerUnder(string parent)
    {
        var task = Guid.NewGuid();
        var owner = Path.Combine(parent, "worktrees", "task-" + task.ToString("N")[..8]);
        Directory.CreateDirectory(owner);
        return (task, owner);
    }

    private static string RunScript(int expected, string relative, params string[] args)
    {
        var exit = RunScriptRaw(relative, args);
        exit.Code.ShouldBe(expected, exit.Text);
        return exit.Text;
    }

    private static (int Code, string Text) RunScriptRaw(string relative, params string[] args)
    {
        var psi = new ProcessStartInfo("pwsh")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        psi.ArgumentList.Add("-NoProfile");
        psi.ArgumentList.Add("-NonInteractive");
        psi.ArgumentList.Add("-File");
        psi.ArgumentList.Add(Path.Combine(RepoRoot(), relative));
        foreach (var arg in args)
            psi.ArgumentList.Add(arg);
        using var process = Process.Start(psi) ?? throw new InvalidOperationException("pwsh did not start");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(40_000))
        {
            try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            throw new TimeoutException(relative);
        }
        return (process.ExitCode, stdout.GetAwaiter().GetResult() + stderr.GetAwaiter().GetResult());
    }

    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "Antiphon.sln")))
                return dir.FullName;
        throw new DirectoryNotFoundException("Could not locate Antiphon.sln.");
    }

    private static string InitRepo(string parent)
    {
        var repo = Path.Combine(parent, "repo-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(repo);
        Git(repo, "init", "-q", "-b", "main");
        Git(repo, "config", "user.email", "card-1981@example.invalid");
        Git(repo, "config", "user.name", "card-1981");
        File.WriteAllText(Path.Combine(repo, "README"), "seed\n");
        Git(repo, "add", "README");
        Git(repo, "commit", "-q", "-m", "seed");
        return repo;
    }

    private static string Git(string cwd, params string[] args)
    {
        var psi = new ProcessStartInfo("git")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = cwd,
        };
        psi.Environment["GIT_TERMINAL_PROMPT"] = "0";
        foreach (var key in new[] { "GIT_DIR", "GIT_WORK_TREE", "GIT_INDEX_FILE" })
            psi.Environment.Remove(key);
        foreach (var arg in args)
            psi.ArgumentList.Add(arg);
        using var process = Process.Start(psi) ?? throw new InvalidOperationException("git did not start");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(40_000))
        {
            try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            throw new TimeoutException("git " + string.Join(' ', args));
        }
        var text = stdout.GetAwaiter().GetResult() + stderr.GetAwaiter().GetResult();
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"git {string.Join(' ', args)} -> {process.ExitCode}: {text}");
        return text;
    }
}
