using Antiphon.Server.Application.Settings;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Text.Json;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Enums;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[NotInParallel]
[ParallelLimiter<ProcessSpawnLimit>]
public class CardFilePrivacyConcurrencyTests
{
    [Test]
    [Arguments("card")] [Arguments("board")] [Arguments("project")] [Arguments("create")] [Arguments("correction")]
    public async Task Revocation_waits_for_inflight_commit_then_next_sync_removes_export(string mutation)
    {
        await using var world = new CardFilePrivacyWorld(); await world.InitializeAsync(); var id = await world.AddCardAsync();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var repo = new CardFileTestRepository { BeforeCommit = async (_, _, ct) => { entered.TrySetResult(); await release.Task.WaitAsync(ct); } };
        await using var syncDb = world.Db();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var sync = world.Service(syncDb, true, repository: repo).SyncBoardAsync(world.BoardId, ct: deadline.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(15));
        await using var editDb = world.Db(); var token = (await editDb.Cards.SingleAsync(c => c.Id == id)).ConcurrencyToken;
        var cards = new CardService(editDb, null!, null!, null!, new MockEventBus(), TimeProvider.System, null!, cardFiles: world.Service(editDb));
        async Task Mutate() {
            if (mutation == "card") await cards.UpdateContentAsync(id, new(token, "private", CardFileVisibility: CardFileVisibility.Private), default);
            if (mutation == "board") await world.Service(editDb).UpdateSettingsAsync(world.BoardId, false, true, new MockEventBus(), default);
            if (mutation == "project") await new ProjectService(editDb, null!, Options.Create(new GithubSettings()), NullLogger<ProjectService>.Instance, cardFiles: world.Service(editDb)).UpdateAsync(world.ProjectId, new("C408 project", "https://example.invalid/c408.git", null, false, false, world.Repo.Path, "master", RepositoryVisibility: RepositoryVisibility.Unknown), default);
            if (mutation == "create") await cards.CreateAsync(world.BoardId, new(null, "New public", PrivateNotes: "C408_NEW_PRIVATE"), default);
            if (mutation == "correction") await cards.UpdateContentAsync(id, new(token, "correction", Description: "C408_CORRECTED_PUBLIC", PrivateNotes: "C408_CORRECTION_PRIVATE"), default);
        }
        var edit = Mutate();
        try { edit.IsCompleted.ShouldBeFalse(); } finally { release.TrySetResult(); }
        (await sync).CommitSha.ShouldNotBeNull(); await edit;
        if (mutation is "card" or "board" or "project") (await world.Service(editDb).GetStatusAsync(world.BoardId, default)).RemovalPending.ShouldBeTrue();
        (await world.SyncAsync(true)).Policy.RemovalPending.ShouldBeFalse();
        var files = Directory.GetFiles(world.DirectoryPath, "*.md");
        files.Length.ShouldBe(mutation == "create" ? 3 : mutation == "correction" ? 2 : 0);
        foreach (var file in files) File.ReadAllText(file).ShouldNotContain("PRIVATE");
    }

    [Test]
    public async Task Cancellation_releases_project_and_repository_leases_for_retry()
    {
        await using var world = new CardFilePrivacyWorld(); await world.InitializeAsync(); await world.AddCardAsync();
        using var cancel = new CancellationTokenSource();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var repo = new CardFileTestRepository { BeforeCommit = async (_, _, ct) => { entered.TrySetResult(); await Task.Delay(Timeout.Infinite, ct); } };
        await using var db = world.Db(); var sync = world.Service(db, true, repository: repo).SyncBoardAsync(world.BoardId, ct: cancel.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(15)); cancel.Cancel();
        await Should.ThrowAsync<OperationCanceledException>(() => sync);
        (await world.SyncAsync(true)).CommitSha.ShouldNotBeNull();
    }

    [Test]
    [Arguments("pin")] [Arguments("write")] [Arguments("stage")]
    public async Task Fresh_service_recovers_from_pin_write_and_staging_failure_using_current_policy(string fault)
    {
        await using var world = new CardFilePrivacyWorld(); await world.InitializeAsync(); var id = await world.AddCardAsync();
        bool fired = false; var writeCalls = 0;
        void Fail() { if (!fired) { fired = true; throw new IOException("C408_SYNTHETIC_PRIVATE_FAILURE"); } }
        var repo = new CardFileTestRepository {
            AfterPin = () => { if (fault == "pin") Fail(); },
            BeforeWrite = () => { if (fault == "write" && writeCalls++ == 1) Fail(); return Task.CompletedTask; },
            BeforeCommit = async (root, expected, _) => {
                if (fault == "stage" && !fired) {
                    var args = new[] { "--literal-pathspecs", "add", "-A", "--" }.Concat(expected.Keys).ToArray();
                    (await ScratchGitRepo.GitInAsync(root, args)).Ok.ShouldBeTrue(); Fail();
                }
            }
        };
        await using (var db = world.Db()) {
            var failed = await world.Service(db, true, repository: repo).SyncBoardAsync(world.BoardId);
            failed.Error.ShouldNotBeNull(); failed.Error.ShouldNotContain("C408_SYNTHETIC_PRIVATE_FAILURE");
            failed.Written.ShouldBe(fault == "pin" ? 0 : fault == "write" ? 1 : 2);
            (await db.Boards.AsNoTracking().SingleAsync(b => b.Id == world.BoardId)).CardFilesDirectorySlug.ShouldBe("board");
        }
        await using (var db = world.Db()) await db.Cards.Where(c => c.Id == id).ExecuteUpdateAsync(s => s.SetProperty(c => c.CardFileVisibility, CardFileVisibility.Private));
        var retry = await world.SyncAsync(true); retry.Error.ShouldBeNull(); retry.Policy.RemovalPending.ShouldBeFalse(); retry.Written.ShouldBe(0);
        (await world.Repo.GitReadAsync("ls-files", "-z")).ShouldNotContain("docs/cards");
        (await world.Repo.GitReadAsync("ls-tree", "-r", "--name-only", "HEAD")).ShouldNotContain("docs/cards");
    }

    [Test]
    public async Task Sweep_reports_one_failed_board_and_still_reconciles_another()
    {
        await using var isolated = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var bad = new CardFilePrivacyWorld(isolated.ConnectionString); await bad.InitializeAsync(); await bad.AddCardAsync();
        await using var good = new CardFilePrivacyWorld(isolated.ConnectionString); await good.InitializeAsync(); await good.AddCardAsync();
        await using var db = bad.Db(); await db.Boards.Where(b => b.Id == bad.BoardId).ExecuteUpdateAsync(s => s.SetProperty(b => b.CardFilesDirectorySlug, "../unsafe"));
        var results = await bad.Service(db).SyncAllAsync();
        results.Single(r => r.BoardId == bad.BoardId).WriteSkipReason.ShouldBe("unsafe_card_file_path");
        results.Single(r => r.BoardId == good.BoardId).Written.ShouldBe(2);
        Directory.Exists(bad.DirectoryPath).ShouldBeFalse();
    }
}
