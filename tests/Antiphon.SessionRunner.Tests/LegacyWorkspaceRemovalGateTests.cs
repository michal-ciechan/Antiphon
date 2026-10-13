using Antiphon.SessionRunner;
using Antiphon.SessionRunner.Contracts;
using Shouldly;
using TUnit.Core;

namespace Antiphon.SessionRunner.Tests;

[Category("Integration"), Category("Slow")]
[NotInParallel("SessionLiveness")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class LegacyWorkspaceRemovalGateTests
{
    [Test]
    public async Task C1032_LegacyRemoveHeldLease()
    {
        await using var f = await Ready();
        await using var use = await ((RunnerWorkspaceUseGate)f.Gate).AcquireUseAsync(f.Checkout, CancellationToken.None);
        var starts = f.GitStarts;
        var refusal = await Should.ThrowAsync<PhoneHomeAdmissionException>(
            f.Workspace().RemoveAsync(new(f.Checkout, true), CancellationToken.None));
        refusal.Code.ShouldBe("workspace_in_use");
        Directory.Exists(f.Checkout).ShouldBeTrue();
        f.GitStarts.ShouldBe(starts);
    }

    [Test]
    public async Task C1032_LegacyPruneHeldLease()
    {
        await using var f = await Ready();
        await using var use = await ((RunnerWorkspaceUseGate)f.Gate).AcquireUseAsync(f.Checkout, CancellationToken.None);
        Directory.Delete(f.Checkout, recursive: true); // Test-owned scratch absence exercises the prune branch.
        var registrations = CompletedCardRunnerFixture.Git(f.Repository, "worktree", "list", "--porcelain");
        var starts = f.GitStarts;
        var refusal = await Should.ThrowAsync<PhoneHomeAdmissionException>(
            f.Workspace().RemoveAsync(new(f.Checkout, true), CancellationToken.None));
        refusal.Code.ShouldBe("workspace_in_use");
        CompletedCardRunnerFixture.Git(f.Repository, "worktree", "list", "--porcelain").ShouldBe(registrations);
        f.GitStarts.ShouldBe(starts);
    }

    [Test]
    public async Task C1032_LegacyRemoveReleasedLease()
    {
        await using var f = await Ready();
        var use = await ((RunnerWorkspaceUseGate)f.Gate).AcquireUseAsync(f.Checkout, CancellationToken.None);
        await use.DisposeAsync();
        (await f.Workspace().RemoveAsync(new(f.Checkout, false), CancellationToken.None)).Removed.ShouldBeTrue();
        Directory.Exists(f.Checkout).ShouldBeFalse();
        CompletedCardRunnerFixture.Git(f.Repository, "worktree", "list", "--porcelain").ShouldNotContain(f.Checkout);
    }

    [Test]
    public async Task C1032_LegacyRemovalCreationOrders()
    {
        await using var f = await Ready();
        var service = f.Workspace();
        (await service.RemoveAsync(new(f.Checkout, false), CancellationToken.None)).Removed.ShouldBeTrue();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        service.WorkspaceAdmissionForTest = async () => { entered.SetResult(); await release.Task; };
        var create = service.MirrorAsync(f.MirrorRequest with { Binding = null }, CancellationToken.None);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            var refusal = await Should.ThrowAsync<PhoneHomeAdmissionException>(
                service.RemoveAsync(new(f.Checkout, true), CancellationToken.None));
            refusal.Code.ShouldBe("workspace_in_use");
        }
        finally { release.TrySetResult(); await create; }
        Directory.Exists(f.Checkout).ShouldBeTrue();
        service.WorkspaceAdmissionForTest = null;
        Task<PhoneHomeWorkspaceMirrorResponse>? recreating = null;
        f.BeforeGitStart = psi =>
        {
            if (!psi.ArgumentList.Contains("remove")) return;
            var starts = f.GitStarts;
            recreating = service.MirrorAsync(f.MirrorRequest with { Binding = null }, CancellationToken.None);
            f.GitStarts.ShouldBe(starts, "creation must start no Git under removal admission");
            recreating.IsCompleted.ShouldBeFalse("creation must wait through removal and prune");
        };
        (await service.RemoveAsync(new(f.Checkout, false), CancellationToken.None)).Removed.ShouldBeTrue();
        f.BeforeGitStart = null;
        recreating.ShouldNotBeNull();
        (await recreating!).Path.ShouldBe(f.Checkout.Replace('\\', '/'));
        CompletedCardRunnerFixture.Git(f.Checkout, "rev-parse", "HEAD").Trim().ShouldBe(f.Sha);
    }

    [Test]
    public async Task C1032_LegacyUnconfiguredRemoval()
    {
        await using var f = await Ready();
        await using var use = await ((RunnerWorkspaceUseGate)f.Gate).AcquireUseAsync(f.Checkout, CancellationToken.None);
        var legacy = f.Workspace(gated: false);
        (await legacy.RemoveAsync(new(f.Checkout, false), CancellationToken.None)).Removed.ShouldBeTrue();
        Directory.Exists(f.Checkout).ShouldBeFalse();
        (await legacy.RemoveAsync(new(f.Checkout, false), CancellationToken.None)).Removed.ShouldBeTrue();
    }

    [Test]
    public async Task C1032_LegacyRemovalReleaseOnFailure()
    {
        foreach (var cancel in new[] { false, true })
        {
            await using var f = await Ready();
            var service = f.Workspace();
            using var cancellation = new CancellationTokenSource();
            f.BeforeGitStart = _ =>
            {
                if (!cancel) throw new FenceEffectException();
                cancellation.Cancel();
                throw new OperationCanceledException(cancellation.Token);
            };
            if (cancel)
                await Should.ThrowAsync<OperationCanceledException>(service.RemoveAsync(new(f.Checkout, true), cancellation.Token));
            else
                await Should.ThrowAsync<FenceEffectException>(service.RemoveAsync(new(f.Checkout, true), cancellation.Token));
            f.BeforeGitStart = null;
            // The nonblocking next removal fails immediately if the exceptional exit leaked its lease.
            (await service.RemoveAsync(new(f.Checkout, false), CancellationToken.None)).Removed.ShouldBeTrue();
        }
    }

    [Test]
    public async Task C1032_LegacyRemovalRetainedOwner()
    {
        await using var f = await Ready();
        var gate = (RunnerWorkspaceUseGate)f.Gate;
        var owned = true;
        gate.RecordOwner(f.Checkout, Guid.NewGuid(), () => owned);
        var refusal = await Should.ThrowAsync<PhoneHomeAdmissionException>(
            f.Workspace().RemoveAsync(new(f.Checkout, true), CancellationToken.None));
        refusal.Code.ShouldBe("workspace_in_use");
        Directory.Exists(f.Checkout).ShouldBeTrue();
        // Prune is repository-wide: a request for an absent sibling must not bypass this owner.
        await Should.ThrowAsync<PhoneHomeAdmissionException>(f.Workspace().RemoveAsync(
            new(Path.Combine(Path.GetDirectoryName(f.Checkout)!, "task-aabbccdd"), true), CancellationToken.None));
        owned = false;
        (await f.Workspace().RemoveAsync(new(f.Checkout, false), CancellationToken.None)).Removed.ShouldBeTrue();
    }

    private static async Task<CompletedCardRunnerFixture> Ready()
    {
        var f = new CompletedCardRunnerFixture();
        f.InstallGate();
        await f.Runtime.AdoptOrphanedHostsAsync(new SystemProcessLivenessProbe(), CancellationToken.None);
        return f;
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task C1032_VerificationRemovalHeldLease(bool absent)
    {
        await using var f = await Ready();
        var service = f.Workspace(publishedBranch: "main");
        var created = await service.CreateVerificationAsync(new("task-abcdef12", f.Sha,
            "feat/card-task-abcdef12"), CancellationToken.None);
        var path = created.Coordinates.WorktreePath;
        await using var use = await ((RunnerWorkspaceUseGate)f.Gate).AcquireUseAsync(path, CancellationToken.None);
        if (absent) Directory.Delete(path, recursive: true);
        var starts = f.GitStarts;
        var refusal = await Should.ThrowAsync<PhoneHomeAdmissionException>(service.RemoveVerificationAsync(
            new(created.Coordinates, f.Sha, []), CancellationToken.None));
        refusal.Code.ShouldBe("workspace_in_use");
        f.GitStarts.ShouldBe(starts);
        Directory.Exists(path).ShouldBe(!absent);
        CompletedCardRunnerFixture.Git(f.Repository, "show-ref", "--verify", "refs/heads/feat/card-task-abcdef12").ShouldContain(f.Sha);
    }

    [Test]
    public async Task C1032_VerificationCreationAdmission()
    {
        await using var f = await Ready();
        var service = f.Workspace(publishedBranch: "main");
        var path = Path.Combine(service.WorktreeRoot, "task-abcdef12");
        var use = await ((RunnerWorkspaceUseGate)f.Gate).AcquireUseAsync(path, CancellationToken.None);
        var starts = f.GitStarts;
        var create = service.CreateVerificationAsync(new("task-abcdef12", f.Sha,
            "feat/card-task-abcdef12"), CancellationToken.None);
        try
        {
            f.GitStarts.ShouldBe(starts, "verification creation must take admission before Git");
            create.IsCompleted.ShouldBeFalse();
        }
        finally { await use.DisposeAsync(); await create; }
        Directory.Exists(path).ShouldBeTrue();
    }

    [Test]
    public async Task C1032_LegacyRemovalJournalFence()
    {
        foreach (var scenario in new[] { "retiring", "torn", "census" })
        {
            await using var f = await Ready();
            if (scenario == "retiring") (await f.Retire()).ShouldBeTrue();
            else if (scenario == "torn") File.WriteAllText(Path.Combine(f.State, "journal.json"), "{");
            else f.InstallGate(restart: true);
            var starts = f.GitStarts;
            var refusal = await Should.ThrowAsync<PhoneHomeAdmissionException>(
                f.Workspace().RemoveAsync(new(f.Checkout, true), CancellationToken.None));
            refusal.Code.ShouldBe("workspace_retiring_or_unknown");
            f.GitStarts.ShouldBe(starts, scenario);
            Directory.Exists(f.Checkout).ShouldBeTrue();
        }
    }
}
