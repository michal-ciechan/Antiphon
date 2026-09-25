using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Infrastructure.Agents.SessionRunner;
using Antiphon.SessionRunner.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Antiphon.Server.Domain.Enums;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[NotInParallel("MessageQueue")]
public class PhoneHomePendingInventoryTests
{
    [Test]
    public async Task C696Red_Pending_inventory_reads_are_bounded()
    {
        foreach (var empty in new[] { false, true })
        {
            await using var h = await PhoneHomeOutageHarness.CreateAsync();
            if (empty)
            {
                await using var db = h.Db();
                await db.AgentSessions.Where(s => s.Id == h.SessionId).ExecuteUpdateAsync(u => u.SetProperty(s => s.Status, SessionStatus.Stopped));
            }
            for (var i = 0; i < 100; i++) h.Runtime.ListLiveOrUnknownSessions().Contains(h.SessionId).ShouldBe(!empty);
            await Task.WhenAll(Enumerable.Range(0, 32).Select(_ => Task.Run(() =>
                h.Runtime.ListLiveOrUnknownSessions().Contains(h.SessionId).ShouldBe(!empty))));
            h.Probe.Reads.ShouldBe(1, $"132 pending reads share one projection, including empty={empty}");
        }
    }
    [Test]
    public async Task Expiry_refreshes_once_and_hits_do_not_slide_the_deadline()
    {
        await using var h = await PhoneHomeOutageHarness.CreateAsync();
        h.Runtime.ListLiveOrUnknownSessions().ShouldContain(h.SessionId);
        h.Clock.Advance(TimeSpan.FromMilliseconds(4999));
        for (var i = 0; i < 10; i++) h.Runtime.ListLiveOrUnknownSessions().ShouldContain(h.SessionId);
        h.Probe.Reads.ShouldBe(1);
        h.Clock.Advance(TimeSpan.FromMilliseconds(1));
        await Task.WhenAll(Enumerable.Range(0, 32).Select(_ => Task.Run(() => h.Runtime.ListLiveOrUnknownSessions().ShouldContain(h.SessionId))));
        h.Probe.Reads.ShouldBe(2);
        for (var i = 0; i < 3; i++)
        {
            h.Clock.Advance(TimeSpan.FromSeconds(5));
            h.Runtime.ListLiveOrUnknownSessions().ShouldContain(h.SessionId);
        }
        h.Clock.Advance(TimeSpan.FromDays(3));
        h.Runtime.ListLiveOrUnknownSessions().ShouldContain(h.SessionId, "TTL expires knowledge freshness, never unknown liveness");
        h.Probe.Reads.ShouldBe(6);
    }

    [Test]
    public async Task Warm_negative_does_not_hide_a_newly_committed_target()
    {
        await using var h = await PhoneHomeOutageHarness.CreateAsync(warmEmpty: true);
        h.Runtime.ListLiveOrUnknownSessions().ShouldNotContain(h.SessionId, "the empty TTL snapshot predates this row's insert");
        (await h.MentionAsync("fake", "target inserted within TTL")).ShouldBeTrue();
        (await h.PreviewAsync()).Target.AgentLive.ShouldBe(true);
        var schedule = await PhoneHomeStrandedQueueTests.SeedSkipWhenDownPromptAsync(h.Schema.ConnectionString, h.Bridge.AgentId, "warm fire");
        await PhoneHomeStrandedQueueTests.FireNowAsync(h.Bridge, schedule);
        await h.Queue.EnqueueAsync(h.SessionId, "warm WhenIdle", MessageSendMode.WhenIdle, CancellationToken.None);
        (await Should.ThrowAsync<ServiceUnavailableException>(() => h.Queue.EnqueueAsync(h.SessionId, "warm Now", MessageSendMode.Now, CancellationToken.None)))
            .Code.ShouldBe(PhoneHomeProblemTypes.Unavailable);
        (await h.RowsAsync()).Count.ShouldBe(3);
        await h.Queue.FlushStrandedQueuesAsync(CancellationToken.None);
        (await h.RowsAsync()).ShouldAllBe(r => r.Status == QueuedMessageStatus.Pending && r.DeliveryAttempts == 0);
        h.Probe.Reads.ShouldBe(1, "row-aware admission does not reload a cache miss");

        // Start a real manual-turn tracker through the adapter, then remove that adapter before
        // the wait starts. The persisted row is known, while list membership is still missing.
        h.Runtime.Register(h.SessionId, h.Bridge.Adapter);
        h.Bridge.Provider.GetRequiredService<IOptions<AgentSessionSettings>>().Value.FirstDeltaTimeoutMs = 100;
        await using (var db = h.Db())
            await db.RunAttempts.Where(a => a.Id == h.AttemptId).ExecuteUpdateAsync(u => u.SetProperty(a => a.Phase, RunPhase.Succeeded));
        h.Bridge.Adapter.BeforeInput = (input, ct) =>
        {
            if (input == "\r") h.Runtime.TryRemove(h.SessionId, out _);
            return Task.CompletedTask;
        };
        await h.Runtime.SendInputAsync(h.SessionId, "manual input", CancellationToken.None);
        await h.Runtime.SendInputAsync(h.SessionId, "\r", CancellationToken.None);
        await PhoneHomeOutageHarness.UntilAsync(() =>
        {
            using var db = h.Db();
            return db.RunAttempts.Any(a => a.AgentSessionId == h.SessionId && a.Id != h.AttemptId && a.Phase != RunPhase.StreamingTurn);
        });
        await using (var db = h.Db())
            (await db.RunAttempts.SingleAsync(a => a.AgentSessionId == h.SessionId && a.Id != h.AttemptId)).Phase.ShouldBe(RunPhase.Succeeded);
    }

