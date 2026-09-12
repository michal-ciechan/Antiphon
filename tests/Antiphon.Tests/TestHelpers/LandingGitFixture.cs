using System.Diagnostics;
using Shouldly;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Infrastructure.Git;

namespace Antiphon.Tests.TestHelpers;

/// <summary>All remotes, checkouts, Git configuration and sentinels are fixture-owned.</summary>
internal sealed class LandingGitFixture : IAsyncDisposable
{
    public string Root { get; }
    public string Repository => Path.Combine(Root, "canonical");
    public string Source => Path.Combine(Root, "trees", "source");
    public string Remote => Path.Combine(Root, "remote.git");
    public string Observer => Path.Combine(Root, "observer");
    public string SourceRef => $"refs/heads/feat/card-task-{TaskId:N}";
    public string TargetRef => "refs/heads/master";
    public Guid TaskId { get; }
    public FixtureGit Git { get; }
    public string SeedSha { get; private set; } = "";
    public LandSourceCoordinates Coordinates => new(TaskId, Repository, Source, SourceRef, TargetRef);
    public string ObservationRef => $"refs/antiphon/land/{TaskId:N}/{Guid.NewGuid():N}/remote-observed";

    public LandingGitFixture(string? root = null, Guid? taskId = null)
    {
        Root = root ?? Path.Combine(Path.GetTempPath(), "antiphon-c448-" + Guid.NewGuid().ToString("N"));
        TaskId = taskId ?? Guid.NewGuid();
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(Repository);
        Directory.CreateDirectory(Remote);
        Directory.CreateDirectory(Path.Combine(Root, "home"));
        Git = new FixtureGit(Path.Combine(Root, "home"), TaskId);
    }

    public async Task InitializeAsync()
    {
        await RequiredAsync(Repository, "init", "-b", "master");
        await RequiredAsync(Remote, "init", "--bare");
        await File.WriteAllTextAsync(Path.Combine(Repository, "keep.txt"), "seed\n");
        await File.WriteAllTextAsync(Path.Combine(Repository, "fixture-owner.txt"), TaskId.ToString("N") + "\n");
        await File.WriteAllTextAsync(Path.Combine(Repository, ".gitignore"), ".antiphon/\n.claude/\nbin-*/\n");
        await RequiredAsync(Repository, "add", ".");
        await RequiredAsync(Repository, "commit", "-m", "seed");
        SeedSha = (await RequiredAsync(Repository, "rev-parse", "HEAD")).Trim();
        await RequiredAsync(Repository, "remote", "add", "origin", Remote);
        await RequiredAsync(Repository, "push", "origin", TargetRef);
        await RequiredAsync(Repository, "worktree", "add", "-b", SourceRef[11..], Source, "HEAD");
        await RequiredAsync(Repository, "push", "origin", SourceRef);
        await RequiredAsync(Root, "clone", "--no-hardlinks", "--branch", "master", "--", Remote, Observer);
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
        var reader = new FixtureGit(Path.Combine(Root, "home"), TaskId); // Independent of fault-injected production I/O.
        var actual = await reader.RunAsync(Remote, ["rev-parse", "--verify", SourceRef + "^{commit}"], CancellationToken.None);
        actual.Succeeded.ShouldBeTrue("the pre-published fixture remote source must not be deleted by landing/refusal/recovery");
        actual.Output.Trim().ShouldBe(SeedSha, "the pre-published remote source must retain its exact original commit");
        var observedRef = "refs/antiphon-observer/source/" + Guid.NewGuid().ToString("N");
        var fetch = await reader.RunAsync(Observer, ["fetch", "--no-tags", Remote, SourceRef + ":" + observedRef], CancellationToken.None);
        fetch.Succeeded.ShouldBeTrue("an independent observer must still fetch the protected remote source");
        var observed = await reader.RunAsync(Observer, ["rev-parse", "--verify", observedRef], CancellationToken.None);
        observed.Succeeded.ShouldBeTrue();
        observed.Output.Trim().ShouldBe(SeedSha);
    }

