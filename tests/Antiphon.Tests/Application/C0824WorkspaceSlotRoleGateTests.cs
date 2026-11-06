using System.Text.Json;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.SessionRunner;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;
using Rig = Antiphon.Tests.Application.RemoteWorkspacePreparerTests.Rig;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0824 S1c CP-21 (RG). The role allow-list end to end: server tick -> preparer -> phone-home socket -> the real
/// runner pool (<see cref="C0824SlotTransportWorld"/>), whose ledger and on-disk tree are the recipient evidence. An
/// unlisted role gets the exact legacy request and never holds; <c>none</c> stops new leases while releases continue;
/// a hot reload applies to the next lease; each preparation keeps its first decision across resends until a definitive
/// 409 refusal clears it (T-D-4), and that decision is saved before the request is sent (T-D-5). The flag-off and
/// no-feature cases run on the scripted peer, where the request shape is the whole oracle.
/// </summary>
[Category("Integration")]
[ParallelLimiter<GitProcessSpawnLimit>]
// Each test builds its own world; scratch Git shares the assembly's Git lane. First green TRX on server2, 2026-11-03 at
// 2561829864 (load 29-33, 19 cases at once): every case 29-38.6 s, most of it the shared fixture start they all wait on.
// The budget is 2x that p95; the design's 45 s estimate left under 7 s of headroom.
[Timeout(80_000)]
public sealed class C0824WorkspaceSlotRoleGateTests
{
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(15);
    private const string DecisionPrefix = "workspace_slot_decision: ";

    [Test]
    public async Task Listed_role_leases_a_slot_on_the_real_runner()
    {
        // V-10 (G-3, P-A). A listed role leases: the ledger, the tree and the row agree, with one recorded decision.
        var monitor = new MutableDelegationMonitor("Plan");
        await using var world = await C0824SlotTransportWorld.StartAsync(configure: WithMonitor(monitor));
        var taskId = await SeedAsync(world, AgentTaskRole.Plan);
        await PrepareAsync(world);

        var request = Mirror(MirrorRequests(world.Rig).ShouldHaveSingleItem());
        request.Slot.ShouldBe(true);
        var slot = LeasedFor(world, taskId).ShouldHaveSingleItem();
        slot.PreparationId.ShouldBe(request.Binding!.PreparationId);
        Git(slot.Path, "rev-parse", "HEAD").Trim().ShouldBe(world.Base);
        Git(slot.Path, "symbolic-ref", "HEAD").Trim().ShouldBe("refs/heads/" + RemoteWorkspaceService.OwnedBranch(taskId));
        var task = await world.Rig.ReadTaskAsync(taskId);
        task.RemoteWorktreePath.ShouldBe(slot.Path);
        task.RemoteWorkspaceSlotId.ShouldBe(slot.Id);
        task.RemoteWorkspaceSlotGeneration.ShouldBe(slot.LeaseGeneration);
        (await DecisionsAsync(world.Rig, taskId)).ShouldBe([Decision("slot", request.Binding.PreparationId, "listed")]);
    }

