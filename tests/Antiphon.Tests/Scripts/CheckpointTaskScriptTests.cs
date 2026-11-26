using System.IO.Abstractions;
using System.Text.Json;
using Antiphon.Nightly;
using Antiphon.Tests.Infrastructure;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// CARD-0153 S4. The checkpoint-task command (Antiphon.Nightly.CheckpointTask, which replaced scripts/checkpoint-task.ps1 in unit
/// 29 of the 2026-11-22 plan) in process, against a recording transport standing in for the API and a real temp git repository:
/// the WIP commit and its subject, a clean tree, a directory that is not a repository, the dry run, the shared-checkout refusal,
/// the worktree that wins over the caller's directory (CARD-0217 S5), the push with and without an upstream (CARD-0217 S8), an
/// unresolvable task and the command line. git is the only child.
/// </summary>
[Category("Integration")]
[ParallelLimiter<GitProcessSpawnLimit>]
public sealed class CheckpointTaskScriptTests
{
    private const string Self = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa";
    private const string Other = "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb";

    [Test]
    public async Task Dirty_worktree_commits_a_wip_checkpoint()
    {
        using var repo = new ScratchGitRepo("card0153-ckpt");
        await repo.CommitFileAsync("README.md", "base\n");
        await File.WriteAllTextAsync(Path.Combine(repo.Path, "wip.cs"), "uncommitted\n");

        var http = Api(repo.Path);
        var run = Run(http, "--task-id", Self);

        run.Exit.ShouldBe(0, run.All);
        run.Output.ShouldContain("checkpoint ");
        run.Output.ShouldContain("not verified");
        var status = await ScratchGitRepo.GitInAsync(repo.Path, "status", "--porcelain");
        status.StdOut.Trim().ShouldBeEmpty("tree is clean after the checkpoint");
        var log = (await repo.GitReadAsync("log", "-1", "--pretty=%s")).Trim();
        log.ShouldBe("wip(checkpoint): CARD-0153 task aaaaaaaa - stalled, 1 files, not verified");
        var sha = (await repo.GitReadAsync("rev-parse", "HEAD")).Trim();
        run.Output.ShouldContain("checkpoint " + sha + Environment.NewLine);
        run.Output.ShouldContain("re-dispatch: scripts/antiphon delegate create --role Code --goal \"resume from checkpoint " + sha + "\" --dir \"" + Slashed(repo.Path) + "\"");
        http.Calls.Select(c => c.Method + " " + c.Path).ShouldBe(
            ["GET /api/agent-tasks/" + Self, "GET /api/agent-tasks?status=Working", "GET /api/agent-tasks?status=Dispatched"]);
        http.Calls.ShouldAllBe(c => c.Headers["X-Antiphon-Caller"] == "checkpoint-task.ps1" && !c.Headers.ContainsKey("X-Antiphon-Task-Token"));
    }

    [Test]
    public async Task Clean_worktree_exits_zero_with_nothing_to_checkpoint()
    {
        using var repo = new ScratchGitRepo("card0153-ckpt-clean");
        await repo.CommitFileAsync("README.md", "base\n");

        var run = Run(Api(repo.Path), "--task-id", Self);

        run.Exit.ShouldBe(0, run.All);
        run.Output.Trim().ShouldBe("nothing to checkpoint");
        var count = (await repo.GitReadAsync("rev-list", "--count", "HEAD")).Trim();
        count.ShouldBe("1", "no empty commit");
    }

    [Test]
    public void Non_git_directory_exits_2()
    {
        var dir = Directory.CreateTempSubdirectory("card0153-nogit").FullName;
        try
        {
            var run = Run(Api(dir), "--task-id", Self);
            run.Exit.ShouldBe(2, run.All);
            run.Error.Trim().ShouldBe("Not a git repository: " + Slashed(dir));
        }
        finally
        {
            ReadOnlyDirectory.Delete(dir);
        }
    }

