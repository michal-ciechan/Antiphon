using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Domain.Enums;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public class CardFilePrivacySyncTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Board_off_never_writes_even_for_explicit_Public_card(bool autoCommit)
    {
        await using var world = new CardFilePrivacyWorld();
        await world.InitializeAsync(false);
        await world.AddCardAsync(visibility: CardFileVisibility.Public);
        var head = await world.Repo.GitReadAsync("rev-parse", "HEAD");
        var result = await world.SyncAsync(autoCommit);
        result.WriteSkipReason.ShouldBe("board_not_opted_in");
        result.Written.ShouldBe(0);
        Directory.Exists(world.DirectoryPath).ShouldBeFalse();
        (await world.Repo.GitReadAsync("rev-parse", "HEAD")).ShouldBe(head);
        (await world.Repo.GitReadAsync("diff", "--cached", "--name-only")).ShouldBeEmpty();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Unknown_repository_visibility_with_remote_present_BLOCKS_publication(bool autoCommit)
    {
        await using var world = new CardFilePrivacyWorld();
        await world.InitializeAsync(visibility: RepositoryVisibility.Unknown);
        await world.Repo.GitAsync("remote", "add", "origin", "https://example.invalid/c408.git");
        await world.AddCardAsync();
        await world.AddCardAsync("CARD-0002", visibility: CardFileVisibility.Public);
        var result = await world.SyncAsync(autoCommit);
        result.WriteSkipReason.ShouldBe("repository_visibility_unknown");
        result.Written.ShouldBe(0);
        result.EligibleCards.ShouldBe(0);
        result.ExcludedCards.ShouldBe(2);
        result.CommitSha.ShouldBeNull();
        result.Policy.RepositoryPath.ShouldBe(world.Repo.Path);
        Directory.Exists(world.DirectoryPath).ShouldBeFalse();
    }

    [Test]
    [Arguments("card", false)]
    [Arguments("card", true)]
    [Arguments("board", false)]
    [Arguments("board", true)]
    [Arguments("unknown", false)]
    [Arguments("unknown", true)]
    [Arguments("board_archive", false)]
    [Arguments("board_archive", true)]
    [Arguments("project_archive", false)]
    [Arguments("project_archive", true)]
    public async Task Revocation_removes_previously_exported_files_and_last_public_index(string revoke, bool autoCommit)
    {
        await using var world = new CardFilePrivacyWorld();
        await world.InitializeAsync();
        var cardId = await world.AddCardAsync(title: "C408_PREVIOUS_PUBLIC");
        var first = await world.SyncAsync(true);
        first.Error.ShouldBeNull();
        first.CommitSha.ShouldNotBeNull();
        const string path = "docs/cards/board/CARD-0001-c408-previous-public.md";
        await File.WriteAllTextAsync(Path.Combine(world.DirectoryPath, "keep.txt"), "unrelated");
        await using (var db = world.Db())
        {
            if (revoke == "card") await db.Cards.Where(c => c.Id == cardId).ExecuteUpdateAsync(s => s.SetProperty(c => c.CardFileVisibility, CardFileVisibility.Private));
            if (revoke == "board") await db.Boards.Where(b => b.Id == world.BoardId).ExecuteUpdateAsync(s => s.SetProperty(b => b.SyncCardFiles, false));
            if (revoke == "unknown") await db.Projects.Where(p => p.Id == world.ProjectId).ExecuteUpdateAsync(s => s.SetProperty(p => p.RepositoryVisibility, RepositoryVisibility.Unknown));
            if (revoke == "board_archive") await db.Boards.Where(b => b.Id == world.BoardId).ExecuteUpdateAsync(s => s.SetProperty(b => b.ArchivedAt, DateTime.UtcNow));
            if (revoke == "project_archive") await db.Projects.Where(p => p.Id == world.ProjectId).ExecuteUpdateAsync(s => s.SetProperty(p => p.ArchivedAt, DateTime.UtcNow));
        }
        var result = await world.SyncAsync(autoCommit);
        result.Error.ShouldBeNull();
        result.Deleted.ShouldBe(2);
        result.Written.ShouldBe(0);
        Directory.GetFiles(world.DirectoryPath, "*.md").ShouldBeEmpty();
        (await File.ReadAllTextAsync(Path.Combine(world.DirectoryPath, "keep.txt"))).ShouldBe("unrelated");
        result.Policy.WorkingTreeRemovalPending.ShouldBe(false);
        result.Policy.GitRemovalPending.ShouldBe(!autoCommit);
        (await world.Repo.GitReadAsync("show", first.CommitSha + ":" + path)).ShouldContain("C408_PREVIOUS_PUBLIC");
        if (autoCommit)
        {
            result.CommitSha.ShouldNotBeNull();
            (await world.Repo.GitReadAsync("ls-tree", "-r", "--name-only", "HEAD")).ShouldNotContain("docs/cards");
            (await world.Repo.GitReadAsync("log", "-1", "--format=%s")).Trim().ShouldBe("antiphon: remove unpublished card files");
            foreach (var blob in (await world.Repo.GitReadAsync("ls-tree", "-r", "--name-only", "HEAD")).Split('\n', StringSplitOptions.RemoveEmptyEntries))
                (await world.Repo.GitReadAsync("show", "HEAD:" + blob.Trim())).ShouldNotContain("C408_PREVIOUS_PUBLIC");
        }
        else
        {
            result.CommitSkipReason.ShouldBe("autocommit_disabled");
            result.Policy.Warnings.ShouldContain("card_file_cleanup_required");
            (await world.Repo.GitReadAsync("rev-parse", "HEAD")).Trim().ShouldBe(first.CommitSha);
        }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Index_only_private_addition_is_unstaged_even_with_AutoCommit_false(bool autoCommit)
    {
        await using var world = new CardFilePrivacyWorld();
        await world.InitializeAsync(false);
        Directory.CreateDirectory(world.DirectoryPath);
        const string path = "docs/cards/board/old[1].md";
        await File.WriteAllTextAsync(Path.Combine(world.Repo.Path, path), "C408_STAGED_PRIVATE");
        await world.Repo.GitAsync("--literal-pathspecs", "add", "--", path);
        File.Delete(Path.Combine(world.Repo.Path, path));
        var head = await world.Repo.GitReadAsync("rev-parse", "HEAD");
        await using (var db = world.Db())
        {
            var status = await world.Service(db).GetStatusAsync(world.BoardId, default);
            status.Warnings.ShouldContain("card_file_staged_private_residue");
        }
        var result = await world.SyncAsync(autoCommit);
        result.Error.ShouldBeNull();
        result.Policy.RemovalPending.ShouldBeFalse();
        (await world.Repo.GitReadAsync("ls-files", "-z")).ShouldNotContain(path);
        (await world.Repo.GitReadAsync("rev-parse", "HEAD")).ShouldBe(head);
    }

    [Test]
    public async Task Dry_run_revocation_keeps_legacy_private_files_and_does_not_consume_warning()
    {
        await using var world = new CardFilePrivacyWorld();
        await world.InitializeAsync(false);
        Directory.CreateDirectory(world.DirectoryPath);
        var path = Path.Combine(world.DirectoryPath, "legacy.md");
        await File.WriteAllTextAsync(path, "C408_LEGACY_PRIVATE");
        var result = await world.SyncAsync(false, true);
        result.Deleted.ShouldBe(1);
        (await File.ReadAllTextAsync(path)).ShouldBe("C408_LEGACY_PRIVATE");
        await using var db = world.Db();
        (await db.Boards.SingleAsync(b => b.Id == world.BoardId)).CardFilesDirectorySlug.ShouldBeNull();
        world.Gate.NoteSkipReason(world.BoardId, world.Repo.Path, "board_not_opted_in").ShouldBeTrue();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Effective_ignore_blocks_new_writes_including_tracked_paths_and_allows_cleanup(bool tracked)
    {
        await using var world = new CardFilePrivacyWorld();
        await world.InitializeAsync();
        var id = await world.AddCardAsync();
        if (tracked) await world.SyncAsync(true);
        await File.WriteAllTextAsync(Path.Combine(world.Repo.Path, ".gitignore"), "/docs/cards/\n");
        var result = await world.SyncAsync(true);
        result.WriteSkipReason.ShouldBe("card_file_path_ignored");
        result.Written.ShouldBe(0); result.EligibleCards.ShouldBe(1);
        result.CommitSha.ShouldBeNull();
        await using var db = world.Db();
        await db.Cards.Where(c => c.Id == id).ExecuteUpdateAsync(s => s.SetProperty(c => c.CardFileVisibility, CardFileVisibility.Private));
        var status = await world.Service(db).GetStatusAsync(world.BoardId, default);
        if (tracked) { status.RemovalPending.ShouldBeTrue(); status.GitRemovalPending.ShouldBe(true); }
        var cleanup = await world.SyncAsync(true);
        cleanup.Policy.RemovalPending.ShouldBeFalse();
        if (tracked) cleanup.CommitSha.ShouldNotBeNull();
    }

    [Test]
    public async Task Private_card_contributes_no_filename_index_count_or_metadata()
    {
        await using var world = new CardFilePrivacyWorld();
        await world.InitializeAsync();
        await world.AddCardAsync();
        await world.AddCardAsync("CARD-9999", "C408_PRIVATE_CARD_TITLE", visibility: CardFileVisibility.Private);
        var result = await world.SyncAsync(true);
        result.Error.ShouldBeNull();
        result.EligibleCards.ShouldBe(1);
        result.ExcludedCards.ShouldBe(1);
        var index = await File.ReadAllTextAsync(Path.Combine(world.DirectoryPath, "INDEX.md"));
        index.ShouldContain("1 card, 0 archived.");
        index.ShouldNotContain("CARD-9999");
        foreach (var path in Directory.GetFiles(world.DirectoryPath))
        {
            path.ShouldNotContain("PRIVATE_CARD");
            var body = await File.ReadAllTextAsync(path);
            body.ShouldNotContain("C408_NOTE");
            body.ShouldNotContain("C408_PRIVATE_CARD");
        }
    }

    [Test]
    public async Task Enabled_false_freezes_IO_and_does_not_claim_erasure()
    {
        await using var world = new CardFilePrivacyWorld();
        await world.InitializeAsync();
        await world.AddCardAsync();
        await world.SyncAsync();
        await using var db = world.Db();
        await db.Boards.Where(b => b.Id == world.BoardId).ExecuteUpdateAsync(s => s.SetProperty(b => b.SyncCardFiles, false));
        var service = world.Service(db, enabled: false);
        (await Should.ThrowAsync<ConflictException>(() => service.SyncBoardAsync(world.BoardId))).Code.ShouldBe("card_file_sync_disabled");
        Directory.GetFiles(world.DirectoryPath, "*.md").Length.ShouldBe(2);
        (await service.GetStatusAsync(world.BoardId, default)).RemovalPending.ShouldBeTrue();
        (await service.SyncAllAsync()).ShouldBeEmpty();
        (await world.SyncAsync()).Deleted.ShouldBe(2);
        Directory.GetFiles(world.DirectoryPath, "*.md").ShouldBeEmpty();
    }

    [Test]
    public async Task Dry_run_changes_no_database_file_ignore_index_HEAD_or_warning_state()
    {
        await using var world = new CardFilePrivacyWorld();
        await world.InitializeAsync();
        await world.AddCardAsync();
        var head = await world.Repo.GitReadAsync("rev-parse", "HEAD");
        var index = await world.Repo.GitReadAsync("ls-files", "--stage", "-z");
        var ignore = await File.ReadAllTextAsync(Path.Combine(world.Repo.Path, ".gitignore"));
        var result = await world.SyncAsync(true, true);
        result.Written.ShouldBe(2);
        Directory.Exists(world.DirectoryPath).ShouldBeFalse();
        await using var db = world.Db();
        (await db.Boards.SingleAsync(b => b.Id == world.BoardId)).CardFilesDirectorySlug.ShouldBeNull();
        (await world.Repo.GitReadAsync("rev-parse", "HEAD")).ShouldBe(head);
        (await world.Repo.GitReadAsync("ls-files", "--stage", "-z")).ShouldBe(index);
        (await File.ReadAllTextAsync(Path.Combine(world.Repo.Path, ".gitignore"))).ShouldBe(ignore);
    }
}
