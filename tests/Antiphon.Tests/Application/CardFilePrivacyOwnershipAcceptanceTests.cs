using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Enums;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public class CardFilePrivacyOwnershipAcceptanceTests
{
    [Test]
    [Arguments(false)] [Arguments(true)]
    public async Task Projects_sharing_one_Git_repository_write_only_their_distinct_owned_targets(bool subdirectories)
    {
        await using var first = new CardFilePrivacyWorld(); await first.InitializeAsync(); await first.AddCardAsync(title: "First");
        await using var second = new CardFilePrivacyWorld(); await second.InitializeAsync(); await second.AddCardAsync(title: "Second");
        var rootA = subdirectories ? Directory.CreateDirectory(Path.Combine(first.Repo.Path, "one")).FullName : first.Repo.Path;
        var rootB = subdirectories ? Directory.CreateDirectory(Path.Combine(first.Repo.Path, "two")).FullName : first.Repo.Path;
        foreach (var root in new[] { rootA, rootB }.Distinct()) await File.WriteAllTextAsync(Path.Combine(root, ".gitignore"), "/docs/cards/*\n!/docs/cards/board/\n!/docs/cards/other/\n");
        await using var db = first.Db();
        await db.Projects.Where(p => p.Id == first.ProjectId).ExecuteUpdateAsync(s => s.SetProperty(p => p.LocalRepositoryPath, rootA));
        await db.Projects.Where(p => p.Id == second.ProjectId).ExecuteUpdateAsync(s => s.SetProperty(p => p.LocalRepositoryPath, rootB));
        await db.Boards.Where(b => b.Id == second.BoardId).ExecuteUpdateAsync(s => s.SetProperty(b => b.Name, "Other"));
        // A separate project's exception is meaningful only at the same target root.
        if (subdirectories) {
            await File.WriteAllTextAsync(Path.Combine(rootA, ".gitignore"), "/docs/cards/*\n!/docs/cards/board/\n");
            await File.WriteAllTextAsync(Path.Combine(rootB, ".gitignore"), "/docs/cards/*\n!/docs/cards/other/\n");
        }
        var service = first.Service(db, true);
        var a = await service.SyncBoardAsync(first.BoardId); a.Error.ShouldBeNull(); a.Written.ShouldBe(2);
        var b = await service.SyncBoardAsync(second.BoardId); b.Error.ShouldBeNull(); b.Written.ShouldBe(2);
        var bodyA = File.ReadAllText(Directory.GetFiles(Path.Combine(rootA, "docs/cards/board"), "CARD-*.md").Single());
        var bodyB = File.ReadAllText(Directory.GetFiles(Path.Combine(rootB, "docs/cards/other"), "CARD-*.md").Single());
        bodyA.ShouldContain("First"); bodyA.ShouldNotContain("Second"); bodyB.ShouldContain("Second"); bodyB.ShouldNotContain("First");
        (await first.Repo.GitReadAsync("ls-tree", "-r", "--name-only", "HEAD")).Split('\n').Count(p => p.EndsWith(".md")).ShouldBe(4);
    }

    [Test]
    [Arguments("unknown")] [Arguments("pathless")] [Arguments("missing")] [Arguments("nongit")] [Arguments("ignore")]
    public async Task Settings_and_board_creation_share_opt_in_prerequisites_and_leave_no_half_board(string failure)
    {
        await using var world = new CardFilePrivacyWorld(); await world.InitializeAsync(false);
        var nongit = Directory.CreateDirectory(Path.Combine(world.Repo.WorktreeRoot, "nongit")).FullName;
        await File.WriteAllTextAsync(Path.Combine(world.Repo.Path, ".gitignore"), "/docs/cards/\n");
        await using (var seed = world.Db()) {
            if (failure == "unknown") await seed.Projects.Where(p => p.Id == world.ProjectId).ExecuteUpdateAsync(s => s.SetProperty(p => p.RepositoryVisibility, RepositoryVisibility.Unknown));
            if (failure is "pathless" or "missing" or "nongit") {
                string? path = failure == "pathless" ? null : failure == "missing" ? Path.Combine(nongit, "missing") : nongit;
                await seed.Projects.Where(p => p.Id == world.ProjectId).ExecuteUpdateAsync(s => s.SetProperty(p => p.LocalRepositoryPath, path));
            }
        }
        if (failure == "ignore") File.Delete(Path.Combine(world.Repo.Path, ".gitignore"));
        await using var db = world.Db(); var events = new MockEventBus(); var policy = world.Service(db);
        (await Should.ThrowAsync<ConflictException>(() => policy.UpdateSettingsAsync(world.BoardId, true, false, events, default))).Code.ShouldBe("card_file_policy_refused");
        var boards = new BoardService(db, events, TimeProvider.System, cardFiles: policy);
        (await Should.ThrowAsync<ConflictException>(() => boards.CreateAsync(new(world.ProjectId, "New board", SyncCardFiles: true), default))).Code.ShouldBe("card_file_policy_refused");
        (await db.Boards.CountAsync(b => b.ProjectId == world.ProjectId)).ShouldBe(1);
        (await db.Boards.AsNoTracking().SingleAsync(b => b.Id == world.BoardId)).SyncCardFiles.ShouldBeFalse();
        events.PublishedEvents.ShouldBeEmpty(); Directory.Exists(world.DirectoryPath).ShouldBeFalse();
    }

    [Test]
    public async Task Explicit_Public_opt_in_is_idempotent_and_disabling_unreachable_target_remains_possible()
    {
        await using var world = new CardFilePrivacyWorld(); await world.InitializeAsync(false);
        await File.WriteAllTextAsync(Path.Combine(world.Repo.Path, ".gitignore"), "/docs/cards/\n");
        await using var db = world.Db(); var events = new MockEventBus(); var service = world.Service(db);
        var status = await service.UpdateSettingsAsync(world.BoardId, true, false, events, default);
        status.SyncCardFiles.ShouldBeTrue(); status.Warnings.ShouldContain("public_repository");
        events.PublishedEvents.Count.ShouldBe(1);
        (await service.UpdateSettingsAsync(world.BoardId, true, true, events, default)).SyncCardFiles.ShouldBeTrue();
        events.PublishedEvents.Count.ShouldBe(1);
        await db.Projects.Where(p => p.Id == world.ProjectId).ExecuteUpdateAsync(s => s.SetProperty(p => p.LocalRepositoryPath, Path.Combine(world.Repo.Path, "missing")));
        status = await service.UpdateSettingsAsync(world.BoardId, false, true, events, default);
        status.SyncCardFiles.ShouldBeFalse(); status.RemovalPending.ShouldBeTrue(); events.PublishedEvents.Count.ShouldBe(2);
    }

    [Test]
    [Arguments("working", false)] [Arguments("staged", false)] [Arguments("unreachable", false)]
    [Arguments("working", true)] [Arguments("staged", true)] [Arguments("unreachable", true)]
    public async Task Owner_deletion_refuses_legacy_working_index_or_unreachable_residue(string residue, bool project)
    {
        await using var world = new CardFilePrivacyWorld(); await world.InitializeAsync(false);
        Directory.CreateDirectory(world.DirectoryPath);
        var path = Path.Combine(world.DirectoryPath, "legacy.md"); await File.WriteAllTextAsync(path, "C408_OLD_PRIVATE");
        if (residue == "staged") { await world.Repo.GitAsync("add", "--", "docs/cards/board/legacy.md"); File.Delete(path); }
        await using var db = world.Db();
        if (residue == "unreachable") await db.Projects.Where(p => p.Id == world.ProjectId).ExecuteUpdateAsync(s => s.SetProperty(p => p.LocalRepositoryPath, Path.Combine(world.Repo.Path, "missing")));
        var service = world.Service(db);
        Task Delete() => project
            ? new ProjectService(db, null!, Options.Create(new GithubSettings()), NullLogger<ProjectService>.Instance, cardFiles: service).DeleteAsync(world.ProjectId, true, default)
            : new BoardService(db, new MockEventBus(), TimeProvider.System, cardFiles: service).DeleteAsync(world.BoardId, default);
        (await Should.ThrowAsync<ConflictException>(Delete)).Code.ShouldBe("card_file_cleanup_required");
        (await db.Boards.AnyAsync(b => b.Id == world.BoardId)).ShouldBeTrue(); (await db.Projects.AnyAsync(p => p.Id == world.ProjectId)).ShouldBeTrue();
        if (residue != "unreachable") { await world.SyncAsync(); await Delete(); (await db.Boards.AnyAsync(b => b.Id == world.BoardId)).ShouldBeFalse(); }
    }
    [Test]
    public async Task Board_creation_reloads_cached_project_visibility_under_the_gate()
    {
        await using var world = new CardFilePrivacyWorld(); await world.InitializeAsync(false);
        await File.WriteAllTextAsync(Path.Combine(world.Repo.Path, ".gitignore"), "/docs/cards/\n");
        await using var db = world.Db(); await db.Projects.SingleAsync(p => p.Id == world.ProjectId);
        await using (var changed = world.Db()) await changed.Projects.Where(p => p.Id == world.ProjectId).ExecuteUpdateAsync(s => s.SetProperty(p => p.RepositoryVisibility, RepositoryVisibility.Unknown));
        var service = new BoardService(db, new MockEventBus(), TimeProvider.System, cardFiles: world.Service(db));
        (await Should.ThrowAsync<ConflictException>(() => service.CreateAsync(new(world.ProjectId, "New", SyncCardFiles: true), default))).Code.ShouldBe("card_file_policy_refused");
        (await db.Boards.CountAsync(b => b.ProjectId == world.ProjectId)).ShouldBe(1);
    }

    [Test]
    public async Task Project_reassignment_refuses_another_projects_generated_directory_before_persistence()
    {
        await using var first = new CardFilePrivacyWorld(); await first.InitializeAsync(false);
        await using var second = new CardFilePrivacyWorld(); await second.InitializeAsync(false);
        await using var db = first.Db();
        var service = new ProjectService(db, null!, Options.Create(new GithubSettings()), NullLogger<ProjectService>.Instance, cardFiles: first.Service(db));
        var request = new UpdateProjectRequest("synthetic", "", null, false, false, second.Repo.Path, "master");
        (await Should.ThrowAsync<ConflictException>(() => service.UpdateAsync(first.ProjectId, request, default))).Code.ShouldBe("card_file_directory_conflict");
        (await db.Projects.AsNoTracking().SingleAsync(p => p.Id == first.ProjectId)).LocalRepositoryPath.ShouldBe(first.Repo.Path);
    }

}
