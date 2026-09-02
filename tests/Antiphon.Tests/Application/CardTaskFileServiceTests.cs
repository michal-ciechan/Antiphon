using System.Text;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0004: load, render, compare, write, delete (S1), path-scoped commit with guards (S2),
/// and <c>SyncAllAsync</c> fleet sweep (S3). Assertions are scoped to this test's scratch
/// directory and its own rows — the assembly shares one Postgres.
/// </summary>
[Category("Integration")]
public class CardTaskFileServiceTests
{
    [Test]
    public async Task First_sync_writes_n_plus_index_with_lf_and_second_sync_writes_nothing()
    {
        await using var world = new World();
        var repo = world.CreateRepo();
        var project = await world.AddProjectAsync(repo.Path);
        var board = await world.AddBoardAsync(project.Id, "Antiphon");
        await world.AddCardAsync(board, "CARD-0001", "First");
        await world.AddCardAsync(board, "CARD-0002", "Second");

        var first = await world.SyncAsync(board.Id);

        first.WriteSkipReason.ShouldBeNull();
        first.Written.ShouldBe(3);
        first.Deleted.ShouldBe(0);
        first.Unchanged.ShouldBe(0);
        first.CommitSkipReason.ShouldBe("autocommit_disabled");
        first.CommitSha.ShouldBeNull();
        first.Directory.ShouldBe("docs/cards/antiphon");
        first.DryRun.ShouldBeFalse();

        var dir = world.BoardDir(repo, "antiphon");
        Directory.GetFiles(dir, "*.md").Length.ShouldBe(3);
        foreach (var file in Directory.GetFiles(dir, "*.md"))
            AssertLfNoBom(await File.ReadAllBytesAsync(file));

        var second = await world.SyncAsync(board.Id);
        second.Written.ShouldBe(0);
        second.Deleted.ShouldBe(0);
        second.Unchanged.ShouldBe(3);
        second.CommitSkipReason.ShouldBe("autocommit_disabled");
    }

    [Test]
    public async Task Title_edit_deletes_the_old_file_and_leaves_one_file_per_card()
    {
        await using var world = new World();
        var repo = world.CreateRepo();
        var project = await world.AddProjectAsync(repo.Path);
        var board = await world.AddBoardAsync(project.Id, "Edit Board");
        var card = await world.AddCardAsync(board, "CARD-0001", "Old Title");
        await world.SyncAsync(board.Id);

        await world.RenameCardAsync(card.Id, "New Title");
        var result = await world.SyncAsync(board.Id);

        var dir = world.BoardDir(repo, "edit-board");
        var names = Directory.GetFiles(dir, "*.md").Select(Path.GetFileName).OrderBy(n => n).ToArray();
        names.ShouldBe([
            "CARD-0001-new-title.md",
            "INDEX.md",
        ]);
        File.Exists(Path.Combine(dir, "CARD-0001-old-title.md")).ShouldBeFalse();
        result.Deleted.ShouldBe(1);
        result.Written.ShouldBeGreaterThanOrEqualTo(2);
    }

    [Test]
    public async Task Archived_card_keeps_its_file_with_archived_keys_and_an_archived_index_group()
    {
        await using var world = new World();
        var repo = world.CreateRepo();
        var project = await world.AddProjectAsync(repo.Path);
        var board = await world.AddBoardAsync(project.Id, "Archive Board");
        var card = await world.AddCardAsync(board, "CARD-0001", "Keep me");
        await world.ArchiveCardAsync(card.Id, "operator", "duplicate");

        var result = await world.SyncAsync(board.Id);
        result.Written.ShouldBe(2);

        var dir = world.BoardDir(repo, "archive-board");
        var body = await File.ReadAllTextAsync(Path.Combine(dir, "CARD-0001-keep-me.md"));
        body.ShouldContain("archived:");
        body.ShouldContain("archived_by: \"operator\"");
        body.ShouldContain("archived_reason: \"duplicate\"");
        var index = await File.ReadAllTextAsync(Path.Combine(dir, "INDEX.md"));
        index.ShouldContain("## Archived (1)");
        index.ShouldContain("1 card, 1 archived.");
    }

