using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Agents.SessionRunner;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Server.Infrastructure.Security;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

public sealed partial class PhoneHomeRollingRunnerTests
{
    /// <summary>Options for a rolling world that is not the V-2 pair. Null keeps that pair.</summary>
    private sealed class RollingOptions
    {
        public PhoneHomeRunnerSettings? Settings { get; init; }
        public Action<PhoneHomeRunnerSettings>? ConfigureSettings { get; init; }
        public Func<PhoneHomeTestHost, ISessionRunnerDirectory>? Directory { get; init; }
        public TimeProvider? Clock { get; init; }
        public int? PeerACapacity { get; init; }
        public int? PeerBCapacity { get; init; }
    }

    [Test]
    [Timeout(120_000)]
    public async Task Drain_requires_the_operator_token_persists_the_state_and_survives_a_directory_rebuild()
    {
        await using var world = await RollingWorld.StartAsync();
        var body = new DrainBody("image upgrade", RollingRunnerSettings.Server2Temp, false);

        var denied = await PostDrainAsync(world, RollingRunnerSettings.Server2, body, token: false, proxied: true);
        denied.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        await using (var db = world.NewDb())
            (await db.SessionRunnerStates.CountAsync()).ShouldBe(0);

        var drained = await PostDrainAsync(world, RollingRunnerSettings.Server2, body, token: true);
        drained.StatusCode.ShouldBe(HttpStatusCode.OK);
        await using (var db = world.NewDb())
        {
            var row = await db.SessionRunnerStates.SingleAsync(s => s.RunnerId == RollingRunnerSettings.Server2);
            row.Draining.ShouldBeTrue();
            row.DrainedAt.ShouldNotBeNull();
            row.RedirectTo.ShouldBe(RollingRunnerSettings.Server2Temp);
        }

        var status = await ReadStatusAsync(world, RollingRunnerSettings.Server2);
        status.GetProperty("draining").GetBoolean().ShouldBeTrue();
        status.GetProperty("acceptingNewWork").GetBoolean().ShouldBeFalse();
        status.GetProperty("dispatchEligible").GetBoolean().ShouldBeTrue();

        var rebuilt = new PhoneHomeRunnerDirectory(
            world.Host.Local,
            Options.Create(world.Configured),
            world.Host.App.Services.GetRequiredService<IServiceScopeFactory>(),
            TimeProvider.System);
        var loader = new RunnerStateLoader(world.Host.App.Services.GetRequiredService<IServiceScopeFactory>(), rebuilt);
        await loader.StartAsync(CancellationToken.None);
        var refused = Should.Throw<ServiceUnavailableException>(() => rebuilt.ResolveForNewWork(RollingRunnerSettings.Server2));
        refused.Code.ShouldBe(PhoneHomeProblemTypes.RunnerDraining);
    }

    [Test]
    [Timeout(120_000)]
    public async Task A_draining_runner_still_serves_input_transcript_kill_and_release_for_its_sessions()
    {
        await using var world = await RollingWorld.StartAsync();
        var drained = await PostDrainAsync(
            world, RollingRunnerSettings.Server2, new DrainBody("keep sessions", RollingRunnerSettings.Server2Temp), token: true);
        drained.StatusCode.ShouldBe(HttpStatusCode.OK);

        var routing = new RoutingSessionRunnerClient(world.RunnerDirectory);
        await routing.SendInputAsync(world.SessionA, "still-here", CancellationToken.None);
        await routing.GetTranscriptAsync(world.SessionA, CancellationToken.None);
        await routing.KillGenerationAsync(world.SessionA, DateTime.UtcNow, CancellationToken.None);
        await routing.ReleaseSlotAsync(world.SessionA, "drain", CancellationToken.None);
        foreach (var operation in new[]
                 {
                     PhoneHomeOperation.Input, PhoneHomeOperation.Transcript,
                     PhoneHomeOperation.KillGeneration, PhoneHomeOperation.ReleaseSlot,
                 })
        {
            world.PeerA.RequestCount(operation).ShouldBeGreaterThanOrEqualTo(1);
            world.PeerB.RequestCount(operation).ShouldBe(0);
        }

        var inventory = await world.RunnerDirectory.GetInventoryAsync(RollingRunnerSettings.Server2, CancellationToken.None);
        inventory.ShouldBeOfType<RunnerInventory.Available>();
        world.PeerA.RequestCount(PhoneHomeOperation.List).ShouldBeGreaterThanOrEqualTo(1);

        const string message = "queued-while-draining";
        await world.Queue.EnqueueAsync(world.SessionA, message, MessageSendMode.WhenIdle, CancellationToken.None);
        world.PeerA.Inputs.ShouldContain(frame => InputText(frame) == message);
        world.PeerB.Inputs.ShouldNotContain(frame => InputText(frame) == message);
        await using var db = world.NewDb();
        var row = await db.SessionQueuedMessages.AsNoTracking().SingleAsync(m => m.AgentSessionId == world.SessionA);
        row.Status.ShouldBe(QueuedMessageStatus.Sent);
        row.DeliveryVerdict.ShouldBe(DeliveryVerdict.Delivered);
    }

