using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[Category("Slow")]
[ParallelLimiter<ProcessSpawnLimit>]
[Timeout(60_000)] // CARD-0824 provisional private Git/schema lifecycle budget.
public sealed class C0824RefOnlyBoundaryTests
{
    [Test]
    public async Task Legacy_release_keeps_the_classified_revision()
    {
        foreach (var site in new[] { "legacy-precheck", "legacy-action" })
        {
            await using var w = await DesktopLegacyWorld.CreateAsync();
            var changed = await w.AddTaskAsync(ageHours: 60);
            var control = await w.AddTaskAsync(ageHours: 50);
            var source = (await w.ReadAsync(changed.Path, "rev-parse", "HEAD")).Trim();
            var path = Path.Combine(changed.Path, changed.Id8 + ".txt");
            var bytes = await File.ReadAllBytesAsync(path);
            var reached = false;
            await using var scope = w.Host.Services.CreateAsyncScope();
            var service = scope.ServiceProvider.GetRequiredService<DesktopLegacyReleasePolicyService>();
            service.DesktopBoundaryAsync = async (boundary, id, ct) =>
            {
                if (id != changed.Id || boundary != "before:" + site) return;
                reached = true;
                await using var db = w.Host.CreateContext();
                var task = await db.AgentTasks.SingleAsync(t => t.Id == id, ct);
                task.ConcurrencyToken = Guid.NewGuid();
                await db.SaveChangesAsync(ct);
            };
            var run = await service.RunTickAsync(default);
            reached.ShouldBeTrue("reached:A1:" + site + ":revision-gap");
            (await w.RowAsync(run.RunId, changed)).ReasonCode.ShouldBe("task_changed", "stale-snapshot-held:" + site);
            (await w.ReleasesAsync(changed)).ShouldBeEmpty("no-stale-release:" + site);
            (await w.ReadAsync(w.Fixture.Repository, "rev-parse", changed.FullRef)).Trim().ShouldBe(source);
            (await File.ReadAllBytesAsync(path)).ShouldBe(bytes);
            (await w.ReleasesAsync(control)).ShouldNotBeEmpty("same-token-control-acts");
            Directory.Exists(control.Path).ShouldBeFalse("same-token-control-retires");
        }
    }