    [Test]
    public async Task DryRun_lists_files_and_does_not_commit()
    {
        using var repo = new ScratchGitRepo("card0153-ckpt-dry");
        await repo.CommitFileAsync("README.md", "base\n");
        await File.WriteAllTextAsync(Path.Combine(repo.Path, "wip.cs"), "uncommitted\n");
        await File.WriteAllTextAsync(Path.Combine(repo.Path, "README.md"), "changed\n");

        var run = Run(Api(repo.Path), "--task-id", Self, "--dry-run");

        run.Exit.ShouldBe(0, run.All);
        run.Output.Replace("\r\n", "\n").ShouldBe("Would checkpoint:\n   M README.md\n  ?? wip.cs\n");
        var count = (await repo.GitReadAsync("rev-list", "--count", "HEAD")).Trim();
        count.ShouldBe("1");
    }

    [Test]
    public async Task Shared_checkout_with_another_active_worker_exits_3()
    {
        using var repo = new ScratchGitRepo("card0153-ckpt-shared");
        await repo.CommitFileAsync("README.md", "base\n");
        await File.WriteAllTextAsync(Path.Combine(repo.Path, "wip.cs"), "uncommitted\n");

        // The other worker's directory is the same checkout written differently: a trailing separator and another case.
        var run = Run(Api(repo.Path, other: repo.Path.ToUpperInvariant() + Path.DirectorySeparatorChar), "--task-id", Self);

        run.Exit.ShouldBe(3, run.All);
        run.Error.Trim().ShouldBe("Refusing to checkpoint a shared checkout with another active worker: bbbbbbbb");
        var count = (await repo.GitReadAsync("rev-list", "--count", "HEAD")).Trim();
        count.ShouldBe("1", "refused, so nothing committed");
    }

    [Test]
    public async Task The_worktree_wins_over_the_callers_directory_and_an_unreadable_list_is_skipped()
    {
        // CARD-0217 S5: workingDirectory is where the delegate command ran; the edits live in worktreePath.
        using var caller = new ScratchGitRepo("card0153-ckpt-caller");
        await caller.CommitFileAsync("README.md", "caller\n");
        using var worktree = new ScratchGitRepo("card0153-ckpt-worktree");
        await worktree.CommitFileAsync("README.md", "base\n");
        await File.WriteAllTextAsync(Path.Combine(worktree.Path, "wip.cs"), "uncommitted\n");
        var task = JsonSerializer.Serialize(new
        {
            summary = new { id = Self, title = "no card here", workingDirectory = Slashed(caller.Path), worktreePath = Slashed(worktree.Path) },
        });
        var http = new RecordingOperatorHttp(call => call.Path.Contains("status=Working", StringComparison.Ordinal)
            ? new NightlyReportHttpResponse(500, "{\"detail\":\"list down\"}")
            : call.Path.Contains("status=", StringComparison.Ordinal) ? new NightlyReportHttpResponse(200, """{"items":null}""") : new NightlyReportHttpResponse(200, task));

        var run = Run(http, "--task-id", Self);

        run.Exit.ShouldBe(0, run.All);
        (await worktree.GitReadAsync("log", "-1", "--pretty=%s")).Trim().ShouldBe("wip(checkpoint): task aaaaaaaa - stalled, 1 files, not verified");
        (await caller.GitReadAsync("rev-list", "--count", "HEAD")).Trim().ShouldBe("1", "the caller's checkout is untouched");
    }

    [Test]
    public async Task Push_sets_the_upstream_on_origin_when_the_branch_has_none_and_pushes_when_it_has_one()
    {
        using var repo = new ScratchGitRepo("card0153-ckpt-push");
        await repo.CommitFileAsync("README.md", "base\n");
        await repo.AddBareOriginAsync();
        await repo.GitAsync("checkout", "-b", "wip-task");
        await File.WriteAllTextAsync(Path.Combine(repo.Path, "wip.cs"), "uncommitted\n");

        var first = Run(Api(repo.Path), "--task-id", Self, "--push");

        first.Exit.ShouldBe(0, first.All);
        (await repo.GitReadAsync("rev-parse", "--abbrev-ref", "--symbolic-full-name", "@{u}")).Trim().ShouldBe("origin/wip-task");
        var head = (await repo.GitReadAsync("rev-parse", "HEAD")).Trim();
        (await repo.GitReadAsync("ls-remote", "origin", "refs/heads/wip-task")).ShouldStartWith(head);

        await File.WriteAllTextAsync(Path.Combine(repo.Path, "more.cs"), "uncommitted\n");
        var second = Run(Api(repo.Path), "--task-id", Self, "--push");

        second.Exit.ShouldBe(0, second.All);
        var next = (await repo.GitReadAsync("rev-parse", "HEAD")).Trim();
        next.ShouldNotBe(head);
        (await repo.GitReadAsync("ls-remote", "origin", "refs/heads/wip-task")).ShouldStartWith(next);
    }

