using System.Diagnostics;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Infrastructure.Git;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;
using TUnit.Core.Exceptions;

namespace Antiphon.Tests.Infrastructure;

[Category("Unit")]
public class WorktreeManagerTests
{
    [Test]
    public void ParseWorktreeList_captures_locked_reason_and_prunable_reason()
    {
        const string porcelain = """
            worktree /repo
            HEAD abcdef
            branch refs/heads/master

            worktree /trees/card-task-1
            HEAD 111
            branch refs/heads/feat/card-task-1
            locked initializing

            worktree /trees/card-task-2
            HEAD 222
            branch refs/heads/feat/card-task-2
            prunable gitdir file points to non-existent location

            """;

        var entries = WorktreeManager.ParseWorktreeList(porcelain);
        entries.Count.ShouldBe(3);

        var locked = entries.Single(e => e.Path == "/trees/card-task-1");
        locked.Locked.ShouldBeTrue();
        locked.LockReason.ShouldBe("initializing");
        locked.Prunable.ShouldBeFalse();

        var prunable = entries.Single(e => e.Path == "/trees/card-task-2");
        prunable.Prunable.ShouldBeTrue();
        prunable.PrunableReason.ShouldBe("gitdir file points to non-existent location");
        prunable.Locked.ShouldBeFalse();
    }

    [Test]
    public void ParseWorktreeList_captures_a_bare_locked_line()
    {
        const string porcelain = """
            worktree /trees/card-task-3
            HEAD 333
            branch refs/heads/feat/card-task-3
            locked

            """;

        var entries = WorktreeManager.ParseWorktreeList(porcelain);
        entries.ShouldHaveSingleItem();
        entries[0].Locked.ShouldBeTrue();
        entries[0].LockReason.ShouldBe("");
        entries[0].Prunable.ShouldBeFalse();
    }

    [Test]
    public void IsAlreadyUnregisteredWorktreeError_matches_only_the_not_a_working_tree_fatal()
    {
        WorktreeManager.IsAlreadyUnregisteredWorktreeError(
            "fatal: 'C:\\trees\\card-x' is not a working tree\n").ShouldBeTrue();
        WorktreeManager.IsAlreadyUnregisteredWorktreeError(
            "fatal: 'C:/trees/card-x' is not a working tree").ShouldBeTrue();
        WorktreeManager.IsAlreadyUnregisteredWorktreeError(
            "fatal: cannot remove a locked working tree;\nuse 'remove -f -f' to override or unlock first\n")
            .ShouldBeFalse();
        WorktreeManager.IsAlreadyUnregisteredWorktreeError(
            "fatal: validation failed, cannot remove working tree: 'C:\\trees\\card-x/.git' does not exist\n")
            .ShouldBeFalse();
        WorktreeManager.IsAlreadyUnregisteredWorktreeError(
            "error: failed to delete '...': Permission denied\n").ShouldBeFalse();
        WorktreeManager.IsAlreadyUnregisteredWorktreeError("").ShouldBeFalse();
    }
}

[Category("Unit")]
public class WorktreeManagerSafetyTests
{
    [Test]
    public void Sanitise_rejects_path_traversal_and_special_chars()
    {
        foreach (var cardId in new[] { "../x", "x/../y", "x;y", "x y", "x\\y", "x/y", "x\0y", "#3" })
        {
            Should.Throw<ValidationException>(() => WorktreeManager.ValidateCardId(cardId));
        }
    }

    [Test]
    public void Sanitise_accepts_safe_identifier_and_builds_expected_names()
    {
        WorktreeManager.ValidateCardId("E03_001-alpha.2").ShouldBe("E03_001-alpha.2");
        WorktreeManager.BuildDirectoryName("E03_001-alpha.2").ShouldBe("card-E03_001-alpha.2");
        WorktreeManager.BuildBranchName("E03_001-alpha.2").ShouldBe("feat/card-E03_001-alpha.2");
    }

