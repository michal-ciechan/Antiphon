using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>CARD-0535: lease/cap holds through TraceHeldAsync, HeldAged 300/900s.</summary>
[Category("Integration")]
public sealed class DispatchHoldVisibilityTests
{
    [Test]
    public async Task lease_hold_traces_once_per_holder_and_names_the_running_land()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        using var workspace = new TempWorkspace();
        var t0 = UtcMs();
        var clock = new FakeTimeProvider(new DateTimeOffset(t0, TimeSpan.Zero));
        var lease = new FakeLease { Held = true };
        await using var world = CreateWorld(schema.ConnectionString, clock, lease);
        var (agentId, _) = await ModelAvailabilityDispatcherTests.SeedWarmAgentAsync(
            schema.ConnectionString, workspace.Path);
        var queued = await SeedQueuedAsync(schema, workspace.Path, agentId, t0, repoPath: workspace.Path);
        var holderH = await SeedSucceededAsync(schema, workspace.Path, t0, "holder H land");
        var requestH = await SeedRunningLandAsync(schema, holderH, t0.AddSeconds(-60));

        for (var i = 0; i < 3; i++)
            (await world.Dispatcher.TickAsync(CancellationToken.None)).Dispatched.ShouldBe(0);

        await using (var verify = CreateContext(schema))
        {
            var held = await HeldAsync(verify, queued.Id);
            held.Count.ShouldBe(1);
            held[0].Detail.ShouldContain(DelegationReportFormatter.Short(holderH.Id));
            held[0].Detail.ShouldContain("holder H land");
            held[0].Detail.ShouldContain(DelegationReportFormatter.Short(requestH.Id));
        }

        await CompleteLandAsync(schema, requestH.Id);
        var holderK = await SeedSucceededAsync(schema, workspace.Path, t0, "holder K land");
        var requestK = await SeedRunningLandAsync(schema, holderK, t0.AddSeconds(-30));
        await world.Dispatcher.TickAsync(CancellationToken.None);

        await using (var verify = CreateContext(schema))
        {
            var held = await HeldAsync(verify, queued.Id);
            held.Count.ShouldBe(2);
            held.ShouldContain(e => e.Detail.Contains(DelegationReportFormatter.Short(holderK.Id)));
            held.ShouldContain(e => e.Detail.Contains(DelegationReportFormatter.Short(requestK.Id)));
        }

