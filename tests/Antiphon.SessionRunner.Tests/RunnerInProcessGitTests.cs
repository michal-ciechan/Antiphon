using System.Diagnostics;
using Shouldly;
using TUnit.Core;

namespace Antiphon.SessionRunner.Tests;

/// <summary>
/// The runner's git reads that libgit2 answers give git's answer: the same output and exit code as a forced git
/// child, and git itself wherever the child's config could change what it reads.
/// </summary>
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class RunnerInProcessGitTests
{
    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(60);

    // The real starter is eligible; any other delegate (here a lambda) always starts git.
    private static Task<(int ExitCode, string Stdout, string Stderr)> Fast(string cwd, params string[] args) =>
        RunnerWorkspaceService.RunGitAsync(cwd, Process.Start, Budget, default, args);

    private static Task<(int ExitCode, string Stdout, string Stderr)> Spawned(string cwd, params string[] args) =>
        RunnerWorkspaceService.RunGitAsync(cwd, psi => Process.Start(psi), Budget, default, args);

    [Test]
    public async Task LsRemoteOfLocalPathMatchesGit()
    {
        using var scratch = new Root();
        var origin = Path.Combine(scratch.Path, "origin.git");
        C0824SlotScratch.Git(scratch.Path, "init", "--bare", "-b", "master", origin);
        C0824SlotScratch.Git(scratch.Repo, "commit", "--allow-empty", "-m", "base");
        C0824SlotScratch.Git(scratch.Repo, "push", origin, "master", "master:refs/heads/x/master");

        foreach (var reference in new[] { "refs/heads/master", "master", "refs/heads/absent" })
        {
            string[] args = ["ls-remote", "--refs", "--exit-code", "--", origin, reference];
            var fast = await Fast(scratch.Repo, args);
            fast.ShouldBe(await Spawned(scratch.Repo, args), reference);
        }

        // A rewrite rule in the repository's config: git lists the other endpoint, so git must answer.
        var other = Path.Combine(scratch.Path, "other.git");
        C0824SlotScratch.Git(scratch.Path, "init", "--bare", "-b", "master", other);
        C0824SlotScratch.Git(scratch.Repo, "config", "url." + other + ".insteadOf", origin);
        string[] rewritten = ["ls-remote", "--refs", "--exit-code", "--", origin, "refs/heads/master"];
        var answer = await Fast(scratch.Repo, rewritten);
        answer.ShouldBe(await Spawned(scratch.Repo, rewritten));
        answer.ExitCode.ShouldBe(2, "the rewritten endpoint is empty");
    }

    private sealed class Root : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory("runner-inproc-").FullName;
        public string Repo => System.IO.Path.Combine(Path, "repo");

        public Root()
        {
            C0824SlotScratch.Git(Path, "init", "-b", "master", Repo);
            C0824SlotScratch.Git(Repo, "config", "user.name", "runner test");
            C0824SlotScratch.Git(Repo, "config", "user.email", "runner@example.invalid");
            C0824SlotScratch.Git(Repo, "config", "commit.gpgsign", "false");
        }

        public void Dispose()
        {
            try
            {
                foreach (var file in Directory.EnumerateFiles(Path, "*", SearchOption.AllDirectories))
                    File.SetAttributes(file, FileAttributes.Normal);
                Directory.Delete(Path, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
    }
}
