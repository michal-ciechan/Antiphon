using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Agents.SessionRunner;
using Antiphon.Tests.TestHelpers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0659 V-2. Automatic placement uses the REAL <see cref="PhoneHomeRunnerDirectory"/>
/// readiness boundary: a connected runner that has not finished its catch-up List, or whose lease
/// expired while its store id is still remembered, is not dispatch-eligible, so an omitted-runner
/// create falls back to the desktop. A task already placed on the runner is never migrated when
/// the runner later disconnects.
/// </summary>
[Category("Integration")]
public sealed class DefaultRunnerEligibilityTests
{
    [Test]
    public async Task Before_recovery_falls_back_after_recovery_selects_remote()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        await using var host = await PhoneHomeTestHost.StartAsync(clock);
        await using var peer = await host.ConnectPeerAsync();
        var live = await host.WaitLiveAsync();
        live.DispatchEligible.ShouldBeFalse("precondition: connected but not yet recovered");
        host.Directory.GetLiveStoreId(host.AllowedRunnerId).ShouldBe(host.StoreId, "precondition: the store is known before recovery");
        var kit = Kit(schema.ConnectionString, host);
        await using var db = kit.Context();
        var service = kit.Service(db);

        var before = await service.CreateAsync(Request("c659 before recovery"), kit.Caller, CancellationToken.None);
        var beforeSaved = await kit.ReadAsync(before.Id);
        beforeSaved.Task.RunnerId.ShouldBeNull("a runner that has not completed catch-up is not ready");
        beforeSaved.Created.ShouldContain(
            $"runner source=default requested=unset default={host.AllowedRunnerId} selected=local reason=runner_not_dispatch_eligible",
            Case.Sensitive);
        beforeSaved.Warnings.Count(w => w.Contains("runner_not_dispatch_eligible", StringComparison.Ordinal)).ShouldBe(1);

        using var cts = new CancellationTokenSource();
        (await Pump(host).RunCycleAsync(cts.Token).WaitAsync(TimeSpan.FromSeconds(5))).ShouldBeTrue();
        live.DispatchEligible.ShouldBeTrue();

        var after = await service.CreateAsync(Request("c659 after recovery"), kit.Caller, CancellationToken.None);
        var afterSaved = await kit.ReadAsync(after.Id);
        afterSaved.Task.RunnerId.ShouldBe(host.AllowedRunnerId);
        afterSaved.Created.ShouldContain(
            $"runner source=default requested=unset default={host.AllowedRunnerId} selected={host.AllowedRunnerId} reason=eligible",
            Case.Sensitive);
        afterSaved.Warnings.ShouldBeEmpty();
        (await kit.ReadAsync(before.Id)).Task.RunnerId.ShouldBeNull("the earlier decision is not recalculated");
        cts.Cancel();
    }

    [Test]
    public async Task Expired_lease_falls_back_even_with_a_retained_store_id()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        await using var host = await PhoneHomeTestHost.StartAsync(clock);
        await using var peer = await host.ConnectPeerAsync();
        var live = await host.WaitLiveAsync();
        using var cts = new CancellationTokenSource();
        (await Pump(host).RunCycleAsync(cts.Token).WaitAsync(TimeSpan.FromSeconds(5))).ShouldBeTrue();
        var kit = Kit(schema.ConnectionString, host);
        await using var db = kit.Context();
        var service = kit.Service(db);

        var fresh = await service.CreateAsync(Request("c659 fresh lease"), kit.Caller, CancellationToken.None);
        (await kit.ReadAsync(fresh.Id)).Task.RunnerId.ShouldBe(host.AllowedRunnerId, "precondition: a live, recovered runner is selected");

        clock.Advance(TimeSpan.FromSeconds(91));
        host.Directory.GetLiveStoreId(host.AllowedRunnerId).ShouldBe(host.StoreId, "the store id is still remembered after the lease lapsed");
        host.Directory.KnownRunnerIds.ShouldContain(host.AllowedRunnerId);

        var stale = await service.CreateAsync(Request("c659 expired lease"), kit.Caller, CancellationToken.None);
        var staleSaved = await kit.ReadAsync(stale.Id);
        staleSaved.Task.RunnerId.ShouldBeNull("an expired lease is not readiness, whatever store id is retained");
        staleSaved.Created.ShouldContain("selected=local reason=runner_not_dispatch_eligible", Case.Sensitive);
        live.DispatchEligible.ShouldBeFalse();
        cts.Cancel();
    }

    [Test]
    public async Task Selected_task_does_not_migrate_after_disconnect()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        await using var host = await PhoneHomeTestHost.StartAsync(clock);
        var peer = await host.ConnectPeerAsync();
        await host.WaitLiveAsync();
        using var cts = new CancellationTokenSource();
        (await Pump(host).RunCycleAsync(cts.Token).WaitAsync(TimeSpan.FromSeconds(5))).ShouldBeTrue();
        var kit = Kit(schema.ConnectionString, host);
        await using var db = kit.Context();
        var service = kit.Service(db);

        var placed = await service.CreateAsync(Request("c659 placed remote"), kit.Caller, CancellationToken.None);
        (await kit.ReadAsync(placed.Id)).Task.RunnerId.ShouldBe(host.AllowedRunnerId);

        await peer.DisposeAsync();
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (host.Directory.SnapshotLive() is not null && DateTime.UtcNow < deadline)
            await Task.Delay(20);
        host.Directory.SnapshotLive().ShouldBeNull("the runner is now known down");

        // The placed task keeps its runner and stays queued for the normal dispatcher hold; a new
        // automatic request, decided while the runner is known down, goes to the desktop.
        var saved = await kit.ReadAsync(placed.Id);
        saved.Task.RunnerId.ShouldBe(host.AllowedRunnerId);
        saved.Task.Status.ShouldBe(AgentTaskStatus.Queued);
        saved.Task.AgentSessionId.ShouldBeNull();

        var later = await service.CreateAsync(Request("c659 after disconnect"), kit.Caller, CancellationToken.None);
        var laterSaved = await kit.ReadAsync(later.Id);
        laterSaved.Task.RunnerId.ShouldBeNull();
        laterSaved.Created.ShouldContain("selected=local reason=runner_not_dispatch_eligible", Case.Sensitive);

        host.Local.Calls.ShouldBeEmpty("nothing was launched on the desktop runner client");
        cts.Cancel();
    }

    private static CreateAgentTaskRequest Request(string goal) =>
        new(goal, Role: AgentTaskRole.Code, AgentKind: AgentKind.ClaudeCode);

    private static DefaultRunnerKit Kit(string connectionString, PhoneHomeTestHost host) =>
        DefaultRunnerKit.Create(connectionString, host.AllowedRunnerId,
            allowedRunnerId: host.AllowedRunnerId, realDirectory: host.Directory);

    private static PhoneHomeRecoveryPump Pump(PhoneHomeTestHost host) => new(
        host.Directory,
        Options.Create(new PhoneHomeRunnerSettings
        {
            Enabled = true,
            AllowedRunnerId = host.AllowedRunnerId,
            StandingAgentId = Guid.NewGuid(),
            HostWorkspaceRoot = @"C:\work",
            SharedSecret = host.Secret,
        }),
        host.App.Services.GetRequiredService<IServiceScopeFactory>(),
        NullLogger<PhoneHomeRecoveryPump>.Instance);
}