    [Test]
    [Arguments("role-not-listed")]
    [Arguments("none")]
    [Arguments("invalid")]
    [Arguments("mutation")]
    public async Task Unlisted_role_gets_the_legacy_mirror_and_never_holds(string mode)
    {
        // V-11 (G-3, G-4, G-15, G-28). The "never blocks" oracle: the exact legacy request, a task-<id8> mirror on the
        // owned branch, no slot leased, no hold, and one legacy decision naming why.
        var monitor = new MutableDelegationMonitor(mode switch { "role-not-listed" => "Plan", "none" => "none", _ => "*" });
        if (mode == "invalid")
            monitor.Invalid();
        await using var world = await C0824SlotTransportWorld.StartAsync(configure: WithMonitor(monitor));
        // An unlisted Review task (a Code task fails at the progress-baseline capture in this world before remote prep).
        var role = mode switch { "role-not-listed" => AgentTaskRole.Review, "mutation" => AgentTaskRole.Mutation, _ => AgentTaskRole.Plan };
        var taskId = await SeedAsync(world, role, cold: mode == "role-not-listed");
        await PrepareAsync(world);

        var frame = MirrorRequests(world.Rig).ShouldHaveSingleItem();
        AssertLegacyShape(frame, taskId);
        var mirror = LegacyMirror(world, taskId);
        Directory.Exists(mirror).ShouldBeTrue(mirror);
        Git(mirror, "rev-parse", "HEAD").Trim().ShouldBe(world.Base);
        Git(mirror, "symbolic-ref", "HEAD").Trim().ShouldBe("refs/heads/" + RemoteWorkspaceService.OwnedBranch(taskId));
        LeasedFor(world, taskId).ShouldBeEmpty("no slot leased");
        var task = await world.Rig.ReadTaskAsync(taskId);
        Normal(task.RemoteWorktreePath).ShouldBe(Normal(mirror));
        task.RemoteWorkspaceSlotId.ShouldBeNull();
        // Never held for a slot and never warned (a gate that threw would leave the task Queued with a Warning). The
        // ordinary remote-prep Held events (in flight, then ready) are not slot holds.
        (await AllEventsAsync(world.Rig, taskId))
            .Where(item => item.Detail.Contains(WorkspaceSlotCodes.Unavailable, StringComparison.Ordinal)).ShouldBeEmpty("never held");
        (await world.Rig.EventsAsync(taskId, AgentTaskEventType.Warning)).ShouldBeEmpty("never warned");
        var reason = mode switch
        {
            "role-not-listed" => "role_not_listed", "none" => "roles_none", "invalid" => "roles_invalid", _ => "mutation",
        };
        (await DecisionsAsync(world.Rig, taskId)).ShouldBe([Decision("legacy", Mirror(frame).Binding!.PreparationId, reason)]);
    }

    [Test]
    [Arguments("reconcile-pass")]
    [Arguments("hosted-loop")]
    public async Task None_stops_new_leases_while_releases_continue(string pass)
    {
        // V-12 (G-2, G-16, G-17, P-B). After "none", a new task gets the legacy mirror, and an already-leased slot is still
        // released by a direct pass and by the hosted loop (whose IOptions also says "none").
        var monitor = new MutableDelegationMonitor("Plan");
        await using var world = await C0824SlotTransportWorld.StartAsync(configure: WithMonitor(monitor));
        var a = await SeedAsync(world, AgentTaskRole.Plan);
        await PrepareAsync(world);
        var leased = LeasedFor(world, a).ShouldHaveSingleItem();
        // A is made an eligible terminal task now, so the next tick does not launch it (one seat).
        await MakeTerminalAsync(world.Rig, a);

        monitor.Roles = "none";
        var b = await SeedAsync(world, AgentTaskRole.Plan);
        await PrepareAsync(world);
        AssertLegacyShape(MirrorRequests(world.Rig).Where(frame => Mirror(frame).Binding!.TaskId == b).ShouldHaveSingleItem(), b);
        Directory.Exists(LegacyMirror(world, b)).ShouldBeTrue();
        LeasedFor(world, b).ShouldBeEmpty();
        world.Pool.Ledger().Slots.Count.ShouldBe(1, "no new ledger slot");

        if (pass == "reconcile-pass")
        {
            using var scope = world.Rig.Provider.CreateScope();
            (await scope.ServiceProvider.GetRequiredService<WorkspaceSlotReleaseService>()
                .ReconcileAsync(CancellationToken.None).WaitAsync(Bound)).ShouldBe(1);
        }
        else
        {
            using var hosted = new WorkspaceSlotReleaseHostedService(
                world.Rig.Provider.GetRequiredService<IServiceScopeFactory>(),
                Options.Create(new DelegationSettings { WorkspaceSlotsEnabled = true, WorkspaceSlotRoles = "none" }),
                world.Rig.Clock, NullLogger<WorkspaceSlotReleaseHostedService>.Instance);
            await hosted.StartAsync(CancellationToken.None);
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
            while ((await world.Rig.ReadTaskAsync(a)).RemoteWorkspaceSlotReleasedAt is null && DateTime.UtcNow < deadline)
                await Task.Delay(100);
            await hosted.StopAsync(CancellationToken.None);
        }

        var free = world.Pool.Ledger().Slot(leased.Id)!;
        free.State.ShouldBe(WorkspaceSlotStates.Free);
        Git(free.Path, "rev-parse", "HEAD").Trim().ShouldBe(world.Base, "detached at the published SHA");
        Should.Throw<InvalidOperationException>(() => Git(free.Path, "symbolic-ref", "HEAD"), "HEAD is detached");
        var task = await world.Rig.ReadTaskAsync(a);
        task.RemoteWorkspaceSlotReleasedAt.ShouldNotBeNull();
        task.RemoteWorktreePath.ShouldBeNull();
        task.RemoteWorkspaceSlotId.ShouldBe(leased.Id);
        task.RemoteWorkspaceSlotGeneration.ShouldBe(leased.LeaseGeneration);
    }

