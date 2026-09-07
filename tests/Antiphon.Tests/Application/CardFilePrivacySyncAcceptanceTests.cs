using System.Text.Json;
using Antiphon.Server.Domain.Enums;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[NotInParallel]
[ParallelLimiter<ProcessSpawnLimit>]
public class CardFilePrivacySyncAcceptanceTests
{
    [Test]
    [Arguments(false)] [Arguments(true)]
    public async Task Public_Private_Inherit_transitions_publish_only_when_the_board_is_opted_in(bool optedIn)
    {
        await using var world = new CardFilePrivacyWorld(); await world.InitializeAsync(optedIn); var id = await world.AddCardAsync();
        await using var db = world.Db();
        foreach (var visibility in new[] { CardFileVisibility.Public, CardFileVisibility.Private, CardFileVisibility.Inherit }) {
            await db.Cards.Where(c => c.Id == id).ExecuteUpdateAsync(s => s.SetProperty(c => c.CardFileVisibility, visibility));
            var result = await world.SyncAsync(true); result.Error.ShouldBeNull();
            var files = Directory.Exists(world.DirectoryPath) ? Directory.GetFiles(world.DirectoryPath, "*.md") : [];
            files.Length.ShouldBe(optedIn && visibility != CardFileVisibility.Private ? 2 : 0);
            result.EligibleCards.ShouldBe(optedIn && visibility != CardFileVisibility.Private ? 1 : 0);
        }
    }

    [Test]
    public async Task First_legacy_unpinned_off_board_cleanup_commits_only_removal_and_never_grandfathers()
    {
        await using var world = new CardFilePrivacyWorld(); await world.InitializeAsync(false, RepositoryVisibility.Unknown); await world.AddCardAsync();
        Directory.CreateDirectory(world.DirectoryPath);
        await world.Repo.CommitFileAsync("docs/cards/board/legacy.md", "C408_OLD_PRIVATE");
        await world.Repo.CommitFileAsync("docs/cards/board/INDEX.md", "C408_OLD_PRIVATE_INDEX");
        var old = (await world.Repo.GitReadAsync("rev-parse", "HEAD")).Trim();
        var result = await world.SyncAsync(true);
        result.Written.ShouldBe(0); result.Deleted.ShouldBe(2); result.CommitSha.ShouldNotBeNull();
        (await world.Repo.GitReadAsync("log", "-1", "--format=%s")).Trim().ShouldBe("antiphon: remove unpublished card files");
        (await world.Repo.GitReadAsync("ls-tree", "-r", "--name-only", "HEAD")).ShouldNotContain("docs/cards/");
        (await world.Repo.GitReadAsync("show", old+":docs/cards/board/legacy.md")).ShouldBe("C408_OLD_PRIVATE");
        await using var db = world.Db(); (await db.Boards.AsNoTracking().SingleAsync(b => b.Id == world.BoardId)).SyncCardFiles.ShouldBeFalse();
    }

    [Test]
    public async Task Staged_old_generated_body_is_replaced_by_current_public_projection_at_same_path()
    {
        await using var world = new CardFilePrivacyWorld(); await world.InitializeAsync(); var id = await world.AddCardAsync();
        await world.SyncAsync(true);
        var path = Directory.GetFiles(world.DirectoryPath, "CARD-*.md").Single();
        await File.WriteAllTextAsync(path, "C408_STAGED_OLD_PRIVATE");
        var relative = Path.GetRelativePath(world.Repo.Path, path).Replace('\\', '/');
        await world.Repo.GitAsync("add", "--", relative);
        await using var db = world.Db(); await db.Cards.Where(c => c.Id == id).ExecuteUpdateAsync(s => s.SetProperty(c => c.Description, "C408_CURRENT_PUBLIC").SetProperty(c => c.PrivateNotes, "C408_STAGED_OLD_PRIVATE"));
        var result = await world.SyncAsync(true); result.CommitSha.ShouldNotBeNull();
        var head = await world.Repo.GitReadAsync("show", "HEAD:"+relative); head.ShouldContain("C408_CURRENT_PUBLIC"); head.ShouldNotContain("C408_STAGED_OLD_PRIVATE");
        (await world.Repo.GitReadAsync("show", ":"+relative)).ShouldBe(head);
    }

