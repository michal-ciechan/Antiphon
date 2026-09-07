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
public class CardFilePrivacyOwnershipTests
{
    [Test]
    public async Task Rename_retains_pinned_slug_and_does_not_orphan_files()
    {
        await using var world = new CardFilePrivacyWorld();
        await world.InitializeAsync(); await world.AddCardAsync(); await world.SyncAsync();
        await using var db = world.Db();
        await db.Boards.Where(b => b.Id == world.BoardId).ExecuteUpdateAsync(s => s.SetProperty(b => b.Name, "Renamed"));
        var result = await world.SyncAsync();
        result.Directory.ShouldBe("docs/cards/board");
        result.Written.ShouldBe(1, "only INDEX public board title changes");
        Directory.Exists(Path.Combine(world.Repo.Path, "docs/cards/renamed")).ShouldBeFalse();
    }

    [Test]
    public async Task Subdirectory_projects_use_canonical_git_root_gate()
    {
        await using var world = new CardFilePrivacyWorld();
        await world.InitializeAsync();
        var subdirectory = Directory.CreateDirectory(Path.Combine(world.Repo.Path, "src/component")).FullName;
        await using var db = world.Db();
        await db.Projects.Where(p => p.Id == world.ProjectId).ExecuteUpdateAsync(s => s.SetProperty(p => p.LocalRepositoryPath, subdirectory));
        using var held = await world.Gate.TryEnterAsync(world.Repo.Path);
        held.ShouldNotBeNull();
        var ex = await Should.ThrowAsync<ConflictException>(() => world.Service(db).SyncBoardAsync(world.BoardId));
        ex.Code.ShouldBe("card_file_sync_running");
    }

    [Test]
    public async Task Settings_compare_after_waiting_for_lease_and_only_broadcast_IDs()
    {
        await using var world = new CardFilePrivacyWorld();
        await world.InitializeAsync();
        await using var db = world.Db();
        var events = new MockEventBus();
        var service = world.Service(db);
        using var held = await world.Gate.EnterProjectAsync(world.ProjectId, true, default);
        var waiting = service.UpdateSettingsAsync(world.BoardId, false, true, events, default);
        waiting.IsCompleted.ShouldBeFalse();
        await using (var second = world.Db())
            await second.Boards.Where(b => b.Id == world.BoardId).ExecuteUpdateAsync(s => s.SetProperty(b => b.SyncCardFiles, false));
        held!.Dispose();
        (await Should.ThrowAsync<ConflictException>(() => waiting)).Code.ShouldBe("card_file_policy_changed");
        events.PublishedEvents.ShouldBeEmpty();
        var current = await service.UpdateSettingsAsync(world.BoardId, false, false, events, default);
        current.SyncCardFiles.ShouldBeFalse();
        events.PublishedEvents.ShouldBeEmpty();
    }

    [Test]
    public async Task Project_path_change_refuses_residue_then_releases_old_pin_and_resets_visibility()
    {
        await using var world = new CardFilePrivacyWorld();
        await world.InitializeAsync(); await world.AddCardAsync(); await world.SyncAsync();
        using var next = new ScratchGitRepo("c408-next"); await next.CommitFileAsync("seed", "seed\n");
        await using var db = world.Db();
        var projects = new ProjectService(db, null!, Options.Create(new GithubSettings()), NullLogger<ProjectService>.Instance, cardFiles: world.Service(db));
        var current = await db.Projects.SingleAsync(p => p.Id == world.ProjectId);
        var request = new UpdateProjectRequest(current.Name, current.GitRepositoryUrl, null, false, false, next.Path, "master");
        (await Should.ThrowAsync<ConflictException>(() => projects.UpdateAsync(world.ProjectId, request, default))).Code.ShouldBe("card_file_cleanup_required");
        (await db.Projects.AsNoTracking().SingleAsync(p => p.Id == world.ProjectId)).LocalRepositoryPath.ShouldBe(world.Repo.Path);
        await db.Boards.Where(b => b.Id == world.BoardId).ExecuteUpdateAsync(s => s.SetProperty(b => b.SyncCardFiles, false));
        await world.SyncAsync();
        var changed = await projects.UpdateAsync(world.ProjectId, request, default);
        changed.RepositoryVisibility.ShouldBe(RepositoryVisibility.Unknown);
        var board = await db.Boards.AsNoTracking().SingleAsync(b => b.Id == world.BoardId);
        board.CardFilesRepositoryPath.ShouldBeNull(); board.CardFilesDirectorySlug.ShouldBeNull();
        File.ReadAllText(Path.Combine(next.Path, ".gitignore")).ShouldContain("/docs/cards/");
    }

    [Test]
    public async Task URL_change_without_visibility_resets_Unknown_but_explicit_value_is_retained()
    {
        await using var world = new CardFilePrivacyWorld();
        await world.InitializeAsync();
        await using var db = world.Db();
        var projects = new ProjectService(db, null!, Options.Create(new GithubSettings()), NullLogger<ProjectService>.Instance, cardFiles: world.Service(db));
        var request = new UpdateProjectRequest("synthetic", "https://example.invalid/changed.git", null, false, false, world.Repo.Path, "master");
        (await projects.UpdateAsync(world.ProjectId, request, default)).RepositoryVisibility.ShouldBe(RepositoryVisibility.Unknown);
        (await projects.UpdateAsync(world.ProjectId, request with { RepositoryVisibility = RepositoryVisibility.Private }, default)).RepositoryVisibility.ShouldBe(RepositoryVisibility.Private);
        (await projects.UpdateAsync(world.ProjectId, request, default)).RepositoryVisibility.ShouldBe(RepositoryVisibility.Private);
    }
}
