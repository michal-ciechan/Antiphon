using System.Diagnostics;
using System.Text;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Review;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Review;
using Antiphon.Tests.TestHelpers;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Review;

/// <summary>
/// The one-shot reviewers' own clones (<c>Review:Agents:MirrorRoot</c>): which path a GitHub repo is checked
/// out from, when the runner asks for a mirror, the git web root of a GitHub API root, and the fetch token
/// reaching git only through its environment.
/// </summary>
[Category("Unit")]
public class ReviewMirrorCheckoutTests
{
    private const string Token = "ghs_mirror-test-token-0123456789";

    [Test]
    public void A_github_repo_without_a_local_path_uses_its_lower_cased_mirror_and_a_row_or_a_local_subject_does_not()
    {
        var root = Path.Combine(Path.GetTempPath(), "mirrors");
        var settings = new ReviewAgentSettings { MirrorRoot = root };

        CliReviewAgentRunner.RepoPath(Request("Acme/Widgets"), settings).ShouldBe(Path.Combine(root, "acme", "widgets.git"));
        CliReviewAgentRunner.RepoPath(Request("acme/widgets"), new ReviewAgentSettings()).ShouldBeNull("no MirrorRoot, no checkout");
        settings.Repos.Add(new ReviewAgentRepo { Repo = "acme/widgets", LocalPath = @"C:\clones\widgets" });
        CliReviewAgentRunner.RepoPath(Request("acme/widgets"), settings).ShouldBe(@"C:\clones\widgets", "a LocalPath row wins");
        CliReviewAgentRunner.RepoPath(Request("acme/gadgets") with { Host = ReviewHost.Local, LocalPath = @"C:\local\gadgets" }, settings)
            .ShouldBe(@"C:\local\gadgets");
        CliReviewAgentRunner.MirrorPath(root, "../etc").ShouldBeNull();
        CliReviewAgentRunner.MirrorPath(root, "acme/wid gets").ShouldBeNull();
        CliReviewAgentRunner.MirrorPath(root, "acme/widgets/extra").ShouldBeNull();
    }

    [Test]
    public async Task The_runner_asks_for_a_mirror_only_for_a_mirror_path()
    {
        var root = Path.Combine(Path.GetTempPath(), "antiphon-review-mirror-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var checkout = new RecordingCheckout();
            var settings = new ReviewAgentSettings
            {
                MirrorRoot = Path.Combine(root, "mirrors"),
                Repos = [new ReviewAgentRepo { Repo = "acme/clone", LocalPath = @"C:\clones\clone" }],
            };
            using var runner = new CliReviewAgentRunner(new AnsweringCli(), checkout, Options.Create(settings), TimeProvider.System,
                NullLogger<CliReviewAgentRunner>.Instance, Path.Combine(root, "work"));

            foreach (var repo in new[] { "acme/widgets", "acme/clone" })
            {
                var started = await runner.RunAsync(Request(repo), CancellationToken.None);
                await runner.Completion(started.Handle!)!.WaitAsync(TimeSpan.FromSeconds(30));
            }

            checkout.Adds.ShouldBe([
                (Path.Combine(root, "mirrors", "acme", "widgets.git"), new ReviewCheckoutRemote("acme/widgets", CreateMirror: true)),
                (@"C:\clones\clone", new ReviewCheckoutRemote("acme/clone", CreateMirror: false)),
            ]);
        }
        finally
        {
            if (Directory.Exists(root))
                ReadOnlyDirectory.Delete(root);
        }
    }

    [Test]
    [Arguments(null, "https://github.com/")]
    [Arguments("https://api.github.com", "https://github.com/")]
    [Arguments("https://api.github.com/", "https://github.com/")]
    [Arguments("https://ghe.acme.internal/api/v3", "https://ghe.acme.internal/")]
    [Arguments("https://ghe.acme.internal:8443/api/v3/", "https://ghe.acme.internal:8443/")]
    [Arguments("https://api.acme.ghe.com", "https://acme.ghe.com/")]
    [Arguments("not a url", "https://github.com/")]
    public void The_web_root_comes_from_the_api_root(string? baseUrl, string expected) =>
        GitHubWebRoot.FromApiBaseUrl(baseUrl).AbsoluteUri.ShouldBe(expected);

