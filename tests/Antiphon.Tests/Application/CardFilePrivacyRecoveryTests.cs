using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Git;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public class CardFilePrivacyRecoveryTests
{
    [Test]
    [Arguments("board")] [Arguments("card")] [Arguments("project")]
    public async Task Authoritative_policy_load_after_acquiring_repository_lease_observes_revocation(string mutation)
    {
        await using var world = new CardFilePrivacyWorld();
        await world.InitializeAsync(); await world.AddCardAsync();
        var repository = new FaultRepository("", () => {
            using var db = world.Db();
            if (mutation == "board") db.Boards.Where(b => b.Id == world.BoardId).ExecuteUpdate(s => s.SetProperty(b => b.SyncCardFiles, false));
            if (mutation == "card") db.Cards.Where(c => c.BoardId == world.BoardId).ExecuteUpdate(s => s.SetProperty(c => c.CardFileVisibility, CardFileVisibility.Private));
            if (mutation == "project") db.Projects.Where(p => p.Id == world.ProjectId).ExecuteUpdate(s => s.SetProperty(p => p.RepositoryVisibility, RepositoryVisibility.Unknown));
        });
        await using var context = world.Db();
        var result = await world.Service(context, true, repository: repository).SyncBoardAsync(world.BoardId);
        result.WriteSkipReason.ShouldBe(mutation == "board" ? "board_not_opted_in" : mutation == "card" ? "no_publishable_cards" : "repository_visibility_unknown");
        result.Written.ShouldBe(0);
        Directory.Exists(world.DirectoryPath).ShouldBeFalse();
    }

    [Test]
    [Arguments("delete")]
    [Arguments("after-delete")]
    [Arguments("commit")]
    public async Task Failed_cleanup_blocks_public_additions_and_fresh_service_recovers(string fault)
    {
        await using var world = new CardFilePrivacyWorld();
        await world.InitializeAsync();
        var privateId = await world.AddCardAsync(title: "C408_OLD_EXPORT");
        (await world.SyncAsync(true)).CommitSha.ShouldNotBeNull();
        await using (var db = world.Db())
            await db.Cards.Where(c => c.Id == privateId).ExecuteUpdateAsync(s => s.SetProperty(c => c.CardFileVisibility, CardFileVisibility.Private));
        await world.AddCardAsync("CARD-0002", "New public");
        await using (var db = world.Db())
        {
            var result = await world.Service(db, true, repository: new FaultRepository(fault)).SyncBoardAsync(world.BoardId);
            result.Error.ShouldNotBeNull();
            result.Policy.RemovalPending.ShouldBeTrue();
            result.Written.ShouldBe(0);
            File.Exists(Path.Combine(world.DirectoryPath, "CARD-0002-new-public.md")).ShouldBeFalse();
            (await db.Boards.AsNoTracking().SingleAsync(b => b.Id == world.BoardId)).CardFilesDirectorySlug.ShouldBe("board");
        }
        var retry = await world.SyncAsync(true);
        retry.Error.ShouldBeNull();
        retry.Policy.RemovalPending.ShouldBeFalse();
        File.Exists(Path.Combine(world.DirectoryPath, "CARD-0002-new-public.md")).ShouldBeTrue();
        (await world.Repo.GitReadAsync("ls-tree", "-r", "--name-only", "HEAD")).ShouldNotContain("old-export");
    }

    [Test]
    public async Task Failed_status_preserves_safe_target_and_never_claims_cleanup_complete()
    {
        await using var world = new CardFilePrivacyWorld();
        await world.InitializeAsync();
        await world.AddCardAsync();
        await using var db = world.Db();
        var status = await world.Service(db, repository: new FaultRepository("inspect")).GetStatusAsync(world.BoardId, default);
        status.Reason.ShouldBe("status_unavailable");
        status.RepositoryPath.ShouldBe(world.Repo.Path);
        status.Directory.ShouldBe("docs/cards/board");
        status.RemovalPending.ShouldBeTrue();
        status.WorkingTreeRemovalPending.ShouldBeNull();
        status.GitRemovalPending.ShouldBeNull();
    }

    private sealed class FaultRepository(string fault, Action? beforeValidation = null) : ICardFileRepository
    {
        private readonly CardFileRepository _inner = new(new GitProcessGate(), Options.Create(new GitSettings()), NullLogger<CardFileRepository>.Instance);
        private bool _fired;
        private bool _validated;
        private void Fail(string point) { if (!_fired && point == fault) { _fired = true; throw new IOException("C408_SYNTHETIC_PRIVATE_ERROR"); } }
        public void ValidatePath(string root, string path) { if (!_validated) { _validated = true; beforeValidation?.Invoke(); } _inner.ValidatePath(root, path); }
        public Task<IReadOnlyList<string>> GetManagedGitPathsAsync(string r, string d, CancellationToken ct) => _inner.GetManagedGitPathsAsync(r, d, ct);
        public Task<CardFileCommitResult> CommitAsync(string r, string d, IReadOnlyDictionary<string, string?> expected, string subject, CancellationToken ct) { Fail("commit"); return _inner.CommitAsync(r, d, expected, subject, ct); }
        public Task<CardFileRepositoryState> InspectAsync(string r, string d, CancellationToken ct) { Fail("inspect"); return _inner.InspectAsync(r, d, ct); }
        public Task<string> HashAsync(string r, string p, string body, CancellationToken ct) => _inner.HashAsync(r, p, body, ct);
        public Task UnstageAsync(string r, string d, IReadOnlyList<string> paths, CancellationToken ct) => _inner.UnstageAsync(r, d, paths, ct);
        public Task<bool> IsIgnoredAsync(string r, IReadOnlyList<string> paths, CancellationToken ct) => _inner.IsIgnoredAsync(r, paths, ct);
        public Task<bool> HasIgnoreProtectionAsync(string r, IReadOnlyList<string> slugs, CancellationToken ct) => _inner.HasIgnoreProtectionAsync(r, slugs, ct);
        public Task InstallIgnoreAsync(string r, CancellationToken ct) => _inner.InstallIgnoreAsync(r, ct);
        public Task<string?> ReadAsync(string r, string p, CancellationToken ct) => _inner.ReadAsync(r, p, ct);
        public void Delete(string r, string p) { Fail("delete"); _inner.Delete(r, p); Fail("after-delete"); }
        public Task WriteAsync(string r, string p, string body, Guid boardId, CancellationToken ct) => _inner.WriteAsync(r, p, body, boardId, ct);
        public void RemoveTemporaryFiles(string r, string d, Guid boardId) => _inner.RemoveTemporaryFiles(r, d, boardId);
    }
}
