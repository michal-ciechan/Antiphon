using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Tests.TestHelpers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class GitWorkspaceServiceIndexLockTests
{
    [Test]
    public async Task Status_never_takes_the_optional_index_lock()
    {
        using var repo = new ScratchGitRepo("c543-status");
        await repo.CommitFileAsync("tracked.txt", "seed\n");
        await Task.Delay(1100);
        await File.WriteAllTextAsync(Path.Combine(repo.Path, "tracked.txt"), "changed\n");
        var index = Path.Combine(repo.Path, ".git", "index");
        var before = File.GetLastWriteTimeUtc(index);
        var git = new GitWorkspaceService(new ListLogger<GitWorkspaceService>([]), new GitProcessGate(),
            Options.Create(new GitSettings()));
        var result = await git.TryGetChangesAsync(repo.Path, CancellationToken.None);
        result.Succeeded.ShouldBeTrue();
        File.GetLastWriteTimeUtc(index).ShouldBe(before);

        await File.WriteAllTextAsync(Path.Combine(repo.Path, "tracked.txt"), "changed again\n");
        (await ScratchGitRepo.GitInAsync(repo.Path, "status", "--porcelain")).Ok.ShouldBeTrue();
        File.GetLastWriteTimeUtc(index).ShouldNotBe(before);
    }

    [Test]
    public async Task Timeout_kill_does_not_delete_index_lock()
    {
        using var repo = new ScratchGitRepo("c543-timeout");
        await repo.CommitFileAsync("a.txt", "a\n");
        await File.WriteAllTextAsync(Path.Combine(repo.Path, "b.txt"), "b\n");
        await repo.GitAsync("add", "--", "b.txt");
        var hooks = Path.Combine(repo.Path, "c543-hooks");
        Directory.CreateDirectory(hooks);
        var hook = Path.Combine(hooks, "pre-commit");
        // Git-for-Windows sh has no sleep on PATH; ping -n 9 is ~8 s and is the delay that
        // actually holds the lock-taking child so TimeoutSeconds=1 can kill it.
        await File.WriteAllTextAsync(hook,
            "#!/bin/sh\necho started > hook-started\nping -n 9 127.0.0.1 >/dev/null 2>&1\necho ended > hook-ended\n".Replace("\r\n", "\n"));
        await repo.GitAsync("config", "core.hooksPath", hooks);
        var lockPath = (await ScratchGitRepo.GitInAsync(repo.Path, "rev-parse", "--path-format=absolute", "--git-path", "index.lock"))
            .StdOut.Trim();
        var head = (await repo.GitReadAsync("rev-parse", "HEAD")).Trim();
        var logs = new List<string>();
        var git = new GitWorkspaceService(new ListLogger<GitWorkspaceService>(logs), new GitProcessGate(),
            Options.Create(new GitSettings { TimeoutSeconds = 1 }));
        var (code, stderr) = await git.CommitOnlyAsync(repo.Path, ["b.txt"], "timeout commit", [], CancellationToken.None);
        code.ShouldBe(-1);
        stderr.ShouldBe("timeout");
        File.Exists(Path.Combine(repo.Path, "hook-started")).ShouldBeTrue();
        logs.ShouldNotContain(l => l.Contains("Reclaimed orphaned git index lock", StringComparison.Ordinal));
        logs.ShouldContain(l => l.Contains("timed out", StringComparison.OrdinalIgnoreCase));
        (await repo.GitReadAsync("rev-parse", "HEAD")).Trim().ShouldBe(head);
        if (File.Exists(lockPath))
            File.Delete(lockPath);
        File.Delete(hook);
        (await git.CommitOnlyAsync(repo.Path, ["b.txt"], "after hook removed", [], CancellationToken.None))
            .Code.ShouldBe(0);
    }

    [Test]
    public async Task Timeout_kill_does_not_delete_a_foreign_lock_created_during_fsmonitor_stall()
    {
        using var repo = new ScratchGitRepo("c543-fsmonitor");
        await repo.CommitFileAsync("a.txt", "a\n");
        await File.WriteAllTextAsync(Path.Combine(repo.Path, "b.txt"), "b\n");
        await repo.GitAsync("add", "--", "b.txt");
        var hook = Path.Combine(repo.Path, "fsmonitor.cmd");
        await File.WriteAllTextAsync(hook,
            "@echo off\r\necho started>fsmonitor-started\r\nping -n 6 127.0.0.1 >nul\r\necho 1\r\n");
        await repo.GitAsync("config", "core.useBuiltinFSMonitor", "false");
        await repo.GitAsync("config", "core.fsmonitor", hook.Replace('\\', '/'));
        var lockPath = (await ScratchGitRepo.GitInAsync(repo.Path, "rev-parse", "--path-format=absolute", "--git-path", "index.lock"))
            .StdOut.Trim();
        var marker = Path.Combine(repo.Path, "fsmonitor-started");
        var logs = new List<string>();
        var git = new GitWorkspaceService(new ListLogger<GitWorkspaceService>(logs), new GitProcessGate(),
            Options.Create(new GitSettings { TimeoutSeconds = 2 }));
        var commit = git.CommitOnlyAsync(repo.Path, ["b.txt"], "fsmonitor stall", [], CancellationToken.None);
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(8);
        while (!File.Exists(marker) && DateTime.UtcNow < deadline)
            await Task.Delay(50);
        File.Exists(marker).ShouldBeTrue("core.fsmonitor must run before git takes index.lock");
        File.Exists(lockPath).ShouldBeFalse();
        var foreign = "foreign-lock"u8.ToArray();
        await File.WriteAllBytesAsync(lockPath, foreign);
        var (code, stderr) = await commit;
        code.ShouldBe(-1);
        stderr.ShouldBe("timeout");
        File.Exists(lockPath).ShouldBeTrue();
        (await File.ReadAllBytesAsync(lockPath)).ShouldBe(foreign);
        logs.ShouldNotContain(l => l.Contains("Reclaimed orphaned git index lock", StringComparison.Ordinal));
    }

    private sealed class ListLogger<T>(List<string> sink) : ILogger<T>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            lock (sink)
                sink.Add(formatter(state, exception));
        }
    }
}
