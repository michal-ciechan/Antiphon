using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Agents.SessionRunner;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0633 D-2: a phone-home runner becomes dispatch-eligible only after a successful catch-up
/// List. Before this, a failed or timed-out List was swallowed and the pump marked the runner
/// recovered anyway, so dispatch went to a runner whose owner inventory had never been read.
/// </summary>
[Category("Integration")]
public class PhoneHomeRecoveryEligibilityTests
{
    [Test]
    public async Task Silent_list_reply_leaves_the_runner_ineligible_until_catch_up_succeeds()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        await using var host = await PhoneHomeTestHost.StartAsync(clock);
        await using var peer = await host.ConnectPeerAsync();
        peer.SilentFor(PhoneHomeOperation.List);
        var live = await host.WaitLiveAsync();
        var settings = Settings(host);
        var pump = Pump(host, settings, host.App.Services.GetRequiredService<IServiceScopeFactory>());
        using var cts = new CancellationTokenSource();

        var cycle = pump.RunCycleAsync(cts.Token);
        await peer.WaitForAsync(PhoneHomeOperation.List);
        // The request's budget timer is armed right after the frame is written; give the server a
        // moment to reach it before moving the fake clock, as the connection timeout test does.
        await Task.Delay(100);
        clock.Advance(PhoneHomeLiveConnection.RequestTimeoutFor(PhoneHomeOperation.List) + TimeSpan.FromSeconds(1));

        var recovered = await cycle.WaitAsync(TimeSpan.FromSeconds(5));
        recovered.ShouldBeFalse("a List that never answered must not count as a catch-up");
        live.DispatchEligible.ShouldBeFalse();
        peer.RequestCount(PhoneHomeOperation.List).ShouldBe(1);

