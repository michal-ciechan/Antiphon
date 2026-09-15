using Antiphon.Server.Application.Services;
using Antiphon.Server.Infrastructure.Git;
using Antiphon.Tests.TestHelpers;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class GatedCommitServiceTests
{
    [Test]
    [Arguments("allowed.txt", false, true)]
    [Arguments("allowed.txt", false, false)]
    [Arguments("blocked.txt", true, true)]
    [Arguments("blocked.txt", true, false)]
    [Arguments("!literal.txt", true, true)]
    [Arguments("!literal.txt", true, false)]
    public async Task Ignore_exceptions_and_escaped_literals_follow_Git_semantics(string path, bool ignored, bool scoped)
    {
        using var repo = await SeedAsync();
        await repo.CommitFileAsync(".gitignore", "_private/\n*.txt\n!allowed.txt\n\\!literal.txt\n");
        await File.WriteAllTextAsync(Path.Combine(repo.Path, path), "candidate");
        if (ignored) await repo.GitAsync("add", "-f", path);
        var head = await repo.GitReadAsync("rev-parse", "HEAD");
        var index = await repo.GitReadAsync("diff", "--cached", "--binary");
        var (svc, spy) = Gate();
        var result = await svc.CommitAsync(repo.Path, scoped ? [path] : null, Message(), Trailers(), CancellationToken.None);
        result.Outcome.ShouldBe(ignored ? GatedCommitOutcome.IgnoredPathStaged : GatedCommitOutcome.Committed);
        if (ignored)
        {
            result.Refusals.ShouldHaveSingleItem().Path.ShouldBe(path);
            result.Refusals[0].Rule.ShouldEndWith(path == "!literal.txt" ? "\\!literal.txt" : "*.txt");
            spy.Verbs.ShouldNotContain("add");
            spy.Verbs.ShouldNotContain("commit");
            (await repo.GitReadAsync("rev-parse", "HEAD")).ShouldBe(head);
            (await repo.GitReadAsync("diff", "--cached", "--binary")).ShouldBe(index);
        }
        else
        {
            result.Files.ShouldBe(new[] { path });
            result.Refusals.ShouldBeEmpty();
            result.Sha.ShouldBe((await repo.GitReadAsync("rev-parse", "HEAD")).Trim());
            (await repo.GitReadAsync("show", $"HEAD:{path}")).ShouldBe("candidate");
        }
        (await File.ReadAllTextAsync(Path.Combine(repo.Path, path))).ShouldBe("candidate");
        spy.Verbs.ShouldNotContain("push");
    }

    [Test]
    public async Task Post_commit_receipt_uses_the_operation_even_when_HEAD_advances()
    {
        using var repo = await SeedAsync();
        await File.WriteAllTextAsync(Path.Combine(repo.Path, "new.txt"), "new");
        var (svc, spy) = Gate();
        string? committed = null;
        spy.BeforeRun = async args =>
        {
            if (committed is null && spy.Verbs.Contains("commit") && args[0] == "rev-parse")
            {
                committed = (await repo.GitReadAsync("rev-parse", "HEAD")).Trim();
                await repo.CommitFileAsync("later.txt", "later");
            }
        };
        var result = await svc.CommitAsync(repo.Path, ["new.txt"], Message(), Trailers(), CancellationToken.None);
        committed.ShouldNotBeNull();
        result.Sha.ShouldBe(committed);
        result.Files.ShouldBe(new[] { "new.txt" });
        (await repo.GitReadAsync("rev-parse", "HEAD")).Trim().ShouldNotBe(committed);
        spy.Verbs.Count(v => v == "commit").ShouldBe(1);
    }

    [Test]
    [Arguments("unborn")]
    [Arguments("head-error")]
    [Arguments("history-error")]
    public async Task Settlement_recovery_distinguishes_unborn_from_unavailable_history(string variant)
    {
        using var repo = new ScratchGitRepo("c527-unborn");
        if (variant == "history-error") await repo.CommitFileAsync("seed.txt", "seed");
        var spy = new RecordingGitWorkspaceService();
        spy.OverrideRun = args => (variant == "head-error" && args[0] == "rev-parse")
            || (variant == "history-error" && args[0] == "log") ? (128, "", "inspection unavailable") : null;
        var result = await spy.FindSettlementCommitsAsync(repo.Path, Guid.NewGuid(), new string('a', 64), CancellationToken.None);
        result.Succeeded.ShouldBe(variant == "unborn");
        result.Items.ShouldBeEmpty();
        spy.Verbs.ShouldNotContain("add");
        spy.Verbs.ShouldNotContain("commit");
    }

    [Test]
    [Arguments("valid")]
    [Arguments("body")]
    [Arguments("task")]
    [Arguments("gate")]
    [Arguments("identity")]
    [Arguments("duplicate")]
    public async Task Settlement_recovery_requires_exact_unique_trailers(string variant)
    {
        using var repo = await SeedAsync();
        var id = Guid.NewGuid();
        var settlement = new string('a', 64);
        var task = variant == "task" ? Guid.NewGuid() : id;
        var gate = variant == "gate" ? "ungated" : "gated";
        var identity = variant == "identity" ? new string('b', 64) : settlement;
        var trailers = $"antiphon: true\nantiphon-task: {task:D}\nantiphon-commit: {gate}\nantiphon-settlement: {identity}";
        if (variant == "duplicate") trailers += "\nantiphon-task: " + Guid.NewGuid();
        var message = "settlement fixture\n\n" + trailers + (variant == "body" ? "\n\nOrdinary prose after the apparent trailers." : "");
        await repo.GitAsync("commit", "--allow-empty", "-m", message);
        var result = await new RecordingGitWorkspaceService().FindSettlementCommitsAsync(repo.Path, id, settlement, CancellationToken.None);
        result.Succeeded.ShouldBeTrue();
        result.Items.Count.ShouldBe(variant == "valid" ? 1 : 0);
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(true, false)]
    [Arguments(false, true)]
    [Arguments(true, true)]
    public async Task Renamed_ignore_rule_endpoint_refuses_before_staging(bool scoped, bool destination)
    {
        using var repo = await SeedAsync();
        if (destination)
        {
            await repo.GitAsync("mv", "tracked.txt", "nested-ignore");
            await repo.GitAsync("commit", "-m", "rename fixture");
            Directory.CreateDirectory(System.IO.Path.Combine(repo.Path, "nested"));
            await repo.GitAsync("mv", "nested-ignore", "nested/.gitignore");
        }
        else
            await repo.GitAsync("mv", ".gitignore", "saved-rules.txt");
        await File.WriteAllTextAsync(System.IO.Path.Combine(repo.Path, "new.txt"), "new");
        var head = await repo.GitReadAsync("rev-parse", "HEAD");
        var index = await repo.GitReadAsync("diff", "--cached", "--binary");
        var (svc, spy) = Gate();

        var result = await svc.CommitAsync(repo.Path, scoped ? ["new.txt"] : null,
            Message(), Trailers(), CancellationToken.None);

        result.Outcome.ShouldBe(GatedCommitOutcome.IgnoreRulesChanged);
        result.Refusals.ShouldContain(r => r.Path == (destination ? "nested/.gitignore" : ".gitignore"));
        spy.Verbs.ShouldNotContain("add");
        spy.Verbs.ShouldNotContain("commit");
        (await repo.GitReadAsync("rev-parse", "HEAD")).ShouldBe(head);
        (await repo.GitReadAsync("diff", "--cached", "--binary")).ShouldBe(index);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Scoped_rename_selecting_either_endpoint_commits_both(bool oldEndpoint)
    {
        using var repo = await SeedAsync();
        await repo.GitAsync("mv", "tracked.txt", "renamed.txt");
        var (svc, _) = Gate();
        var result = await svc.CommitAsync(repo.Path, [oldEndpoint ? "tracked.txt" : "renamed.txt"],
            Message(), Trailers(), CancellationToken.None);
        result.Outcome.ShouldBe(GatedCommitOutcome.Committed);
        result.Files.Order().ShouldBe(new[] { "renamed.txt", "tracked.txt" });
        (await repo.GitReadAsync("status", "--porcelain")).ShouldBeEmpty();
    }

    [Test]
    [Arguments(128)]
    [Arguments(-1)]
    public async Task Unavailable_ignore_check_refuses_before_staging(int exitCode)
    {
        using var repo = await SeedAsync();
        await File.WriteAllTextAsync(System.IO.Path.Combine(repo.Path, "new.txt"), "new");
        var (svc, spy) = Gate();
        spy.OverrideRun = args => args[0] == "check-ignore" ? (exitCode, "", "inspection unavailable") : null;
        var head = await repo.GitReadAsync("rev-parse", "HEAD");
        var result = await svc.CommitAsync(repo.Path, ["new.txt"], Message(), Trailers(), CancellationToken.None);
        result.Outcome.ShouldBe(GatedCommitOutcome.CommitFailed);
        result.Stderr.ShouldContain("inspection unavailable");
        spy.Verbs.ShouldNotContain("add");
        spy.Verbs.ShouldNotContain("commit");
        (await repo.GitReadAsync("rev-parse", "HEAD")).ShouldBe(head);
        (await repo.GitReadAsync("diff", "--cached", "--name-only")).ShouldBeEmpty();
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(true, false)]
    [Arguments(false, true)]
    public async Task Unavailable_post_stage_inspection_restores_exact_prior_index(bool scoped, bool ignoreCheck)
    {
        using var repo = await SeedAsync();
        await File.WriteAllTextAsync(System.IO.Path.Combine(repo.Path, "tracked.txt"), "staged version");
        await repo.GitAsync("add", "tracked.txt");
        await File.WriteAllTextAsync(System.IO.Path.Combine(repo.Path, "tracked.txt"), "unstaged version");
        await File.WriteAllTextAsync(System.IO.Path.Combine(repo.Path, "new.txt"), "new");
        var index = await repo.GitReadAsync("diff", "--cached", "--binary");
        var head = await repo.GitReadAsync("rev-parse", "HEAD");
        var (svc, spy) = Gate();
        var staged = false;
        spy.BeforeRun = args => { if (args[0] == "add") staged = true; return Task.CompletedTask; };
        spy.OverrideRun = args => staged && (ignoreCheck ? args[0] == "check-ignore"
            : args[0] == "diff" && args.Contains("--cached")) ? (128, "", "late inspection unavailable") : null;

        var result = await svc.CommitAsync(repo.Path, scoped ? ["new.txt"] : null,
            Message(), Trailers(), CancellationToken.None);

        result.Outcome.ShouldBe(GatedCommitOutcome.CommitFailed);
        result.Stderr.ShouldContain("late inspection unavailable");
        spy.Verbs.ShouldNotContain("commit");
        (await repo.GitReadAsync("diff", "--cached", "--binary")).ShouldBe(index);
        (await repo.GitReadAsync("rev-parse", "HEAD")).ShouldBe(head);
        (await File.ReadAllTextAsync(System.IO.Path.Combine(repo.Path, "tracked.txt"))).ShouldBe("unstaged version");
    }

    [Test]
    public async Task Case1_new_file_beside_ignored_untracked_commits_only_the_new_file()
    {
        using var repo = await SeedAsync();
        await File.WriteAllTextAsync(System.IO.Path.Combine(repo.Path, "_private", "more.txt"), "secret");
        await File.WriteAllTextAsync(System.IO.Path.Combine(repo.Path, "new.txt"), "new");
        var (svc, spy) = Gate();

        var result = await svc.CommitAsync(repo.Path, pathspec: null, Message(), Trailers(), CancellationToken.None);

        result.Outcome.ShouldBe(GatedCommitOutcome.Committed);
        result.Files.ShouldBe(["new.txt"]);
        File.Exists(System.IO.Path.Combine(repo.Path, "_private", "more.txt")).ShouldBeTrue();
        (await repo.GitReadAsync("ls-files")).ShouldNotContain("_private/more.txt");
        (await repo.GitReadAsync("status", "--porcelain", "--ignored")).ShouldContain("_private/");
        spy.Verbs.ShouldContain("commit");
        spy.Verbs.ShouldNotContain("push");
    }

    [Test]
    public async Task Case2_tracked_then_ignored_file_is_refused_IgnoredPathStaged()
    {
        using var repo = await SeedAsync();
        await File.WriteAllTextAsync(System.IO.Path.Combine(repo.Path, ".gitignore"), "_private/\n*.secret\ntracked.txt\n");
        await repo.GitAsync("add", ".gitignore");
        await repo.GitAsync("commit", "-m", "ignore tracked");
        await File.WriteAllTextAsync(System.IO.Path.Combine(repo.Path, "tracked.txt"), "changed");
        await File.WriteAllTextAsync(System.IO.Path.Combine(repo.Path, "new.txt"), "new");
        var (svc, _) = Gate();
        var head = (await repo.GitReadAsync("rev-parse", "HEAD")).Trim();

        var result = await svc.CommitAsync(
            repo.Path, ["tracked.txt", "new.txt"], Message(), Trailers(), CancellationToken.None);

        result.Outcome.ShouldBe(GatedCommitOutcome.IgnoredPathStaged);
        result.Refusals.ShouldHaveSingleItem();
        result.Refusals[0].Path.ShouldBe("tracked.txt");
        result.Refusals[0].Rule.ShouldBe(".gitignore:3:tracked.txt");
        (await repo.GitReadAsync("diff", "--cached", "--name-only")).Trim().ShouldBeEmpty();
        (await repo.GitReadAsync("rev-parse", "HEAD")).Trim().ShouldBe(head);
    }

    [Test]
    public async Task Case3_deleted_gitignore_is_refused_IgnoreRulesChanged_and_nothing_is_staged()
    {
        using var repo = await SeedAsync();
        File.Delete(System.IO.Path.Combine(repo.Path, ".gitignore"));
        await File.WriteAllTextAsync(System.IO.Path.Combine(repo.Path, "new.txt"), "new");
        var (svc, spy) = Gate();
        var head = (await repo.GitReadAsync("rev-parse", "HEAD")).Trim();

        foreach (IReadOnlyList<string>? pathspec in new IReadOnlyList<string>?[] { null, ["new.txt"] })
        {
            spy.Verbs.Clear();
            var result = await svc.CommitAsync(repo.Path, pathspec, Message(), Trailers(), CancellationToken.None);
            result.Outcome.ShouldBe(GatedCommitOutcome.IgnoreRulesChanged);
            result.Refusals.ShouldContain(r => r.Path == ".gitignore");
            spy.Verbs.ShouldNotContain("add");
            (await repo.GitReadAsync("diff", "--cached", "--name-only")).Trim().ShouldBeEmpty();
            (await repo.GitReadAsync("rev-parse", "HEAD")).Trim().ShouldBe(head);
        }
    }

    [Test]
    [Arguments("docs/.gitignore", "modified")]
    [Arguments("sub/deep/.gitignore", "untracked")]
    [Arguments(".gitignore", "modified")]
    public async Task Case3b_modified_nested_or_new_gitignore_is_refused(string relative, string kind)
    {
        using var repo = await SeedAsync();
        var full = System.IO.Path.Combine(repo.Path, relative.Replace('/', System.IO.Path.DirectorySeparatorChar));
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);
        if (kind == "modified" && relative != ".gitignore")
        {
            await File.WriteAllTextAsync(full, "old\n");
            await repo.GitAsync("add", relative);
            await repo.GitAsync("commit", "-m", "add nested gitignore");
        }

        if (kind == "modified")
            await File.WriteAllTextAsync(full, relative == ".gitignore" ? "_private/\n*.secret\nextra\n" : "new\n");
        else
            await File.WriteAllTextAsync(full, "untracked\n");
        await File.WriteAllTextAsync(System.IO.Path.Combine(repo.Path, "new.txt"), "new");
        var (svc, spy) = Gate();

        var result = await svc.CommitAsync(repo.Path, pathspec: null, Message(), Trailers(), CancellationToken.None);

        result.Outcome.ShouldBe(GatedCommitOutcome.IgnoreRulesChanged);
        result.Refusals.ShouldContain(r => r.Path == relative);
        spy.Verbs.ShouldNotContain("add");
    }

    [Test]
    public async Task Case4_force_added_ignored_file_is_refused_naming_path_and_rule()
    {
        using var repo = await SeedAsync();
        await File.WriteAllTextAsync(System.IO.Path.Combine(repo.Path, "a.secret"), "secret");
        await repo.GitAsync("add", "-f", "a.secret");
        await repo.GitAsync("commit", "-m", "force add secret");
        await File.WriteAllTextAsync(System.IO.Path.Combine(repo.Path, "a.secret"), "changed");
        await File.WriteAllTextAsync(System.IO.Path.Combine(repo.Path, "new.txt"), "new");
        var (svc, _) = Gate();
        var head = (await repo.GitReadAsync("rev-parse", "HEAD")).Trim();

        var result = await svc.CommitAsync(
            repo.Path, ["a.secret", "new.txt"], Message(), Trailers(), CancellationToken.None);

        result.Outcome.ShouldBe(GatedCommitOutcome.IgnoredPathStaged);
        result.Refusals.ShouldContain(r => r.Path == "a.secret" && r.Rule.EndsWith("*.secret", StringComparison.Ordinal));
        result.Refusals[0].Rule.ShouldBe(".gitignore:2:*.secret");
        (await repo.GitReadAsync("rev-parse", "HEAD")).Trim().ShouldBe(head);
        (await repo.GitReadAsync("status", "--porcelain")).ShouldContain("new.txt");
    }

    [Test]
    public async Task Scoped_commit_leaves_a_foreign_staged_path_staged_and_out_of_the_commit()
    {
        using var repo = await SeedAsync();
        await File.WriteAllTextAsync(System.IO.Path.Combine(repo.Path, "foo.txt"), "foo");
        await File.WriteAllTextAsync(System.IO.Path.Combine(repo.Path, "bar.txt"), "bar");
        await repo.GitAsync("add", "foo.txt");
        var (svc, _) = Gate();

        var result = await svc.CommitAsync(repo.Path, ["bar.txt"], Message(), Trailers(), CancellationToken.None);

        result.Outcome.ShouldBe(GatedCommitOutcome.Committed);
        var staged = (await repo.GitReadAsync("diff", "--cached", "--name-only")).Trim();
        staged.ShouldBe("foo.txt");
        var committed = (await repo.GitReadAsync("diff-tree", "--no-commit-id", "--name-only", "-r", "HEAD")).Trim();
        committed.ShouldBe("bar.txt");
        committed.ShouldNotContain("foo.txt");
    }

    [Test]
    public async Task Held_lease_is_RepositoryBusy_and_nothing_is_staged()
    {
        using var repo = await SeedAsync();
        await File.WriteAllTextAsync(System.IO.Path.Combine(repo.Path, "new.txt"), "new");
        var spy = new RecordingGitWorkspaceService();
        var leases = new RepositoryMutationLease(new LandingGit());
        var svc = new GatedCommitService(spy, leases, NullLogger<GatedCommitService>.Instance);
        await using var held = await leases.TryAcquireAsync(repo.Path, CancellationToken.None);
        held.ShouldNotBeNull();

        var result = await svc.CommitAsync(repo.Path, pathspec: null, Message(), Trailers(), CancellationToken.None);

        result.Outcome.ShouldBe(GatedCommitOutcome.RepositoryBusy);
        spy.Verbs.ShouldNotContain("add");
        spy.Verbs.ShouldNotContain("commit");
        foreach (var verb in spy.Verbs)
            (verb is "rev-parse" or "status" or "--no-optional-locks").ShouldBeTrue(verb);
    }

    [Test]
    public async Task Trailers_name_the_task_and_the_gate()
    {
        using var repo = await SeedAsync();
        await File.WriteAllTextAsync(System.IO.Path.Combine(repo.Path, "new.txt"), "new");
        var (svc, _) = Gate();
        var id = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");

        var result = await svc.CommitAsync(
            repo.Path,
            ["new.txt"],
            "task abcd1234: wrote new.txt\n\nWrote a.md and b.md.",
            [("antiphon", "true"), ("antiphon-task", id.ToString()), ("antiphon-commit", "gated")],
            CancellationToken.None);

        result.Outcome.ShouldBe(GatedCommitOutcome.Committed);
        var body = await repo.GitReadAsync("log", "-1", "--format=%B");
        body.ShouldStartWith("task abcd1234: wrote new.txt");
        body.ShouldContain("Wrote a.md and b.md.");
        body.ShouldContain("antiphon: true");
        body.ShouldContain($"antiphon-task: {id}");
        body.ShouldContain("antiphon-commit: gated");
    }

    [Test]
    public async Task Nothing_is_ever_pushed()
    {
        using var repo = await SeedAsync();
        await repo.AddBareOriginAsync();
        var originBefore = (await repo.GitReadAsync("rev-parse", "origin/master")).Trim();
        await File.WriteAllTextAsync(System.IO.Path.Combine(repo.Path, "new.txt"), "new");
        var (svc, spy) = Gate();

        var result = await svc.CommitAsync(repo.Path, pathspec: null, Message(), Trailers(), CancellationToken.None);

        result.Outcome.ShouldBe(GatedCommitOutcome.Committed);
        spy.Verbs.ShouldContain("commit");
        spy.Verbs.ShouldNotContain("push");
        (await repo.GitReadAsync("rev-parse", "origin/master")).Trim().ShouldBe(originBefore);
    }

    [Test]
    public async Task Whole_tree_recheck_catches_a_file_force_added_mid_run_and_resets_the_index()
    {
        using var repo = await SeedAsync();
        await File.WriteAllTextAsync(System.IO.Path.Combine(repo.Path, "new.txt"), "new");
        var spy = new RecordingGitWorkspaceService();
        spy.BeforeRun = async args =>
        {
            if (args.Length > 0 && args[0] == "add")
            {
                await File.WriteAllTextAsync(System.IO.Path.Combine(repo.Path, "late.secret"), "late");
                (await ScratchGitRepo.GitInAsync(repo.Path, "add", "-f", "late.secret")).Ok.ShouldBeTrue();
            }
        };
        var svc = new GatedCommitService(
            spy, new RepositoryMutationLease(new LandingGit()), NullLogger<GatedCommitService>.Instance);
        var head = (await repo.GitReadAsync("rev-parse", "HEAD")).Trim();

        var result = await svc.CommitAsync(repo.Path, pathspec: null, Message(), Trailers(), CancellationToken.None);

        result.Outcome.ShouldBe(GatedCommitOutcome.IgnoredPathStaged);
        result.Refusals.ShouldContain(r => r.Path == "late.secret");
        (await repo.GitReadAsync("diff", "--cached", "--name-only")).Trim().ShouldBeEmpty();
        (await repo.GitReadAsync("rev-parse", "HEAD")).Trim().ShouldBe(head);
    }

    [Test]
    public async Task Scoped_commit_with_a_deletion_records_the_deletion()
    {
        using var repo = await SeedAsync();
        File.Delete(System.IO.Path.Combine(repo.Path, "tracked.txt"));
        var (svc, _) = Gate();

        var result = await svc.CommitAsync(repo.Path, ["tracked.txt"], Message(), Trailers(), CancellationToken.None);

        result.Outcome.ShouldBe(GatedCommitOutcome.Committed);
        var status = await repo.GitReadAsync("diff-tree", "--no-commit-id", "--name-status", "-r", "HEAD");
        status.ShouldContain("D");
        status.ShouldContain("tracked.txt");
    }

    [Test]
    public async Task Hook_failure_is_CommitFailed_and_index_stays_staged()
    {
        using var repo = await SeedAsync();
        await repo.InstallFailingPreCommitHookAsync("gate says no");
        await File.WriteAllTextAsync(System.IO.Path.Combine(repo.Path, "new.txt"), "new");
        var (svc, _) = Gate();
        var head = (await repo.GitReadAsync("rev-parse", "HEAD")).Trim();

        var result = await svc.CommitAsync(repo.Path, ["new.txt"], Message(), Trailers(), CancellationToken.None);

        result.Outcome.ShouldBe(GatedCommitOutcome.CommitFailed);
        result.Stderr.ShouldNotBeNull();
        result.Stderr.ShouldContain("gate says no");
        (await repo.GitReadAsync("rev-parse", "HEAD")).Trim().ShouldBe(head);
        (await repo.GitReadAsync("diff", "--cached", "--name-only")).Trim().ShouldBe("new.txt");
    }

    private static (GatedCommitService Svc, RecordingGitWorkspaceService Spy) Gate()
    {
        var spy = new RecordingGitWorkspaceService();
        var svc = new GatedCommitService(
            spy, new RepositoryMutationLease(new LandingGit()), NullLogger<GatedCommitService>.Instance);
        return (svc, spy);
    }

    private static async Task<ScratchGitRepo> SeedAsync()
    {
        var repo = new ScratchGitRepo("c527-gate");
        Directory.CreateDirectory(System.IO.Path.Combine(repo.Path, "_private"));
        await File.WriteAllTextAsync(System.IO.Path.Combine(repo.Path, ".gitignore"), "_private/\n*.secret\n");
        await File.WriteAllTextAsync(System.IO.Path.Combine(repo.Path, "tracked.txt"), "tracked\n");
        await repo.GitAsync("add", ".");
        await repo.GitAsync("commit", "-m", "seed");
        await File.WriteAllTextAsync(System.IO.Path.Combine(repo.Path, "_private", "verbatim.txt"), "verbatim");
        return repo;
    }

    private static string Message() => "task abcd1234: gate\n\nbody";

    private static (string Key, string Value)[] Trailers() =>
        [("antiphon", "true"), ("antiphon-task", Guid.NewGuid().ToString()), ("antiphon-commit", "gated")];
}
