using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Enums;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// The extracted live-lease snapshot (CARD-0304 S2). These pin the helper independently of the
/// dispatcher tick so a historical Held event cannot be mistaken for a current hold.
/// </summary>
[Category("Unit")]
public class SharedWriterLeaseProjectionTests
{
    [Test]
    public void unscoped_shared_writers_in_one_checkout_serialise()
    {
        var holder = Holder("writer", WorkspaceMode.Shared, scope: null, key: @"C:\repo");
        var queuedScope = ScopeResolver.Resolve(null, AreaMap.Empty);

        var decision = SharedWriterLeaseProjection.Decide(
            [holder], holder.Key, WorkspaceMode.Shared, queuedScope, serialiseSharedWriters: true);

        decision.IsSerialised.ShouldBeTrue();
        decision.Blocking!.Holder.TaskId.ShouldBe(holder.TaskId);
        SharedWriterLeaseProjection.SerialisingHolders(
                [holder], holder.Key, WorkspaceMode.Shared, queuedScope, true)
            .Count.ShouldBe(1);
    }

    [Test]
    public void unscoped_shared_writers_run_when_the_setting_is_off()
    {
        var holder = Holder("writer", WorkspaceMode.Shared, scope: null, key: @"C:\repo");
        var queuedScope = ScopeResolver.Resolve(null, AreaMap.Empty);

        SharedWriterLeaseProjection.Decide(
                [holder], holder.Key, WorkspaceMode.Shared, queuedScope, serialiseSharedWriters: false)
            .IsSerialised.ShouldBeFalse();
    }

    [Test]
    public void read_only_queued_work_does_not_participate()
    {
        SharedWriterLeaseProjection.Participates(WorkspaceMode.ReadOnly, AgentTaskRole.Code)
            .ShouldBeFalse();
        SharedWriterLeaseProjection.Participates(WorkspaceMode.Shared, AgentTaskRole.Check)
            .ShouldBeFalse();
        SharedWriterLeaseProjection.Participates(WorkspaceMode.Shared, AgentTaskRole.Diagnose)
            .ShouldBeFalse();
        SharedWriterLeaseProjection.Participates(WorkspaceMode.Shared, AgentTaskRole.Code)
            .ShouldBeTrue();
    }

    [Test]
    public void a_worktree_against_a_shared_holder_warns_rather_than_holds()
    {
        var holder = Holder("shared", WorkspaceMode.Shared, scope: "delivery", key: @"C:\repo");
        var queuedScope = ScopeResolver.Resolve("delivery", AreaMap.Empty);

        var decision = SharedWriterLeaseProjection.Decide(
            [holder], holder.Key, WorkspaceMode.Worktree, queuedScope, serialiseSharedWriters: true);

        decision.IsSerialised.ShouldBeFalse();
        decision.Warnings.Count.ShouldBe(1);
        decision.Warnings[0].Policy.ShouldBe(ScopeOverlapPolicy.Warn);
    }

    [Test]
    public void same_repository_subdirectory_compares_on_the_repo_key()
    {
        var holder = SharedWriterLeaseProjection.Holder.From(
            Guid.NewGuid(), "root-directory writer",
            repoPath: @"C:\repo", workingDirectory: @"C:\repo",
            scope: "client/src/App.tsx", WorkspaceMode.Shared, branch: null, AreaMap.Empty);
        var queuedKey = ScopeResolver.KeyFor(@"C:\repo", @"C:\repo\client");
        var queuedScope = ScopeResolver.Resolve("client/**", AreaMap.Empty);

        SharedWriterLeaseProjection.Decide(
                [holder], queuedKey, WorkspaceMode.Shared, queuedScope, serialiseSharedWriters: true)
            .IsSerialised.ShouldBeTrue();
    }

    [Test]
    public void an_empty_holder_snapshot_is_not_a_live_hold()
    {
        // A released historical Held event is not an input. The helper only sees current writers.
        var queuedScope = ScopeResolver.Resolve("delivery", AreaMap.Empty);
        var decision = SharedWriterLeaseProjection.Decide(
            [], @"C:\repo", WorkspaceMode.Shared, queuedScope, serialiseSharedWriters: true);

        decision.IsSerialised.ShouldBeFalse();
        SharedWriterLeaseProjection.SerialisingHolders(
                [], @"C:\repo", WorkspaceMode.Shared, queuedScope, true)
            .ShouldBeEmpty();
    }

    private static SharedWriterLeaseProjection.Holder Holder(
        string title, WorkspaceMode workspace, string? scope, string key) =>
        new(
            Guid.NewGuid(),
            title,
            key,
            ScopeResolver.Resolve(scope, AreaMap.Empty),
            workspace,
            Branch: null);
}
