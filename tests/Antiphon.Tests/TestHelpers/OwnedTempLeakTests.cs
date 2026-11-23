using System.Diagnostics;
using System.IO.Abstractions;
using Antiphon.Nightly;
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
            var container = NewScratch(task, owner, parent);
            Directory.Exists(container).ShouldBeTrue();
            var removed = RemoveScratch(container, parent);
            removed.Code.ShouldBe(0, removed.Text);
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
            var exit = RemoveScratch(fake, parent);
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
            var container = NewScratch(task, owner, parent);
            var child = Path.Combine(container, "master");
            Directory.CreateDirectory(child);
            File.WriteAllText(Path.Combine(child, ".git"), "gitdir: /nowhere\n");
            var exit = RemoveScratch(container, parent);
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
            var container = NewScratch(task, owner, parent);
            Directory.CreateSymbolicLink(Path.Combine(container, "link"), outside);
            var exit = RemoveScratch(container, parent);
            File.Exists(readme).ShouldBeTrue("foreign worktree must stay");
            // Git for Windows checks the committed blob out as CRLF. The bytes must still be the seed.
            File.ReadAllText(readme).Replace("\r\n", "\n").ShouldBe("seed\n");
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
            var container = NewScratch(task, owner, parent);
            var nested = Path.Combine(container, "sub", "wt");
            Git(repo, "worktree", "add", "-q", "--detach", nested, "HEAD");
            var dirty = Path.Combine(nested, "uncommitted.txt");
            File.WriteAllText(dirty, "keep-me\n");
            var exit = RemoveScratch(container, parent);
            File.Exists(dirty).ShouldBeTrue("uncommitted file must stay");
            File.ReadAllText(dirty).ShouldBe("keep-me\n");
            exit.Code.ShouldNotBe(0);
            exit.Text.ShouldContain("Refusing nested git metadata inside the scratch container");
            Directory.Exists(container).ShouldBeTrue();
            // git worktree list prints '/' on Windows, including when the temp path is the long form.
            NormalizeWorktreePorcelain(Git(repo, "worktree", "list", "--porcelain"))
                .ShouldContain(FullSlashPath(nested));
        }
        finally
        {
            OwnedTempRoot.Release(parent);
        }
    }

    [Test]
    public void Review_scratch_remove_removes_a_linked_worktree_child()
    {
        var parent = OwnedTempRoot.CreateOwned();
        try
        {
            var repo = InitRepo(parent);
            var (task, owner) = OwnerUnder(parent);
            var container = NewScratch(task, owner, parent);
            var child = Path.Combine(container, "master");
            Git(repo, "worktree", "add", "-q", "--detach", child, "HEAD");
            var removed = RemoveScratch(container, parent);
            removed.Code.ShouldBe(0, removed.Text);
            Directory.Exists(container).ShouldBeFalse();
            // git worktree list prints '/' on Windows; the command compares full paths, so it named the child and removed it.
            NormalizeWorktreePorcelain(Git(repo, "worktree", "list", "--porcelain")).ShouldNotContain(FullSlashPath(child));
            File.Exists(Path.Combine(repo, "README")).ShouldBeTrue("the owning repository stays");
        }
        finally
        {
            OwnedTempRoot.Release(parent);
        }
    }

    [Test]
    public void Review_scratch_remove_refuses_a_standalone_clone()
    {
        var parent = OwnedTempRoot.CreateOwned();
        try
        {
            var repo = InitRepo(parent);
            var (task, owner) = OwnerUnder(parent);
            var container = NewScratch(task, owner, parent);
            Git(parent, "clone", "-q", repo, Path.Combine(container, "master"));
            var exit = RemoveScratch(container, parent);
            exit.Code.ShouldBe(1);
            exit.Text.Trim().ShouldBe("Refusing standalone clone; container kept.");
            Directory.Exists(Path.Combine(container, "master", ".git")).ShouldBeTrue();
        }
        finally
        {
            OwnedTempRoot.Release(parent);
        }
    }

    [Test]
    public void Review_scratch_remove_refuses_a_foreign_name_parent_or_missing_container()
    {
        var parent = OwnedTempRoot.CreateOwned();
        try
        {
            RemoveScratch(Path.Combine(parent, "not-a-container"), parent).Text.Trim().ShouldBe("Not a review scratch container.");
            RemoveScratch(Path.Combine(parent, "nested", "antiphon-review-0123abcd"), parent).Text.Trim().ShouldBe("Container must sit directly under the temp root.");
            var missing = RemoveScratch(Path.Combine(parent, "antiphon-review-0123abcd"), parent);
            (missing.Code, missing.Text.Trim()).ShouldBe((1, "Scratch container does not exist."));
        }
        finally
        {
            OwnedTempRoot.Release(parent);
        }
    }

    [Test]
    public void Review_scratch_new_writes_the_owner_marker_and_refuses_a_foreign_owner_or_an_existing_container()
    {
        var parent = OwnedTempRoot.CreateOwned();
        try
        {
            var (task, owner) = OwnerUnder(parent);
            NewScratchRaw(Guid.NewGuid(), owner, parent).Text.Trim().ShouldBe("Task id must match its owner worktree.");
            var loose = Path.Combine(parent, "elsewhere", Path.GetFileName(owner));
            Directory.CreateDirectory(loose);
            NewScratchRaw(task, loose, parent).Text.Trim().ShouldBe("Owner must be a task or workspace-slot directory under the runner worktrees root.");
            NewScratchRaw(task, Path.Combine(parent, "worktrees", "task-0badc0de"), parent).Text.Trim().ShouldBe("Owner worktree must exist.");

            var container = NewScratch(task, owner, parent);
            Path.GetFileName(container).ShouldBe("antiphon-review-" + task.ToString("N")[..8]);
            Path.GetDirectoryName(container).ShouldBe(Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar));
            File.ReadAllText(Path.Combine(container, ".antiphon-scratch-owner")).ShouldBe(Path.GetFullPath(owner).Replace('\\', '/'));
            var again = NewScratchRaw(task, owner, parent);
            (again.Code, again.Text.Trim()).ShouldBe((1, "Scratch container already exists; do not replace its ownership."));

            // A workspace-slot owner takes any task id.
            var slot = Path.Combine(parent, "worktrees", "ws-slot-1");
            Directory.CreateDirectory(slot);
            NewScratchRaw(Guid.NewGuid(), slot, parent).Code.ShouldBe(0);

            File.ReadAllBytes(Path.Combine(RepoRoot(), "scripts", "lib", "Antiphon.Nightly", "ReviewScratch.cs")).Count(b => b > 127).ShouldBe(0, "ASCII only");
            foreach (var script in new[] { "new-review-scratch.ps1", "remove-review-scratch.ps1" })
                File.Exists(Path.Combine(RepoRoot(), "scripts", script)).ShouldBeFalse(script + " is ported to C#");
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

    // The review scratch commands (Antiphon.Nightly.ReviewScratch, which replaced scripts/new-review-scratch.ps1 and
    // scripts/remove-review-scratch.ps1 in unit 25 of the 2026-11-22 plan) run in process: their flags parsed as the CLI
    // parses them, the production host (real git) and the real file system under the owned temporary root.
    private static string NewScratch(Guid task, string owner, string temp)
    {
        var created = NewScratchRaw(task, owner, temp);
        created.Code.ShouldBe(0, created.Text);
        return created.Text.Trim();
    }

    private static (int Code, string Text) NewScratchRaw(Guid task, string owner, string temp)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var options = NewReviewScratchOptions.Parse(["--task-id", task.ToString(), "--owner-worktree", owner, "--temp-root", temp], Path.GetTempPath());
        var code = ReviewScratch.New(options, NightlyHost.Production(output, error), new FileSystem());
        return (code, output.ToString() + error.ToString());
    }

    private static (int Code, string Text) RemoveScratch(string container, string temp)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var options = RemoveReviewScratchOptions.Parse(["--root", container, "--temp-root", temp], Path.GetTempPath());
        var code = ReviewScratch.Remove(options, NightlyHost.Production(output, error), new FileSystem());
        return (code, output.ToString() + error.ToString());
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

    private static string FullSlashPath(string path) =>
        Path.GetFullPath(path).Replace('\\', '/');

    private static string NormalizeWorktreePorcelain(string porcelain)
    {
        var lines = porcelain.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            const string prefix = "worktree ";
            if (!lines[i].StartsWith(prefix, StringComparison.Ordinal))
                continue;
            var raw = lines[i][prefix.Length..];
            if (raw.Length == 0)
                continue;
            lines[i] = prefix + Path.GetFullPath(raw).Replace('\\', '/');
        }

        return string.Join('\n', lines);
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
