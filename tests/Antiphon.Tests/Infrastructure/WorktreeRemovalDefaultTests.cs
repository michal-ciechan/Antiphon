using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Infrastructure;

[Category("Unit")]
public sealed class WorktreeRemovalDefaultTests
{
    [Test]
    [Arguments("legacy")]
    [Arguments("publication")]
    [Arguments("local")]
    [Arguments("unknown")]
    public async Task C448_V36_InterfaceDefaultsNeverDelegateDeletion(string entry)
    {
        var implementation = new LegacyOnlyManager();
        IWorktreeManager manager = implementation;
        var source = new LandSourceCoordinates(Guid.NewGuid(), "fixture-repo", "fixture-tree",
            "refs/heads/source", "refs/heads/master");
        var request = new WorktreeRemovalRequest(entry switch
        {
            "publication" => WorktreeRemovalPurpose.Publication,
            "local" => WorktreeRemovalPurpose.LocalMerge,
            _ => (WorktreeRemovalPurpose)999,
        }, source, "fixture-common", "fixture-admin", new string('a', 40), new string('b', 40),
            Guid.NewGuid(), null!);
        var result = entry == "legacy"
            ? await manager.TryRemoveAsync(source.RepositoryPath, source.WorktreePath, "master", CancellationToken.None)
            : await manager.TryRemoveAsync(request, CancellationToken.None);
        implementation.RemovalCalls.ShouldBe(0, "a default interface implementation has no authority to invoke legacy deletion");
        result.IsClean.ShouldBeFalse();
        result.Residue.ShouldBe(entry == "legacy" ? "typed_removal_authority_required" : "guarded_removal_not_implemented");
    }

    private sealed class LegacyOnlyManager : IWorktreeManager
    {
        public int RemovalCalls { get; private set; }
        public Task<WorktreeInfo> CreateAsync(string repoPath, string cardId, string baseRef, CancellationToken ct)
            => throw new NotSupportedException();
        public Task<IReadOnlyList<WorktreeInfo>> ListAsync(string repoPath, CancellationToken ct)
            => throw new NotSupportedException();
        public Task RemoveAsync(string repoPath, string worktreePath, CancellationToken ct)
        { RemovalCalls++; return Task.CompletedTask; }
        public Task TouchAsync(string worktreePath, CancellationToken ct) => throw new NotSupportedException();
        public Task<int> PruneStaleAsync(CancellationToken ct) => throw new NotSupportedException();
    }
}
