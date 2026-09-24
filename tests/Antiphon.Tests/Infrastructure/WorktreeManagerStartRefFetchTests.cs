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

/// <summary>
/// CARD-0666. A -StartRef SHA pushed by another checkout (server2 phone-home runner, a sibling
/// task) is missing from the server's clone until something fetches it. Create must fetch the
/// exact SHA from origin before refusing, and a refusal must name the ref, not the opaque 422 text.
/// The origin here is a local bare repository, so the tests need no network.
/// </summary>
[Category("GitIntegration")]
[Category("Integration")]
[ParallelLimiter<Antiphon.Tests.TestHelpers.ProcessSpawnLimit>]
public class WorktreeManagerStartRefFetchTests
{
    private static readonly TimeSpan GitTimeout = TimeSpan.FromSeconds(60);

    [Test]
    public async Task Create_fetches_a_start_sha_that_exists_only_on_origin()
    {
        await SkipIfGitUnavailableAsync();
        var root = Path.Combine(Path.GetTempPath(), $"antiphon-startref-{Guid.NewGuid():N}");
        try
        {
            var (repo, worktrees, origin) = await CreateRepoWithOriginAsync(root);

            // Another clone pushes two commits to a task branch; the start SHA is the first, so
            // it is reachable on origin but not a branch tip and was never fetched into repo.
            var other = Path.Combine(root, "other");
            await GitAsync(root, "clone", "--quiet", origin, other);
            var startSha = await CommitAsync(other, "task.txt", "one");
            await CommitAsync(other, "task.txt", "two");
            await GitAsync(other, "push", "--quiet", "origin", "HEAD:refs/heads/feat/card-task-0badc0de");
            (await TryGitAsync(repo, "rev-parse", "--verify", "--quiet", startSha + "^{commit}")).ExitCode
                .ShouldNotBe(0, "precondition: the start SHA must be missing locally");

            var worktree = await BuildManager(worktrees)
                .CreateAsync(repo, "task-2cd14d4c", startSha, CancellationToken.None);

            (await GitAsync(worktree.Path, "rev-parse", "HEAD")).Trim().ShouldBe(startSha);
            worktree.Branch.ShouldBe("feat/card-task-2cd14d4c");
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Test]
    public async Task Create_names_a_start_sha_missing_locally_and_on_origin()
    {
        await SkipIfGitUnavailableAsync();
        var root = Path.Combine(Path.GetTempPath(), $"antiphon-startref-{Guid.NewGuid():N}");
        try
        {
            var (repo, worktrees, _) = await CreateRepoWithOriginAsync(root);
            const string missing = "0123456789abcdef0123456789abcdef01234567";

            var ex = await Should.ThrowAsync<ValidationException>(() => BuildManager(worktrees)
                .CreateAsync(repo, "task-2cd14d4c", missing, CancellationToken.None));

            ex.Message.ShouldContain(missing);
            ex.Message.ShouldContain("not on origin");
            ex.Message.ShouldNotContain("One or more validation errors occurred");
            Directory.Exists(Path.Combine(worktrees, "card-task-2cd14d4c")).ShouldBeFalse();
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    private static WorktreeManager BuildManager(string worktreeRoot) => new(
        Options.Create(new GitSettings { WorktreeBasePath = worktreeRoot }),
        TimeProvider.System,
        NullLogger<WorktreeManager>.Instance);

    private static async Task<(string Repo, string Worktrees, string Origin)> CreateRepoWithOriginAsync(string root)
    {
        var repo = Path.Combine(root, "repo");
        var worktrees = Path.Combine(root, "worktrees");
        var origin = Path.Combine(root, "origin.git");
        Directory.CreateDirectory(repo);
        Directory.CreateDirectory(worktrees);
        Directory.CreateDirectory(origin);
        await GitAsync(origin, "init", "--quiet", "--bare");
        await GitAsync(repo, "init", "--quiet");
        await CommitAsync(repo, "README.md", "# Test Repo");
        await GitAsync(repo, "remote", "add", "origin", origin);
        await GitAsync(repo, "push", "--quiet", "origin", "HEAD:refs/heads/master");
        return (repo, worktrees, origin);
    }

    private static async Task<string> CommitAsync(string repo, string file, string content)
    {
        await File.WriteAllTextAsync(Path.Combine(repo, file), content);
        await GitAsync(repo, "add", file);
        await GitAsync(repo, "-c", "user.email=test@antiphon.dev", "-c", "user.name=Antiphon Test",
            "commit", "--quiet", "-m", content);
        return (await GitAsync(repo, "rev-parse", "HEAD")).Trim();
    }

    private static async Task SkipIfGitUnavailableAsync()
    {
        try
        {
            await GitAsync(Environment.CurrentDirectory, "--version");
        }
        catch (Exception ex)
        {
            throw new SkipTestException($"git is required for WorktreeManager integration tests: {ex.Message}");
        }
    }

    private static async Task<string> GitAsync(string workingDirectory, params string[] arguments)
    {
        var result = await TryGitAsync(workingDirectory, arguments);
        if (result.ExitCode != 0)
            throw new InvalidOperationException(
                $"git {string.Join(" ", arguments)} failed with exit code {result.ExitCode}: {result.Stderr}");
        return result.Stdout;
    }

    private static async Task<(int ExitCode, string Stdout, string Stderr)> TryGitAsync(
        string workingDirectory, params string[] arguments)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        psi.Environment["GIT_TERMINAL_PROMPT"] = "0";
        foreach (var argument in arguments)
            psi.ArgumentList.Add(argument);

        using var process = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start git.");
        using var cts = new CancellationTokenSource(GitTimeout);
        var stdoutTask = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        var stderrTask = process.StandardError.ReadToEndAsync(CancellationToken.None);
        try
        {
            await process.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { /* best-effort */ }
            throw new System.TimeoutException($"git {string.Join(" ", arguments)} timed out after {GitTimeout.TotalSeconds:0}s");
        }
        return (process.ExitCode, await stdoutTask, await stderrTask);
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