    [Test]
    public async Task Null_repository_path_skips_and_writes_nothing()
    {
        await using var world = new World();
        var project = await world.AddProjectAsync(localPath: null);
        var board = await world.AddBoardAsync(project.Id, "No Path");
        await world.AddCardAsync(board, "CARD-0001", "Ghost");

        var result = await world.SyncAsync(board.Id);

        result.WriteSkipReason.ShouldBe("no_repository_path");
        result.Directory.ShouldBeNull();
        result.Written.ShouldBe(0);
    }

    [Test]
    public async Task A_path_that_is_not_a_repo_skips_and_writes_nothing()
    {
        await using var world = new World();
        var dir = world.CreateNonRepoDir();
        var project = await world.AddProjectAsync(dir);
        var board = await world.AddBoardAsync(project.Id, "Not Git");
        await world.AddCardAsync(board, "CARD-0001", "Ghost");

        var result = await world.SyncAsync(board.Id);

        result.WriteSkipReason.ShouldBe("not_a_git_repository");
        Directory.Exists(Path.Combine(dir, "docs")).ShouldBeFalse();
    }

    [Test]
    public async Task Two_boards_in_one_project_each_with_card_0001_get_two_directories()
    {
        await using var world = new World();
        var repo = world.CreateRepo();
        var project = await world.AddProjectAsync(repo.Path);
        var a = await world.AddBoardAsync(project.Id, "Alpha");
        var b = await world.AddBoardAsync(project.Id, "Beta");
        await world.AddCardAsync(a, "CARD-0001", "From Alpha");
        await world.AddCardAsync(b, "CARD-0001", "From Beta");

        (await world.SyncAsync(a.Id)).Directory.ShouldBe("docs/cards/alpha");
        (await world.SyncAsync(b.Id)).Directory.ShouldBe("docs/cards/beta");

        File.Exists(Path.Combine(world.BoardDir(repo, "alpha"), "CARD-0001-from-alpha.md")).ShouldBeTrue();
        File.Exists(Path.Combine(world.BoardDir(repo, "beta"), "CARD-0001-from-beta.md")).ShouldBeTrue();
    }

    [Test]
    public async Task Stray_md_in_the_board_directory_is_deleted_and_txt_is_not()
    {
        await using var world = new World();
        var repo = world.CreateRepo();
        var project = await world.AddProjectAsync(repo.Path);
        var board = await world.AddBoardAsync(project.Id, "Stray");
        await world.AddCardAsync(board, "CARD-0001", "Keep");
        var dir = world.BoardDir(repo, "stray");
        Directory.CreateDirectory(dir);
        await File.WriteAllTextAsync(Path.Combine(dir, "notes.md"), "stale");
        await File.WriteAllTextAsync(Path.Combine(dir, "notes.txt"), "keep me");

        var result = await world.SyncAsync(board.Id);

        result.Deleted.ShouldBe(1);
        File.Exists(Path.Combine(dir, "notes.md")).ShouldBeFalse();
        File.Exists(Path.Combine(dir, "notes.txt")).ShouldBeTrue();
        (await File.ReadAllTextAsync(Path.Combine(dir, "notes.txt"))).ShouldBe("keep me");
    }

    [Test]
    public async Task Dry_run_reports_counts_and_writes_nothing()
    {
        await using var world = new World();
        var repo = world.CreateRepo();
        var project = await world.AddProjectAsync(repo.Path);
        var board = await world.AddBoardAsync(project.Id, "Dry");
        await world.AddCardAsync(board, "CARD-0001", "One");

        var result = await world.SyncAsync(board.Id, dryRun: true);

        result.DryRun.ShouldBeTrue();
        result.Written.ShouldBe(2);
        result.Deleted.ShouldBe(0);
        result.CommitSkipReason.ShouldBe("dry_run");
        result.Directory.ShouldBe("docs/cards/dry");
        Directory.Exists(world.BoardDir(repo, "dry")).ShouldBeFalse();
    }