        peer.Speak(PhoneHomeOperation.List);
        clock.Advance(TimeSpan.FromSeconds(settings.CatchUpRetrySeconds));
        var second = await pump.RunCycleAsync(cts.Token).WaitAsync(TimeSpan.FromSeconds(5));
        second.ShouldBeTrue();
        live.DispatchEligible.ShouldBeTrue();
        peer.RequestCount(PhoneHomeOperation.List).ShouldBe(2);
        cts.Cancel();
    }

    [Test]
    public async Task Failed_catch_up_waits_the_retry_delay_before_asking_again()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        await using var host = await PhoneHomeTestHost.StartAsync(clock);
        await using var peer = await host.ConnectPeerAsync();
        peer.Reply = frame => frame.Operation == PhoneHomeOperation.List
            ? new PhoneHomeFrame(
                PhoneHomeFrameKind.Error, frame.Epoch, frame.RequestId, frame.Operation,
                ErrorCode: "runner_list_failed", ErrorDetail: "list exploded", StatusCode: 503)
            : null;
        var live = await host.WaitLiveAsync();
        var settings = Settings(host);
        var pump = Pump(host, settings, host.App.Services.GetRequiredService<IServiceScopeFactory>());
        using var cts = new CancellationTokenSource();

        (await pump.RunCycleAsync(cts.Token).WaitAsync(TimeSpan.FromSeconds(5))).ShouldBeFalse();
        peer.RequestCount(PhoneHomeOperation.List).ShouldBe(1);
        live.DispatchEligible.ShouldBeFalse();

        (await pump.RunCycleAsync(cts.Token).WaitAsync(TimeSpan.FromSeconds(5))).ShouldBeFalse();
        peer.RequestCount(PhoneHomeOperation.List).ShouldBe(
            1, "a failed catch-up must wait CatchUpRetrySeconds before sending another List");

        clock.Advance(TimeSpan.FromSeconds(settings.CatchUpRetrySeconds));
        (await pump.RunCycleAsync(cts.Token).WaitAsync(TimeSpan.FromSeconds(5))).ShouldBeFalse();
        peer.RequestCount(PhoneHomeOperation.List).ShouldBe(2);
        live.DispatchEligible.ShouldBeFalse();
        cts.Cancel();
    }

    [Test]
    public async Task One_session_transcript_failure_is_a_warning_not_a_fence()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var h = await BridgeQueueHarness.CreateAsync(new() { ConnectionString = schema.ConnectionString });
        await using var host = await PhoneHomeTestHost.StartAsync(connectionString: schema.ConnectionString);
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var good = await SeedAsync(db, host.AllowedRunnerId, host.StoreId);
        var bad = await SeedAsync(db, host.AllowedRunnerId, host.StoreId);
        await using var peer = await host.ConnectPeerAsync();
        peer.Sessions.Add(new RunnerSessionDto(good.Id, 1, DateTime.UtcNow, "Running", null, "", 1));
        peer.Sessions.Add(new RunnerSessionDto(bad.Id, 1, DateTime.UtcNow, "Running", null, "", 1));
        peer.Transcripts[good.Id] = new RunnerTranscriptDto(good.Id, [Prompt(good.Id, "caught-up", "good-1")], 1);
        peer.Reply = frame => frame.Operation == PhoneHomeOperation.Transcript
            && frame.Payload?.GetProperty("sessionId").GetGuid() == bad.Id
            ? new PhoneHomeFrame(
                PhoneHomeFrameKind.Error, frame.Epoch, frame.RequestId, frame.Operation,
                ErrorCode: "transcript_unreadable", ErrorDetail: "no transcript", StatusCode: 409)
            : null;
        var live = await host.WaitLiveAsync();
        var logger = new RecordingLogger<PhoneHomeRecoveryPump>();
        var pump = Pump(host, Settings(host), h.Provider.GetRequiredService<IServiceScopeFactory>(), logger);
        using var cts = new CancellationTokenSource();

        var recovered = await pump.RunCycleAsync(cts.Token).WaitAsync(TimeSpan.FromSeconds(10));

        recovered.ShouldBeTrue("one unreadable session must not fence the whole runner");
        live.DispatchEligible.ShouldBeTrue();
        (await db.TranscriptEntries.CountAsync(t => t.AgentSessionId == good.Id && t.Uuid == "good-1")).ShouldBe(1);
        pump.LastCatchUpTranscriptFailures.ShouldBe(1);
        var warnings = logger.Entries.Where(e => e.Level == LogLevel.Warning).ToList();
        warnings.Count(e => e.Message.Contains(bad.Id.ToString())).ShouldBe(
            1, "the failed session's catch-up must be visible at Warning, naming the session");
        warnings.ShouldNotContain(e => e.Message.Contains(good.Id.ToString()));
        cts.Cancel();
    }

    private static PhoneHomeRunnerSettings Settings(PhoneHomeTestHost host) => new()
    {
        Enabled = true,
        AllowedRunnerId = host.AllowedRunnerId,
        StandingAgentId = Guid.NewGuid(),
        HostWorkspaceRoot = @"C:\work",
        SharedSecret = host.Secret,
    };

    private static PhoneHomeRecoveryPump Pump(
        PhoneHomeTestHost host,
        PhoneHomeRunnerSettings settings,
        IServiceScopeFactory scopes,
        ILogger<PhoneHomeRecoveryPump>? logger = null) =>
        new(host.Directory, Options.Create(settings), scopes, logger ?? NullLogger<PhoneHomeRecoveryPump>.Instance);

    private static async Task<AgentSession> SeedAsync(AppDbContext db, string runnerId, Guid storeId)
    {
        var session = new AgentSession
        {
            Id = Guid.NewGuid(),
            DefinitionName = "grok",
            AgentKind = AgentKind.Grok,
            Status = SessionStatus.Running,
            Cwd = @"C:\work",
            Cols = 80,
            Rows = 24,
            CreatedAt = DateTime.UtcNow,
            StartedAt = DateTime.UtcNow,
            LastSeenAt = DateTime.UtcNow,
            RunnerId = runnerId,
            RunnerStoreId = storeId,
            RunnerCwd = "/work",
        };
        db.AgentSessions.Add(session);
        await db.SaveChangesAsync();
        return session;
    }

    private static RunnerTranscriptEvent Prompt(Guid sessionId, string text, string uuid) =>
        new(sessionId, 1, TranscriptKinds.UserPrompt, uuid, null, DateTimeOffset.UtcNow, "user", text, null, null, null, null, null);
}
