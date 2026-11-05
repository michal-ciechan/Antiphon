using System.Reflection;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

public sealed partial class ChannelInputWithheldIncidentTests
{
    [Test]
    [Arguments("RunWakeWorkerAsync")]
    [Arguments("RunWakeWorkerStallCheckAsync")]
    [Arguments("RunWithheldIncidentLoopAsync")]
    [Timeout(60_000)]
    public async Task C1723_NonShutdownCancellationKeepsWorkerAlive(string worker, CancellationToken ct)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var clock = Clock();
        var commands = new ScanCommands();
        await using var h = await OpenAsync(schema.ConnectionString, clock, o => o.AddInterceptors(commands));
        var logger = new CaptureLogger();
        using var bridge = Bridge(h, clock, logger);
        var attempts = 0;
        commands.Fault = _ => Interlocked.Increment(ref attempts) == 1
            ? new OperationCanceledException("independent database cancellation") : null;
        commands.Armed = true;
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var method = typeof(ChannelBridgeService).GetMethod(worker, BindingFlags.Instance | BindingFlags.NonPublic)!;
        var loop = (Task)method.Invoke(bridge, [stop.Token])!;
        Exception? error = null;
        try
        {
            var until = DateTime.UtcNow.AddSeconds(20);
            while (!loop.IsCompleted && Volatile.Read(ref attempts) < 2 && DateTime.UtcNow < until)
            {
                clock.Advance(TimeSpan.FromMinutes(6));
                await Task.Delay(25, ct);
            }
        }
        finally
        {
            await stop.CancelAsync();
            try { await loop.WaitAsync(TimeSpan.FromSeconds(5), ct); }
            catch (Exception ex) { error = ex; }
        }
        error.ShouldBeNull("a non-shutdown cancellation must not fault the hosted sibling");
        attempts.ShouldBeGreaterThanOrEqualTo(2, "the same worker must perform another pass");
        logger.Lines.ShouldContain(line => line.Contains("failed", StringComparison.OrdinalIgnoreCase));
        h.Adapter.SentInput.ShouldBeEmpty();
    }

    [Test]
    [Arguments("incident")]
    [Arguments("stopped")]
    [Timeout(120_000)]
    public async Task C1723_IneligibleBacklogDoesNotConsumePage(string history, CancellationToken ct)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var clock = Clock();
        var commands = new ScanCommands();
        await using var h = await OpenAsync(schema.ConnectionString, clock, o => o.AddInterceptors(commands));
        var now = clock.GetUtcNow().UtcDateTime;
        await using var db = Db(schema.ConnectionString);
        var stoppedId = Guid.NewGuid();
        db.AgentSessions.Add(new AgentSession { Id = stoppedId, Status = SessionStatus.Stopped,
            CreatedAt = now, StartedAt = now, LastSeenAt = now });
        const int backlog = 192;
        for (var n = 0; n < backlog; n++)
        {
            var id = Guid.NewGuid();
            db.SessionQueuedMessages.Add(new SessionQueuedMessage { Id = id,
                AgentSessionId = history == "stopped" ? stoppedId : h.SessionId,
                Body = "old withheld body", Sequence = n + 1, Origin = QueuedMessageOrigin.Channel,
                Status = QueuedMessageStatus.Pending, WithholdCount = 1,
                CreatedAt = now.AddHours(-3), FirstWithheldAt = now.AddHours(-2),
                LastWithheldAt = now.AddHours(-2), LastWithholdReason = WithholdReason });
            db.ChannelInbounds.Add(new ChannelInbound { Id = Guid.NewGuid(), AgentId = h.AgentId,
                Provider = "telegram", ConversationId = "old", NativeMessageId = n.ToString(),
                QueueMessageId = id, AcceptedAt = now.AddHours(-3),
                InputWithheldIncidentAt = history == "incident" ? now.AddHours(-1) : null });
        }
        await db.SaveChangesAsync(ct);
        var fresh = await SeedAsync(h, now.AddHours(-1), now.AddMinutes(-10), 1);
        commands.Armed = true;
        await Bridge(h).ScanWithheldChannelIncidentsAsync(ct);
        commands.Armed = false;
        (await db.ChannelInbounds.AsNoTracking().SingleAsync(i => i.Id == fresh.InboundId, ct))
            .InputWithheldIncidentAt.ShouldNotBeNull("192 irrelevant older heads must not delay a fresh eligible row");
        (await WithheldIncidentsAsync(db, h.AgentId)).ShouldBe(1);
        (await db.SessionQueuedMessages.CountAsync(ct)).ShouldBe(backlog + 1);
        commands.Count.ShouldBeLessThanOrEqualTo(16, "filter in SQL without per-row probes or extra pages");
        h.Adapter.SentInput.ShouldBeEmpty();
    }

    [Test]
    [Timeout(120_000)]
    public async Task C1723_RepeatedScanTimeoutRaisesOnceAndSuccessRearms(CancellationToken ct)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var clock = Clock();
        var commands = new ScanCommands();
        await using var h = await OpenAsync(schema.ConnectionString, clock, o => o.AddInterceptors(commands));
        var attempts = 0;
        var fail = true;
        commands.Fault = sql =>
        {
            if (!sql.Contains("SELECT q.\"Id\", q.\"FirstWithheldAt\"", StringComparison.Ordinal)) return null;
            Interlocked.Increment(ref attempts);
            return Volatile.Read(ref fail) ? new TimeoutException("database scan deadline") : null;
        };
        commands.Armed = true;
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(ct);
        using var bridge = Bridge(h, clock);
        var loop = bridge.RunWithheldIncidentLoopAsync(stop.Token);
        async Task Passes(int target)
        {
            var until = DateTime.UtcNow.AddSeconds(20);
            while (Volatile.Read(ref attempts) < target && !loop.IsCompleted && DateTime.UtcNow < until)
            {
                clock.Advance(TimeSpan.FromSeconds(10));
                await Task.Delay(25, ct);
            }
            attempts.ShouldBe(target);
            // Let the asynchronous pass finish before advancing its next timer.
            await Task.Delay(50, ct);
        }
        async Task<int> Incidents()
        {
            await using var probe = Db(schema.ConnectionString);
            return await probe.AgentIncidents.CountAsync(i => i.FailureReason == "ChannelWithheldScanFailed", ct);
        }
        try
        {
            await Passes(2);
            (await Incidents()).ShouldBe(0, "two failures are below the three-pass threshold");
            await Passes(3);
            await WaitForAsync(async () => await Incidents() == 1);
            await Passes(5);
            (await Incidents()).ShouldBe(1, "a sustained outage raises once");
            Volatile.Write(ref fail, false);
            await Passes(6);
            Volatile.Write(ref fail, true);
            await Passes(8);
            (await Incidents()).ShouldBe(1, "success resets the consecutive-failure count");
            await Passes(9);
            await WaitForAsync(async () => await Incidents() == 2);
            await using var verify = Db(schema.ConnectionString);
            var incidents = await verify.AgentIncidents.Where(i => i.FailureReason == "ChannelWithheldScanFailed")
                .ToListAsync(ct);
            incidents.ShouldAllBe(i => i.Kind == AgentIncidentKind.ChannelReplyLost && i.Severity == AlertSeverity.Critical);
            var attention = new AttentionService(verify, h.Runner,
                h.Provider.GetRequiredService<IOptions<SupervisionSettings>>(),
                h.Provider.GetRequiredService<IOptions<DelegationSettings>>(), clock, NullLogger<AttentionService>.Instance);
            var rows = (await attention.GetAsync(ct, includeProgressProbe: false)).Items.Where(i =>
                i.Kind == AttentionKind.RecentCriticalIncident && i.Evidence.Contains("withheld", StringComparison.OrdinalIgnoreCase)).ToList();
            rows.Count.ShouldBe(1, "the existing attention surface groups the monitor's episodes");
            rows[0].Evidence.ShouldContain("3 consecutive");
        }
        finally
        {
            await stop.CancelAsync();
            await loop.WaitAsync(TimeSpan.FromSeconds(5), ct);
        }
    }
}