    [Test]
    public async Task A_crlf_rewrite_on_disk_is_rewritten_lf_and_counts_as_written()
    {
        await using var world = new World();
        var repo = world.CreateRepo();
        var project = await world.AddProjectAsync(repo.Path);
        var board = await world.AddBoardAsync(project.Id, "Crlf");
        await world.AddCardAsync(board, "CARD-0001", "One");
        await world.SyncAsync(board.Id);

        var path = Path.Combine(world.BoardDir(repo, "crlf"), "CARD-0001-one.md");
        var lf = await File.ReadAllTextAsync(path);
        await File.WriteAllTextAsync(path, lf.Replace("\n", "\r\n", StringComparison.Ordinal));

        var result = await world.SyncAsync(board.Id);

        result.Written.ShouldBe(1);
        result.Unchanged.ShouldBe(1, "INDEX.md is untouched");
        var bytes = await File.ReadAllBytesAsync(path);
        AssertLfNoBom(bytes);
    }

    [Test]
    public async Task Archived_board_and_archived_project_skip_without_touching_disk()
    {
        await using var world = new World();
        var repo = world.CreateRepo();
        var liveProject = await world.AddProjectAsync(repo.Path);
        var archivedProject = await world.AddProjectAsync(repo.Path, archived: true);
        var archivedBoard = await world.AddBoardAsync(liveProject.Id, "Archived Board", archived: true);
        var boardOnArchivedProject = await world.AddBoardAsync(archivedProject.Id, "Orphan Board");
        await world.AddCardAsync(archivedBoard, "CARD-0001", "Hidden");
        await world.AddCardAsync(boardOnArchivedProject, "CARD-0001", "Hidden");

        (await world.SyncAsync(archivedBoard.Id)).WriteSkipReason.ShouldBe("board_archived");
        (await world.SyncAsync(boardOnArchivedProject.Id)).WriteSkipReason.ShouldBe("project_archived");
        Directory.Exists(Path.Combine(repo.Path, "docs")).ShouldBeFalse();
    }

    [Test]
    public async Task Colliding_board_slugs_suffix_the_later_board()
    {
        await using var world = new World();
        var repo = world.CreateRepo();
        var project = await world.AddProjectAsync(repo.Path);
        var first = await world.AddBoardAsync(project.Id, "Foo", createdAt: new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var later = await world.AddBoardAsync(project.Id, "Foo!!", createdAt: new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc));
        await world.AddCardAsync(first, "CARD-0001", "A");
        await world.AddCardAsync(later, "CARD-0001", "B");

        (await world.SyncAsync(first.Id)).Directory.ShouldBe("docs/cards/foo");
        var laterDir = (await world.SyncAsync(later.Id)).Directory;
        laterDir.ShouldNotBe("docs/cards/foo");
        laterDir.ShouldStartWith("docs/cards/foo-");
        laterDir!.Length.ShouldBe("docs/cards/foo-".Length + 8);
    }

    [Test]
    public async Task First_sync_commits_one_trailered_commit_and_second_sync_leaves_head()
    {
        await using var world = new World();
        var repo = world.CreateRepo();
        var project = await world.AddProjectAsync(repo.Path);
        var board = await world.AddBoardAsync(project.Id, "Sync Board");
        await world.AddCardAsync(board, "CARD-0001", "First");
        await world.AddCardAsync(board, "CARD-0002", "Second");
        var headBefore = (await repo.GitReadAsync("rev-parse", "HEAD")).Trim();

        var first = await world.SyncAsync(board.Id, autoCommit: true);

        first.WriteSkipReason.ShouldBeNull();
        first.CommitSkipReason.ShouldBeNull();
        first.CommitSha.ShouldNotBeNull();
        first.CommitSha.ShouldNotBe(headBefore);
        first.Written.ShouldBe(3);

        var subject = (await repo.GitReadAsync("log", "-1", "--format=%s")).Trim();
        subject.ShouldBe("antiphon: sync card files (Sync Board)");
        var trailer = (await repo.GitReadAsync("log", "-1", "--format=%(trailers:key=antiphon,valueonly)")).Trim();
        trailer.ShouldBe("true");
        var message = await repo.GitReadAsync("log", "-1", "--format=%B");
        message.ShouldNotContain("CARD-0001");
        message.ShouldNotContain("CARD-0002");
        SyncCommitCount(await repo.GitReadAsync("log", "--format=%s")).ShouldBe(1);
        (await repo.GitReadAsync("rev-parse", "HEAD")).Trim().ShouldBe(first.CommitSha);

        var second = await world.SyncAsync(board.Id, autoCommit: true);
        second.Written.ShouldBe(0);
        second.Deleted.ShouldBe(0);
        second.CommitSkipReason.ShouldBe("nothing_to_commit");
        second.CommitSha.ShouldBeNull();
        (await repo.GitReadAsync("rev-parse", "HEAD")).Trim().ShouldBe(first.CommitSha);
        SyncCommitCount(await repo.GitReadAsync("log", "--format=%s")).ShouldBe(1);
    }

