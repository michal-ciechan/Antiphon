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
[ParallelLimiter<ProcessSpawnLimit>]
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
        await File.WriteAllTextAsync(hook, "#!/bin/sh\necho C408_CHANGED > docs/cards/board/card.md\necho reached > c408-hook-reached\n");
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
        else await File.WriteAllTextAsync(hook, fault == "stderr"
            ? "#!/bin/sh\necho C408_SYNTHETIC_CREDENTIAL_ERROR >&2\nexit 1\n"
            : "#!/bin/sh\necho started > hook-started\nsleep 8\necho ended > hook-ended\n");
        var expected = new Dictionary<string, string?> { [relative] = "C408_PUBLIC\n" };
        using var cancel = new CancellationTokenSource();
        var task = Repository(fault == "timeout" ? 2 : 30).CommitAsync(repo.Path, directory, expected, "public sync", cancel.Token);
        if (fault == "cancel")
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            try { while (!File.Exists(Path.Combine(repo.Path, "hook-started"))) { if (task.IsCompleted) break; await Task.Delay(25, deadline.Token); } }
            finally { cancel.Cancel(); }
            File.Exists(Path.Combine(repo.Path, "hook-started")).ShouldBeTrue();
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
}