    [Test]
    public async Task Removed_role_slot_is_released_normally()
    {
        // V-13 (G-18). Removing a role from the list does not strand the slots that role already holds.
        var monitor = new MutableDelegationMonitor("Plan,Docs");
        await using var world = await C0824SlotTransportWorld.StartAsync(configure: WithMonitor(monitor));
        var taskId = await SeedAsync(world, AgentTaskRole.Plan);
        await PrepareAsync(world);
        var leased = LeasedFor(world, taskId).ShouldHaveSingleItem();
        monitor.Roles = "Docs";
        await MakeTerminalAsync(world.Rig, taskId);

        using (var scope = world.Rig.Provider.CreateScope())
            (await scope.ServiceProvider.GetRequiredService<WorkspaceSlotReleaseService>()
                .ReconcileAsync(CancellationToken.None).WaitAsync(Bound)).ShouldBe(1);
        world.Pool.Ledger().Slot(leased.Id)!.State.ShouldBe(WorkspaceSlotStates.Free);
        (await world.Rig.ReadTaskAsync(taskId)).RemoteWorkspaceSlotReleasedAt.ShouldNotBeNull();
    }

    [Test]
    public async Task Hot_reload_applies_to_the_next_lease_without_restart()
    {
        // V-14 (G-14, P-C). A real reloading JSON file through S-2 in one provider: "Plan" leases, "none" gives the
        // legacy mirror, "Plan" leases again; no rig restart.
        var directory = Path.Combine(Path.GetTempPath(), "c0824-s1c-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var file = Path.Combine(directory, "appsettings.Development.json");
        File.WriteAllText(file, Roles("Plan"));
        using var configuration = (ConfigurationRoot)new ConfigurationBuilder()
            .AddJsonFile(file, optional: false, reloadOnChange: true).Build();
        try
        {
            await using var world = await C0824SlotTransportWorld.StartAsync(configure: services =>
            {
                services.AddDelegationSettings(configuration);
                services.AddSingleton<WorkspaceSlotRoleGate>();
            });
            var gate = world.Rig.Provider.GetRequiredService<WorkspaceSlotRoleGate>();

            var first = await SeedAsync(world, AgentTaskRole.Plan);
            await PrepareAsync(world);
            Mirror(LastRequestFor(world.Rig, first)).Slot.ShouldBe(true);
            LeasedFor(world, first).ShouldHaveSingleItem();
            await MakeTerminalAsync(world.Rig, first);

            Replace(file, Roles("none"));
            await PollAsync(gate, "roles_none");
            var second = await SeedAsync(world, AgentTaskRole.Plan);
            await PrepareAsync(world);
            AssertLegacyShape(LastRequestFor(world.Rig, second), second);
            Directory.Exists(LegacyMirror(world, second)).ShouldBeTrue();
            LeasedFor(world, second).ShouldBeEmpty();
            await MakeTerminalAsync(world.Rig, second);

            Replace(file, Roles("Plan"));
            await PollAsync(gate, "listed");
            var third = await SeedAsync(world, AgentTaskRole.Plan);
            await PrepareAsync(world);
            Mirror(LastRequestFor(world.Rig, third)).Slot.ShouldBe(true);
            LeasedFor(world, third).ShouldHaveSingleItem();
        }
        finally
        {
            try { Directory.Delete(directory, recursive: true); } catch (IOException) { }
        }
    }

    [Test]
    [Arguments("slot-then-none")]
    [Arguments("legacy-then-listed")]
    [Arguments("busy-then-none")]
    [Arguments("slot-refused-then-none")]
    public async Task Retry_keeps_the_first_decision_of_its_preparation(string flip)
    {
        // V-15 (G-22..G-25). A resend of one preparation keeps its recorded decision across a role flip, so no task ever
        // holds both a slot and a task-<id8>; only a definitive 409 refusal clears it and lets the next send decide again.
        var monitor = new MutableDelegationMonitor(flip == "legacy-then-listed" ? "Docs" : "Plan");
        await using var world = await C0824SlotTransportWorld.StartAsync(configure: WithMonitor(monitor));
        var taskId = await SeedAsync(world, AgentTaskRole.Plan);

        if (flip == "busy-then-none")
        {
            world.Rig.Peer.SilentFor(PhoneHomeOperation.WorkspaceMirror);
            await world.Rig.TickAsync().WaitAsync(Bound);
            var held = await world.Rig.WaitForRequestsAsync(PhoneHomeOperation.WorkspaceMirror, 1);
            monitor.Roles = "none";
            await world.Rig.TickAsync().WaitAsync(Bound);
            await world.Rig.TickAsync().WaitAsync(Bound);
            world.Rig.Peer.RequestCount(PhoneHomeOperation.WorkspaceMirror).ShouldBe(1, "no second request while busy");
            await world.Rig.Peer.EmitAsync(await Task.Run(() => world.Deliver(held)));
            await world.Rig.Preparer.WhenIdleAsync().WaitAsync(Bound);
            var busy = LeasedFor(world, taskId).ShouldHaveSingleItem();
            (await world.Rig.ReadTaskAsync(taskId)).RemoteWorkspaceSlotId.ShouldBe(busy.Id);
            world.Pool.Ledger().Slots.Count.ShouldBe(1);
            return;
        }

        var first = true;
        world.Intercept = frame =>
        {
            if (frame.Operation != PhoneHomeOperation.WorkspaceMirror || !first)
                return null;
            first = false;
            if (flip == "slot-refused-then-none")
                return new PhoneHomeFrame(PhoneHomeFrameKind.Error, frame.Epoch, frame.RequestId, frame.Operation,
                    ErrorCode: PhoneHomeProblemTypes.UnsupportedTarget,
                    ErrorDetail: WorkspaceSlotCodes.UnavailableDetail(WorkspaceSlotCodes.OverflowCap), StatusCode: 409);
            world.Deliver(frame);
            return new PhoneHomeFrame(PhoneHomeFrameKind.Result, frame.Epoch, frame.RequestId, frame.Operation,
                JsonSerializer.SerializeToElement(new { }, PhoneHomeFraming.Json));
        };
        await PrepareAsync(world);
        (await world.Rig.ReadTaskAsync(taskId)).RemoteWorktreePath.ShouldBeNull("the first send failed");

        monitor.Roles = flip == "legacy-then-listed" ? "Plan" : "none";
        await world.Rig.AdvanceAsync(TimeSpan.FromHours(1));
        await PrepareAsync(world);

        var requests = MirrorRequests(world.Rig);
        requests.Count.ShouldBe(2);
        var preparation = Mirror(requests[0]).Binding!.PreparationId;
        Mirror(requests[1]).Binding!.PreparationId.ShouldBe(preparation, "the retry resends the same preparation");
        var task = await world.Rig.ReadTaskAsync(taskId);
        var leased = LeasedFor(world, taskId).ToList();
        var mirror = LegacyMirror(world, taskId);
        var decisions = await DecisionsAsync(world.Rig, taskId);
        switch (flip)
        {
            case "slot-then-none":
                Mirror(requests[1]).Slot.ShouldBe(true, "the resend keeps the recorded slot decision");
                leased.ShouldHaveSingleItem();
                task.RemoteWorkspaceSlotId.ShouldBe(leased[0].Id, "the row names the ledger's Leased slot");
                Directory.Exists(mirror).ShouldBeFalse("no task-<id8> beside the slot");
                decisions.ShouldBe([Decision("slot", preparation, "listed")]);
                break;
            case "legacy-then-listed":
                AssertLegacyShape(requests[1], taskId);
                leased.ShouldBeEmpty("no slot beside the task-<id8>");
                Normal(task.RemoteWorktreePath).ShouldBe(Normal(mirror));
                decisions.ShouldBe([Decision("legacy", preparation, "role_not_listed")]);
                break;
            default:
                Mirror(requests[0]).Slot.ShouldBe(true);
                AssertLegacyShape(requests[1], taskId);
                leased.ShouldBeEmpty();
                Directory.Exists(mirror).ShouldBeTrue();
                Normal(task.RemoteWorktreePath).ShouldBe(Normal(mirror), "the task has its mirror, not a hold");
                decisions.ShouldBe([
                    Decision("slot", preparation, "listed"),
                    DecisionPrefix + $"cleared preparation={preparation:D} refusal={WorkspaceSlotCodes.Unavailable}",
                    Decision("legacy", preparation, "roles_none"),
                ]);
                break;
        }

        (leased.Count > 0 && Directory.Exists(mirror)).ShouldBeFalse("never both a slot and a task-<id8>");
    }

    [Test]
    public async Task Decision_is_recorded_before_the_mirror_is_sent()
    {
        // V-16 (G-26). Inside the send, the preparation's decision is already durable.
        var monitor = new MutableDelegationMonitor("Plan");
        await using var world = await C0824SlotTransportWorld.StartAsync(configure: WithMonitor(monitor));
        var taskId = await SeedAsync(world, AgentTaskRole.Plan);
        List<string>? seen = null;
        Guid? sentPreparation = null;
        world.Intercept = frame =>
        {
            if (frame.Operation != PhoneHomeOperation.WorkspaceMirror || seen is not null)
                return null;
            sentPreparation = Mirror(frame).Binding!.PreparationId;
            using var db = world.Rig.NewDb();
            seen = db.AgentTaskEvents.AsNoTracking()
                .Where(item => item.AgentTaskId == taskId && item.Type == AgentTaskEventType.WorkspaceSlotDecision)
                .Select(item => item.Detail).ToList();
            return null;
        };
        await PrepareAsync(world);

        seen.ShouldNotBeNull("the interceptor ran");
        seen.ShouldBe([Decision("slot", sentPreparation!.Value, "listed")], "saved before the request");
        LeasedFor(world, taskId).ShouldHaveSingleItem();
    }

    [Test]
    [Arguments("flag-off:Plan")]
    [Arguments("flag-off:none")]
    [Arguments("flag-off:*")]
    [Arguments("runner-lacks-feature")]
    public async Task Flag_off_or_no_feature_ignores_the_list(string mode)
    {
        // V-17 (G-19, G-21, G-27). Either half off: the exact legacy request whatever the list, and no decision event.
        var monitor = new MutableDelegationMonitor(mode.StartsWith("flag-off:", StringComparison.Ordinal) ? mode["flag-off:".Length..] : "Plan");
        await using var rig = mode == "runner-lacks-feature"
            ? await Rig.StartAsync(settings => settings.WorkspaceSlotsEnabled = true, configure: WithMonitor(monitor))
            : await Rig.StartAsync(capabilities: SlotRunner(), configure: WithMonitor(monitor));
        rig.Peer.SilentFor(PhoneHomeOperation.WorkspaceMirror);
        var taskId = await rig.SeedAsync(task => task.Role = AgentTaskRole.Plan);
        await rig.TickAsync().WaitAsync(Bound);
        var request = await rig.WaitForRequestsAsync(PhoneHomeOperation.WorkspaceMirror, 1);
        AssertLegacyShape(request, taskId);
        var path = "/work/worktrees/" + RemoteWorkspaceService.MirrorName(taskId);
        await rig.Peer.EmitAsync(RemoteWorkspacePreparerTests.MirrorResult(request, path));
        await rig.Preparer.WhenIdleAsync().WaitAsync(Bound);
        var task = await rig.ReadTaskAsync(taskId);
        task.RemoteWorktreePath.ShouldBe(path);
        task.RemoteWorkspaceSlotId.ShouldBeNull();
        (await DecisionsAsync(rig, taskId)).ShouldBeEmpty("no decision event unless flag, binding and feature all hold");
    }

    [Test]
    public async Task Flag_off_hosted_loop_sends_no_release()
    {
        // V-18 (G-20). The flag gates the release loop: an eligible slot-bound row gets no release frame.
        await using var rig = await Rig.StartAsync(capabilities: SlotRunner(),
            configure: services => services.AddScoped<WorkspaceSlotReleaseService>());
        rig.Peer.SilentFor(PhoneHomeOperation.WorkspaceSlotRelease);
        var taskId = await rig.SeedAsync(task =>
        {
            task.Status = AgentTaskStatus.Succeeded;
            task.CompletedAt = DateTime.UtcNow;
            task.RemoteWorktreePath = "/work/worktrees/ws-01";
            task.RemoteWorkspaceSlotId = "ws-01";
            task.RemoteWorkspaceSlotGeneration = 1;
            task.WorktreePath = Path.Combine(Path.GetTempPath(), "c0824-desktop-" + task.Id.ToString("N"));
        });
        using var hosted = new WorkspaceSlotReleaseHostedService(rig.Provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new DelegationSettings { WorkspaceSlotsEnabled = false, WorkspaceSlotRoles = "*" }),
            rig.Clock, NullLogger<WorkspaceSlotReleaseHostedService>.Instance);
        await hosted.StartAsync(CancellationToken.None);
        await Task.Delay(TimeSpan.FromSeconds(2));
        await hosted.StopAsync(CancellationToken.None);
        rig.Peer.RequestCount(PhoneHomeOperation.WorkspaceSlotRelease).ShouldBe(0);
        (await rig.ReadTaskAsync(taskId)).RemoteWorkspaceSlotReleasedAt.ShouldBeNull();
    }