    [Test]
    public async Task Title_edit_commit_is_one_rename_and_nothing_else()
    {
        await using var world = new World();
        var repo = world.CreateRepo();
        var project = await world.AddProjectAsync(repo.Path);
        var board = await world.AddBoardAsync(project.Id, "Rename Board");
        var card = await world.AddCardAsync(board, "CARD-0001", "Old Title");
        await world.SyncAsync(board.Id, autoCommit: true);

        await world.RenameCardAsync(card.Id, "New Title");
        var result = await world.SyncAsync(board.Id, autoCommit: true);

        result.CommitSkipReason.ShouldBeNull();
        result.CommitSha.ShouldNotBeNull();

        var status = await repo.GitReadAsync("diff", "--name-status", "--find-renames", "HEAD~1", "HEAD");
        var lines = SplitGitLines(status);
        lines.Count(l => l.StartsWith('R')).ShouldBe(1);
        lines.ShouldContain(l => l.Contains("CARD-0001-old-title.md", StringComparison.Ordinal)
            && l.Contains("CARD-0001-new-title.md", StringComparison.Ordinal));
        lines.ShouldAllBe(l =>
            l.StartsWith('R')
            || (l.StartsWith('M') && l.Contains("INDEX.md", StringComparison.Ordinal)));
        lines.ShouldNotContain(l => l.StartsWith('A') || l.StartsWith('D'));
    }

    [Test]
    public async Task Unrelated_staged_file_stays_staged_and_is_absent_from_the_sync_commit()
    {
        await using var world = new World();
        var repo = world.CreateRepo();
        var project = await world.AddProjectAsync(repo.Path);
        var board = await world.AddBoardAsync(project.Id, "Staged Board");
        await world.AddCardAsync(board, "CARD-0001", "Keep");
        await File.WriteAllTextAsync(Path.Combine(repo.Path, "sidecar.txt"), "stay staged");
        await repo.GitAsync("add", "sidecar.txt");

        var result = await world.SyncAsync(board.Id, autoCommit: true);

        result.CommitSha.ShouldNotBeNull();
        var staged = await repo.GitReadAsync("diff", "--cached", "--name-only");
        SplitGitLines(staged).ShouldContain("sidecar.txt");
        var committed = await repo.GitReadAsync("diff-tree", "--no-commit-id", "--name-only", "-r", "HEAD");
        SplitGitLines(committed).ShouldNotContain("sidecar.txt");
        SplitGitLines(committed).ShouldNotContain(p => p.Contains("sidecar", StringComparison.Ordinal));
    }

    [Test]
    public async Task Rebase_in_progress_writes_files_skips_commit_and_leaves_head()
    {
        await using var world = new World();
        var repo = world.CreateRepo();
        var project = await world.AddProjectAsync(repo.Path);
        var board = await world.AddBoardAsync(project.Id, "Rebase Board");
        await world.AddCardAsync(board, "CARD-0001", "One");
        var rebasePath = ResolveGitPath(repo.Path, (await repo.GitReadAsync("rev-parse", "--git-path", "rebase-merge")).Trim());
        Directory.CreateDirectory(rebasePath);
        var headBefore = (await repo.GitReadAsync("rev-parse", "HEAD")).Trim();

        var result = await world.SyncAsync(board.Id, autoCommit: true);

        result.Written.ShouldBeGreaterThan(0);
        result.CommitSkipReason.ShouldBe("rebase_in_progress");
        result.CommitSha.ShouldBeNull();
        File.Exists(Path.Combine(world.BoardDir(repo, "rebase-board"), "CARD-0001-one.md")).ShouldBeTrue();
        (await repo.GitReadAsync("rev-parse", "HEAD")).Trim().ShouldBe(headBefore);
    }

