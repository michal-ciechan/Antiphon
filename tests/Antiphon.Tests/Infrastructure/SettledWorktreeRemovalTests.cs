using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Infrastructure;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class SettledWorktreeRemovalTests
{
    [Test]
    public async Task C459_InterfaceDefaultCannotDeleteSettledTask()
    {
        var implementation = new LegacyOnlyManager();
        IWorktreeManager manager = implementation;
        var source = new LandSourceCoordinates(Guid.NewGuid(), "fixture-repo", "fixture-tree",
            "refs/heads/source", "refs/heads/master");
        var request = new WorktreeRemovalRequest(WorktreeRemovalPurpose.SettledTask, source,
            "fixture-common", "fixture-admin", new string('a', 40), new string('b', 40),
            null, null!, RetirementId: Guid.NewGuid());
        var result = await manager.TryRemoveAsync(request, CancellationToken.None);
        implementation.LegacyRemovalCalls.ShouldBe(0);
        result.IsClean.ShouldBeFalse();
        result.Residue.ShouldBe("guarded_removal_not_implemented");
        await Task.CompletedTask;
    }

    [Test]
    public async Task C459_PurposeCannotBorrowAuthority()
    {
        var implementation = new LegacyOnlyManager();
        IWorktreeManager manager = implementation;
        var source = new LandSourceCoordinates(Guid.NewGuid(), "fixture-repo", "fixture-tree",
            "refs/heads/source", "refs/heads/master");
        var request = new WorktreeRemovalRequest((WorktreeRemovalPurpose)999, source,
            "fixture-common", "fixture-admin", new string('a', 40), new string('b', 40),
            Guid.NewGuid(), null!);
        var result = await manager.TryRemoveAsync(request, CancellationToken.None);
        implementation.LegacyRemovalCalls.ShouldBe(0);
        result.IsClean.ShouldBeFalse();
    }

    private sealed class LegacyOnlyManager : IWorktreeManager
    {
        public int LegacyRemovalCalls { get; private set; }
        public Task<WorktreeInfo> CreateAsync(string repoPath, string cardId, string baseRef, CancellationToken ct)
            => throw new NotSupportedException();
        public Task<IReadOnlyList<WorktreeInfo>> ListAsync(string repoPath, CancellationToken ct)
            => throw new NotSupportedException();
        public Task RemoveAsync(string repoPath, string worktreePath, CancellationToken ct)
        { LegacyRemovalCalls++; return Task.CompletedTask; }
        public Task TouchAsync(string worktreePath, CancellationToken ct) => throw new NotSupportedException();
        public Task<int> PruneStaleAsync(CancellationToken ct) => throw new NotSupportedException();
    }
}