    [Test]
    [Timeout(120_000)]
    public async Task Default_placement_follows_the_drain_redirect_and_falls_back_without_one()
    {
        await using var world = await RollingWorld.StartAsync();
        world.RunnerDirectory.ApplyState(RollingRunnerSettings.Server2, Draining(RollingRunnerSettings.Server2Temp));
        await world.SetDefaultsAsync(RollingRunnerSettings.Server2);

        var redirected = await world.ReadTaskAsync((await world.CreateTaskAsync("global redirect")).Id);
        redirected.Task.RunnerId.ShouldBe(RollingRunnerSettings.Server2Temp);
        redirected.Created.ShouldContain("source=default");
        redirected.Created.ShouldContain("reason=drain_redirect:server2");
        redirected.Warnings.ShouldBeEmpty();

        await world.SetDefaultsAsync(
            RollingRunnerSettings.Server2,
            new PutRunnerKindDefault(AgentKind.ClaudeCode, RollingRunnerSettings.Server2));
        var kind = await world.ReadTaskAsync((await world.CreateTaskAsync("kind redirect")).Id);
        kind.Task.RunnerId.ShouldBe(RollingRunnerSettings.Server2Temp);
        kind.Created.ShouldContain("source=kind-default");
        kind.Created.ShouldContain("reason=drain_redirect:server2");

        var liveB = world.RunnerDirectory.SnapshotLive(RollingRunnerSettings.Server2Temp)!;
        world.RunnerDirectory.Disconnect(liveB, "offline");
        var offline = await world.ReadTaskAsync((await world.CreateTaskAsync("offline redirect")).Id);
        offline.Task.RunnerId.ShouldBeNull();
        offline.Created.ShouldContain("reason=runner_draining");
        offline.Warnings.Count.ShouldBe(1);

        world.RunnerDirectory.ApplyState(RollingRunnerSettings.Server2, Draining(null));
        var nowhere = await world.ReadTaskAsync((await world.CreateTaskAsync("no redirect")).Id);
        nowhere.Task.RunnerId.ShouldBeNull();
        nowhere.Created.ShouldContain("reason=runner_draining");
        nowhere.Warnings.Count.ShouldBe(1);

        var pinned = await world.ReadTaskAsync((await world.CreateTaskAsync("explicit", runnerId: RollingRunnerSettings.Server2)).Id);
        pinned.Task.RunnerId.ShouldBe(RollingRunnerSettings.Server2);
        pinned.Task.RunnerSelectionSource.ShouldBe(RunnerSelectionSource.Explicit);
    }