    public async Task CaptureAsync(string kind)
    {
        if (!LandingEvidence.Enabled) return;
        var contents = new List<object>();
        if (Directory.Exists(Source))
            foreach (var file in Directory.EnumerateFiles(Source, "*", SearchOption.AllDirectories))
            {
                string digest;
                try { digest = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(await File.ReadAllBytesAsync(file))); }
                catch (IOException) { digest = "unreadable"; }
                catch (UnauthorizedAccessException) { digest = "inaccessible"; }
                contents.Add(new { path = Path.GetRelativePath(Source, file), digest });
            }
        var reader = new LandingGit();
        var refs = await reader.RunAsync(Repository, ["show-ref"], CancellationToken.None);
        var remote = await reader.RunAsync(Remote, ["show-ref"], CancellationToken.None);
        var registration = await reader.RunAsync(Repository, ["worktree", "list", "--porcelain", "-z"], CancellationToken.None);
        var index = new List<object>();
        foreach (var directory in new[] { Source, Repository })
        {
            if (!Directory.Exists(directory)) continue;
            var admin = await reader.RunAsync(directory, ["rev-parse", "--absolute-git-dir"], CancellationToken.None);
            var path = Path.Combine(admin.Output.Trim(), "index");
            string? digest = null;
            try { if (admin.Succeeded && File.Exists(path)) digest = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(await File.ReadAllBytesAsync(path))); }
            catch (IOException) { digest = "unreadable"; }
            var state = await reader.RunAsync(directory, ["status", "--porcelain=v1", "-z", "--untracked-files=all", "--ignored", "--ignore-submodules=none"], CancellationToken.None);
            var head = await reader.RunAsync(directory, ["rev-parse", "--verify", "HEAD"], CancellationToken.None);
            var symbolic = await reader.RunAsync(directory, ["symbolic-ref", "-q", "HEAD"], CancellationToken.None);
            var active = new List<string>();
            if (admin.Succeeded)
                foreach (var marker in new[] { "MERGE_HEAD", "CHERRY_PICK_HEAD", "REVERT_HEAD", "rebase-merge", "rebase-apply", "sequencer" })
                    if (Path.Exists(Path.Combine(admin.Output.Trim(), marker))) active.Add(marker);
            index.Add(new { directory, gitDirectory = admin.Output, digest, state.Output, state.ExitCode,
                head = head.Output, headExit = head.ExitCode, symbolic = symbolic.Output, symbolicExit = symbolic.ExitCode, active });
        }
        LandingEvidence.Write(TaskId, kind, new { directoryExists = Directory.Exists(Source), contents, index, registrations = registration.Output, registrationExit = registration.ExitCode,
            localRefs = refs.Output, localQueryExit = refs.ExitCode, remoteRefs = remote.Output, remoteQueryExit = remote.ExitCode });
    }

    public async ValueTask DisposeAsync()
    {
        var full = Path.GetFullPath(Root);
        var temp = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath())) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(temp, StringComparison.OrdinalIgnoreCase)
            || !Path.GetFileName(full).StartsWith("antiphon-c448-", StringComparison.Ordinal))
            throw new InvalidOperationException("Fixture disposal escaped owned root");
        await CaptureAsync("terminal_image");
        // Fixtures with junctions must remove their own link before this final disposal.
        foreach (var file in Directory.EnumerateFiles(full, "*", SearchOption.AllDirectories))
            File.SetAttributes(file, FileAttributes.Normal);
        Directory.Delete(full, true);
    }

    internal class FixtureGit(string home, Guid taskId) : LandingGit
    {
        public List<string[]> Trace { get; } = [];
        public Func<IReadOnlyList<string>, Task>? BeforeObservedCommand { get; set; }
        public Func<string, IReadOnlyList<string>, Task<LandingGitResult?>>? BeforeCommand { get; set; }
        public Func<string, IReadOnlyList<string>, LandingGitResult, Task>? AfterCommand { get; set; }
        protected override void ConfigureProcess(ProcessStartInfo start)
        {
            start.Environment["HOME"] = home;
            start.Environment["GIT_EDITOR"] = "cmd.exe /c exit 0";
            start.Environment["GIT_SEQUENCE_EDITOR"] = "cmd.exe /c exit 0";
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
            if (BeforeObservedCommand is not null) await BeforeObservedCommand(arguments);
            LandingEvidence.Write(taskId, "git_start", new { repository, arguments });
            var result = await base.RunAsync(repository, arguments, ct);
            LandingEvidence.Write(taskId, "git_exit", new { arguments, result.ExitCode, result.RebaseHeadSha });
            if (AfterCommand is not null) await AfterCommand(repository, arguments, result);
            return result;
        }

        public override async Task<LandingGitResult> RunOwnedAsync(string repository, IReadOnlyList<string> arguments,
            Func<int, long, CancellationToken, Task> started, CancellationToken ct)
        {
            Trace.Add(arguments.ToArray());
            if (BeforeCommand is not null && await BeforeCommand(repository, arguments) is { } injected) return injected;
            if (BeforeObservedCommand is not null) await BeforeObservedCommand(arguments);
            LandingEvidence.Write(taskId, "owned_git_start", new { repository, arguments });
            var result = await base.RunOwnedAsync(repository, arguments, started, ct);
            LandingEvidence.Write(taskId, "owned_git_exit", new { arguments, result.ExitCode, result.RebaseHeadSha });
            if (AfterCommand is not null) await AfterCommand(repository, arguments, result);
            return result;
        }
    }
}
