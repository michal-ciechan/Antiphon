using System.Diagnostics;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Infrastructure.Git;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Infrastructure;

/// <summary>
/// CARD-1453: the post-window source <c>status</c> (the CARD-1449 C688 inventory,
/// <c>status --porcelain=v1 -z --untracked-files=no</c> in <c>ObserveSourceResidueAsync</c>)
/// runs in a live task worktree. A bare <c>git status</c> refreshes the index and creates
/// <c>index.lock</c>. The land read must not.
/// </summary>
[Category("Integration")]
[ParallelLimiter<GitProcessSpawnLimit>]
public sealed class LandingSourceOptionalLockTests
{
    private static readonly string[] SourceStatus = ["status", "--porcelain=v1", "-z", "--untracked-files=no"];

    [Test]
    public async Task C1453_PostWindowSourceStatusCreatesNoIndexLock()
    {
        await using var fixture = new LandingGitFixture();
        await fixture.InitializeAsync();
        var source = fixture.Source;
        var gitDir = (await new LandingGit().RunAsync(source, ["rev-parse", "--absolute-git-dir"], CancellationToken.None))
            .Output.Trim();
        var index = Path.Combine(gitDir, "index");
        var tracked = Path.Combine(source, "keep.txt");
        File.Exists(tracked).ShouldBeTrue();
        File.Exists(index).ShouldBeTrue();

        MakeStatDirty(tracked, index);
        var control = await WatchAsync(gitDir, () => RawStatusAsync(source, "1"));
        control.LockCreated.ShouldBeTrue("control: a bare git status creates index.lock, so a miss below means something");
        control.IndexRewritten.ShouldBeTrue("control: a bare git status rewrites the task index");

        MakeStatDirty(tracked, index);
        var production = new ProcessStatusGit();
        var guarded = await WatchAsync(gitDir, async () =>
        {
            var status = await production.StatusVerdictAsync(source, SourceStatus, CancellationToken.None);
            status.Succeeded.ShouldBeTrue(status.Diagnostic);
            status.Output.ShouldBeEmpty();
        });
        production.StatusSpawns.ShouldBe(1, "the hazard is the real git status, not an in-process answer");
        production.OptionalLocks.ShouldBe("0");
        guarded.LockCreated.ShouldBeFalse("the post-window source status creates no index.lock");
        guarded.IndexRewritten.ShouldBeFalse("the post-window source status does not refresh the task index");

        // ConfigureProcess runs after the historical early assignment and can clear it. The suppression
        // has to be applied again after that setup, or this live checkout takes the lock.
        MakeStatDirty(tracked, index);
        var stripped = new StrippingStatusGit();
        var afterSetup = await WatchAsync(gitDir, async () =>
        {
            var status = await stripped.StatusVerdictAsync(source, SourceStatus, CancellationToken.None);
            status.Succeeded.ShouldBeTrue(status.Diagnostic);
            status.Output.ShouldBeEmpty();
        });
        stripped.StatusSpawns.ShouldBe(1);
        stripped.OptionalLocks.ShouldBe("0", "caller setup cannot re-enable optional locks on a land read");
        afterSetup.LockCreated.ShouldBeFalse("a ConfigureProcess that clears GIT_OPTIONAL_LOCKS still creates no index.lock");
        afterSetup.IndexRewritten.ShouldBeFalse();

        MakeStatDirty(tracked, index);
        var inProcess = new SpawnCountingGit();
        var answered = await WatchAsync(gitDir, async () =>
        {
            var status = await inProcess.StatusVerdictAsync(source, SourceStatus, CancellationToken.None);
            status.Succeeded.ShouldBeTrue(status.Diagnostic);
            status.Output.ShouldBeEmpty();
        });
        inProcess.StatusSpawns.ShouldBe(0, "a clean tree is answered in process");
        answered.LockCreated.ShouldBeFalse("the in-process source status creates no index.lock");
        answered.IndexRewritten.ShouldBeFalse("the in-process source status does not refresh the task index");
        File.Exists(Path.Combine(gitDir, "index.lock")).ShouldBeFalse();
    }