    [Test]
    public async Task Directory_removed_next_sync_commits()
    {
        await using var world = new World();
        var repo = world.CreateRepo();
        var project = await world.AddProjectAsync(repo.Path);
        var board = await world.AddBoardAsync(project.Id, "Removed Board");
        await world.AddCardAsync(board, "CARD-0001", "One");
        var first = await world.SyncAsync(board.Id, autoCommit: true);
        first.CommitSha.ShouldNotBeNull();

        Directory.Delete(world.BoardDir(repo, "removed-board"), recursive: true);
        // Removing the files from HEAD (not just the working tree) is the case that needs a
        // restore commit; a working-tree-only delete of identical content is nothing_to_commit.
        await repo.GitAsync("add", "-A", "--", "docs/cards");
        await repo.GitAsync("commit", "-m", "removed card files");
        var afterDelete = (await repo.GitReadAsync("rev-parse", "HEAD")).Trim();

        var second = await world.SyncAsync(board.Id, autoCommit: true);

        second.CommitSha.ShouldNotBeNull();
        second.CommitSha.ShouldNotBe(first.CommitSha);
        second.CommitSha.ShouldNotBe(afterDelete);
        second.Written.ShouldBeGreaterThan(0);
        second.CommitSkipReason.ShouldBeNull();
        File.Exists(Path.Combine(world.BoardDir(repo, "removed-board"), "INDEX.md")).ShouldBeTrue();
    }

    [Test]
    public async Task Detached_head_writes_files_and_skips_commit()
    {
        await using var world = new World();
        var repo = world.CreateRepo();
        var project = await world.AddProjectAsync(repo.Path);
        var board = await world.AddBoardAsync(project.Id, "Detach Board");
        await world.AddCardAsync(board, "CARD-0001", "One");
        await repo.GitAsync("checkout", "--detach");
        var headBefore = (await repo.GitReadAsync("rev-parse", "HEAD")).Trim();

        var result = await world.SyncAsync(board.Id, autoCommit: true);

        result.Written.ShouldBeGreaterThan(0);
        result.CommitSkipReason.ShouldBe("detached_head");
        result.CommitSha.ShouldBeNull();
        (await repo.GitReadAsync("rev-parse", "HEAD")).Trim().ShouldBe(headBefore);
        File.Exists(Path.Combine(world.BoardDir(repo, "detach-board"), "CARD-0001-one.md")).ShouldBeTrue();
    }

    [Test]
    public async Task AutoCommit_false_is_the_production_default_and_leaves_a_dirty_tree()
    {
        new CardFileSyncSettings().AutoCommit.ShouldBeFalse(
            "operator decision over the plan's original true: do not flip this default");

        await using var world = new World();
        var repo = world.CreateRepo();
        var project = await world.AddProjectAsync(repo.Path);
        var board = await world.AddBoardAsync(project.Id, "Dirty Board");
        await world.AddCardAsync(board, "CARD-0001", "One");
        var headBefore = (await repo.GitReadAsync("rev-parse", "HEAD")).Trim();

        var result = await world.SyncAsync(board.Id);

        result.Written.ShouldBeGreaterThan(0);
        result.CommitSkipReason.ShouldBe("autocommit_disabled");
        result.CommitSha.ShouldBeNull();
        (await repo.GitReadAsync("rev-parse", "HEAD")).Trim().ShouldBe(headBefore);
        SplitGitLines(await repo.GitReadAsync("status", "--porcelain", "--", "docs/cards"))
            .ShouldNotBeEmpty();
    }

