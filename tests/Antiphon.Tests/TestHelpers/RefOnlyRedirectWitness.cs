using System.Text.Json;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Git;
using Antiphon.SessionRunner;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.Application;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace Antiphon.Tests.TestHelpers;

internal static class RefOnlyRedirectWitness
{
    public static async Task VerifyAsync()
    {
        // One schema, host and pair of recipients for the whole method. Each variant
        // leases a distinct task/ref; restoring its single fault must reach target preparation.
            const string target = "redirect-target";
            await using var world = await C0824SlotTransportWorld.StartAsync(runnerSettings: settings =>
            {
                settings.AllowDelegatedTasks = true;
                settings.StandingAgentId = Guid.Empty;
                settings.ClaudeAuthProbeEnabled = false;
                settings.Runners[settings.AllowedRunnerId] = PhoneHomeRunnerCatalog.FromLegacy(settings);
                settings.Runners[target] = PhoneHomeRunnerCatalog.FromLegacy(settings);
            });
            var rig = world.Rig;
            var targetStore = Guid.NewGuid();
            var recipient = world.CreatePeerRunner(target, targetStore);
            await using var peer = await rig.Host.ConnectPeerAsync(runnerId: target, storeId: targetStore,
                secret: rig.Host.Secret, capabilities: new("InboxConhost", "inbox", "fixture", false,
                    Features: [RunnerCapabilityFeatures.WorkspaceSlotV1]));
            rig.Host.Directory.MarkRecovered(await rig.Host.WaitLiveAsync(runnerId: target));
            peer.Reply = frame => frame.Operation is PhoneHomeOperation.WorkspaceMirror or PhoneHomeOperation.Capabilities
                ? recipient.Runner.DispatchAsync(frame, default).GetAwaiter().GetResult() : null;
            C0824SlotTransportWorld.Git(world.Origin, "remote", "add", "origin", world.Origin);
        foreach (var variant in new[] { "store", "generation", "unknown", "unreadable-reply", "result-generation", "valid" })
        {
            var targetPreparationsBefore = peer.RequestCount(PhoneHomeOperation.WorkspaceMirror);
            var oldReleasesBefore = rig.Peer.RequestCount(PhoneHomeOperation.WorkspaceSlotRelease);
            world.RealDesktopGit = false;
            world.Intercept = null;
            rig.Host.Directory.ApplyState(rig.RunnerId, new(false, null, null, null, false, null, null, null));
            var id = await world.SeedAsync();
            await rig.TickAsync();
            await rig.Preparer.WhenIdleAsync();
            var slot = world.Pool.Ledger().Slots.Single(s => s.TaskId == id);
            slot.State.ShouldBe(WorkspaceSlotStates.Leased, "reached:C4:" + variant + ":old-ledger-leased");
            C0824SlotTransportWorld.Git(slot.Path, "config", "user.email", "c4@localhost");
            C0824SlotTransportWorld.Git(slot.Path, "config", "user.name", "c4");
            await File.WriteAllTextAsync(Path.Combine(slot.Path, "custody.txt"), "independent source bytes");
            C0824SlotTransportWorld.Git(slot.Path, "add", "custody.txt");
            C0824SlotTransportWorld.Git(slot.Path, "commit", "-m", "published source");
            C0824SlotTransportWorld.Git(slot.Path, "push", "origin", "HEAD");
            var source = C0824SlotTransportWorld.Git(slot.Path, "rev-parse", "HEAD").Trim();
            var branch = RemoteWorkspaceService.OwnedBranch(id);
            var fullRef = "refs/heads/" + branch;
            // Ordinary publication may refresh this task's origin-tracking ref. Seed it
            // at the independently published S before freezing the complete foreign-ref inventory.
            C0824SlotTransportWorld.Git(world.Origin, "fetch", "origin", fullRef + ":refs/remotes/origin/" + branch);
            var git = new TaskProgressGit();
            Guid bindingId;
            Guid mirrorId;
            var identity = Path.Combine(world.Root, ".ref-only", id.ToString("N"));
            await using (var db = rig.NewDb())
            {
                var task = await db.AgentTasks.SingleAsync(t => t.Id == id);
                task.RepoPath = world.Origin;
                task.WorkingDirectory = world.Origin;
                task.WorktreeBaseSha = world.Base;
                task.WorktreeBaseRef = "refs/heads/main";
                task.WorktreeBaseSource = WorktreeBaseSource.Explicit;
                task.ProgressBaselineJson = TaskProgressJson.SerializeBaseline(new(1, DateTime.UtcNow, DateTime.UtcNow,
                    new(world.Origin, await git.CommonDirectoryAsync(world.Origin, default), id, null, fullRef, world.Base,
                        new(ProgressRemoteState.Present, world.Base, await git.EndpointFingerprintAsync(world.Origin, default))), null));
                await db.SaveChangesAsync();
                await RefOnlyWorkspaceTestSetup.RecordAsync(db, task, git, world.Root);
                bindingId = task.TaskRefWorkspaceBindingId!.Value;
                var mirror = await db.TaskWorktreeMirrorBindings.SingleAsync(b => b.TaskId == id);
                mirrorId = mirror.Id;
                if (variant == "store") mirror.RunnerStoreId = Guid.NewGuid().ToString("D");
                if (variant == "generation") task.RemoteWorkspaceSlotGeneration++;
                await db.SaveChangesAsync();
            }
            world.RealDesktopGit = true;
            var head = C0824SlotTransportWorld.Git(world.Origin, "rev-parse", "HEAD");
            var index = C0824SlotTransportWorld.Git(world.Origin, "ls-files", "-s");
            var registrations = C0824SlotTransportWorld.Git(world.Origin, "worktree", "list", "--porcelain");
            var refs = C0824SlotTransportWorld.Git(world.Origin, "for-each-ref", "--format=%(refname) %(objectname)");
            var fault = variant is "unknown" or "unreadable-reply" or "result-generation";
            world.Intercept = frame =>
            {
                if (!fault || frame.Operation != PhoneHomeOperation.WorkspaceSlotRelease) return null;
                if (variant == "unknown")
                {
                    var request = frame.Payload!.Value.Deserialize<PhoneHomeWorkspaceSlotReleaseRequest>(PhoneHomeFraming.Json)!;
                    return world.Deliver(frame with { Payload = JsonSerializer.SerializeToElement(request with { SlotId = "ws-9999" }, PhoneHomeFraming.Json) });
                }
                var reply = world.Deliver(frame);
                if (variant == "unreadable-reply")
                    return reply with { Payload = JsonSerializer.SerializeToElement(new { }, PhoneHomeFraming.Json) };
                var response = reply.Payload!.Value.Deserialize<PhoneHomeWorkspaceSlotReleaseResponse>(PhoneHomeFraming.Json)!;
                return reply with { Payload = JsonSerializer.SerializeToElement(response with { Generation = response.Generation + 1 }, PhoneHomeFraming.Json) };
            };
            rig.Host.Directory.ApplyState(rig.RunnerId, new(true, DateTimeOffset.UtcNow, "C4", target, false, null, null, null));
            await rig.TickAsync();
            if (variant != "valid")
            {
                var held = await rig.ReadTaskAsync(id);
                held.RunnerId.ShouldBe(rig.RunnerId, "redirect-refused:" + variant);
                held.RemoteWorkspaceSlotReleasedAt.ShouldBeNull("old-release-unconfirmed:" + variant);
                peer.RequestCount(PhoneHomeOperation.WorkspaceMirror).ShouldBe(targetPreparationsBefore, "target-unprepared:" + variant);
                var old = world.Pool.Ledger().Slot(slot.Id)!;
                old.State.ShouldBe(variant is "unreadable-reply" or "result-generation" ? WorkspaceSlotStates.Free : WorkspaceSlotStates.Leased,
                    "independent-old-ledger:" + variant);
                if (variant == "store") rig.Peer.RequestCount(PhoneHomeOperation.WorkspaceSlotRelease).ShouldBe(oldReleasesBefore, "wrong-store-zero-release");
                fault = false;
                await using (var db = rig.NewDb())
                {
                    var task = await db.AgentTasks.SingleAsync(t => t.Id == id);
                    task.RemoteWorkspaceSlotGeneration = slot.LeaseGeneration;
                    (await db.TaskWorktreeMirrorBindings.SingleAsync(b => b.Id == mirrorId)).RunnerStoreId = rig.Host.StoreId.ToString("D");
                    await db.SaveChangesAsync();
                }
                world.StartRunner(); // Rebuild recipient service/pool over its retained ledger.
                await rig.RestartAsync();
                await rig.TickAsync();
            }
            await rig.Preparer.WhenIdleAsync();
            var moved = await rig.ReadTaskAsync(id);
            var recoveryEvents = await rig.EventsAsync(id, AgentTaskEventType.Warning);
            moved.RunnerId.ShouldBe(target, "remote-redirect:" + variant + "; status=" + moved.Status
                + "; release=" + moved.RemoteWorkspaceSlotReleasedAt
                + "; release-requests=" + rig.Peer.RequestCount(PhoneHomeOperation.WorkspaceSlotRelease)
                + "; events=" + string.Join(" | ", recoveryEvents.Select(e => e.Detail)));
            moved.TaskRefWorkspaceBindingId.ShouldBe(bindingId, "source-binding-retained:" + variant);
            moved.WorktreeBaseSha.ShouldBe(world.Base, "creation-base-retained:" + variant);
            var oldFree = world.Pool.Ledger().Slot(slot.Id)!;
            oldFree.State.ShouldBe(WorkspaceSlotStates.Free, "old-ledger-free:" + variant);
            oldFree.TaskId.ShouldBeNull("old-owner-detached:" + variant);
            var targetSlots = recipient.Pool.Ledger().Slots.Where(s => s.TaskId == id).ToList();
            await using (var diagnostics = rig.NewDb())
            {
                var events = await diagnostics.AgentTaskEvents.AsNoTracking()
                    .Where(e => e.AgentTaskId == id).OrderBy(e => e.At).Select(e => e.Detail).ToListAsync();
                targetSlots.Count.ShouldBe(1, "target-real-preparation:" + variant + "; status=" + moved.Status
                    + "; prep-failures=" + moved.RemotePrepFailures + "; not-before=" + moved.DispatchNotBeforeAt
                    + "; mirror-requests=" + peer.RequestCount(PhoneHomeOperation.WorkspaceMirror)
                    + "; events=" + string.Join(" | ", events));
            }
            var newSlot = targetSlots.Single();
            moved.RemoteWorktreePath.ShouldBe(newSlot.Path, "target-real-preparation:" + variant);
            C0824SlotTransportWorld.Git(newSlot.Path, "rev-parse", "HEAD").Trim().ShouldBe(source, "target-at-S:" + variant);
            C0824SlotTransportWorld.Git(world.Origin, "rev-parse", fullRef).Trim().ShouldBe(source, "source-ref-survives:" + variant);
            C0824SlotTransportWorld.Git(world.Origin, "show", source + ":custody.txt").Trim().ShouldBe("independent source bytes");
            C0824SlotTransportWorld.Git(world.Origin, "rev-parse", "HEAD").ShouldBe(head, "main-intact:" + variant);
            C0824SlotTransportWorld.Git(world.Origin, "ls-files", "-s").ShouldBe(index, "index-intact:" + variant);
            C0824SlotTransportWorld.Git(world.Origin, "worktree", "list", "--porcelain").ShouldBe(registrations, "desktop-registry-intact:" + variant);
            C0824SlotTransportWorld.Git(world.Origin, "for-each-ref", "--format=%(refname) %(objectname)").ShouldBe(refs, "all-source-refs-intact:" + variant);
            Directory.Exists(identity).ShouldBeFalse("no-desktop-worktree:" + variant);
            // Fixture cleanup uses the actual target release; no following variant inherits a seat.
            (await recipient.Pool.ReleaseAsync(new(newSlot.Id, newSlot.LeaseGeneration, id), default)).Outcome
                .ShouldBeOneOf("released", "already_free", "quarantined");
            await using var finished = rig.NewDb();
            await finished.AgentTasks.Where(t => t.Id == id).ExecuteUpdateAsync(s => s.SetProperty(t => t.Status, AgentTaskStatus.Canceled));
        }
    }
}
