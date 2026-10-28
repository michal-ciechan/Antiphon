using System.Diagnostics;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Infrastructure.Git;
using Antiphon.Tests.TestHelpers;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[ParallelLimiter<GitProcessSpawnLimit>]
public class CardFileGitFailureAcceptanceTests
{
    private static CardFileRepository Repository(int seconds = 30) => new(new GitProcessGate(), Options.Create(new GitSettings { TimeoutSeconds = seconds }), NullLogger<CardFileRepository>.Instance);

    [Test]
    public async Task Worktree_change_from_real_post_index_hook_is_detected_after_staging_before_commit()
    {
        using var repo = new ScratchGitRepo("c408-after-stage"); await repo.CommitFileAsync("seed", "seed");
        var directory = Directory.CreateDirectory(Path.Combine(repo.Path, "docs/cards/board")).FullName;
        const string relative = "docs/cards/board/card.md"; var path = Path.Combine(repo.Path, relative);
        await File.WriteAllTextAsync(path, "C408_PUBLIC\n");
        var hook = Path.Combine(repo.Path, ".git/hooks/post-index-change");
        await ScratchGitRepo.WriteExecutableHookAsync(hook, "#!/bin/sh\necho C408_CHANGED > docs/cards/board/card.md\necho reached > c408-hook-reached\n");
        var head = await repo.GitReadAsync("rev-parse", "HEAD");
        var expected = new Dictionary<string, string?> { [relative] = "C408_PUBLIC\n" };
        var result = await Repository().CommitAsync(repo.Path, directory, expected, "sync", default);
        File.Exists(Path.Combine(repo.Path, "c408-hook-reached")).ShouldBeTrue();
        result.SkipReason.ShouldBe("generated_files_changed"); result.Sha.ShouldBeNull();
        (await repo.GitReadAsync("rev-parse", "HEAD")).ShouldBe(head);
        File.Delete(hook); await File.WriteAllTextAsync(path, "C408_PUBLIC\n");
        (await Repository().CommitAsync(repo.Path, directory, expected, "sync", default)).Sha.ShouldNotBeNull();
    }

    [Test]
    [Arguments("lock")] [Arguments("stderr")] [Arguments("timeout")] [Arguments("cancel")]
    public async Task Git_failures_are_sanitized_preserve_HEAD_and_retry_after_owned_fault_is_removed(string fault)
    {
        using var repo = new ScratchGitRepo("c408-fault"); await repo.CommitFileAsync("seed", "seed\n");
        var directory = Directory.CreateDirectory(Path.Combine(repo.Path, "docs/cards/board")).FullName;
        const string relative = "docs/cards/board/card.md";
        await File.WriteAllTextAsync(Path.Combine(repo.Path, relative), "C408_PUBLIC\n");
        var head = await repo.GitReadAsync("rev-parse", "HEAD");
        var marker = Path.Combine(repo.Path, ".git/index.lock");
        var hook = Path.Combine(repo.Path, ".git/hooks/pre-commit");
        if (fault == "lock") await File.WriteAllTextAsync(marker, "owned test lock");
        else await ScratchGitRepo.WriteExecutableHookAsync(hook, fault == "stderr"
            ? "#!/bin/sh\necho C408_SYNTHETIC_CREDENTIAL_ERROR >&2\nexit 1\n"
            : StallHook());
        var expected = new Dictionary<string, string?> { [relative] = "C408_PUBLIC\n" };
        using var cancel = new CancellationTokenSource();
        var task = Repository(fault == "timeout" ? 2 : 30).CommitAsync(repo.Path, directory, expected, "public sync", cancel.Token);
        if (fault == "cancel")
        {
            var hookStarted = Path.Combine(repo.Path, "hook-started");
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            try
            {
                while (!File.Exists(hookStarted))
                {
                    if (task.IsCompleted) break;
                    await Task.Delay(25, deadline.Token);
                }
            }
            catch (OperationCanceledException) when (deadline.IsCancellationRequested)
            {
            }
            finally { cancel.Cancel(); }
            File.Exists(hookStarted).ShouldBeTrue();
            await Should.ThrowAsync<OperationCanceledException>(() => task);
        }
        else
        {
            var result = await task; result.Sha.ShouldBeNull(); result.SkipReason.ShouldBe("git_error");
            result.Error.ShouldNotContain("C408_SYNTHETIC_CREDENTIAL_ERROR");
            if (fault == "timeout") File.Exists(Path.Combine(repo.Path, "hook-started")).ShouldBeTrue();
        }
        (await repo.GitReadAsync("rev-parse", "HEAD")).ShouldBe(head);
        File.Exists(Path.Combine(repo.Path, "hook-ended")).ShouldBeFalse();
        if (fault is "timeout" or "cancel") await AssertOwnedHookKilledAsync(repo.Path);
        if (File.Exists(marker)) File.Delete(marker);
        if (File.Exists(hook)) File.Delete(hook);
        var retry = await Repository().CommitAsync(repo.Path, directory, expected, "public sync", default);
        retry.Error.ShouldBeNull(); retry.Sha.ShouldNotBeNull();
        (await repo.GitReadAsync("show", "HEAD:"+relative)).ShouldBe("C408_PUBLIC\n");
    }