    [Test]
    [Timeout(180_000)]
    public async Task A_queued_task_bound_to_a_draining_runner_is_rebound_to_the_redirect_before_claim()
    {
        await using var world = await RollingWorld.StartAsync();
        var later = DateTime.UtcNow.AddMinutes(10);
        var moved = await world.SeedQueuedAsync(RollingRunnerSettings.Server2, "moved", task =>
        {
            task.RemoteWorktreePath = "/work/worktrees/old";
            task.RemotePrepFailures = 2;
            task.DispatchNotBeforeAt = later;
            task.RunnerSelectionSource = RunnerSelectionSource.GlobalDefault;
        });
        var explicitTask = await world.SeedQueuedAsync(RollingRunnerSettings.Server2, "explicit", task =>
            task.RunnerSelectionSource = RunnerSelectionSource.Explicit);
        var stayed = await world.SeedQueuedAsync(RollingRunnerSettings.Server2, "live", task =>
        {
            task.Status = AgentTaskStatus.Dispatched;
            task.AgentSessionId = world.SessionA;
            task.RunnerSelectionSource = RunnerSelectionSource.Explicit;
        });

        var drained = await PostDrainAsync(
            world, RollingRunnerSettings.Server2, new DrainBody("move unlaunched", RollingRunnerSettings.Server2Temp), token: true);
        drained.StatusCode.ShouldBe(HttpStatusCode.OK);

        for (var tick = 0; tick < 5 && world.PeerB.Launches.Count < 2; tick++)
        {
            await world.TickAsync();
            await world.WaitPrepAsync();
        }

        world.PeerA.RequestCount(PhoneHomeOperation.WorkspaceRemove).ShouldBe(1);
        world.PeerA.Launches.ShouldBeEmpty();
        world.PeerB.Launches.Count.ShouldBe(2);
        world.PeerB.RequestCount(PhoneHomeOperation.WorkspaceMirror).ShouldBeGreaterThanOrEqualTo(1);
        await AssertRebindAsync(world, moved, RunnerSelectionSource.GlobalDefault);
        await AssertRebindAsync(world, explicitTask, RunnerSelectionSource.Explicit);
        await using var db = world.NewDb();
        (await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == stayed)).RunnerId.ShouldBe(RollingRunnerSettings.Server2);
    }

    [Test]
    [Timeout(120_000)]
    public async Task A_queued_task_on_a_draining_runner_without_an_eligible_redirect_is_held()
    {
        await using (var world = await RollingWorld.StartAsync())
        {
            var id = await world.SeedQueuedAsync(RollingRunnerSettings.Server2, "no-redirect");
            (await PostDrainAsync(world, RollingRunnerSettings.Server2, new DrainBody("hold"), token: true))
                .StatusCode.ShouldBe(HttpStatusCode.OK);
            await world.TickAsync();
            await world.TickAsync();
            await AssertHeldAsync(world, id);
        }

        await using (var world = await RollingWorld.StartAsync())
        {
            var id = await world.SeedQueuedAsync(RollingRunnerSettings.Server2, "offline-redirect");
            var liveB = world.RunnerDirectory.SnapshotLive(RollingRunnerSettings.Server2Temp)!;
            world.RunnerDirectory.Disconnect(liveB, "offline");
            (await PostDrainAsync(
                    world, RollingRunnerSettings.Server2,
                    new DrainBody("hold", RollingRunnerSettings.Server2Temp), token: true))
                .StatusCode.ShouldBe(HttpStatusCode.OK);
            await world.TickAsync();
            await world.TickAsync();
            await AssertHeldAsync(world, id);
            (await world.ReadTaskAsync(id)).Task.RunnerId.ShouldBe(RollingRunnerSettings.Server2);
        }
    }

    [Test]
    [Timeout(120_000)]
    public async Task Clear_drain_restores_new_work_and_resets_the_retire_fields()
    {
        await using var world = await RollingWorld.StartAsync();
        world.RunnerDirectory.ApplyState(RollingRunnerSettings.Server2, new RunnerState(
            true, DateTimeOffset.UtcNow, "upgrade", RollingRunnerSettings.Server2Temp, true,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, "idle"));
        await world.SetDefaultsAsync(RollingRunnerSettings.Server2);

        var denied = await PostClearAsync(world, RollingRunnerSettings.Server2, token: false, proxied: true);
        denied.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        world.RunnerDirectory.DrainState(RollingRunnerSettings.Server2)!.Draining.ShouldBeTrue();

        var cleared = await PostClearAsync(world, RollingRunnerSettings.Server2, token: true);
        cleared.StatusCode.ShouldBe(HttpStatusCode.OK);
        await using (var db = world.NewDb())
        {
            var row = await db.SessionRunnerStates.SingleAsync(s => s.RunnerId == RollingRunnerSettings.Server2);
            row.Draining.ShouldBeFalse();
            row.RedirectTo.ShouldBeNull();
            row.RetiredAt.ShouldBeNull();
            row.RetireWhenIdle.ShouldBeFalse();
            row.IdleObservedAt.ShouldBeNull();
            row.RetireReason.ShouldBeNull();
        }

        var created = await world.ReadTaskAsync((await world.CreateTaskAsync("after clear")).Id);
        created.Task.RunnerId.ShouldBe(RollingRunnerSettings.Server2);
        var status = await ReadStatusAsync(world, RollingRunnerSettings.Server2);
        status.GetProperty("acceptingNewWork").GetBoolean().ShouldBeTrue();
    }

    [Test]
    [Timeout(120_000)]
    public async Task Status_reports_sessions_queued_tasks_and_runner_sessions_per_runner()
    {
        await using var world = await RollingWorld.StartAsync();
        await world.SeedSessionAsync(SessionStatus.Starting, RollingRunnerSettings.Server2);
        await world.SeedQueuedAsync(RollingRunnerSettings.Server2, "waiting");
        world.PeerA.Sessions.Add(new RunnerSessionDto(
            Guid.NewGuid(), 1, DateTime.UtcNow, "Running", null, "", 0, AcceptedStartedAt: DateTime.UtcNow));
        world.PeerA.Sessions.Add(new RunnerSessionDto(
            Guid.NewGuid(), 1, DateTime.UtcNow, "Exited", 0, "done", 0, AcceptedStartedAt: DateTime.UtcNow));
        await world.RunnerDirectory.GetInventoryAsync(RollingRunnerSettings.Server2, CancellationToken.None);
        await world.RunnerDirectory.GetInventoryAsync(RollingRunnerSettings.Server2Temp, CancellationToken.None);

        var server2 = await ReadStatusAsync(world, RollingRunnerSettings.Server2);
        NullableInt(server2, "sessions").ShouldBe(2);
        NullableInt(server2, "queuedTasks").ShouldBe(1);
        NullableInt(server2, "runnerSessions").ShouldBe(1);
        var temp = await ReadStatusAsync(world, RollingRunnerSettings.Server2Temp);
        NullableInt(temp, "sessions").ShouldBe(0);
        NullableInt(temp, "queuedTasks").ShouldBe(0);
        NullableInt(temp, "runnerSessions").ShouldBe(0);

        world.RunnerDirectory.ApplyState(RollingRunnerSettings.Server2, Draining(null, "upgrade window"));
        var drained = await ReadStatusAsync(world, RollingRunnerSettings.Server2);
        drained.GetProperty("drainReason").GetString().ShouldBe("upgrade window");
        drained.GetProperty("drainedAt").ValueKind.ShouldNotBe(JsonValueKind.Null);
    }

    [Test]
    [Timeout(180_000)]
    public async Task A_source_landing_mutation_for_a_draining_runner_is_admitted_held_and_never_moved()
    {
        await using var world = await RollingWorld.StartAsync();
        var drained = await PostDrainAsync(
            world, RollingRunnerSettings.Server2, new DrainBody("keep snapshot", RollingRunnerSettings.Server2Temp), token: true);
        drained.StatusCode.ShouldBe(HttpStatusCode.OK);

        HttpStatusCode status;
        AgentTaskCreatedDto? created = null;
        try
        {
            created = await world.CreateSourceLandingAsync();
            status = HttpStatusCode.Created;
        }
        catch (ServiceUnavailableException)
        {
            status = HttpStatusCode.ServiceUnavailable;
        }
        catch (ConflictException ex)
        {
            status = (HttpStatusCode)ex.StatusCode;
        }

        status.ShouldBe(HttpStatusCode.Created);
        var saved = await world.ReadTaskAsync(created!.Id);
        await world.TickAsync();
        saved = await world.ReadTaskAsync(created.Id);
        saved.Task.RunnerId.ShouldBe(RollingRunnerSettings.Server2);
        saved.Task.Status.ShouldBe(AgentTaskStatus.Queued);
        var events = await world.EventTextAsync(created.Id);
        events.ShouldContain(text => text.Contains(DispatchHoldDetails.RunnerDraining(
            RollingRunnerSettings.Server2, "keep snapshot", RollingRunnerSettings.Server2Temp), StringComparison.Ordinal));
        events.ShouldNotContain(text => text.Contains("drain_redirect", StringComparison.Ordinal));
        world.PeerB.Launches.ShouldBeEmpty();
        world.PeerB.RequestCount(PhoneHomeOperation.WorkspaceMirror).ShouldBe(0);
    }

    [Test]
    [Timeout(180_000)]
    public async Task A_rebound_task_waits_for_capacity_on_the_redirect_target()
    {
        await using var world = await RollingWorld.StartAsync(new RollingOptions
        {
            ConfigureSettings = settings => settings.Runners[RollingRunnerSettings.Server2Temp].MaxCapacity = 1,
            PeerBCapacity = 1,
        });
        await world.SeedSessionAsync(SessionStatus.Running, RollingRunnerSettings.Server2Temp);
        var id = await world.SeedQueuedAsync(RollingRunnerSettings.Server2, "capacity");
        (await PostDrainAsync(
                world, RollingRunnerSettings.Server2, new DrainBody("to temp", RollingRunnerSettings.Server2Temp), token: true))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        await world.TickAsync();
        await world.WaitPrepAsync();
        var held = await world.ReadTaskAsync(id);
        held.Task.RunnerId.ShouldBe(RollingRunnerSettings.Server2Temp);
        world.PeerB.Launches.Count.ShouldBe(0);

        await using (var db = world.NewDb())
        {
            await db.AgentSessions.Where(s => s.RunnerId == RollingRunnerSettings.Server2Temp)
                .ExecuteUpdateAsync(s => s.SetProperty(row => row.Status, SessionStatus.Stopped));
        }

        for (var tick = 0; tick < 5 && world.PeerB.Launches.Count == 0; tick++)
        {
            await world.TickAsync();
            await world.WaitPrepAsync();
        }

        world.PeerB.Launches.Count.ShouldBe(1);
    }

    [Test]
    [Timeout(180_000)]
    public async Task A_rebind_survives_a_failed_target_mirror_and_a_fresh_dispatcher_without_a_second_rebind()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        await using var world = await RollingWorld.StartAsync(new RollingOptions { Clock = clock });
        world.PeerBMirrorErrors = 1;
        var id = await world.SeedQueuedAsync(RollingRunnerSettings.Server2, "retry-mirror", task =>
            task.RemoteWorktreePath = "/work/worktrees/once");
        (await PostDrainAsync(
                world, RollingRunnerSettings.Server2, new DrainBody("once", RollingRunnerSettings.Server2Temp), token: true))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        await world.TickAsync();
        await world.WaitPrepAsync();
        world.PeerA.RequestCount(PhoneHomeOperation.WorkspaceRemove).ShouldBe(1);
        (await world.EventTextAsync(id)).Count(text => text.Contains("drain_redirect from=server2 to=server2-temp", StringComparison.Ordinal))
            .ShouldBe(1);

        clock.Advance(TimeSpan.FromSeconds(31));
        for (var tick = 0; tick < 5 && world.PeerB.Launches.Count == 0; tick++)
        {
            await world.TickAsync();
            await world.WaitPrepAsync();
        }

        world.PeerB.Launches.Count.ShouldBe(1);
        world.PeerA.RequestCount(PhoneHomeOperation.WorkspaceRemove).ShouldBe(1);
        (await world.EventTextAsync(id)).Count(text => text.Contains("drain_redirect from=server2 to=server2-temp", StringComparison.Ordinal))
            .ShouldBe(1);
    }

    [Test]
    [Timeout(180_000)]
    public async Task A_failed_workspace_remove_on_the_draining_runner_does_not_block_the_rebind()
    {
        await using var world = await RollingWorld.StartAsync();
        world.PeerARemoveErrors = 1;
        const string oldPath = "/work/worktrees/stuck";
        var id = await world.SeedQueuedAsync(RollingRunnerSettings.Server2, "remove-fails", task =>
            task.RemoteWorktreePath = oldPath);
        (await PostDrainAsync(
                world, RollingRunnerSettings.Server2, new DrainBody("remove fails", RollingRunnerSettings.Server2Temp), token: true))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        for (var tick = 0; tick < 5 && world.PeerB.Launches.Count == 0; tick++)
        {
            await world.TickAsync();
            await world.WaitPrepAsync();
        }

        var saved = await world.ReadTaskAsync(id);
        saved.Task.RunnerId.ShouldBe(RollingRunnerSettings.Server2Temp);
        (await world.EventTextAsync(id)).ShouldContain(text => text.Contains(oldPath, StringComparison.Ordinal));
        world.PeerB.Launches.Count.ShouldBe(1);
    }

    [Test]
    [Timeout(120_000)]
    public async Task A_message_queued_while_a_session_on_a_draining_runner_is_busy_is_delivered_after_its_turn()
    {
        await using var world = await RollingWorld.StartAsync();
        (await PostDrainAsync(
                world, RollingRunnerSettings.Server2, new DrainBody("busy session", RollingRunnerSettings.Server2Temp), token: true))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        await BridgeQueueHarness.InsertEntryAsync(
            world.SessionA, TranscriptKinds.UserPrompt, "open-turn", timestamp: DateTime.UtcNow,
            connectionString: world.Schema.ConnectionString);

        const string message = "after-the-turn";
        await world.Queue.EnqueueAsync(
            world.SessionA, message, MessageSendMode.WhenIdle, CancellationToken.None, deliverIfIdle: false);
        world.PeerA.Inputs.ShouldNotContain(frame => InputText(frame) == message);

        await BridgeQueueHarness.InsertEntryAsync(
            world.SessionA, TranscriptKinds.TurnEnd, stopReason: "end_turn", timestamp: DateTime.UtcNow,
            connectionString: world.Schema.ConnectionString);
        await world.Queue.OnTurnEndAsync(world.SessionA, CancellationToken.None);

        world.PeerA.Inputs.ShouldContain(frame => InputText(frame) == message);
        await using var db = world.NewDb();
        var row = await db.SessionQueuedMessages.AsNoTracking().SingleAsync(m => m.AgentSessionId == world.SessionA);
        row.Status.ShouldBe(QueuedMessageStatus.Sent);
        row.DeliveryVerdict.ShouldBe(DeliveryVerdict.Delivered);
    }

    [Test]
    [Timeout(120_000)]
    public async Task Remote_prep_refuses_a_runner_that_began_draining_after_the_claim_check()
    {
        await using var world = await RollingWorld.StartAsync(new RollingOptions
        {
            Directory = host => new DrainAfterClaimCheckDirectory(host.Directory),
        });
        var id = await world.SeedQueuedAsync(RollingRunnerSettings.Server2, "late-drain");
        await world.TickAsync();
        await world.WaitPrepAsync();

        world.PeerA.RequestCount(PhoneHomeOperation.WorkspaceMirror).ShouldBe(0);
        var saved = await world.ReadTaskAsync(id);
        saved.Task.Status.ShouldBe(AgentTaskStatus.Queued);
        saved.Task.FailureReason.ShouldBeNull();
        (await world.EventTextAsync(id)).ShouldContain(text =>
            text.Contains("draining", StringComparison.OrdinalIgnoreCase));
    }

    [Test]
    [Timeout(120_000)]
    public async Task Drain_redirect_validation_refuses_unknown_disabled_desktop_self_and_draining_targets()
    {
        var secretA = "rolling-a-" + Guid.NewGuid().ToString("N");
        var secretB = "rolling-b-" + Guid.NewGuid().ToString("N");
        var settings = RollingRunnerSettings.Pair(secretA, secretB);
        settings.Runners["server2-off"] = RollingRunnerSettings.Entry("off", "off-secret");
        settings.Runners["server2-off"].Enabled = false;
        await using var world = await RollingWorld.StartAsync(new RollingOptions { Settings = settings });

        await RefuseRedirectAsync(world, "nope", "unknown");
        await RefuseRedirectAsync(world, "server2-off", "disabled");
        await RefuseRedirectAsync(world, "desktop", "desktop");
        await RefuseRedirectAsync(world, RollingRunnerSettings.Server2, "self");

        (await PostDrainAsync(
                world, RollingRunnerSettings.Server2Temp, new DrainBody("temp first", RollingRunnerSettings.Server2), token: true))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        await RefuseRedirectAsync(world, RollingRunnerSettings.Server2Temp, "draining target");

        (await PostDrainAsync(world, RollingRunnerSettings.Server2, new DrainBody(""), token: true))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest, "empty reason");
        (await PostDrainAsync(world, RollingRunnerSettings.Server2, new DrainBody(new string('x', 201)), token: true))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest, "201-character reason");
    }

    private static RunnerState Draining(string? redirect, string reason = "upgrade") => new(
        true, DateTimeOffset.UtcNow, reason, redirect, false, null, null, null);

    private static async Task AssertRebindAsync(RollingWorld world, Guid id, RunnerSelectionSource source)
    {
        var saved = await world.ReadTaskAsync(id);
        saved.Task.RunnerId.ShouldBe(RollingRunnerSettings.Server2Temp);
        saved.Task.RunnerSelectionSource.ShouldBe(source);
        (await world.EventTextAsync(id)).ShouldContain(text =>
            text.Contains("drain_redirect from=server2 to=server2-temp", StringComparison.Ordinal));
    }

    private static async Task AssertHeldAsync(RollingWorld world, Guid id)
    {
        var saved = await world.ReadTaskAsync(id);
        saved.Task.RunnerId.ShouldBe(RollingRunnerSettings.Server2);
        saved.Task.Status.ShouldBe(AgentTaskStatus.Queued);
        var held = (await world.EventTextAsync(id)).Where(text => text.StartsWith("Held:", StringComparison.Ordinal)).ToList();
        held.Count.ShouldBe(1);
        held[0].ShouldStartWith("Held: runner 'server2' is draining");
        world.PeerA.Launches.ShouldBeEmpty();
        world.PeerA.RequestCount(PhoneHomeOperation.WorkspaceMirror).ShouldBe(0);
    }

    private static async Task RefuseRedirectAsync(RollingWorld world, string redirect, string arm)
    {
        var response = await PostDrainAsync(
            world, RollingRunnerSettings.Server2, new DrainBody("bad target", redirect), token: true);
        response.StatusCode.ShouldBe(HttpStatusCode.Conflict, arm);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("code").GetString().ShouldBe(PhoneHomeProblemTypes.RedirectInvalid, arm);
        await using var db = world.NewDb();
        (await db.SessionRunnerStates.CountAsync(s => s.RunnerId == RollingRunnerSettings.Server2)).ShouldBe(0, arm);
    }

    private static Task<HttpResponseMessage> PostDrainAsync(
        RollingWorld world, string runnerId, DrainBody body, bool token, bool proxied = false) =>
        world.Host.PostOperatorAsync(
            $"/api/session-runners/{runnerId}/drain",
            body,
            token ? OperatorTokenFile.ReadOrCreate(world.Host.OperatorTokenPath) : null,
            proxied);

    private static Task<HttpResponseMessage> PostClearAsync(
        RollingWorld world, string runnerId, bool token, bool proxied = false) =>
        world.Host.PostOperatorAsync(
            $"/api/session-runners/{runnerId}/drain/clear",
            new ClearBody("cleared"),
            token ? OperatorTokenFile.ReadOrCreate(world.Host.OperatorTokenPath) : null,
            proxied);

    private static async Task<JsonElement> ReadStatusAsync(RollingWorld world, string runnerId)
    {
        var response = await world.Host.Http.GetAsync($"/api/session-runners/{runnerId}/status");
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static int? NullableInt(JsonElement body, string name)
    {
        var value = body.GetProperty(name);
        return value.ValueKind == JsonValueKind.Null ? null : value.GetInt32();
    }

    private sealed record DrainBody(string Reason, string? RedirectTo = null, bool RetireWhenIdle = false);

    private sealed record ClearBody(string Reason);

    private sealed partial class RollingWorld
    {
        public PhoneHomeRunnerSettings Configured { get; private set; } = null!;
        public int PeerBMirrorErrors { get; set; }
        public int PeerARemoveErrors { get; set; }

        public async Task SetDefaultsAsync(string? globalRunner, params PutRunnerKindDefault[] kinds)
        {
            await using var db = NewDb();
            var defaults = new RunnerDefaultSettingsService(
                db, Options.Create(Harness.Delegation), TimeProvider.System, Harness.EventBus, RunnerDirectory);
            await defaults.EnsureInitializedAsync(CancellationToken.None);
            var current = await defaults.GetAsync(CancellationToken.None);
            await defaults.PutAsync(new PutRunnerDefaultsRequest(
                current.Revision, globalRunner, kinds, "drain placement", "Human"), null, CancellationToken.None);
        }

        public async Task<AgentTaskCreatedDto> CreateTaskAsync(string goal, string? runnerId = null)
        {
            using var scope = Harness.Provider.CreateScope();
            _scopes.Add(scope);
            return await scope.ServiceProvider.GetRequiredService<AgentTaskService>().CreateAsync(
                new CreateAgentTaskRequest(goal, Role: AgentTaskRole.Code, AgentKind: AgentKind.ClaudeCode,
                    Workspace: WorkspaceMode.Worktree, RunnerId: runnerId),
                new AgentTaskService.Caller(null, null, _root),
                CancellationToken.None);
        }

        public async Task<Guid> SeedQueuedAsync(string runnerId, string title, Action<AgentTask>? edit = null)
        {
            var id = Guid.NewGuid();
            var task = new AgentTask
            {
                Id = id,
                RootTaskId = id,
                Title = title,
                Goal = title,
                Kind = AgentTaskKind.Worker,
                Role = AgentTaskRole.Code,
                AgentKind = AgentKind.ClaudeCode,
                ModelLevel = AgentModelLevel.Frontier,
                Workspace = WorkspaceMode.Worktree,
                WorkingDirectory = _root,
                WorktreePath = _root,
                WorktreeBranch = "feat/" + title,
                RunnerId = runnerId,
                Status = AgentTaskStatus.Queued,
                ReplyTo = AgentTaskReplyTo.None,
                CreatedAt = DateTime.UtcNow,
                ConcurrencyToken = Guid.NewGuid(),
                RunnerSelectionSource = RunnerSelectionSource.GlobalDefault,
            };
            edit?.Invoke(task);
            await using var db = NewDb();
            db.AgentTasks.Add(task);
            await db.SaveChangesAsync();
            return id;
        }

        public async Task SeedSessionAsync(SessionStatus status, string runnerId)
        {
            var now = DateTime.UtcNow;
            await using var db = NewDb();
            db.AgentSessions.Add(new AgentSession
            {
                Id = Guid.NewGuid(),
                DefinitionName = "claude",
                AgentKind = AgentKind.ClaudeCode,
                Status = status,
                Cwd = _root,
                Cols = 80,
                Rows = 24,
                CreatedAt = now,
                StartedAt = now,
                LastSeenAt = now,
                RunnerId = runnerId,
                RunnerStoreId = runnerId == RollingRunnerSettings.Server2 ? StoreA : StoreB,
                RunnerCwd = "/work",
            });
            await db.SaveChangesAsync();
        }

        public async Task<IReadOnlyList<string>> EventTextAsync(Guid taskId)
        {
            await using var db = NewDb();
            return await db.AgentTaskEvents.AsNoTracking()
                .Where(e => e.AgentTaskId == taskId)
                .Select(e => e.Detail ?? "")
                .ToListAsync();
        }

        public async Task WaitPrepAsync()
        {
            using var scope = Harness.Provider.CreateScope();
            await scope.ServiceProvider.GetRequiredService<RemoteWorkspacePreparer>().WhenIdleAsync();
        }

        public async Task<AgentTaskCreatedDto> CreateSourceLandingAsync()
        {
            var sha = new string('a', 40);
            var fingerprint = new string('b', 64);
            var sourceId = Guid.NewGuid();
            var operationId = Guid.NewGuid();
            var projectId = Guid.NewGuid();
            var boardId = Guid.NewGuid();
            var columnId = Guid.NewGuid();
            var sourceCard = Guid.NewGuid();
            var companion = Guid.NewGuid();
            var now = DateTime.UtcNow;
            await using (var db = NewDb())
            {
                db.Projects.Add(new Project
                {
                    Id = projectId, Name = "rolling", LocalRepositoryPath = _root, CreatedAt = now,
                });
                db.Boards.Add(new Board { Id = boardId, ProjectId = projectId, Name = "board", CreatedAt = now });
                db.BoardColumns.Add(new BoardColumn
                {
                    Id = columnId, BoardId = boardId, Name = "Ready", StateKey = "ready", CreatedAt = now,
                });
                db.Cards.Add(new Card
                {
                    Id = sourceCard, BoardId = boardId, BoardColumnId = columnId, Identifier = "CARD-1000",
                    Title = "source", CreatedAt = now, UpdatedAt = now,
                });
                db.Cards.Add(new Card
                {
                    Id = companion, BoardId = boardId, BoardColumnId = columnId, Identifier = "CARD-1001",
                    Title = "companion", CreatedAt = now, UpdatedAt = now,
                });
                db.AgentTasks.Add(new AgentTask
                {
                    Id = sourceId,
                    RootTaskId = sourceId,
                    Title = "landed",
                    Goal = "landed",
                    Kind = AgentTaskKind.Worker,
                    Role = AgentTaskRole.Code,
                    AgentKind = AgentKind.ClaudeCode,
                    ModelLevel = AgentModelLevel.Frontier,
                    Workspace = WorkspaceMode.Worktree,
                    WorkingDirectory = _root,
                    RepoPath = Path.GetFullPath(_root),
                    ProjectId = projectId,
                    CardId = sourceCard,
                    Status = AgentTaskStatus.Succeeded,
                    ReplyTo = AgentTaskReplyTo.None,
                    CreatedAt = now,
                    ConcurrencyToken = Guid.NewGuid(),
                });
                db.AgentTaskLandings.Add(new AgentTaskLanding
                {
                    Id = operationId,
                    TaskId = sourceId,
                    SchemaVersion = 1,
                    Active = true,
                    Phase = LandPhase.PublicationConfirmed,
                    Publication = LandPublicationOutcome.Landed,
                    RepositoryPath = Path.GetFullPath(_root),
                    CommonDirectory = Path.GetFullPath(_root),
                    WorktreePath = Path.GetFullPath(_root),
                    GitDirectory = Path.GetFullPath(_root),
                    SourceFullRef = "refs/heads/feat/source",
                    TargetFullRef = "refs/heads/master",
                    DestinationFullRef = "refs/heads/master",
                    OriginalSourceSha = sha,
                    VerifiedSourceSha = sha,
                    TargetBeforeSha = sha,
                    ObservedRemoteTargetSha = sha,
                    RemoteFingerprint = fingerprint,
                    RemoteConfirmedAt = now,
                    VerifiedAt = now,
                    SourcePinned = true,
                    TargetPinned = true,
                    VerificationPassed = true,
                    ConfirmationMethod = "push-endpoint-read-fetch-ancestry",
                    RecoveryRefPrefix = $"refs/antiphon/land/{sourceId:N}/{operationId:N}",
                    CreatedAt = now,
                    UpdatedAt = now,
                });
                await db.SaveChangesAsync();
            }

            using var scope = Harness.Provider.CreateScope();
            _scopes.Add(scope);
            return await scope.ServiceProvider.GetRequiredService<AgentTaskService>().CreateAsync(
                new CreateAgentTaskRequest(
                    "mutate the snapshot",
                    Role: AgentTaskRole.Mutation,
                    AgentKind: AgentKind.ClaudeCode,
                    Workspace: WorkspaceMode.Worktree,
                    RunnerId: RollingRunnerSettings.Server2,
                    Card: companion.ToString("D"),
                    SourceLandingOperationId: operationId),
                new AgentTaskService.Caller(null, null, _root),
                CancellationToken.None);
        }
    }
}