    [Test]
    public void Worktree_path_confinement_rejects_resolved_escape()
    {
        var root = Path.Combine(Path.GetTempPath(), $"antiphon-root-{Guid.NewGuid():N}");
        var underRoot = Path.Combine(root, "card-E03");
        var escaped = Path.Combine(root, "..", $"antiphon-escape-{Guid.NewGuid():N}");

        WorktreeManager.IsPathUnderRoot(underRoot, root).ShouldBeTrue();
        WorktreeManager.IsPathUnderRoot(escaped, root).ShouldBeFalse();
    }
}

[Category("GitIntegration")]
public class WorktreeManagerGitIntegrationTests
{
    [Test]
    public async Task WorktreeManager_create_produces_worktree_under_root()
    {
        await GitTestEnvironment.SkipIfGitUnavailableAsync();
        var env = await GitTestEnvironment.CreateAsync();

        try
        {
            var manager = BuildManager(env.WorktreeRoot);

            var worktree = await manager.CreateAsync(env.RepoPath, "E03-001", "HEAD", CancellationToken.None);

            WorktreeManager.IsPathUnderRoot(worktree.Path, env.WorktreeRoot).ShouldBeTrue();
            Directory.Exists(worktree.Path).ShouldBeTrue();
            worktree.Branch.ShouldBe("feat/card-E03-001");
            (await GitTestEnvironment.RunGitAsync(worktree.Path, "branch", "--show-current"))
                .Trim()
                .ShouldBe("feat/card-E03-001");

            var metadataDir = Path.Combine(env.WorktreeRoot, ".antiphon", "worktrees");
            Directory.EnumerateFiles(metadataDir, "*.json").Count().ShouldBe(1);
        }
        finally
        {
            env.Dispose();
        }
    }

    [Test]
    public async Task WorktreeManager_list_returns_only_worktrees_under_root()
    {
        await GitTestEnvironment.SkipIfGitUnavailableAsync();
        var env = await GitTestEnvironment.CreateAsync();
        var outsideRoot = Path.Combine(env.TempRoot, "outside-worktree");

        try
        {
            var manager = BuildManager(env.WorktreeRoot);
            var managed = await manager.CreateAsync(env.RepoPath, "E03-002", "HEAD", CancellationToken.None);
            await GitTestEnvironment.RunGitAsync(
                env.RepoPath,
                "worktree",
                "add",
                "-b",
                "feat/card-outside",
                outsideRoot,
                "HEAD");

            var listed = await manager.ListAsync(env.RepoPath, CancellationToken.None);

            listed.Select(w => w.Path).ShouldContain(managed.Path);
            listed.Select(w => w.Path).ShouldNotContain(Path.GetFullPath(outsideRoot));
        }
        finally
        {
            await GitTestEnvironment.TryRunGitAsync(env.RepoPath, "worktree", "remove", "--force", outsideRoot);
            await GitTestEnvironment.TryRunGitAsync(env.RepoPath, "branch", "-D", "feat/card-outside");
            env.Dispose();
        }
    }

    [Test]
    public async Task WorktreeManager_remove_deletes_worktree_and_branch()
    {
        await GitTestEnvironment.SkipIfGitUnavailableAsync();
        var env = await GitTestEnvironment.CreateAsync();

        try
        {
            var manager = BuildManager(env.WorktreeRoot);
            await GitTestEnvironment.RunGitAsync(env.RepoPath, "branch", "unrelated");
            var worktree = await manager.CreateAsync(env.RepoPath, "E03-003", "HEAD", CancellationToken.None);

            await manager.RemoveAsync(env.RepoPath, worktree.Path, CancellationToken.None);

            Directory.Exists(worktree.Path).ShouldBeFalse();
            (await GitTestEnvironment.RunGitAsync(env.RepoPath, "worktree", "list", "--porcelain"))
                .ShouldNotContain(worktree.Path);
            (await GitTestEnvironment.RunGitAsync(env.RepoPath, "branch", "--list", "feat/card-E03-003"))
                .ShouldBe(string.Empty);
            (await GitTestEnvironment.RunGitAsync(env.RepoPath, "branch", "--list", "unrelated"))
                .ShouldContain("unrelated");
        }
        finally
        {
            env.Dispose();
        }
    }

