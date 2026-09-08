using System.Diagnostics;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Infrastructure.Git;

namespace Antiphon.Tests.TestHelpers;

/// <summary>All remotes, checkouts, Git configuration and sentinels are fixture-owned.</summary>
internal sealed class LandingGitFixture : IAsyncDisposable
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "antiphon-c448-" + Guid.NewGuid().ToString("N"));
    public string Repository => Path.Combine(Root, "canonical");
    public string Source => Path.Combine(Root, "trees", "source");
    public string Remote => Path.Combine(Root, "remote.git");
    public string SourceRef => "refs/heads/feat/card-task-11223344";
    public string TargetRef => "refs/heads/master";
    public Guid TaskId { get; } = Guid.NewGuid();
    public FixtureGit Git { get; }
    public string SeedSha { get; private set; } = "";
    public LandSourceCoordinates Coordinates => new(TaskId, Repository, Source, SourceRef, TargetRef);
    public string ObservationRef => $"refs/antiphon/land/{TaskId:N}/{Guid.NewGuid():N}/remote-observed";

    public LandingGitFixture()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(Repository);
        Directory.CreateDirectory(Remote);
        Directory.CreateDirectory(Path.Combine(Root, "home"));
        Git = new FixtureGit(Path.Combine(Root, "home"));
    }

    public async Task InitializeAsync()
    {
        await RequiredAsync(Repository, "init", "-b", "master");
        await RequiredAsync(Remote, "init", "--bare");
        await File.WriteAllTextAsync(Path.Combine(Repository, "keep.txt"), "seed\n");
        await File.WriteAllTextAsync(Path.Combine(Repository, ".gitignore"), ".antiphon/\n.claude/\nbin-*/\n");
        await RequiredAsync(Repository, "add", ".");
        await RequiredAsync(Repository, "commit", "-m", "seed");
        SeedSha = (await RequiredAsync(Repository, "rev-parse", "HEAD")).Trim();
        await RequiredAsync(Repository, "remote", "add", "origin", Remote);
        await RequiredAsync(Repository, "push", "origin", TargetRef);
        await RequiredAsync(Repository, "worktree", "add", "-b", SourceRef[11..], Source, "HEAD");
        await RequiredAsync(Repository, "push", "origin", SourceRef);
        Git.Trace.Clear();
    }

    public async Task<string> RequiredAsync(string path, params string[] arguments)
    {
        var result = await Git.RunAsync(path, arguments, CancellationToken.None);
        if (!result.Succeeded) throw new InvalidOperationException($"fixture_git_failed:{arguments[0]}:{result.ExitCode}");
        return result.Output;
    }

    public async Task AssertRemoteSourceAsync()
    {
        var actual = (await RequiredAsync(Remote, "rev-parse", "--verify", SourceRef + "^{commit}")).Trim();
        if (actual != SeedSha) throw new InvalidOperationException("Remote source was modified");
    }

    public ValueTask DisposeAsync()
    {
        var full = Path.GetFullPath(Root);
        var temp = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath())) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(temp, StringComparison.OrdinalIgnoreCase)
            || !Path.GetFileName(full).StartsWith("antiphon-c448-", StringComparison.Ordinal))
            throw new InvalidOperationException("Fixture disposal escaped owned root");
        // Fixtures with junctions must remove their own link before this final disposal.
        foreach (var file in Directory.EnumerateFiles(full, "*", SearchOption.AllDirectories))
            File.SetAttributes(file, FileAttributes.Normal);
        Directory.Delete(full, true);
        return ValueTask.CompletedTask;
    }

    internal sealed class FixtureGit(string home) : LandingGit
    {
        public List<string[]> Trace { get; } = [];
        public Func<string, IReadOnlyList<string>, Task<LandingGitResult?>>? BeforeCommand { get; set; }
        public Func<string, IReadOnlyList<string>, LandingGitResult, Task>? AfterCommand { get; set; }
        protected override void ConfigureProcess(ProcessStartInfo start)
        {
            start.Environment["HOME"] = home;
            start.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
            start.Environment["GIT_CONFIG_GLOBAL"] = Path.Combine(home, "empty-config");
            start.Environment["GIT_AUTHOR_NAME"] = "C448 Fixture";
            start.Environment["GIT_AUTHOR_EMAIL"] = "fixture@example.invalid";
            start.Environment["GIT_COMMITTER_NAME"] = "C448 Fixture";
            start.Environment["GIT_COMMITTER_EMAIL"] = "fixture@example.invalid";
            start.Environment["GIT_CONFIG_COUNT"] = "3";
            start.Environment["GIT_CONFIG_KEY_0"] = "commit.gpgSign";
            start.Environment["GIT_CONFIG_VALUE_0"] = "false";
            start.Environment["GIT_CONFIG_KEY_1"] = "credential.helper";
            start.Environment["GIT_CONFIG_VALUE_1"] = "";
            start.Environment["GIT_CONFIG_KEY_2"] = "core.hooksPath";
            start.Environment["GIT_CONFIG_VALUE_2"] = Path.Combine(home, "no-hooks");
        }

        public override async Task<LandingGitResult> RunAsync(string repository, IReadOnlyList<string> arguments, CancellationToken ct)
        {
            Trace.Add(arguments.ToArray());
            if (BeforeCommand is not null && await BeforeCommand(repository, arguments) is { } injected) return injected;
            var result = await base.RunAsync(repository, arguments, ct);
            if (AfterCommand is not null) await AfterCommand(repository, arguments, result);
            return result;
        }

        public override async Task<LandingGitResult> RunOwnedAsync(string repository, IReadOnlyList<string> arguments,
            Func<int, long, CancellationToken, Task> started, CancellationToken ct)
        {
            Trace.Add(arguments.ToArray());
            if (BeforeCommand is not null && await BeforeCommand(repository, arguments) is { } injected) return injected;
            var result = await base.RunOwnedAsync(repository, arguments, started, ct);
            if (AfterCommand is not null) await AfterCommand(repository, arguments, result);
            return result;
        }
    }
}