    private static RunnerCapabilitiesDto SlotRunner() =>
        new("InboxConhost", "inbox", "test", false, Features: [RunnerCapabilityFeatures.WorkspaceSlotV1], VerificationCustodyBackend: null);

    private static Action<IServiceCollection> WithMonitor(MutableDelegationMonitor monitor) => services =>
    {
        services.AddSingleton<IOptionsMonitor<DelegationSettings>>(monitor);
        services.AddSingleton<WorkspaceSlotRoleGate>();
    };

    private static async Task<Guid> SeedAsync(C0824SlotTransportWorld world, AgentTaskRole role, bool cold = false)
    {
        var id = await world.SeedAsync();
        await using var db = world.Rig.NewDb();
        await db.AgentTasks.Where(t => t.Id == id).ExecuteUpdateAsync(set => set
            .SetProperty(t => t.Role, role).SetProperty(t => t.WorkspaceColdReset, cold));
        return id;
    }

    private static async Task PrepareAsync(C0824SlotTransportWorld world)
    {
        await world.Rig.TickAsync().WaitAsync(Bound);
        await world.Rig.Preparer.WhenIdleAsync().WaitAsync(Bound);
    }

    private static async Task MakeTerminalAsync(Rig rig, Guid taskId)
    {
        await using var db = rig.NewDb();
        await db.AgentTasks.Where(t => t.Id == taskId).ExecuteUpdateAsync(set => set
            .SetProperty(t => t.Status, AgentTaskStatus.Succeeded).SetProperty(t => t.CompletedAt, DateTime.UtcNow));
    }