    [Test]
    [Arguments("off", false, "dry")] [Arguments("off", true, "dry")]
    [Arguments("unknown", false, "dry")] [Arguments("unknown", true, "dry")]
    [Arguments("off", false, "sweep")] [Arguments("off", true, "sweep")]
    [Arguments("unknown", false, "sweep")] [Arguments("unknown", true, "sweep")]
    public async Task Dry_run_and_sweep_preserve_publication_refusals_without_directory_or_index_effects(string policy, bool autoCommit, string entry)
    {
        await using var isolated = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var world = new CardFilePrivacyWorld(isolated.ConnectionString);
        await world.InitializeAsync(policy != "off", policy == "unknown" ? RepositoryVisibility.Unknown : RepositoryVisibility.Public);
        await world.AddCardAsync(); await world.AddCardAsync("CARD-0002", visibility: CardFileVisibility.Public);
        await world.Repo.GitAsync("remote", "add", "origin", "https://example.invalid/c408.git");
        var head = await world.Repo.GitReadAsync("rev-parse", "HEAD"); var index = await world.Repo.GitReadAsync("ls-files", "--stage", "-z");
        await using var db = world.Db();
        await db.Projects.Where(p => p.Id == world.ProjectId).ExecuteUpdateAsync(s => s.SetProperty(p => p.GitHubIntegrationEnabled, true));
        var service = world.Service(db, autoCommit);
        var result = entry == "dry" ? await service.SyncBoardAsync(world.BoardId, true) : (await service.SyncAllAsync()).Single(r => r.BoardId == world.BoardId);
        result.WriteSkipReason.ShouldBe(policy == "off" ? "board_not_opted_in" : "repository_visibility_unknown");
        result.EligibleCards.ShouldBe(0); result.ExcludedCards.ShouldBe(2); result.Written.ShouldBe(0); result.CommitSha.ShouldBeNull();
        Directory.Exists(world.DirectoryPath).ShouldBeFalse();
        (await world.Repo.GitReadAsync("rev-parse", "HEAD")).ShouldBe(head); (await world.Repo.GitReadAsync("ls-files", "--stage", "-z")).ShouldBe(index);
    }

    [Test]
    [Arguments("card", false, true)] [Arguments("card", true, true)]
    [Arguments("board", false, true)] [Arguments("board", true, true)]
    [Arguments("unknown", false, true)] [Arguments("unknown", true, true)]
    [Arguments("board_archive", false, true)] [Arguments("board_archive", true, true)]
    [Arguments("project_archive", false, true)] [Arguments("project_archive", true, true)]
    [Arguments("all_private", false, true)] [Arguments("all_private", true, true)]
    [Arguments("delete_last", false, true)] [Arguments("delete_last", true, true)]
    [Arguments("delete_last", false, false)] [Arguments("delete_last", true, false)]
    [Arguments("all_private", false, false)] [Arguments("all_private", true, false)]
    public async Task Manual_and_sweep_revoke_old_exports_and_preserve_nested_sibling_and_non_markdown_files(string revoke, bool autoCommit, bool sweep)
    {
        await using var isolated = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var world = new CardFilePrivacyWorld(isolated.ConnectionString); await world.InitializeAsync(); var id = await world.AddCardAsync();
        if (revoke == "all_private") await world.AddCardAsync("CARD-0002");
        await world.SyncAsync(true);
        Directory.CreateDirectory(Path.Combine(world.DirectoryPath, "nested"));
        var keep = new[] { Path.Combine(world.DirectoryPath, "keep.txt"), Path.Combine(world.DirectoryPath, "nested/keep.md"), Path.Combine(world.Repo.Path, "docs/cards/sibling.md") };
        foreach (var file in keep) await File.WriteAllTextAsync(file, "C408_UNRELATED");
        await using var db = world.Db();
        if (revoke == "all_private") await db.Cards.Where(c => c.BoardId == world.BoardId).ExecuteUpdateAsync(s => s.SetProperty(c => c.CardFileVisibility, CardFileVisibility.Private));
        if (revoke == "card") await db.Cards.Where(c => c.Id == id).ExecuteUpdateAsync(s => s.SetProperty(c => c.CardFileVisibility, CardFileVisibility.Private));
        if (revoke == "delete_last") await db.Cards.Where(c => c.Id == id).ExecuteDeleteAsync();
        if (revoke == "board") await db.Boards.Where(b => b.Id == world.BoardId).ExecuteUpdateAsync(s => s.SetProperty(b => b.SyncCardFiles, false));
        if (revoke == "unknown") await db.Projects.Where(p => p.Id == world.ProjectId).ExecuteUpdateAsync(s => s.SetProperty(p => p.RepositoryVisibility, RepositoryVisibility.Unknown));
        if (revoke == "board_archive") await db.Boards.Where(b => b.Id == world.BoardId).ExecuteUpdateAsync(s => s.SetProperty(b => b.ArchivedAt, DateTime.UtcNow));
        if (revoke == "project_archive") await db.Projects.Where(p => p.Id == world.ProjectId).ExecuteUpdateAsync(s => s.SetProperty(p => p.ArchivedAt, DateTime.UtcNow));
        var service = world.Service(db, autoCommit);
        var result = sweep ? (await service.SyncAllAsync()).Single(r => r.BoardId == world.BoardId) : await service.SyncBoardAsync(world.BoardId);
        result.Error.ShouldBeNull(); result.Written.ShouldBe(0); result.Deleted.ShouldBe(revoke == "all_private" ? 3 : 2);
        Directory.GetFiles(world.DirectoryPath, "*.md").ShouldBeEmpty();
        foreach (var file in keep) (await File.ReadAllTextAsync(file)).ShouldBe("C408_UNRELATED");
        result.Policy.GitRemovalPending.ShouldBe(!autoCommit);
    }