    [Test]
    public async Task Autocrlf_checkout_may_rewrite_but_commits_nothing()
    {
        await using var world = new World();
        var repo = world.CreateRepo();
        await repo.GitAsync("config", "core.autocrlf", "true");
        await File.WriteAllTextAsync(Path.Combine(repo.Path, ".gitattributes"), "* text=auto\n");
        await repo.GitAsync("add", ".gitattributes");
        await repo.GitAsync("commit", "-m", "gitattributes");

        var project = await world.AddProjectAsync(repo.Path);
        var board = await world.AddBoardAsync(project.Id, "Crlf Pin");
        await world.AddCardAsync(board, "CARD-0001", "One");
        var first = await world.SyncAsync(board.Id, autoCommit: true);
        first.CommitSha.ShouldNotBeNull();
        var head = first.CommitSha;

        await repo.GitAsync("checkout", "--", "docs/cards");
        var second = await world.SyncAsync(board.Id, autoCommit: true);

        second.CommitSkipReason.ShouldBe("nothing_to_commit");
        second.CommitSha.ShouldBeNull();
        (await repo.GitReadAsync("rev-parse", "HEAD")).Trim().ShouldBe(head);
    }

    [Test]
    public async Task SyncAllAsync_syncs_the_pathed_project_reports_pathless_and_skips_archived()
    {
        // Isolated schema so this sweep cannot write into another test's LocalRepositoryPath.
        // The production method still iterates every live board; the pin is that our four boards
        // are the only ones it can see here.
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var world = new World(schema.ConnectionString);
        var repo = world.CreateRepo();
        var withPath = await world.AddProjectAsync(repo.Path);
        var withoutPath = await world.AddProjectAsync(localPath: null);
        var archivedProject = await world.AddProjectAsync(repo.Path, archived: true);
        var liveBoard = await world.AddBoardAsync(withPath.Id, "Live Sweep");
        var pathlessBoard = await world.AddBoardAsync(withoutPath.Id, "Pathless Sweep");
        var archivedBoard = await world.AddBoardAsync(withPath.Id, "Archived Sweep", archived: true);
        var boardOnArchivedProject = await world.AddBoardAsync(archivedProject.Id, "Orphan Sweep");
        await world.AddCardAsync(liveBoard, "CARD-0001", "Live Card");
        await world.AddCardAsync(pathlessBoard, "CARD-0001", "Ghost");
        await world.AddCardAsync(archivedBoard, "CARD-0001", "Hidden");
        await world.AddCardAsync(boardOnArchivedProject, "CARD-0001", "Hidden");

        var results = await world.SyncAllAsync();

        results.Count.ShouldBe(2);
        var live = results.Single(r => r.BoardId == liveBoard.Id);
        live.WriteSkipReason.ShouldBeNull();
        live.Written.ShouldBeGreaterThan(0);
        live.Directory.ShouldBe("docs/cards/live-sweep");
        File.Exists(Path.Combine(world.BoardDir(repo, "live-sweep"), "CARD-0001-live-card.md"))
            .ShouldBeTrue();

        var pathless = results.Single(r => r.BoardId == pathlessBoard.Id);
        pathless.WriteSkipReason.ShouldBe("no_repository_path");
        pathless.Written.ShouldBe(0);
        pathless.Directory.ShouldBeNull();

        results.ShouldNotContain(r => r.BoardId == archivedBoard.Id);
        results.ShouldNotContain(r => r.BoardId == boardOnArchivedProject.Id);
        Directory.Exists(world.BoardDir(repo, "archived-sweep")).ShouldBeFalse();
        Directory.Exists(world.BoardDir(repo, "orphan-sweep")).ShouldBeFalse();
    }

    [Test]
    public async Task Index_lock_is_git_error_then_next_sync_after_removal_commits()
    {
        await using var world = new World();
        var repo = world.CreateRepo();
        var project = await world.AddProjectAsync(repo.Path);
        var board = await world.AddBoardAsync(project.Id, "Lock Board");
        await world.AddCardAsync(board, "CARD-0001", "One");
        var lockPath = ResolveGitPath(
            repo.Path,
            (await repo.GitReadAsync("rev-parse", "--git-path", "index.lock")).Trim());
        await File.WriteAllTextAsync(lockPath, "");
        var headBefore = (await repo.GitReadAsync("rev-parse", "HEAD")).Trim();

        var first = await world.SyncAsync(board.Id, autoCommit: true);

        first.Written.ShouldBeGreaterThan(0);
        first.CommitSkipReason.ShouldBe("git_error");
        first.CommitSha.ShouldBeNull();
        first.Error.ShouldNotBeNullOrWhiteSpace();
        File.Exists(Path.Combine(world.BoardDir(repo, "lock-board"), "CARD-0001-one.md")).ShouldBeTrue();
        (await repo.GitReadAsync("rev-parse", "HEAD")).Trim().ShouldBe(headBefore);

        File.Delete(lockPath);
        var second = await world.SyncAsync(board.Id, autoCommit: true);
        second.CommitSha.ShouldNotBeNull();
        second.CommitSha.ShouldNotBe(headBefore);
        second.CommitSkipReason.ShouldBeNull();
    }