    private static List<PhoneHomeFrame> MirrorRequests(Rig rig) =>
        rig.Peer.Incoming.ToArray()
            .Where(frame => frame.Kind == PhoneHomeFrameKind.Request && frame.Operation == PhoneHomeOperation.WorkspaceMirror)
            .ToList();

    private static PhoneHomeFrame LastRequestFor(Rig rig, Guid taskId) =>
        MirrorRequests(rig).Last(frame => Mirror(frame).Binding?.TaskId == taskId);

    private static PhoneHomeWorkspaceMirrorRequest Mirror(PhoneHomeFrame frame) =>
        frame.Payload!.Value.Deserialize<PhoneHomeWorkspaceMirrorRequest>(PhoneHomeFraming.Json)!;

    /// <summary>The S1 CP-5 comparer: exactly the five legacy properties (no slot, no cold) and the task-&lt;id8&gt; name.</summary>
    private static void AssertLegacyShape(PhoneHomeFrame frame, Guid taskId)
    {
        var names = frame.Payload!.Value.EnumerateObject().Select(property => property.Name).ToHashSet(StringComparer.Ordinal);
        names.SetEquals(["branch", "sha", "name", "repository", "binding"]).ShouldBeTrue(string.Join(",", names));
        Mirror(frame).Name.ShouldBe(RemoteWorkspaceService.MirrorName(taskId));
    }