    /// <summary>
    /// Git for Windows' system gitconfig sets <c>core.autocrlf</c>. This assembly points libgit2 at an
    /// empty config directory, so a child that still reads that file disagrees and the status falls back
    /// to git. The land read's child has to match the isolated library: a clean tree stays in process.
    /// </summary>
    [Test]
    public async Task C1453_WindowsSystemAutocrlfStillAnswersInProcess()
    {
        await using var fixture = new LandingGitFixture();
        await fixture.InitializeAsync();
        var source = fixture.Source;
        var gitDir = (await new LandingGit().RunAsync(source, ["rev-parse", "--absolute-git-dir"], CancellationToken.None))
            .Output.Trim();
        var system = Path.Combine(fixture.Root, "git-for-windows.gitconfig");
        await File.WriteAllTextAsync(system, "[core]\n\tautocrlf = true\n");
        MakeStatDirty(Path.Combine(source, "keep.txt"), Path.Combine(gitDir, "index"));
        var git = new WindowsSystemAutocrlfGit(system);
        var watched = await WatchAsync(gitDir, async () =>
        {
            var status = await git.StatusVerdictAsync(source, SourceStatus, CancellationToken.None);
            status.Succeeded.ShouldBeTrue(status.Diagnostic);
            status.Output.ShouldBeEmpty();
        });
        git.StatusSpawns.ShouldBe(0, "a clean tree is answered in process when the child matches isolated libgit2");
        watched.LockCreated.ShouldBeFalse("the in-process source status creates no index.lock");
        watched.IndexRewritten.ShouldBeFalse("the in-process source status does not refresh the task index");
    }

    private static void MakeStatDirty(string file, string index)
    {
        File.SetLastWriteTimeUtc(file, DateTime.UtcNow.AddMinutes(-5));
        File.SetLastWriteTimeUtc(index, DateTime.UtcNow.AddDays(-1));
    }

    private static async Task RawStatusAsync(string checkout, string optionalLocks)
    {
        var start = new ProcessStartInfo("git")
        {
            WorkingDirectory = checkout, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
        };
        foreach (var argument in SourceStatus) start.ArgumentList.Add(argument);
        start.Environment["GIT_OPTIONAL_LOCKS"] = optionalLocks;
        using var process = Process.Start(start)!;
        await process.StandardOutput.ReadToEndAsync();
        var error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        process.ExitCode.ShouldBe(0, error);
    }

    private static async Task<Watch> WatchAsync(string gitDir, Func<Task> body)
    {
        var index = Path.Combine(gitDir, "index");
        var seen = 0;
        using var watcher = new FileSystemWatcher(gitDir)
        {
            Filter = "index.lock",
            NotifyFilter = NotifyFilters.FileName,
            EnableRaisingEvents = true,
        };
        watcher.Created += (_, _) => Interlocked.Increment(ref seen);
        var before = File.GetLastWriteTimeUtc(index);
        await body();
        await Task.Delay(50);
        return new Watch(Volatile.Read(ref seen) > 0, File.GetLastWriteTimeUtc(index) != before);
    }

    private readonly record struct Watch(bool LockCreated, bool IndexRewritten);

    private class ProcessStatusGit : LandingGit
    {
        public int StatusSpawns;
        public string? OptionalLocks;
        protected override bool AnswerWorkingTreeReadsInProcess => false;

        protected override Process? StartProcess(ProcessStartInfo start)
        {
            if (start.ArgumentList.Contains("status"))
            {
                StatusSpawns++;
                OptionalLocks = start.Environment.TryGetValue("GIT_OPTIONAL_LOCKS", out var value) ? value : "<absent>";
            }
            return base.StartProcess(start);
        }
    }

    /// <summary>Drops the variable the way a <c>ConfigureProcess</c> that rebuilds the child environment can.</summary>
    private sealed class StrippingStatusGit : ProcessStatusGit
    {
        protected override void ConfigureProcess(ProcessStartInfo start) => start.Environment.Remove("GIT_OPTIONAL_LOCKS");
    }

    private class SpawnCountingGit : LandingGit
    {
        public int StatusSpawns;

        protected override Process? StartProcess(ProcessStartInfo start)
        {
            if (start.ArgumentList.Contains("status")) StatusSpawns++;
            return base.StartProcess(start);
        }
    }

    /// <summary>The Windows runner's child: a system gitconfig sets <c>core.autocrlf</c>, then the suite isolation runs.</summary>
    private sealed class WindowsSystemAutocrlfGit : SpawnCountingGit
    {
        private readonly string _systemFile;

        public WindowsSystemAutocrlfGit(string systemFile) => _systemFile = systemFile;

        protected override void ConfigureProcess(ProcessStartInfo start)
        {
            start.Environment["GIT_CONFIG_SYSTEM"] = _systemFile;
            base.ConfigureProcess(start);
        }
    }
}