    [Test]
    public async Task WorktreeManager_remove_treats_unregistered_leftover_directory_as_already_clean()
    {
        await GitTestEnvironment.SkipIfGitUnavailableAsync();
        var env = await GitTestEnvironment.CreateAsync();

        try
        {
            var manager = BuildManager(env.WorktreeRoot);
            var worktree = await manager.CreateAsync(env.RepoPath, "E03-007", "HEAD", CancellationToken.None);

            GitTestEnvironment.UnregisterWorktreeLeavingDirectory(worktree.Path);
            Directory.Exists(worktree.Path).ShouldBeTrue();

            var refused = await GitTestEnvironment.RunGitCaptureAsync(
                env.RepoPath, "worktree", "remove", "--force", worktree.Path);
            refused.ExitCode.ShouldBe(128);
            refused.Stderr.ShouldContain("is not a working tree");

            await manager.RemoveAsync(env.RepoPath, worktree.Path, CancellationToken.None);

            Directory.Exists(worktree.Path).ShouldBeFalse();
            (await GitTestEnvironment.RunGitAsync(env.RepoPath, "worktree", "list", "--porcelain"))
                .ShouldNotContain(worktree.Path);
            (await GitTestEnvironment.RunGitAsync(env.RepoPath, "branch", "--list", "feat/card-E03-007"))
                .ShouldBe(string.Empty);
            var metadataDir = Path.Combine(env.WorktreeRoot, ".antiphon", "worktrees");
            Directory.EnumerateFiles(metadataDir, "*.json").ShouldBeEmpty();
        }
        finally
        {
            env.Dispose();
        }
    }

    [Test]
    public async Task WorktreeManager_remove_still_throws_when_the_worktree_is_locked()
    {
        await GitTestEnvironment.SkipIfGitUnavailableAsync();
        var env = await GitTestEnvironment.CreateAsync();

        try
        {
            var manager = BuildManager(env.WorktreeRoot);
            var worktree = await manager.CreateAsync(env.RepoPath, "E03-008", "HEAD", CancellationToken.None);
            await GitTestEnvironment.RunGitAsync(env.RepoPath, "worktree", "lock", worktree.Path);

            var ex = await Should.ThrowAsync<InvalidOperationException>(() =>
                manager.RemoveAsync(env.RepoPath, worktree.Path, CancellationToken.None));

            ex.Message.ShouldContain("locked working tree");
            Directory.Exists(worktree.Path).ShouldBeTrue();
        }
        finally
        {
            env.Dispose();
        }
    }

    [Test]
    public async Task WorktreeManager_create_throws_when_branch_or_path_exists()
    {
        await GitTestEnvironment.SkipIfGitUnavailableAsync();
        var env = await GitTestEnvironment.CreateAsync();

        try
        {
            var manager = BuildManager(env.WorktreeRoot);
            await manager.CreateAsync(env.RepoPath, "E03-004", "HEAD", CancellationToken.None);

            await Should.ThrowAsync<ConflictException>(() =>
                manager.CreateAsync(env.RepoPath, "E03-004", "HEAD", CancellationToken.None));
        }
        finally
        {
            env.Dispose();
        }
    }

    [Test]
    public async Task WorktreeJanitor_prunes_stale_worktrees()
    {
        await GitTestEnvironment.SkipIfGitUnavailableAsync();
        var env = await GitTestEnvironment.CreateAsync();
        var clock = new MutableTimeProvider(new DateTimeOffset(2026, 5, 1, 12, 0, 0, TimeSpan.Zero));

        try
        {
            var manager = BuildManager(env.WorktreeRoot, clock, staleAfterDays: 7);
            var worktree = await manager.CreateAsync(env.RepoPath, "E03-005", "HEAD", CancellationToken.None);

            clock.SetUtcNow(new DateTimeOffset(2026, 5, 10, 12, 0, 0, TimeSpan.Zero));
            var pruned = await manager.PruneStaleAsync(CancellationToken.None);

            pruned.ShouldBe(1);
            Directory.Exists(worktree.Path).ShouldBeFalse();
            (await GitTestEnvironment.RunGitAsync(env.RepoPath, "branch", "--list", "feat/card-E03-005"))
                .ShouldBe(string.Empty);
        }
        finally
        {
            env.Dispose();
        }
    }