    [Test]
    [Arguments("off")] [Arguments("unknown")] [Arguments("archive")] [Arguments("ignored")] [Arguments("staged")]
    public async Task Dry_run_leaves_existing_files_pins_tokens_ignore_index_and_HEAD_unchanged(string policy)
    {
        await using var world = new CardFilePrivacyWorld(); await world.InitializeAsync(); await world.AddCardAsync(); await world.SyncAsync(true);
        await using var db = world.Db();
        if (policy is "off" or "staged") await db.Boards.Where(b => b.Id == world.BoardId).ExecuteUpdateAsync(s => s.SetProperty(b => b.SyncCardFiles, false));
        if (policy == "unknown") await db.Projects.Where(p => p.Id == world.ProjectId).ExecuteUpdateAsync(s => s.SetProperty(p => p.RepositoryVisibility, RepositoryVisibility.Unknown));
        if (policy == "archive") await db.Boards.Where(b => b.Id == world.BoardId).ExecuteUpdateAsync(s => s.SetProperty(b => b.ArchivedAt, DateTime.UtcNow));
        if (policy == "ignored") await File.WriteAllTextAsync(Path.Combine(world.Repo.Path, ".gitignore"), "/docs/cards/\n");
        if (policy == "staged") { await File.WriteAllTextAsync(Path.Combine(world.DirectoryPath, "legacy.md"), "C408_STAGED"); await world.Repo.GitAsync("add", "--", "docs/cards/board/legacy.md"); }
        var beforeBoard = JsonSerializer.Serialize(await db.Boards.AsNoTracking().SingleAsync(b => b.Id == world.BoardId));
        var beforeCard = JsonSerializer.Serialize(await db.Cards.AsNoTracking().Where(c => c.BoardId == world.BoardId).ToListAsync());
        var files = Directory.GetFiles(world.DirectoryPath).ToDictionary(p => p, File.ReadAllText);
        var ignore = File.ReadAllText(Path.Combine(world.Repo.Path, ".gitignore")); var head = await world.Repo.GitReadAsync("rev-parse", "HEAD"); var index = await world.Repo.GitReadAsync("ls-files", "--stage", "-z");
        var result = await world.SyncAsync(true, true); result.Written.ShouldBe(0); if (policy != "ignored") result.Deleted.ShouldBeGreaterThan(0);
        JsonSerializer.Serialize(await db.Boards.AsNoTracking().SingleAsync(b => b.Id == world.BoardId)).ShouldBe(beforeBoard);
        JsonSerializer.Serialize(await db.Cards.AsNoTracking().Where(c => c.BoardId == world.BoardId).ToListAsync()).ShouldBe(beforeCard);
        Directory.GetFiles(world.DirectoryPath).ShouldBe(files.Keys, ignoreOrder: true);
        foreach (var (path, body) in files) File.ReadAllText(path).ShouldBe(body);
        File.ReadAllText(Path.Combine(world.Repo.Path, ".gitignore")).ShouldBe(ignore);
        (await world.Repo.GitReadAsync("rev-parse", "HEAD")).ShouldBe(head); (await world.Repo.GitReadAsync("ls-files", "--stage", "-z")).ShouldBe(index);
    }
}