    private static int SyncCommitCount(string log) =>
        SplitGitLines(log).Count(l => l.StartsWith("antiphon: sync card files", StringComparison.Ordinal));

    private static string[] SplitGitLines(string output) =>
        output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static string ResolveGitPath(string repoPath, string gitPathOutput) =>
        Path.IsPathRooted(gitPathOutput)
            ? gitPathOutput
            : Path.GetFullPath(Path.Combine(repoPath, gitPathOutput));

    private static void AssertLfNoBom(byte[] bytes)
    {
        bytes.Length.ShouldBeGreaterThan(0);
        if (bytes.Length >= 3)
            (bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF).ShouldBeFalse();
        Encoding.UTF8.GetString(bytes).ShouldNotContain("\r");
        bytes[^1].ShouldBe((byte)'\n');
    }

    private sealed class World : IAsyncDisposable
    {
        private readonly List<Guid> _projectIds = [];
        private readonly List<Guid> _boardIds = [];
        private readonly List<Guid> _columnIds = [];
        private readonly List<Guid> _cardIds = [];
        private readonly List<ScratchGitRepo> _repos = [];
        private readonly List<string> _tempDirs = [];
        private readonly CardTaskFileSyncGate _gate = new();
        private readonly string? _connectionString;

        public World(string? connectionString = null)
        {
            _connectionString = connectionString;
        }

        public ScratchGitRepo CreateRepo()
        {
            var repo = new ScratchGitRepo("c0004");
            // git commit --only requires HEAD; seed so the first sync is never the repo's first commit.
            repo.CommitFileAsync(".keep", "seed\n").GetAwaiter().GetResult();
            _repos.Add(repo);
            return repo;
        }

        public string CreateNonRepoDir()
        {
            var dir = Directory.CreateTempSubdirectory("c0004-norepo").FullName;
            _tempDirs.Add(dir);
            return dir;
        }

        public string BoardDir(ScratchGitRepo repo, string slug) =>
            Path.Combine(repo.Path, "docs", "cards", slug);

        public async Task<Project> AddProjectAsync(string? localPath, bool archived = false)
        {
            var now = DateTime.UtcNow;
            var project = new Project
            {
                Id = Guid.NewGuid(),
                Name = $"c0004-{Guid.NewGuid():N}"[..20],
                GitRepositoryUrl = "https://example.invalid/repo.git",
                LocalRepositoryPath = localPath,
                BaseBranch = "master",
                CreatedAt = now,
                UpdatedAt = now,
                ArchivedAt = archived ? now : null,
                ArchivedBy = archived ? "test" : null,
                ArchivedReason = archived ? "test" : null,
            };
            await using var db = CreateContext();
            db.Projects.Add(project);
            await db.SaveChangesAsync();
            _projectIds.Add(project.Id);
            return project;
        }

        public async Task<Board> AddBoardAsync(
            Guid projectId, string name, bool archived = false, DateTime? createdAt = null)
        {
            var now = createdAt ?? DateTime.UtcNow;
            var board = new Board
            {
                Id = Guid.NewGuid(),
                ProjectId = projectId,
                Name = name,
                CreatedAt = now,
                UpdatedAt = now,
                ArchivedAt = archived ? now : null,
                ArchivedBy = archived ? "test" : null,
                ArchivedReason = archived ? "test" : null,
            };
            var column = new BoardColumn
            {
                Id = Guid.NewGuid(),
                BoardId = board.Id,
                StateKey = "backlog",
                Name = "Backlog",
                ColumnOrder = 0,
                CardStatus = CardStatus.Backlog,
                IsActive = true,
                CreatedAt = now,
                UpdatedAt = now,
            };
            await using var db = CreateContext();
            db.Boards.Add(board);
            db.BoardColumns.Add(column);
            await db.SaveChangesAsync();
            _boardIds.Add(board.Id);
            _columnIds.Add(column.Id);
            board.Columns.Add(column);
            return board;
        }