    [Test]
    public async Task WorktreeJanitor_prunes_stale_unregistered_leftover_directory()
    {
        await GitTestEnvironment.SkipIfGitUnavailableAsync();
        var env = await GitTestEnvironment.CreateAsync();
        var clock = new MutableTimeProvider(new DateTimeOffset(2026, 5, 1, 12, 0, 0, TimeSpan.Zero));

        try
        {
            var manager = BuildManager(env.WorktreeRoot, clock, staleAfterDays: 7);
            var worktree = await manager.CreateAsync(env.RepoPath, "E03-009", "HEAD", CancellationToken.None);

            GitTestEnvironment.UnregisterWorktreeLeavingDirectory(worktree.Path);
            Directory.Exists(worktree.Path).ShouldBeTrue();

            clock.SetUtcNow(new DateTimeOffset(2026, 5, 10, 12, 0, 0, TimeSpan.Zero));
            var pruned = await manager.PruneStaleAsync(CancellationToken.None);

            pruned.ShouldBe(1);
            Directory.Exists(worktree.Path).ShouldBeFalse();
            (await GitTestEnvironment.RunGitAsync(env.RepoPath, "branch", "--list", "feat/card-E03-009"))
                .ShouldBe(string.Empty);
            var metadataDir = Path.Combine(env.WorktreeRoot, ".antiphon", "worktrees");
            Directory.EnumerateFiles(metadataDir, "*.json").ShouldBeEmpty();
        }
        finally
        {
            env.Dispose();
        }
    }

    [Test]
    public async Task WorktreeManager_touch_updates_last_touched_metadata()
    {
        await GitTestEnvironment.SkipIfGitUnavailableAsync();
        var env = await GitTestEnvironment.CreateAsync();
        var clock = new MutableTimeProvider(new DateTimeOffset(2026, 5, 1, 12, 0, 0, TimeSpan.Zero));

        try
        {
            var manager = BuildManager(env.WorktreeRoot, clock);
            var worktree = await manager.CreateAsync(env.RepoPath, "E03-006", "HEAD", CancellationToken.None);

            var touchedAt = new DateTimeOffset(2026, 5, 2, 12, 0, 0, TimeSpan.Zero);
            clock.SetUtcNow(touchedAt);
            await manager.TouchAsync(worktree.Path, CancellationToken.None);

            var listed = await manager.ListAsync(env.RepoPath, CancellationToken.None);
            listed.Single(w => w.Path == worktree.Path).LastTouchedAt.ShouldBe(touchedAt);
        }
        finally
        {
            env.Dispose();
        }
    }

    private static WorktreeManager BuildManager(
        string worktreeRoot,
        TimeProvider? timeProvider = null,
        int staleAfterDays = 7)
    {
        var settings = Options.Create(new GitSettings
        {
            WorktreeBasePath = worktreeRoot,
            WorktreeStaleAfterDays = staleAfterDays,
            WorktreeJanitorIntervalHours = 24
        });

        return new WorktreeManager(
            settings,
            timeProvider ?? TimeProvider.System,
            NullLogger<WorktreeManager>.Instance);
    }

    private sealed class MutableTimeProvider : TimeProvider
    {
        private DateTimeOffset _utcNow;

        public MutableTimeProvider(DateTimeOffset utcNow)
        {
            _utcNow = utcNow;
        }

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void SetUtcNow(DateTimeOffset utcNow)
        {
            _utcNow = utcNow;
        }
    }

    private sealed class GitTestEnvironment : IDisposable
    {
        private GitTestEnvironment(string tempRoot, string repoPath, string worktreeRoot)
        {
            TempRoot = tempRoot;
            RepoPath = repoPath;
            WorktreeRoot = worktreeRoot;
        }

        public string TempRoot { get; }
        public string RepoPath { get; }
        public string WorktreeRoot { get; }