        lease.Held = false;
        var released = await world.Dispatcher.TickAsync(CancellationToken.None);
        released.Dispatched.ShouldBe(1);
        await using (var verify = CreateContext(schema))
        {
            (await verify.AgentTasks.SingleAsync(t => t.Id == queued.Id)).Status
                .ShouldBe(AgentTaskStatus.Dispatched);
            (await HeldAsync(verify, queued.Id)).Count.ShouldBe(2);
            (await verify.AgentTaskEvents.CountAsync(e =>
                e.AgentTaskId == queued.Id && e.Type == AgentTaskEventType.Dispatched)).ShouldBe(1);
        }
    }

    [Test]
    public async Task lease_fence_is_named_from_the_provider()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        using var workspace = new TempWorkspace();
        var t0 = UtcMs();
        var clock = new FakeTimeProvider(new DateTimeOffset(t0, TimeSpan.Zero));
        var lease = new FakeLease
        {
            Held = true,
            UnavailableReason = "unfinished repository child journal under C:\\tmp\\antiphon\\children; run scripts/recover-repository-children.ps1",
        };
        await using var world = CreateWorld(schema.ConnectionString, clock, lease);
        var (agentId, _) = await ModelAvailabilityDispatcherTests.SeedWarmAgentAsync(
            schema.ConnectionString, workspace.Path);
        var queued = await SeedQueuedAsync(schema, workspace.Path, agentId, t0, repoPath: workspace.Path);

        for (var i = 0; i < 3; i++)
            await world.Dispatcher.TickAsync(CancellationToken.None);

        await using var verify = CreateContext(schema);
        var held = await HeldAsync(verify, queued.Id);
        held.Count.ShouldBe(1);
        held[0].Detail.ShouldContain("recover-repository-children.ps1");
        held[0].Detail.ShouldBe(DispatchHoldDetails.LeaseFenced(lease.UnavailableReason!));
    }

    [Test]
    public async Task unknown_lease_holder_is_stable_text()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        using var workspace = new TempWorkspace();
        var t0 = UtcMs();
        var clock = new FakeTimeProvider(new DateTimeOffset(t0, TimeSpan.Zero));
        var lease = new FakeLease { Held = true };
        await using var world = CreateWorld(schema.ConnectionString, clock, lease);
        var (agentId, _) = await ModelAvailabilityDispatcherTests.SeedWarmAgentAsync(
            schema.ConnectionString, workspace.Path);
        var queued = await SeedQueuedAsync(schema, workspace.Path, agentId, t0, repoPath: workspace.Path);

        for (var i = 0; i < 3; i++)
            await world.Dispatcher.TickAsync(CancellationToken.None);

        await using var verify = CreateContext(schema);
        var held = await HeldAsync(verify, queued.Id);
        held.Count.ShouldBe(1);
        held[0].Detail.ShouldBe(DispatchHoldDetails.LeaseOccupiedUnknown);
    }

    [Test]
    public async Task cap_skip_writes_one_held_event_and_still_counts()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        using var workspace = new TempWorkspace();
        var t0 = UtcMs();
        var clock = new FakeTimeProvider(new DateTimeOffset(t0, TimeSpan.Zero));
        var lease = new FakeLease();
        await using var world = CreateWorld(schema.ConnectionString, clock, lease, maxConcurrent: 1);
        var (agentId, _) = await ModelAvailabilityDispatcherTests.SeedWarmAgentAsync(
            schema.ConnectionString, workspace.Path);
        var active = await SeedDispatchedAsync(schema, workspace.Path, t0);
        var queued = await SeedQueuedAsync(schema, workspace.Path, agentId, t0);

        for (var i = 0; i < 3; i++)
        {
            var tick = await world.Dispatcher.TickAsync(CancellationToken.None);
            tick.SkippedConcurrency.ShouldBe(1);
            tick.Dispatched.ShouldBe(0);
        }

        await using (var verify = CreateContext(schema))
        {
            var held = await HeldAsync(verify, queued.Id);
            held.Count.ShouldBe(1);
            held[0].Detail.ShouldBe(DispatchHoldDetails.ConcurrencyCap(1));
        }

        await SettleAsync(schema, active.Id, t0);
        var released = await world.Dispatcher.TickAsync(CancellationToken.None);
        released.Dispatched.ShouldBe(1);
        await using (var verify = CreateContext(schema))
        {
            (await verify.AgentTasks.SingleAsync(t => t.Id == queued.Id)).Status
                .ShouldBe(AgentTaskStatus.Dispatched);
            (await HeldAsync(verify, queued.Id)).Count.ShouldBe(1);
        }
    }

    [Test]
    public async Task remote_running_task_does_not_consume_the_desktop_cap()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        using var workspace = new TempWorkspace();
        var t0 = UtcMs();
        var clock = new FakeTimeProvider(new DateTimeOffset(t0, TimeSpan.Zero));
        await using var world = CreateWorld(schema.ConnectionString, clock, new FakeLease(), maxConcurrent: 1);
        var (agentId, _) = await ModelAvailabilityDispatcherTests.SeedWarmAgentAsync(
            schema.ConnectionString, workspace.Path);
        var remoteDir = Path.Combine(workspace.Path, "remote-seat");
        Directory.CreateDirectory(remoteDir);
        await SeedDispatchedAsync(schema, remoteDir, t0, runnerId: "server2");
        var queued = await SeedQueuedAsync(schema, workspace.Path, agentId, t0);

        var tick = await world.Dispatcher.TickAsync(CancellationToken.None);
        tick.SkippedConcurrency.ShouldBe(0);
        tick.Dispatched.ShouldBe(1);
        await using var verify = CreateContext(schema);
        (await verify.AgentTasks.SingleAsync(t => t.Id == queued.Id)).Status
            .ShouldBe(AgentTaskStatus.Dispatched);
    }

    [Test]
    public async Task runner_bound_queued_task_is_not_held_on_the_desktop_cap()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        using var workspace = new TempWorkspace();
        var t0 = UtcMs();
        var clock = new FakeTimeProvider(new DateTimeOffset(t0, TimeSpan.Zero));
        await using var world = CreateWorld(schema.ConnectionString, clock, new FakeLease(), maxConcurrent: 1);
        var (agentId, _) = await ModelAvailabilityDispatcherTests.SeedWarmAgentAsync(
            schema.ConnectionString, workspace.Path);
        await SeedDispatchedAsync(schema, workspace.Path, t0);
        var queued = await SeedQueuedAsync(schema, workspace.Path, agentId, t0, runnerId: "server2");

        var tick = await world.Dispatcher.TickAsync(CancellationToken.None);
        tick.SkippedConcurrency.ShouldBe(0);
        await using var verify = CreateContext(schema);
        var held = await HeldAsync(verify, queued.Id);
        held.ShouldNotContain(e => e.Detail == DispatchHoldDetails.ConcurrencyCap(1));
        (await verify.AgentTasks.SingleAsync(t => t.Id == queued.Id)).Status
            .ShouldBe(AgentTaskStatus.Queued);
    }

    [Test]
    public async Task held_age_escalates_at_warning_then_error_once_each()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        using var workspace = new TempWorkspace();
        var t0 = UtcMs();
        var clock = new FakeTimeProvider(new DateTimeOffset(t0, TimeSpan.Zero));
        await using var world = CreateWorld(schema.ConnectionString, clock, new FakeLease(), maxConcurrent: 1);
        var (agentId, _) = await ModelAvailabilityDispatcherTests.SeedWarmAgentAsync(
            schema.ConnectionString, workspace.Path);
        await SeedDispatchedAsync(schema, workspace.Path, t0);
        var queued = await SeedQueuedAsync(schema, workspace.Path, agentId, t0);

        await world.Dispatcher.TickAsync(CancellationToken.None);
        clock.Advance(TimeSpan.FromSeconds(299));
        await world.Dispatcher.TickAsync(CancellationToken.None);
        (await CountAgedAsync(schema, queued.Id)).ShouldBe(0);

        clock.Advance(TimeSpan.FromSeconds(1));
        await world.Dispatcher.TickAsync(CancellationToken.None);
        await using (var verify = CreateContext(schema))
        {
            var aged = await AgedAsync(verify, queued.Id);
            aged.Count.ShouldBe(1);
            aged[0].Detail.ShouldStartWith(DispatchHoldDetails.WarningPrefix);
            aged[0].Detail.ShouldContain("reason=");
            aged[0].Detail.ShouldContain(DispatchHoldDetails.ConcurrencyCap(1));
            aged[0].Detail.ShouldContain("running=1 of 1");
        }

        clock.Advance(TimeSpan.FromSeconds(100));
        await world.Dispatcher.TickAsync(CancellationToken.None);
        (await CountAgedAsync(schema, queued.Id)).ShouldBe(1);

        clock.Advance(TimeSpan.FromSeconds(500));
        await world.Dispatcher.TickAsync(CancellationToken.None);
        await using (var verify = CreateContext(schema))
        {
            var aged = await AgedAsync(verify, queued.Id);
            aged.Count.ShouldBe(2);
            aged.ShouldContain(e => e.Detail.StartsWith(DispatchHoldDetails.ErrorPrefix, StringComparison.Ordinal));
        }

        clock.Advance(TimeSpan.FromSeconds(100));
        await world.Dispatcher.TickAsync(CancellationToken.None);
        (await CountAgedAsync(schema, queued.Id)).ShouldBe(2);
    }

    [Test]
    public async Task escalation_is_idempotent_across_a_restart()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        using var workspace = new TempWorkspace();
        var t0 = UtcMs();
        var clock = new FakeTimeProvider(new DateTimeOffset(t0, TimeSpan.Zero));
        var lease = new FakeLease();
        await using (var world = CreateWorld(schema.ConnectionString, clock, lease, maxConcurrent: 1))
        {
            var (agentId, _) = await ModelAvailabilityDispatcherTests.SeedWarmAgentAsync(
                schema.ConnectionString, workspace.Path);
            await SeedDispatchedAsync(schema, workspace.Path, t0);
            await SeedQueuedAsync(schema, workspace.Path, agentId, t0);
            await world.Dispatcher.TickAsync(CancellationToken.None);
            clock.Advance(TimeSpan.FromSeconds(300));
            await world.Dispatcher.TickAsync(CancellationToken.None);
        }

        clock.Advance(TimeSpan.FromSeconds(650));
        await using var restarted = CreateWorld(schema.ConnectionString, clock, lease, maxConcurrent: 1);
        await restarted.Dispatcher.TickAsync(CancellationToken.None);
        var queuedId = await QueuedIdAsync(schema);
        (await CountAgedAsync(schema, queuedId)).ShouldBe(2);
        await restarted.Dispatcher.TickAsync(CancellationToken.None);
        (await CountAgedAsync(schema, queuedId)).ShouldBe(2);
    }

    [Test]
    public async Task both_thresholds_crossed_in_one_tick_write_both_rows()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        using var workspace = new TempWorkspace();
        var t0 = UtcMs();
        var clock = new FakeTimeProvider(new DateTimeOffset(t0, TimeSpan.Zero));
        await using var world = CreateWorld(schema.ConnectionString, clock, new FakeLease(), maxConcurrent: 1);
        var (agentId, _) = await ModelAvailabilityDispatcherTests.SeedWarmAgentAsync(
            schema.ConnectionString, workspace.Path);
        await SeedDispatchedAsync(schema, workspace.Path, t0);
        var queued = await SeedQueuedAsync(schema, workspace.Path, agentId, t0);

        await world.Dispatcher.TickAsync(CancellationToken.None);
        clock.Advance(TimeSpan.FromSeconds(1000));
        await world.Dispatcher.TickAsync(CancellationToken.None);

        await using var verify = CreateContext(schema);
        var aged = await AgedAsync(verify, queued.Id);
        aged.Count.ShouldBe(2);
        aged.ShouldContain(e => e.Detail.StartsWith(DispatchHoldDetails.WarningPrefix, StringComparison.Ordinal));
        aged.ShouldContain(e => e.Detail.StartsWith(DispatchHoldDetails.ErrorPrefix, StringComparison.Ordinal));
        aged[0].At.ShouldBe(aged[1].At);
    }

    [Test]
    public async Task a_requeue_starts_a_fresh_escalation_stint()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        using var workspace = new TempWorkspace();
        var t0 = UtcMs();
        var clock = new FakeTimeProvider(new DateTimeOffset(t0, TimeSpan.Zero));
        await using var world = CreateWorld(schema.ConnectionString, clock, new FakeLease(), maxConcurrent: 1);
        var (agentId, _) = await ModelAvailabilityDispatcherTests.SeedWarmAgentAsync(
            schema.ConnectionString, workspace.Path);
        await SeedDispatchedAsync(schema, workspace.Path, t0);
        var queued = await SeedQueuedAsync(schema, workspace.Path, agentId, t0);
        await using (var db = CreateContext(schema))
        {
            db.AgentTaskEvents.AddRange(
                Event(queued.Id, AgentTaskEventType.Held, "old hold", t0.AddSeconds(-3000)),
                Event(queued.Id, AgentTaskEventType.HeldAged, "Warning: old", t0.AddSeconds(-2000)),
                Event(queued.Id, AgentTaskEventType.HeldAged, "Error: old", t0.AddSeconds(-2000)),
                Event(queued.Id, AgentTaskEventType.Dispatched, "prior dispatch", t0.AddSeconds(-1000)));
            await db.SaveChangesAsync();
        }

        await world.Dispatcher.TickAsync(CancellationToken.None);
        DateTime newHeldAt;
        await using (var verify = CreateContext(schema))
        {
            var held = (await HeldAsync(verify, queued.Id))
                .Where(e => e.At >= t0).ToList();
            held.Count.ShouldBe(1);
            newHeldAt = held[0].At;
        }

        clock.Advance(TimeSpan.FromSeconds(300));
        await world.Dispatcher.TickAsync(CancellationToken.None);
        await using (var verify = CreateContext(schema))
        {
            var aged = await AgedAsync(verify, queued.Id);
            aged.Count.ShouldBe(3);
            var fresh = aged.Single(e => e.At >= t0);
            fresh.Detail.ShouldStartWith(DispatchHoldDetails.WarningPrefix);
            fresh.Detail.ShouldContain($"since {newHeldAt:O}");
            fresh.Detail.ShouldNotContain(t0.AddSeconds(-3000).ToString("O"));
        }
    }

    [Test]
    public async Task a_dated_routing_pin_never_escalates()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        using var workspace = new TempWorkspace();
        var t0 = UtcMs();
        var clock = new FakeTimeProvider(new DateTimeOffset(t0, TimeSpan.Zero));
        await using var world = CreateWorld(schema.ConnectionString, clock, new FakeLease());
        var (agentId, _) = await ModelAvailabilityDispatcherTests.SeedWarmAgentAsync(
            schema.ConnectionString, workspace.Path);
        var queued = await SeedQueuedAsync(schema, workspace.Path, agentId, t0, role: AgentTaskRole.Plan);
        await using (var db = CreateContext(schema))
        {
            db.RoutingPins.Add(new RoutingPin
            {
                Id = Guid.NewGuid(),
                Role = AgentTaskRole.Plan,
                Provenance = RoutingPinProvenance.Human,
                Strength = RoutingPinStrength.Required,
                NotBefore = t0.AddDays(1),
                Reason = "operator: wait until tomorrow",
                CreatedAt = t0,
                UpdatedAt = t0,
            });
            await db.SaveChangesAsync();
        }

        await world.Dispatcher.TickAsync(CancellationToken.None);
        clock.Advance(TimeSpan.FromSeconds(301));
        await world.Dispatcher.TickAsync(CancellationToken.None);
        clock.Advance(TimeSpan.FromSeconds(600));
        await world.Dispatcher.TickAsync(CancellationToken.None);

        await using var verify = CreateContext(schema);
        var held = await HeldAsync(verify, queued.Id);
        held.Count.ShouldBe(1);
        held[0].Detail.ShouldStartWith(DispatchHoldDetails.RoutingPinPrefix);
        (await AgedAsync(verify, queued.Id)).Count.ShouldBe(0);
    }

    [Test]
    public async Task C672_lease_hold_registers_a_waiter_and_dispatch_clears_it()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        using var workspace = new TempWorkspace();
        var t0 = UtcMs();
        var clock = new FakeTimeProvider(new DateTimeOffset(t0, TimeSpan.Zero));
        var lease = new FakeLease { Held = true };
        await using var world = CreateWorld(schema.ConnectionString, clock, lease);
        var (agentId, _) = await ModelAvailabilityDispatcherTests.SeedWarmAgentAsync(
            schema.ConnectionString, workspace.Path);
        var kept = await SeedQueuedAsync(schema, workspace.Path, agentId, t0, repoPath: workspace.Path);
        var canceled = await SeedQueuedAsync(schema, workspace.Path, agentId, t0.AddMilliseconds(1), repoPath: workspace.Path);
        var common = await world.WaiterKeyAsync(workspace.Path);

        (await world.Dispatcher.TickAsync(CancellationToken.None)).HeldOnLease.ShouldBe(2);
        var first = world.Waiters.Snapshot(common);
        first.Count.ShouldBe(2);
        first.ShouldAllBe(w => w.Purpose == RepositoryLeasePurposes.Dispatch);
        first.ShouldAllBe(w => w.Since == clock.GetUtcNow());
        first.Select(w => w.TaskId).ShouldBe([kept.Id, canceled.Id], ignoreOrder: true);

        await using (var db = CreateContext(schema))
        {
            var row = await db.AgentTasks.SingleAsync(t => t.Id == canceled.Id);
            row.Status = AgentTaskStatus.Canceled;
            row.CompletedAt = t0;
            await db.SaveChangesAsync();
        }

        clock.Advance(TimeSpan.FromSeconds(5));
        await world.Dispatcher.TickAsync(CancellationToken.None);
        var second = world.Waiters.Snapshot(common).ShouldHaveSingleItem();
        second.TaskId.ShouldBe(kept.Id);
        second.Since.ShouldBe(new DateTimeOffset(t0, TimeSpan.Zero), "a repeat refusal keeps the first instant");

        lease.Held = false;
        clock.Advance(TimeSpan.FromSeconds(5));
        var released = await world.Dispatcher.TickAsync(CancellationToken.None);
        released.Dispatched.ShouldBe(1);
        released.HeldOnLease.ShouldBe(0);
        world.Waiters.Snapshot(common).ShouldBeEmpty();
        world.Waiters.IsEmpty.ShouldBeTrue();
    }

    [Test]
    public async Task C672_held_aged_carries_the_per_class_wait_ledger()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        using var workspace = new TempWorkspace();
        var t0 = UtcMs();
        var clock = new FakeTimeProvider(new DateTimeOffset(t0.AddSeconds(350), TimeSpan.Zero));
        var lease = new FakeLease { Held = true };
        await using var world = CreateWorld(schema.ConnectionString, clock, lease);
        var (agentId, _) = await ModelAvailabilityDispatcherTests.SeedWarmAgentAsync(
            schema.ConnectionString, workspace.Path);
        var queued = await SeedQueuedAsync(schema, workspace.Path, agentId, t0.AddSeconds(-10), repoPath: workspace.Path);
        await using (var db = CreateContext(schema))
        {
            db.AgentTaskEvents.AddRange(
                Event(queued.Id, AgentTaskEventType.Held, DispatchHoldDetails.LeaseOccupiedUnknown, t0),
                Event(queued.Id, AgentTaskEventType.Held, DispatchHoldDetails.RemoteMirrorRequested("server2", t0), t0.AddSeconds(120)),
                Event(queued.Id, AgentTaskEventType.Held, DispatchHoldDetails.LeaseOccupiedUnknown, t0.AddSeconds(150)));
            await db.SaveChangesAsync();
        }

        await world.Dispatcher.TickAsync(CancellationToken.None);

        await using var verify = CreateContext(schema);
        var aged = (await AgedAsync(verify, queued.Id)).ShouldHaveSingleItem();
        aged.Detail.ShouldStartWith(DispatchHoldDetails.WarningPrefix);
        aged.Detail.ShouldContain("leaseWait=320s");
        aged.Detail.ShouldContain("prepWait=30s");
        aged.Detail.ShouldContain("runnerWait=0s");
        aged.Detail.ShouldContain("capWait=0s");
        aged.Detail.ShouldContain("otherWait=0s");
        aged.Detail.ShouldContain("class=lease");
        aged.Detail.ShouldContain("running=");
        aged.Detail.ShouldContain("occupants=");
        aged.Detail.IndexOf("occupants=", StringComparison.Ordinal)
            .ShouldBeLessThan(aged.Detail.IndexOf("leaseWait=", StringComparison.Ordinal));
        DispatchHoldDetails.Reason(aged.Detail).ShouldBe(DispatchHoldDetails.LeaseOccupiedUnknown);
    }

    [Test]
    [Arguments(0, 900, false)]
    [Arguments(300, 300, false)]
    [Arguments(900, 300, false)]
    [Arguments(300, 900, true)]
    public void threshold_configuration(int warning, int error, bool valid)
    {
        var settings = new DelegationSettings
        {
            DispatchHeldWarningSeconds = warning,
            DispatchHeldErrorSeconds = error,
        };
        new DelegationSettingsValidator().Validate(null, settings).Succeeded.ShouldBe(valid);
        new DelegationSettings().DispatchHeldWarningSeconds.ShouldBe(300);
        new DelegationSettings().DispatchHeldErrorSeconds.ShouldBe(900);
    }

    private static World CreateWorld(
        string connectionString,
        TimeProvider clock,
        FakeLease lease,
        int maxConcurrent = 6)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<AppDbContext>(o => o.UseNpgsql(connectionString));
        services.AddSingleton<IEventBus, MockEventBus>();
        services.AddSingleton(clock);
        services.AddSingleton(Options.Create(new SupervisionSettings
        {
            DeliveryVerification = new(),
            CapacityRecovery = new CapacityRecoverySettings
            {
                Enabled = true,
                AdmissionIntervalSeconds = 1,
                JitterSeconds = 0,
                MaxEpisodeAttempts = 3,
            },
        }));
        services.AddSingleton(Options.Create(new ChannelBridgeSettings()));
        services.AddSingleton(Options.Create(new DelegationSettings
        {
            PoolReservedForCallerMinutes = 2,
            PoolIdleRetireMinutes = 5,
            PoolMaxIdlePerDirectory = 3,
            MaxConcurrentTasks = maxConcurrent,
        }));
        services.AddOptions<AgentRegistrySettings>().Configure(s =>
        {
            s.DefaultDefinition = "claude";
            s.Definitions["claude"] = new AgentDefinition { Kind = "ClaudeCode", Exe = "claude" };
        });
        services.AddSingleton<AgentRegistry>();
        services.AddSingleton<AgentSessionLaunchQueue>();
        services.AddSingleton<AgentSessionRuntime>();
        services.AddSingleton<SessionMessageQueueService>();
        services.AddSingleton<CompletionNoteFlushQueue>();
        services.AddScoped<AgentTaskLandNotificationService>();
        services.AddSingleton<IDelegateSessionStopper, RecordingSessionStopper>();
        services.AddSingleton<DelegationWorkspaceResolver>();
        services.AddSingleton<IRepositoryMutationLease>(lease);
        services.AddDelegationWorktreeGraph(new GitSettings
        {
            WorktreeBasePath = Path.Combine(Path.GetTempPath(), "antiphon-c535-task-wt"),
        });
        services.AddSingleton<CapacityRecoveryService>();
        services.AddScoped<AgentTaskService>();
        services.AddScoped<ModelAvailability>();
        services.AddScoped<RoutingPinService>();
        services.AddScoped<ComplexityRoutingService>();
        services.AddScoped<AgentTaskDispatcher>();
        var provider = services.BuildServiceProvider();
        var scope = provider.CreateScope();
        return new World(provider, scope, scope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>());
    }

    private static async Task<AgentTask> SeedQueuedAsync(
        IsolatedTestSchema schema, string directory, Guid agentId, DateTime at,
        string? repoPath = null, AgentTaskRole role = AgentTaskRole.Docs, string? runnerId = null)
    {
        var id = Guid.NewGuid();
        var task = new AgentTask
        {
            Id = id,
            RootTaskId = id,
            Title = "queued work",
            Goal = "queued work",
            Role = role,
            AgentKind = AgentKind.ClaudeCode,
            ModelLevel = AgentModelLevel.Medium,
            Workspace = WorkspaceMode.Shared,
            WorkingDirectory = directory,
            RepoPath = repoPath,
            Status = AgentTaskStatus.Queued,
            AgentId = agentId,
            Ephemeral = false,
            CreatedAt = at,
            RunnerId = runnerId,
        };
        await using var db = CreateContext(schema);
        db.AgentTasks.Add(task);
        await db.SaveChangesAsync();
        return task;
    }

    private static async Task<AgentTask> SeedDispatchedAsync(
        IsolatedTestSchema schema, string directory, DateTime at, string? runnerId = null)
    {
        var id = Guid.NewGuid();
        var task = new AgentTask
        {
            Id = id,
            RootTaskId = id,
            Title = "already running",
            Goal = "already running",
            Role = AgentTaskRole.Docs,
            AgentKind = AgentKind.ClaudeCode,
            ModelLevel = AgentModelLevel.Medium,
            Workspace = WorkspaceMode.Shared,
            WorkingDirectory = directory,
            Status = AgentTaskStatus.Dispatched,
            DispatchedAt = at.AddMinutes(-1),
            CapacityWaitRetained = false,
            CreatedAt = at.AddMinutes(-2),
            RunnerId = runnerId,
        };
        await using var db = CreateContext(schema);
        db.AgentTasks.Add(task);
        await db.SaveChangesAsync();
        return task;
    }

    private static async Task<AgentTask> SeedSucceededAsync(
        IsolatedTestSchema schema, string directory, DateTime at, string title)
    {
        var id = Guid.NewGuid();
        var task = new AgentTask
        {
            Id = id,
            RootTaskId = id,
            Title = title,
            Goal = title,
            Role = AgentTaskRole.Code,
            AgentKind = AgentKind.ClaudeCode,
            ModelLevel = AgentModelLevel.Medium,
            Workspace = WorkspaceMode.Worktree,
            WorkingDirectory = directory,
            RepoPath = directory,
            WorktreePath = Path.Combine(directory, "worktrees", id.ToString("N")[..8]),
            Status = AgentTaskStatus.Succeeded,
            CompletedAt = at.AddMinutes(-2),
            CreatedAt = at.AddMinutes(-10),
        };
        await using var db = CreateContext(schema);
        db.AgentTasks.Add(task);
        await db.SaveChangesAsync();
        return task;
    }

    private static async Task<AgentTaskLandRequest> SeedRunningLandAsync(
        IsolatedTestSchema schema, AgentTask holder, DateTime startedAt)
    {
        var request = new AgentTaskLandRequest
        {
            Id = Guid.NewGuid(),
            TaskId = holder.Id,
            RequestedAt = startedAt,
            LastEvaluatedAt = startedAt,
            LastProgressAt = startedAt,
            StartedAt = startedAt,
            State = LandRequestState.Running,
            IsPending = true,
        };
        await using var db = CreateContext(schema);
        var task = await db.AgentTasks.SingleAsync(t => t.Id == holder.Id);
        task.CurrentLandRequestId = request.Id;
        task.LandRequestedAt = startedAt;
        db.AgentTaskLandRequests.Add(request);
        await db.SaveChangesAsync();
        return request;
    }

    private static async Task CompleteLandAsync(IsolatedTestSchema schema, Guid requestId)
    {
        await using var db = CreateContext(schema);
        var request = await db.AgentTaskLandRequests.SingleAsync(r => r.Id == requestId);
        request.IsPending = false;
        request.State = LandRequestState.Completed;
        await db.SaveChangesAsync();
    }

    private static async Task SettleAsync(IsolatedTestSchema schema, Guid taskId, DateTime at)
    {
        await using var db = CreateContext(schema);
        var task = await db.AgentTasks.SingleAsync(t => t.Id == taskId);
        task.Status = AgentTaskStatus.Succeeded;
        task.CompletedAt = at;
        await db.SaveChangesAsync();
    }

    private static async Task<List<AgentTaskEvent>> HeldAsync(AppDbContext db, Guid taskId) =>
        await db.AgentTaskEvents.AsNoTracking()
            .Where(e => e.AgentTaskId == taskId && e.Type == AgentTaskEventType.Held)
            .OrderBy(e => e.At).ThenBy(e => e.Id)
            .ToListAsync();

    private static async Task<List<AgentTaskEvent>> AgedAsync(AppDbContext db, Guid taskId) =>
        await db.AgentTaskEvents.AsNoTracking()
            .Where(e => e.AgentTaskId == taskId && e.Type == AgentTaskEventType.HeldAged)
            .OrderBy(e => e.At).ThenBy(e => e.Id)
            .ToListAsync();

    private static async Task<int> CountAgedAsync(IsolatedTestSchema schema, Guid taskId)
    {
        await using var db = CreateContext(schema);
        return await db.AgentTaskEvents.CountAsync(e =>
            e.AgentTaskId == taskId && e.Type == AgentTaskEventType.HeldAged);
    }

    private static async Task<Guid> QueuedIdAsync(IsolatedTestSchema schema)
    {
        await using var db = CreateContext(schema);
        return await db.AgentTasks.Where(t => t.Status == AgentTaskStatus.Queued)
            .Select(t => t.Id).SingleAsync();
    }

    private static AgentTaskEvent Event(Guid taskId, AgentTaskEventType type, string detail, DateTime at) =>
        new()
        {
            Id = Guid.NewGuid(),
            AgentTaskId = taskId,
            Type = type,
            Detail = detail,
            At = at,
        };

    private static DateTime UtcMs()
    {
        var now = DateTime.UtcNow;
        return now.AddTicks(-(now.Ticks % TimeSpan.TicksPerMillisecond));
    }

    private static AppDbContext CreateContext(IsolatedTestSchema schema) =>
        new(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));

    private sealed class FakeLease : IRepositoryMutationLease
    {
        public bool Held { get; set; }
        public string? UnavailableReason { get; set; }

        public Task<RepositoryLease?> TryAcquireAsync(string repository, CancellationToken ct) =>
            Task.FromResult(Held ? null : (RepositoryLease?)new TrivialLease());

        public bool Owns(RepositoryLease lease, string commonDirectory) => false;

        public Task<string?> DescribeUnavailableAsync(string repository, CancellationToken ct) =>
            Task.FromResult(UnavailableReason);

        private sealed class TrivialLease : RepositoryLease
        {
            public override string CommonDirectory => "fake";
            public override ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    private sealed class World(ServiceProvider provider, IServiceScope scope, AgentTaskDispatcher dispatcher)
        : IAsyncDisposable
    {
        public AgentTaskDispatcher Dispatcher { get; } = dispatcher;

        /// <summary>CARD-0672: the turnstile the dispatcher registers lease refusals in.</summary>
        public RepositoryLeaseWaiters Waiters => provider.GetRequiredService<RepositoryLeaseWaiters>();

        /// <summary>The key the dispatcher uses: the repository's common directory, else the full path.</summary>
        public async Task<string> WaiterKeyAsync(string repository)
        {
            try
            {
                return await provider.GetRequiredService<ILandingGit>().CommonDirectoryAsync(repository, CancellationToken.None);
            }
            catch (Exception)
            {
                return Path.GetFullPath(repository);
            }
        }

        public async ValueTask DisposeAsync()
        {
            scope.Dispose();
            await provider.DisposeAsync();
        }
    }

    private sealed class TempWorkspace : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory("antiphon-c535-hold").FullName;

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); }
            catch (IOException) { }
        }
    }
}