    [Test]
    public async Task Terminal_rows_and_unaccepted_bindings_do_not_gain_liveness()
    {
        foreach (var terminal in new[] { true, false })
        {
            await using var h = await PhoneHomeOutageHarness.CreateAsync();
            h.Runtime.ListLiveOrUnknownSessions().ShouldContain(h.SessionId);
            await using var db = h.Db();
            if (terminal)
                await db.AgentSessions.Where(s => s.Id == h.SessionId).ExecuteUpdateAsync(u => u.SetProperty(s => s.Status, SessionStatus.Failed));
            else
                await db.AgentSessions.Where(s => s.Id == h.SessionId).ExecuteUpdateAsync(u => u.SetProperty(s => s.RunnerId, "unaccepted-runner"));
            (await h.PreviewAsync()).Target.AgentLive.ShouldBe(false);
            (await h.MentionAsync("fake", "must not resurrect")).ShouldBeFalse();
            await Should.ThrowAsync<ConflictException>(() => h.Queue.EnqueueAsync(h.SessionId, "terminal", MessageSendMode.Now, CancellationToken.None));
            await using var scope = h.Bridge.Provider.CreateAsyncScope();
            await Should.ThrowAsync<NotFoundException>(() => scope.ServiceProvider.GetRequiredService<AgentChannelService>()
                .SendToSessionAsync(null, h.SessionId, "terminal direct", CancellationToken.None));
            h.Clock.Advance(TimeSpan.FromSeconds(5));
            h.Runtime.ListLiveOrUnknownSessions().ShouldNotContain(h.SessionId);
            h.Probe.Reads.ShouldBe(2);
        }
    }

    [Test]
    public async Task First_List_overtakes_blocked_pending_query()
    {
        foreach (var includesNew in new[] { false, true })
        {
            await using var h = await PhoneHomeOutageHarness.CreateAsync();
            using var arrived = new ManualResetEventSlim();
            using var release = new ManualResetEventSlim();
            h.Probe.OnRead = () => { arrived.Set(); release.Wait(TimeSpan.FromSeconds(10)).ShouldBeTrue(); };
            var read = Task.Run(h.Runtime.ListLiveOrUnknownSessions);
            try
            {
                await PhoneHomeOutageHarness.UntilAsync(() => arrived.IsSet);
                await using var peer = await h.Host.ConnectPeerAsync();
                var live = await h.Host.WaitLiveAsync();
                var newer = Guid.NewGuid();
                // Same authoritative mutation the recovery pump makes after its real List.
                var inventory = await new PhoneHomeRunnerClient(live).ListAsync(CancellationToken.None);
                inventory.ShouldBeEmpty();
                live.ReplaceKnownLiveSessions(includesNew ? [(newer, (DateTime?)DateTime.UtcNow)] : [], live.BeginInventoryRead());
                h.Host.Directory.MarkRecovered(live);
                release.Set();
                var ids = await read;
                ids.ShouldNotContain(h.SessionId);
                ids.Contains(newer).ShouldBe(includesNew, "return the inventory that won the race, even if the live half was read earlier");
                h.Clock.Advance(TimeSpan.FromSeconds(5));
                h.Runtime.ListLiveOrUnknownSessions().ShouldNotContain(h.SessionId);
                h.Probe.Reads.ShouldBe(1);
            }
            finally { release.Set(); await read; }
        }
    }