    [Test]
    [Arguments("missing")] [Arguments("reappeared")]
    public async Task Missing_desired_or_reappearing_deleted_file_refuses_byte_recheck(string change)
    {
        using var repo = new ScratchGitRepo("c408-recheck"); await repo.CommitFileAsync("seed", "seed");
        var directory = Directory.CreateDirectory(Path.Combine(repo.Path, "docs/cards/board")).FullName;
        const string path = "docs/cards/board/card.md";
        if (change == "reappeared") await File.WriteAllTextAsync(Path.Combine(repo.Path, path), "C408_CHANGED");
        var head = await repo.GitReadAsync("rev-parse", "HEAD");
        var result = await Repository().CommitAsync(repo.Path, directory, new Dictionary<string, string?> { [path] = change == "missing" ? "expected" : null }, "public sync", default);
        result.SkipReason.ShouldBe("generated_files_changed"); result.Sha.ShouldBeNull();
        (await repo.GitReadAsync("rev-parse", "HEAD")).ShouldBe(head);
        (await repo.GitReadAsync("diff", "--cached", "--name-only")).ShouldBeEmpty();
    }

    // Git for Windows sh has no sleep, so a failing sleep falls through and writes hook-ended.
    // The hook waits for hook-release, which this test writes only after the hook pid is gone.
    private static string StallHook()
    {
        var wait = OperatingSystem.IsWindows()
            ? "ping -n 2 127.0.0.1 >/dev/null 2>&1"
            : "sleep 0.2";
        return "#!/bin/sh\n"
            + "echo $$ > hook-pid\n"
            + "echo started > hook-started\n"
            + "while [ ! -f hook-release ]; do\n"
            + $"  {wait} || {{ echo wait-failed > hook-wait-failed; exit 1; }}\n"
            + "done\n"
            + "echo ended > hook-ended\n";
    }

    private static async Task AssertOwnedHookKilledAsync(string root)
    {
        var ended = Path.Combine(root, "hook-ended");
        File.Exists(ended).ShouldBeFalse();
        File.Exists(Path.Combine(root, "hook-wait-failed")).ShouldBeFalse();
        var pid = int.Parse((await File.ReadAllTextAsync(Path.Combine(root, "hook-pid"))).Trim());
        var waited = Stopwatch.StartNew();
        while (HookAlive(pid) && waited.Elapsed < TimeSpan.FromSeconds(15))
            await Task.Delay(50);
        HookAlive(pid).ShouldBeFalse();
        File.Exists(ended).ShouldBeFalse();
        await File.WriteAllTextAsync(Path.Combine(root, "hook-release"), "release\n");
        var released = Stopwatch.StartNew();
        while (!File.Exists(ended) && released.Elapsed < TimeSpan.FromSeconds(3))
            await Task.Delay(50);
        File.Exists(ended).ShouldBeFalse();
    }

    private static bool HookAlive(int pid)
    {
        if (pid <= 0) return false;
        if (OperatingSystem.IsWindows())
        {
            try
            {
                using var process = Process.GetProcessById(pid);
                return !process.HasExited;
            }
            catch (ArgumentException) { return false; }
            catch (InvalidOperationException) { return false; }
        }

        try
        {
            var stat = File.ReadAllText($"/proc/{pid}/stat");
            var close = stat.LastIndexOf(')');
            if (close < 0 || close + 2 >= stat.Length) return false;
            return stat[close + 2] is not ('Z' or 'X');
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }
}