    [Test]
    public void An_unresolvable_task_exits_2_before_git_and_sends_the_task_token()
    {
        var http = new RecordingOperatorHttp(_ => new NightlyReportHttpResponse(404, """{"detail":"Task not found"}"""));

        var run = Run(http, ["--task-id", "missing id"], taskToken: "synthetic-task-token");

        run.Exit.ShouldBe(2, run.All);
        run.Output.ShouldBeEmpty();
        run.Error.Trim().ShouldBe("Could not resolve task 'missing id' from http://127.0.0.1:17202 : Task not found");
        var call = http.Calls.ShouldHaveSingleItem();
        call.Uri.ShouldBe("http://127.0.0.1:17202/api/agent-tasks/missing%20id");
        call.Headers["X-Antiphon-Task-Token"].ShouldBe("synthetic-task-token");
        run.All.ShouldNotContain("synthetic-task-token");
    }

    [Test]
    public void A_malformed_command_line_is_a_usage_error()
    {
        Should.Throw<NightlyUsageException>(() => CheckpointTaskOptions.Parse([], null, null)).Message.ShouldBe("missing --task-id");
        Should.Throw<NightlyUsageException>(() => CheckpointTaskOptions.Parse(["-TaskId", Self], null, null));
        Should.Throw<NightlyUsageException>(() => CheckpointTaskOptions.Parse(["--task-id", Self, "--force"], null, null)).Message.ShouldBe("unknown flag --force");
        var options = CheckpointTaskOptions.Parse(["--task-id", Self, "--push", "--dry-run"], "http://api.test/", null);
        (options.TaskId, options.Push, options.DryRun, options.Api, options.TaskToken).ShouldBe((Self, true, true, "http://api.test", ""));
        CheckpointTaskOptions.Parse(["--task-id", Self], "", "t").Api.ShouldBe("http://localhost:17202");
    }

    // The API as the server answers it: the task in its summary (title naming a card), the Working list with the task itself
    // (and, given other, a second worker in that directory), and an empty Dispatched list.
    private static RecordingOperatorHttp Api(string workingDirectory, string? other = null)
    {
        var dir = Slashed(workingDirectory);
        var self = new
        {
            id = Self,
            summary = new { id = Self, title = "CARD-0153 stall checkpoint", status = "Working", workspace = "Shared", workingDirectory = dir },
        };
        object Envelope(params object[] items) => new { scope = (object?)null, items, excluded = new { total = 0, unscoped = 0, byProject = Array.Empty<object>() } };
        return new RecordingOperatorHttp(call =>
        {
            object body = call.Path.Contains("status=Working", StringComparison.Ordinal)
                ? other is null
                    ? Envelope(self)
                    : Envelope(self, new { id = Other, workingDirectory = other, summary = new { id = Other, status = "Working", workingDirectory = other } })
                : call.Path.Contains("status=", StringComparison.Ordinal) ? Envelope() : self;
            return new NightlyReportHttpResponse(200, JsonSerializer.Serialize(body));
        });
    }

    // The server answers forward slashes; the command prints the directory as the task names it.
    private static string Slashed(string path) => path.Replace('\\', '/');

    private static BoardCommandRun.Result Run(RecordingOperatorHttp http, params string[] args) => Run(http, args, taskToken: null);

    private static BoardCommandRun.Result Run(RecordingOperatorHttp http, string[] args, string? taskToken) =>
        BoardCommandRun.Run(host => CheckpointTask.Run(CheckpointTaskOptions.Parse(args, BoardCommandRun.Api, taskToken), host, new FileSystem(), http.Transport));
}