    private static IEnumerable<WorkspaceSlotRecord> LeasedFor(C0824SlotTransportWorld world, Guid taskId) =>
        world.Pool.Ledger().Slots.Where(slot => slot.State == WorkspaceSlotStates.Leased && slot.TaskId == taskId);

    private static string LegacyMirror(C0824SlotTransportWorld world, Guid taskId) =>
        world.Work + "/worktrees/" + RemoteWorkspaceService.MirrorName(taskId);

    private static string? Normal(string? path) => path?.Replace('\\', '/').TrimEnd('/');

    private static string Decision(string kind, Guid preparation, string reason) =>
        DecisionPrefix + $"{kind} preparation={preparation:D} reason={reason}";

    private static async Task<List<string>> DecisionsAsync(Rig rig, Guid taskId) =>
        (await rig.EventsAsync(taskId, AgentTaskEventType.WorkspaceSlotDecision)).Select(item => item.Detail).ToList();

    private static async Task<List<AgentTaskEvent>> AllEventsAsync(Rig rig, Guid taskId)
    {
        await using var db = rig.NewDb();
        return await db.AgentTaskEvents.AsNoTracking().Where(item => item.AgentTaskId == taskId).ToListAsync();
    }

    private static string Roles(string value) => "{\"Delegation\":{\"WorkspaceSlotRoles\":\"" + value + "\"}}";

    private static void Replace(string path, string json)
    {
        var staged = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllText(staged, json);
        File.Move(staged, path, overwrite: true);
    }

    private static async Task PollAsync(WorkspaceSlotRoleGate gate, string reason)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        var last = gate.Evaluate(AgentTaskRole.Plan, null);
        while (last.Reason != reason && DateTime.UtcNow < deadline)
        {
            await Task.Delay(100);
            last = gate.Evaluate(AgentTaskRole.Plan, null);
        }

        last.Reason.ShouldBe(reason, "the reload did not reach the gate within 10 s");
    }

    private static string Git(string directory, params string[] args) => C0824SlotTransportWorld.Git(directory, args);
}