        public static async Task SkipIfGitUnavailableAsync()
        {
            try
            {
                await RunProcessAsync(Environment.CurrentDirectory, "git", ["--version"], throwOnError: true);
            }
            catch (Exception ex)
            {
                throw new SkipTestException($"git is required for WorktreeManager integration tests: {ex.Message}");
            }
        }

        public static async Task<GitTestEnvironment> CreateAsync()
        {
            var tempRoot = Path.Combine(Path.GetTempPath(), $"antiphon-worktree-tests-{Guid.NewGuid():N}");
            var repoPath = Path.Combine(tempRoot, "repo");
            var worktreeRoot = Path.Combine(tempRoot, "worktrees");
            Directory.CreateDirectory(repoPath);
            Directory.CreateDirectory(worktreeRoot);

            await RunGitAsync(repoPath, "init");
            await RunGitAsync(repoPath, "config", "user.email", "test@antiphon.dev");
            await RunGitAsync(repoPath, "config", "user.name", "Antiphon Test");
            await File.WriteAllTextAsync(Path.Combine(repoPath, "README.md"), "# Test Repo");
            await RunGitAsync(repoPath, "add", "README.md");
            await RunGitAsync(repoPath, "commit", "-m", "Initial commit");

            return new GitTestEnvironment(tempRoot, repoPath, worktreeRoot);
        }

        public static async Task<string> RunGitAsync(string workingDirectory, params string[] arguments)
        {
            var result = await RunProcessAsync(workingDirectory, "git", arguments, throwOnError: true);
            return result.Stdout;
        }

        public static async Task<(int ExitCode, string Stdout, string Stderr)> RunGitCaptureAsync(
            string workingDirectory, params string[] arguments)
        {
            var result = await RunProcessAsync(workingDirectory, "git", arguments, throwOnError: false);
            return (result.ExitCode, result.Stdout, result.Stderr);
        }

        public static void UnregisterWorktreeLeavingDirectory(string worktreePath)
        {
            var gitFile = Path.Combine(worktreePath, ".git");
            if (!File.Exists(gitFile))
                throw new InvalidOperationException($"Worktree {worktreePath} has no .git file.");

            var contents = File.ReadAllText(gitFile);
            const string prefix = "gitdir:";
            var line = contents.Replace("\r\n", "\n", StringComparison.Ordinal)
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .FirstOrDefault(l => l.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException($"Worktree {worktreePath} .git file has no gitdir line: {contents}");

            var adminDir = Path.GetFullPath(line[prefix.Length..].Trim());
            if (!Directory.Exists(adminDir))
                throw new InvalidOperationException($"Worktree admin dir {adminDir} does not exist.");

            DeleteDirectory(adminDir);
        }

        public static async Task TryRunGitAsync(string workingDirectory, params string[] arguments)
        {
            try
            {
                if (Directory.Exists(workingDirectory))
                    await RunProcessAsync(workingDirectory, "git", arguments, throwOnError: false);
            }
            catch
            {
                // Best-effort cleanup only.
            }
        }

        private static async Task<(int ExitCode, string Stdout, string Stderr)> RunProcessAsync(
            string workingDirectory,
            string fileName,
            IReadOnlyList<string> arguments,
            bool throwOnError)
        {
            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            foreach (var argument in arguments)
                psi.ArgumentList.Add(argument);

            using var process = Process.Start(psi)
                ?? throw new InvalidOperationException($"Failed to start {fileName}.");

            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            var stdout = await stdoutTask;
            var stderr = await stderrTask;

            if (throwOnError && process.ExitCode != 0)
                throw new InvalidOperationException(
                    $"{fileName} {string.Join(" ", arguments)} failed with exit code {process.ExitCode}: {stderr}");

            return (process.ExitCode, stdout, stderr);
        }

        public void Dispose()
        {
            DeleteDirectory(TempRoot);
        }

        private static void DeleteDirectory(string path)
        {
            if (!Directory.Exists(path))
                return;

            try
            {
                foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                    File.SetAttributes(file, FileAttributes.Normal);

                Directory.Delete(path, recursive: true);
            }
            catch
            {
                // Best-effort cleanup.
            }
        }
    }
}