    [Test]
    public async Task Local_merge_and_checkout_typed_removal_refuse_ref_only_owner()
    {
        await using var h = new LandingSafetyHarness();
        await h.InitializeAsync();
        var g = h.Fixture;
        var source = (await g.RequiredAsync(g.Repository, "rev-parse", g.SourceRef)).Trim();
        source.ShouldBe((await g.RequiredAsync(g.Repository, "rev-parse", g.TargetRef)).Trim(), "removable-local-merge-target");
        var inspection = await g.Git.InspectAsync(g.Coordinates, default);
        inspection.Accepted.ShouldBeTrue("removable-clean-source");
        var binding = await RefOnlyWorkspaceTestSetup.BindAsync(h, removeAuthoring: false);
        var decoyRef = "refs/heads/foreign-decoy";
        await g.RequiredAsync(g.Repository, "worktree", "add", "-b", decoyRef[11..], binding.Identity, source);
        await File.WriteAllTextAsync(Path.Combine(binding.Identity, "keep.txt"), "foreign decoy");
        var sourceBytes = await File.ReadAllBytesAsync(Path.Combine(g.Source, "keep.txt"));
        await using var scope = h.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var task = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == g.TaskId);
        var service = scope.ServiceProvider.GetRequiredService<DelegationWorktreeService>();
        foreach (var cleanup in new[] { false, true })
        {
            g.Git.Commands.Clear();
            var result = cleanup ? await service.TryCleanupNoChangeAsync(task, default) : await service.TryMergeBackAsync(task, default);
            g.Git.Commands.ShouldBeEmpty("merge-zero-git:" + (cleanup ? "cleanup" : "merge"));
            result.Detail.ShouldBe("desktop_not_required");
        }
        var leases = h.Services.GetRequiredService<IRepositoryMutationLease>();
        await using var lease = await leases.TryAcquireAsync(g.Repository, default);
        lease.ShouldNotBeNull("owned-local-merge-lease");
        var manager = scope.ServiceProvider.GetRequiredService<IWorktreeManager>();
        // An ordinary clean/published LocalMerge request, independently of the merge entry fence.
        var request = new WorktreeRemovalRequest(WorktreeRemovalPurpose.LocalMerge, g.Coordinates,
            inspection.Snapshot!.CommonDirectory, inspection.Snapshot.GitDirectory, source, source, null, lease!,
            TargetCheckoutRecorded: true, TargetCheckoutPath: g.Repository);
        request.Source.DesktopWorkspaceKind.ShouldBe(DesktopWorkspaceKind.Checkout, "independent-checkout-typed-request");
        foreach (var contradictory in new[] { false, true })
        {
            if (contradictory) await db.AgentTasks.Where(t => t.Id == task.Id).ExecuteUpdateAsync(u =>
                u.SetProperty(t => t.DesktopWorkspaceKind, DesktopWorkspaceKind.Checkout));
            g.Git.Commands.Clear();
            var removed = await manager.TryRemoveAsync(request, default);
            var reason = contradictory ? "desktop_workspace_binding_contradictory" : "desktop_not_required";
            removed.Residue.ShouldBe(reason, "owner-row-refused=" + reason);
            g.Git.Commands.ShouldBeEmpty("owner-row-before-remover-git");
            removed.DirectoryGone.ShouldBeFalse(); removed.BranchDeleted.ShouldBeFalse();
            (await File.ReadAllBytesAsync(Path.Combine(g.Source, "keep.txt"))).ShouldBe(sourceBytes);
            (await File.ReadAllTextAsync(Path.Combine(binding.Identity, "keep.txt"))).ShouldBe("foreign decoy");
            (await g.RequiredAsync(g.Repository, "rev-parse", g.SourceRef)).Trim().ShouldBe(source);
            (await g.RequiredAsync(g.Repository, "rev-parse", decoyRef)).Trim().ShouldBe(source);
        }
    }

    [Test]
    public async Task Incomplete_preparation_refuses_initial_and_resumed_land()
    {
        foreach (var state in new[] { TaskRefWorkspacePreparationState.Planned, TaskRefWorkspacePreparationState.RefCreated })
        foreach (var site in new[] { "resolver", "protocol" })
        {
            await using var h = new LandingSafetyHarness();
            await h.InitializeAsync();
            var source = await h.AddSourceAsync();
            var binding = await RefOnlyWorkspaceTestSetup.BindAsync(h);
            await h.RequestAsync("c0824-controlled-verifier", source);
            await using var scope = h.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var task = await db.AgentTasks.SingleAsync(t => t.Id == h.Fixture.TaskId);
            var request = await db.AgentTaskLandRequests.SingleAsync(r => r.Id == task.CurrentLandRequestId);
            var leases = h.Services.GetRequiredService<IRepositoryMutationLease>();
            await using var lease = await leases.TryAcquireAsync(h.Fixture.Repository, default);
            lease.ShouldNotBeNull();
            var resolver = ActivatorUtilities.CreateInstance<AgentTaskLandSourceResolver>(scope.ServiceProvider);
            if (site == "protocol")
            {
                var resolved = await resolver.ResolveAsync(task, request, lease!, default);
                resolved.Operation.ShouldNotBeNull("reached:A3:protocol:persisted-operation");
                task.ActiveLandingId.ShouldBe(resolved.Operation!.Id);
            }
            await using (var writer = h.CreateContext())
                await writer.TaskRefWorkspaceBindings.Where(b => b.Id == binding.Id)
                    .ExecuteUpdateAsync(u => u.SetProperty(b => b.PreparationState, state));
            // Real local and origin S make preparation the decisive refusal.
            (await h.Fixture.RequiredAsync(h.Fixture.Repository, "rev-parse", h.Fixture.SourceRef)).Trim().ShouldBe(source);
            (await h.Fixture.RequiredAsync(h.Fixture.Remote, "rev-parse", h.Fixture.SourceRef)).Trim().ShouldBe(source);
            h.Fixture.Git.Commands.Clear();
            var reason = site == "resolver" ? (await resolver.ResolveAsync(task, request, lease!, default)).Reason
                : (await scope.ServiceProvider.GetRequiredService<AgentTaskLandingProtocol>().RunAsync(task, lease!, request, default)).Reason;
            reason.ShouldBe("ref_only_preparation_incomplete", "preparation-incomplete:" + site + ":" + state);
            h.Fixture.Git.Commands.ShouldBeEmpty("preparation-before-child-ref-remote-writes");
            (await h.Fixture.RequiredAsync(h.Fixture.Remote, "rev-parse", h.Fixture.SourceRef)).Trim().ShouldBe(source);
        }
        await using var published = new LandingSafetyHarness();
        await published.InitializeAsync();
        var ready = await published.AddSourceAsync();
        await RefOnlyWorkspaceTestSetup.BindAsync(published);
        await published.RequestAsync("c0824-controlled-verifier", ready);
        (await published.RunQueuedAsync()).ShouldBe(LandRunResult.Complete, "published-counterpart-lands");
    }
}