    [Test]
    public void The_token_is_an_extraheader_scoped_to_the_host_in_the_environment_only()
    {
        var environment = GitReviewCheckout.FetchEnvironment(new ReviewGitCredential(new Uri("https://ghe.acme.internal/"), Token));

        environment["GIT_CONFIG_COUNT"].ShouldBe("1");
        environment["GIT_CONFIG_KEY_0"].ShouldBe("http.https://ghe.acme.internal/.extraheader");
        environment["GIT_CONFIG_VALUE_0"].ShouldBe("AUTHORIZATION: basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("x-access-token:" + Token)));
        environment["GIT_TERMINAL_PROMPT"].ShouldBe("0");
        new ReviewGitCredential(new Uri("https://github.com/"), Token).ToString().ShouldNotContain(Token);

        var none = GitReviewCheckout.FetchEnvironment(new ReviewGitCredential(new Uri("https://github.com/"), null));
        none.ContainsKey("GIT_CONFIG_COUNT").ShouldBeFalse();
    }

    internal static ReviewAgentRequest Request(string repo) => new(
        Guid.Parse("6f1d3c52-0a5b-4c8e-9d61-2f7a0e4b9c13"), 1, 1, "correctness", "## Diff\n+ x", "abcdef0123456789", ["SEED-0001"], repo, 7,
        new string('b', 40), 1);

    private sealed class RecordingCheckout : IReviewCheckout
    {
        public List<(string RepoPath, ReviewCheckoutRemote? Remote)> Adds { get; } = [];

        public Task AddAsync(string repoPath, ReviewCheckoutRemote? remote, int? prNumber, string headSha, string directory, CancellationToken ct)
        {
            lock (Adds)
                Adds.Add((repoPath, remote));
            Directory.CreateDirectory(directory);
            return Task.CompletedTask;
        }

        public Task RemoveAsync(string repoPath, string directory, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class AnsweringCli : IClaudeCli
    {
        public Task<ClaudeCliResult> RunAsync(ClaudeCliRequest request, CancellationToken ct) =>
            Task.FromResult(new ClaudeCliResult("@abf 1 review-findings/1.0\n=== checks\nSEED-0001 pass\n=== end\n", "claude-sonnet-5-5", 10, 5, 0.01m));
    }
}

/// <summary>
/// <see cref="GitReviewCheckout"/> against real git: a missing mirror created bare from a file:// "host", a PR
/// head fetched from <c>refs/pull/7/head</c> into a worktree, and the token kept out of the mirror's config.
/// </summary>
[Category("Integration"), ParallelLimiter<ProcessSpawnLimit>]
public class ReviewMirrorCheckoutGitTests
{
    private const string Token = "ghs_mirror-test-token-0123456789";
    private static readonly string[] Identity = ["-c", "user.name=t", "-c", "user.email=t@example.com"];

    [Test]
    public async Task A_missing_mirror_is_created_bare_from_the_web_root_and_a_pr_head_is_fetched_into_a_worktree()
    {
        var root = Path.Combine(Path.GetTempPath(), "antiphon-review-mirror-tests", Guid.NewGuid().ToString("N"));
        try
        {
            // The "host": a web root folder holding acme/widgets.git, with the PR head only under refs/pull/7/head.
            var webRoot = Path.Combine(root, "host") + Path.DirectorySeparatorChar;
            var work = Path.Combine(root, "author");
            await GitAsync(root, "init", "--quiet", "-b", "main", work);
            await File.WriteAllTextAsync(Path.Combine(work, "a.txt"), "base\n");
            await GitAsync(work, "add", "a.txt");
            await GitAsync(work, [.. Identity, "commit", "--quiet", "-m", "base"]);
            await File.WriteAllTextAsync(Path.Combine(work, "a.txt"), "change\n");
            await GitAsync(work, [.. Identity, "commit", "--quiet", "-am", "change"]);
            var head = (await GitAsync(work, "rev-parse", "HEAD")).Trim();
            var origin = Path.Combine(webRoot, "acme", "widgets.git");
            await GitAsync(root, "init", "--quiet", "--bare", origin);
            await GitAsync(work, "push", "--quiet", origin, "HEAD~1:refs/heads/main", "HEAD:refs/pull/7/head");

            var mirror = CliReviewAgentRunner.MirrorPath(Path.Combine(root, "mirrors"), "Acme/Widgets")!;
            var credentials = new FixedCredentials(new ReviewGitCredential(new Uri(webRoot), Token));
            var checkout = new GitReviewCheckout(NullLogger<GitReviewCheckout>.Instance, credentials);
            var directory = Path.Combine(root, "worktrees", "r1");
            var remote = new ReviewCheckoutRemote("acme/widgets", CreateMirror: true);

            await checkout.AddAsync(mirror, remote, 7, head, directory, CancellationToken.None);

            (await File.ReadAllTextAsync(Path.Combine(directory, "a.txt"))).TrimEnd().ShouldBe("change", "the worktree is at the PR head (line endings follow core.autocrlf)");
            (await GitAsync(mirror, "rev-parse", "--is-bare-repository")).Trim().ShouldBe("true");
            (await GitAsync(mirror, "rev-parse", "refs/antiphon/review/pull/7")).Trim().ShouldBe(head);
            (await GitAsync(mirror, "remote", "get-url", "origin")).Trim().ShouldBe(new Uri(new Uri(webRoot), "acme/widgets.git").AbsoluteUri);
            var config = await File.ReadAllTextAsync(Path.Combine(mirror, "config"));
            config.ShouldNotContain(Token, Case.Sensitive, "the token is never written to the mirror");
            config.ShouldNotContain("extraheader", Case.Insensitive);
            credentials.Calls.ShouldBe(1, "one credential serves the create and the fetch");

            await checkout.RemoveAsync(mirror, directory, CancellationToken.None);
            Directory.Exists(directory).ShouldBeFalse();

            // A second review of the same head reuses the mirror and needs no fetch, so no credential.
            await checkout.AddAsync(mirror, remote, 7, head, directory, CancellationToken.None);
            credentials.Calls.ShouldBe(1);
            await checkout.RemoveAsync(mirror, directory, CancellationToken.None);
        }
        finally
        {
            TryDelete(root);
        }
    }

    private static async Task<string> GitAsync(string workingDirectory, params string[] arguments)
    {
        Directory.CreateDirectory(workingDirectory);
        var info = new ProcessStartInfo("git")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = workingDirectory,
        };
        foreach (var argument in arguments)
            info.ArgumentList.Add(argument);
        using var process = Process.Start(info)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"git {string.Join(' ', arguments)} failed ({process.ExitCode}): {await stderr}");
        return await stdout;
    }

    private static void TryDelete(string root)
    {
        if (!Directory.Exists(root))
            return;
        try
        {
            foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
                File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(root, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private sealed class FixedCredentials(ReviewGitCredential credential) : IReviewGitCredentials
    {
        public int Calls { get; private set; }

        public Task<ReviewGitCredential> GetAsync(string repo, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(credential);
        }
    }
}