    [Test]
    public async Task Recovered_inventory_and_tombstones_bypass_pending_cache()
    {
        await using var h = await PhoneHomeOutageHarness.CreateAsync();
        h.Runtime.ListLiveOrUnknownSessions().ShouldContain(h.SessionId);
        await using var a = await h.RecoverAsync();
        var live = h.Host.Directory.SnapshotLive()!;
        var generation = a.Sessions.Single().AcceptedStartedAt;
        var sent = live.BeginInventoryRead();
        live.NoteSessionGone(h.SessionId, generation);
        live.NoteSessionLive(h.SessionId, generation, sent).ShouldBeFalse();
        h.Runtime.ListLiveOrUnknownSessions().ShouldNotContain(h.SessionId);
        var launched = Guid.NewGuid();
        live.NoteSessionLive(launched, DateTime.UtcNow, live.BeginInventoryRead()).ShouldBeTrue();
        h.Runtime.ListLiveOrUnknownSessions().ShouldContain(launched);
        await h.DisconnectAsync(a);
        h.Runtime.ListLiveOrUnknownSessions().ShouldContain(launched);
        h.Runtime.ListLiveOrUnknownSessions().ShouldNotContain(h.SessionId);
        h.Clock.Advance(TimeSpan.FromHours(1));
        live.NoteSessionLive(h.SessionId, generation, sent).ShouldBeFalse();
        await using var b = await h.RecoverAsync(includeTarget: false);
        h.Runtime.ListLiveOrUnknownSessions().ShouldBeEmpty();
        h.Probe.Reads.ShouldBe(1, "a recovered process never returns to the bootstrap SELECT");
    }

    [Test]
    public async Task Failed_refresh_is_throttled_without_inventing_absence()
    {
        foreach (var warm in new[] { false, true })
        {
            await using var h = await PhoneHomeOutageHarness.CreateAsync();
            if (warm)
            {
                h.Runtime.ListLiveOrUnknownSessions().ShouldContain(h.SessionId);
                h.Clock.Advance(TimeSpan.FromSeconds(5));
            }
            var before = h.Probe.Reads;
            h.Probe.OnRead = () => throw new InvalidOperationException("controlled projection outage");
            await Task.WhenAll(Enumerable.Range(0, 32).Select(_ => Task.Run(() =>
            {
                if (warm) h.Runtime.ListLiveOrUnknownSessions().ShouldContain(h.SessionId);
                else Should.Throw<InvalidOperationException>(() => h.Runtime.ListLiveOrUnknownSessions());
            })));
            h.Probe.Reads.ShouldBe(before + 1);
            h.Host.Logs.Entries.Count(e => e.Message.StartsWith("Pending session inventory refresh failed", StringComparison.Ordinal)).ShouldBe(1);
            h.Clock.Advance(TimeSpan.FromMilliseconds(4999));
            if (warm) h.Runtime.ListLiveOrUnknownSessions().ShouldContain(h.SessionId);
            else Should.Throw<InvalidOperationException>(() => h.Runtime.ListLiveOrUnknownSessions());
            h.Probe.Reads.ShouldBe(before + 1);
            h.Probe.OnRead = null;
            h.Clock.Advance(TimeSpan.FromMilliseconds(1));
            h.Runtime.ListLiveOrUnknownSessions().ShouldContain(h.SessionId);
            h.Probe.Reads.ShouldBe(before + 2);
        }
    }

    [Test]
    public async Task Disabled_runner_and_local_only_lists_do_not_query_pending_inventory()
    {
        await using var h = await PhoneHomeOutageHarness.CreateAsync(remote: false);
        h.Runtime.ListLiveSessions().ShouldContain(h.SessionId);
        h.Probe.Reads.ShouldBe(0);
        var disabled = new PhoneHomeRunnerDirectory(h.Host.Local, Options.Create(new PhoneHomeRunnerSettings { Enabled = false }),
            h.Host.App.Services.GetRequiredService<IServiceScopeFactory>(), h.Clock);
        disabled.UnknownRemoteSessionIds().ShouldBeEmpty();
        disabled.RemoteInventoryPending(h.Host.AllowedRunnerId).ShouldBeFalse();
        h.Probe.Reads.ShouldBe(0);
        var snapshot = h.Host.Directory.UnknownRemoteSessionIds();
        snapshot.ShouldBeEmpty();
        if (snapshot is ICollection<Guid> collection) Should.Throw<NotSupportedException>(() => collection.Add(h.SessionId));
        h.Host.Directory.UnknownRemoteSessionIds().ShouldBeEmpty();
        h.Probe.Reads.ShouldBe(1);
        var other = new PhoneHomeRunnerDirectory(h.Host.Local, Options.Create(new PhoneHomeRunnerSettings
            { Enabled = true, AllowedRunnerId = "other-runner" }), h.Host.App.Services.GetRequiredService<IServiceScopeFactory>(), h.Clock);
        other.UnknownRemoteSessionIds().ShouldBeEmpty();
        h.Probe.Reads.ShouldBe(2, "a new directory owns its own cold snapshot");
    }
}