        public async Task<Card> AddCardAsync(Board board, string identifier, string title)
        {
            var columnId = board.Columns.Count > 0
                ? board.Columns.First().Id
                : throw new InvalidOperationException("AddBoardAsync must run first so the board has a column.");
            var now = DateTime.UtcNow;
            var card = new Card
            {
                Id = Guid.NewGuid(),
                BoardId = board.Id,
                BoardColumnId = columnId,
                Identifier = identifier,
                Title = title,
                Description = $"body of {identifier}",
                Status = CardStatus.Backlog,
                Importance = CardImportance.Normal,
                LabelsJson = "[]",
                CreatedAt = now,
                UpdatedAt = now,
            };
            await using var db = CreateContext();
            db.Cards.Add(card);
            await db.SaveChangesAsync();
            _cardIds.Add(card.Id);
            return card;
        }

        public async Task RenameCardAsync(Guid cardId, string title)
        {
            await using var db = CreateContext();
            var card = await db.Cards.FirstAsync(c => c.Id == cardId);
            card.Title = title;
            card.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }

        public async Task ArchiveCardAsync(Guid cardId, string by, string reason)
        {
            await using var db = CreateContext();
            var card = await db.Cards.FirstAsync(c => c.Id == cardId);
            card.ArchivedAt = DateTime.UtcNow;
            card.ArchivedBy = by;
            card.ArchivedReason = reason;
            await db.SaveChangesAsync();
        }

        public async Task<Antiphon.Server.Application.Dtos.CardFileSyncBoardResult> SyncAsync(
            Guid boardId, bool dryRun = false, bool autoCommit = false)
        {
            await using var db = CreateContext();
            return await CreateService(db, autoCommit).SyncBoardAsync(boardId, dryRun, CancellationToken.None);
        }

        public async Task<IReadOnlyList<Antiphon.Server.Application.Dtos.CardFileSyncBoardResult>> SyncAllAsync(
            bool dryRun = false, bool autoCommit = false)
        {
            await using var db = CreateContext();
            return await CreateService(db, autoCommit).SyncAllAsync(dryRun, CancellationToken.None);
        }

        private CardTaskFileService CreateService(AppDbContext db, bool autoCommit)
        {
            IOptions<CardFileSyncSettings>? settings = autoCommit
                ? Options.Create(new CardFileSyncSettings { AutoCommit = true })
                : null;
            return new CardTaskFileService(
                db,
                _gate,
                new GitWorkspaceService(NullLogger<GitWorkspaceService>.Instance),
                NullLogger<CardTaskFileService>.Instance,
                settings);
        }

        public async ValueTask DisposeAsync()
        {
            await using var db = CreateContext();
            await db.ExternalIssueRefs.Where(r => _cardIds.Contains(r.CardId)).ExecuteDeleteAsync();
            await db.CardRevisions.Where(r => _cardIds.Contains(r.CardId)).ExecuteDeleteAsync();
            await db.Cards.Where(c => _cardIds.Contains(c.Id)).ExecuteDeleteAsync();
            await db.BoardColumns.Where(c => _columnIds.Contains(c.Id)).ExecuteDeleteAsync();
            await db.Boards.Where(b => _boardIds.Contains(b.Id)).ExecuteDeleteAsync();
            await db.Projects.Where(p => _projectIds.Contains(p.Id)).ExecuteDeleteAsync();

            foreach (var repo in _repos)
                repo.Dispose();
            foreach (var dir in _tempDirs)
            {
                try { Directory.Delete(dir, recursive: true); }
                catch (IOException) { /* best effort */ }
                catch (UnauthorizedAccessException) { /* best effort */ }
            }
        }

        private AppDbContext CreateContext() =>
            new(TestDbFixture.CreateDbContextOptions(_connectionString));
    }
}
