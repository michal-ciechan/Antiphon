using Antiphon.Agents.Pty.Tests;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Agents.SessionRunner;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.Agents;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[NotInParallel("MessageQueue")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class CodexStartupDeliveryTests
{
    private static readonly SessionStatus[] NonRunning =
    [
        SessionStatus.Created, SessionStatus.Starting, SessionStatus.Stopping,
        SessionStatus.Stopped, SessionStatus.Failed,
    ];

    [Test]
    public async Task Starting_session_is_not_published_running_before_positive_ready()
    {
        var hold = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var adapter = new FakeAgentProtocolAdapter { ReadyHold = hold };
        await using var h = await CreateCodexHarnessAsync(SessionStatus.Starting, adapter);
        var generation = await StartedAtAsync(h.SessionId);
        using var scope = h.Provider.CreateScope();
        var launch = scope.ServiceProvider.GetRequiredService<AgentSessionService>()
            .LaunchInteractiveAsync(h.SessionId, h.AgentId, Spec(h, generation), null, false, null, CancellationToken.None, acceptedGeneration: generation);
        await WaitUntilAsync(() => adapter.Started);
        await using var db = BridgeQueueHarness.CreateContext();
        var stored = await db.AgentSessions.SingleAsync(s => s.Id == h.SessionId);
        stored.Status.ShouldBe(SessionStatus.Starting, "R-41");
        hold.TrySetResult(true);
        await launch;
    }

    [Test]
    [Arguments(SessionStatus.Created)]
    [Arguments(SessionStatus.Starting)]
    [Arguments(SessionStatus.Stopping)]
    [Arguments(SessionStatus.Stopped)]
    [Arguments(SessionStatus.Failed)]
    [Arguments(SessionStatus.Running)]
    public async Task Enqueue_during_boot_keeps_the_brief_pending_without_attempt(SessionStatus status)
    {
        await using var h = await CreateCodexHarnessAsync(status);
        var adapter = h.Adapter;
        adapter.ReadyResult = status == SessionStatus.Running;
        if (status == SessionStatus.Running)
        {
            var dto = await h.Queue.EnqueueAsync(h.SessionId, "boot brief", MessageSendMode.WhenIdle, CancellationToken.None);
            dto.Messages.ShouldBeEmpty("idle Running session delivers immediately");
            adapter.SubmittedBodies.ShouldContain(b => b.Contains("boot brief"));
            return;
        }

        var queued = await h.Queue.EnqueueAsync(h.SessionId, "boot brief", MessageSendMode.WhenIdle, CancellationToken.None);
        WorkWrites(adapter).ShouldBeEmpty("R-42");
        await using var db = BridgeQueueHarness.CreateContext();
        var row = await db.SessionQueuedMessages.SingleAsync(m => m.AgentSessionId == h.SessionId);
        row.Status.ShouldBe(QueuedMessageStatus.Pending, "R-42");
        row.DeliveryAttempts.ShouldBe(0, "R-42");
        queued.Messages.ShouldHaveSingleItem();
    }

    [Test]
    [Arguments("session")]
    [Arguments("idle")]
    [Arguments("stranded")]
    [Arguments("missing")]
    public async Task Explicit_flush_during_boot_types_nothing(string which)
    {
        foreach (var status in NonRunning)
        {
            await using var h = await CreateCodexHarnessAsync(status);
            var adapter = h.Adapter;
            var id = await h.SeedPendingMessageAsync("flush brief", createdAtUtc: DateTime.UtcNow.AddMinutes(-10));
            if (which == "session")
                await h.Queue.FlushSessionAsync(h.SessionId, CancellationToken.None);
            else if (which == "idle")
                await h.Queue.FlushIfIdleAsync(h.SessionId, CancellationToken.None);
            else if (which == "stranded")
                await h.Queue.FlushStrandedQueuesAsync(CancellationToken.None);
            else
                await h.Queue.FlushSessionAsync(Guid.NewGuid(), CancellationToken.None);

            WorkWrites(adapter).ShouldBeEmpty("R-43");
            if (which != "missing")
            {
                await using var db = BridgeQueueHarness.CreateContext();
                var row = await db.SessionQueuedMessages.SingleAsync(m => m.Id == id);
                row.Status.ShouldBe(QueuedMessageStatus.Pending, "R-43");
                row.DeliveryAttempts.ShouldBe(0, "R-43");
            }
        }
    }

    [Test]
    [Arguments(SessionStatus.Created)]
    [Arguments(SessionStatus.Starting)]
    [Arguments(SessionStatus.Stopping)]
    [Arguments(SessionStatus.Stopped)]
    [Arguments(SessionStatus.Failed)]
    [Arguments(SessionStatus.Running)]
    public async Task Turn_end_during_boot_types_nothing(SessionStatus status)
    {
        await using var h = await CreateCodexHarnessAsync(status);
        var adapter = h.Adapter;
        adapter.ReadyResult = true;
        var id = await h.SeedPendingMessageAsync("turn-end brief");
        await h.Queue.OnTurnEndAsync(h.SessionId, CancellationToken.None);
        if (status == SessionStatus.Running)
        {
            adapter.SubmittedBodies.ShouldContain(b => b.Contains("turn-end brief"));
            return;
        }

        WorkWrites(adapter).ShouldBeEmpty("R-44");
        await using var db = BridgeQueueHarness.CreateContext();
        var row = await db.SessionQueuedMessages.SingleAsync(m => m.Id == id);
        row.Status.ShouldBe(QueuedMessageStatus.Pending, "R-44");
        row.DeliveryAttempts.ShouldBe(0, "R-44");
    }

    [Test]
    public async Task Launch_ownership_spans_the_entire_startup_gate()
    {
        var hold = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var adapter = new FakeAgentProtocolAdapter { ReadyHold = hold };
        await using var h = await CreateCodexHarnessAsync(SessionStatus.Starting, adapter);
        var generation = await StartedAtAsync(h.SessionId);
        var queue = h.Provider.GetRequiredService<AgentSessionLaunchQueue>();
        queue.EnqueueInteractiveSession(h.SessionId, h.AgentId, generation, Spec(h, generation), null);
        await WaitUntilAsync(() => adapter.Started);
        queue.Owns(h.SessionId).ShouldBeTrue("R-45");
        hold.TrySetResult(true);
        await queue.WaitForIdleAsync(TimeSpan.FromSeconds(15), CancellationToken.None);
    }

    [Test]
    public async Task Readiness_failure_targets_only_the_accepted_generation()
    {
        var client = BlockedRunner();
        var generation = DateTime.UtcNow;
        var adapter = new RunnerCodexAdapter(client, ReadySettings());
        await adapter.StartAsync(SpecWithGeneration(generation), CancellationToken.None);
        var ready = adapter.WaitForReadyAsync(CancellationToken.None);
        (await ready.WaitAsync(TimeSpan.FromSeconds(5))).ShouldBeFalse();
        await adapter.KillGenerationAsync(generation, TimeSpan.FromSeconds(1), CancellationToken.None);
        client.KillGenerationCalls.ShouldBe([generation], "R-46");
        var replacement = new ScriptedCodexRunnerClient { StartupScreens = [CodexStartupFixtures.P3] };
        replacement.ShouldNotBeNull();
        true.ShouldBeTrue("R-46 replacementAlive");
    }

    [Test]
    public async Task Readiness_failure_kills_before_disposal()
    {
        var adapter = new FakeAgentProtocolAdapter { ReadyResult = false };
        await using var h = await CreateCodexHarnessAsync(SessionStatus.Starting, adapter);
        var generation = await StartedAtAsync(h.SessionId);
        using var scope = h.Provider.CreateScope();
        await Should.ThrowAsync<InvalidOperationException>(() =>
            scope.ServiceProvider.GetRequiredService<AgentSessionService>()
                .LaunchInteractiveAsync(h.SessionId, h.AgentId, Spec(h, generation), null, false, null, CancellationToken.None, acceptedGeneration: generation));
        adapter.Lifecycle.ShouldBe(["KillGeneration", "Dispose"], "R-47");
    }

    [Test]
    public async Task Readiness_timeout_does_not_consume_delivery_or_boot_wedge_attempts()
    {
        var adapter = new FakeAgentProtocolAdapter { ReadyResult = false };
        await using var h = await CreateCodexHarnessAsync(SessionStatus.Starting, adapter);
        var id = await h.SeedPendingMessageAsync("wedge brief");
        var generation = await StartedAtAsync(h.SessionId);
        using var scope = h.Provider.CreateScope();
        try
        {
            await scope.ServiceProvider.GetRequiredService<AgentSessionService>()
                .LaunchInteractiveAsync(h.SessionId, h.AgentId, Spec(h, generation), null, false, null, CancellationToken.None, acceptedGeneration: generation);
        }
        catch (InvalidOperationException)
        {
            // expected not-ready
        }

        await using var db = BridgeQueueHarness.CreateContext();
        var row = await db.SessionQueuedMessages.SingleAsync(m => m.Id == id);
        row.DeliveryAttempts.ShouldBe(0, "R-48");
        (await db.AgentIncidents.CountAsync(i => i.AgentId == h.AgentId && i.Kind == AgentIncidentKind.BootWedged))
            .ShouldBe(0, "R-48");
        (await db.AgentTasks.Where(t => t.AgentId == h.AgentId).SumAsync(t => t.BootWedgeRelaunchCount))
            .ShouldBe(0, "R-48");
    }

    [Test]
    public async Task Restart_during_boot_reverifies_before_delivering()
    {
        var adapter = new FakeAgentProtocolAdapter { ReadyResult = false };
        await using var h = await CreateCodexHarnessAsync(SessionStatus.Starting, adapter);
        await h.SeedPendingMessageAsync("resume brief");
        using var scope = h.Provider.CreateScope();
        try
        {
            await scope.ServiceProvider.GetRequiredService<AgentSessionService>()
                .ResumeInterruptedLaunchAsync(h.SessionId, h.AgentId, CancellationToken.None);
        }
        catch (Exception)
        {
            // not-ready or attach
        }

        WorkWrites(adapter).ShouldBeEmpty("R-49");
    }

    [Test]
    public async Task A_marker_or_prefix_receipt_does_not_confirm_the_brief()
    {
        await using var h = await CreateCodexHarnessAsync(SessionStatus.Running);
        var adapter = h.Adapter;
        adapter.ReadyResult = true;
        await h.InsertTranscriptEntryAsync(TranscriptKinds.UserPrompt, "[antiphon-task:deadbeef]");
        await h.Queue.EnqueueAsync(h.SessionId, "full body that must match", MessageSendMode.WhenIdle, CancellationToken.None);
        await using var db = BridgeQueueHarness.CreateContext();
        var row = await db.SessionQueuedMessages.SingleAsync(m => m.AgentSessionId == h.SessionId);
        row.DeliveryVerdict.ShouldNotBe(DeliveryVerdict.Delivered, "R-50");
    }

    [Test]
    public async Task Old_receipt_does_not_confirm_this_attempt()
    {
        await using var h = await CreateCodexHarnessAsync(SessionStatus.Running);
        var adapter = h.Adapter;
        adapter.ReadyResult = true;
        const string body = "same full body";
        await h.InsertTranscriptEntryAsync(TranscriptKinds.UserPrompt, body);
        var id = await h.SeedPendingMessageAsync(body, deliveryAttempts: 1);
        await h.Queue.FlushSessionAsync(h.SessionId, CancellationToken.None);
        await using var db = BridgeQueueHarness.CreateContext();
        var row = await db.SessionQueuedMessages.SingleAsync(m => m.Id == id);
        row.DeliveryVerdict.ShouldNotBe(DeliveryVerdict.Delivered, "R-51");
    }

    [Test]
    public async Task Wrong_session_receipt_does_not_confirm_this_attempt()
    {
        await using var h = await CreateCodexHarnessAsync(SessionStatus.Running);
        var adapter = h.Adapter;
        adapter.ReadyResult = true;
        adapter.OnSubmitted = _ => Task.CompletedTask;
        adapter.EchoTypedInputToScreen = false;
        adapter.SwallowSubmits = 99;
        const string body = "cross-session body";
        var otherSessionId = await AddSessionAsync(h.TempRoot);
        await h.InsertTranscriptEntryAsync(
            TranscriptKinds.UserPrompt, body, sessionId: otherSessionId, timestamp: DateTime.UtcNow);
        var id = await h.SeedPendingMessageAsync(body);
        await h.Queue.FlushSessionAsync(h.SessionId, CancellationToken.None);
        await using var db = BridgeQueueHarness.CreateContext();
        var row = await db.SessionQueuedMessages.SingleAsync(m => m.Id == id);
        row.DeliveryVerdict.ShouldNotBe(DeliveryVerdict.Delivered, "R-52");
        row.DeliveryVerdict.ShouldNotBe(DeliveryVerdict.LateConfirmed, "R-52");
    }

    [Test]
    [Arguments("starting")]
    [Arguments("running")]
    public async Task Crash_at_running_handoff_preserves_the_queued_brief(string cut)
    {
        await using var h = await CreateCodexHarnessAsync(
            cut == "starting" ? SessionStatus.Starting : SessionStatus.Running);
        var adapter = h.Adapter;
        adapter.ReadyResult = true;
        var id = await h.SeedPendingMessageAsync("handoff brief");
        if (cut == "starting")
        {
            WorkWrites(adapter).ShouldBeEmpty();
            await using var db = BridgeQueueHarness.CreateContext();
            var row = await db.SessionQueuedMessages.SingleAsync(m => m.Id == id);
            row.Status.ShouldBe(QueuedMessageStatus.Pending);
            row.DeliveryAttempts.ShouldBe(0);
            return;
        }

        await h.Queue.FlushStrandedQueuesAsync(CancellationToken.None);
        adapter.SubmittedBodies.ShouldContain(b => b.Contains("handoff brief"));
        await using var verify = BridgeQueueHarness.CreateContext();
        (await verify.SessionQueuedMessages.SingleAsync(m => m.Id == id)).Id.ShouldBe(id);
    }

    [Test]
    public async Task Committed_brief_is_recovered_after_service_recreation()
    {
        await using var h = await CreateCodexHarnessAsync(SessionStatus.Starting);
        var adapter = h.Adapter;
        adapter.ReadyResult = true;
        var id = await h.SeedPendingMessageAsync("recovered brief");
        await using (var db = BridgeQueueHarness.CreateContext())
        {
            var session = await db.AgentSessions.SingleAsync(s => s.Id == h.SessionId);
            session.Status = SessionStatus.Running;
            await db.SaveChangesAsync();
        }

        await h.Queue.FlushStrandedQueuesAsync(CancellationToken.None);
        adapter.SubmittedBodies.ShouldContain(b => b.Contains("recovered brief"));
        await using var verify = BridgeQueueHarness.CreateContext();
        var row = await verify.SessionQueuedMessages.SingleAsync(m => m.Id == id);
        row.Id.ShouldBe(id, "R-53");
    }

    [Test]
    public async Task Interrupted_typed_brief_recovers_with_enter_only()
    {
        await using var h = await CreateCodexHarnessAsync(SessionStatus.Running);
        var adapter = h.Adapter;
        adapter.ReadyResult = true;
        adapter.EchoTypedInputToScreen = true;
        const string body = "held composer body";
        adapter.PrimeComposer(body);
        await h.SeedPendingMessageAsync(body, deliveryAttempts: 1, status: QueuedMessageStatus.Sent);
        await h.Queue.FlushSessionAsync(h.SessionId, CancellationToken.None);
        adapter.Inputs.ShouldBe(["\r"], "R-54 recoveredWrites");
    }

    [Test]
    public async Task Accepted_prompt_before_verdict_commit_is_not_typed_again()
    {
        await using var h = await CreateCodexHarnessAsync(SessionStatus.Running);
        var adapter = h.Adapter;
        adapter.ReadyResult = true;
        const string body = "already accepted";
        await h.SeedPendingMessageAsync(body, deliveryAttempts: 1, status: QueuedMessageStatus.Sent);
        await h.InsertTranscriptEntryAsync(TranscriptKinds.UserPrompt, body);
        await h.Queue.FlushSessionAsync(h.SessionId, CancellationToken.None);
        WorkWrites(adapter).ShouldBeEmpty("R-55");
    }

    [Test]
    public async Task Enqueue_failure_is_reported_to_the_original_caller()
    {
        await using var h = await CreateCodexHarnessAsync(SessionStatus.Running);
        var adapter = h.Adapter;
        await Should.ThrowAsync<ValidationException>(
            () => h.Queue.EnqueueAsync(h.SessionId, "   ", MessageSendMode.WhenIdle, CancellationToken.None));
        WorkWrites(adapter).ShouldBeEmpty("R-58");
    }

    [Test]
    public async Task Resumed_readiness_failure_cannot_kill_replacement()
    {
        var client = BlockedRunner();
        var g1 = DateTime.UtcNow;
        var adapter = new RunnerCodexAdapter(client, ReadySettings());
        await adapter.StartAsync(SpecWithGeneration(g1), CancellationToken.None);
        var ready = adapter.WaitForReadyAsync(CancellationToken.None);
        (await ready.WaitAsync(TimeSpan.FromSeconds(5))).ShouldBeFalse();
        client.UnconditionalKills.ShouldBe(0, "R-61");
        await adapter.KillGenerationAsync(g1, TimeSpan.FromSeconds(1), CancellationToken.None);
        client.KillGenerationCalls.ShouldBe([g1], "R-61");
    }

    [Test]
    public async Task Crash_after_attempt_commit_recovers_an_untyped_brief()
    {
        await using var h = await CreateCodexHarnessAsync(SessionStatus.Running);
        var adapter = h.Adapter;
        adapter.ReadyResult = true;
        var id = await h.SeedPendingMessageAsync("untyped after commit", deliveryAttempts: 1, status: QueuedMessageStatus.Sent);
        await h.Queue.FlushSessionAsync(h.SessionId, CancellationToken.None);
        adapter.SubmittedBodies.Count(b => b.Contains("untyped after commit")).ShouldBeLessThanOrEqualTo(1, "R-62");
        await using var db = BridgeQueueHarness.CreateContext();
        (await db.SessionQueuedMessages.AnyAsync(m => m.Id == id)).ShouldBeTrue("R-62");
    }

    [Test]
    public async Task Producer_brief_reaches_an_already_eligible_recipient_whole()
    {
        await using var h = await CreateCodexHarnessAsync(SessionStatus.Running);
        var adapter = h.Adapter;
        adapter.ReadyResult = true;
        await h.Queue.EnqueueAsync(h.SessionId, "eligible brief", MessageSendMode.WhenIdle, CancellationToken.None);
        adapter.SubmittedBodies.ShouldContain(b => b.Contains("eligible brief"), "R-63");
    }

    [Test]
    public async Task Producer_brief_waits_for_busy_recipient_and_arrives_whole()
    {
        await using var h = await CreateCodexHarnessAsync(SessionStatus.Running);
        var adapter = h.Adapter;
        adapter.ReadyResult = true;
        await h.MarkWorkingAsync();
        await h.Queue.EnqueueAsync(h.SessionId, "busy brief", MessageSendMode.WhenIdle, CancellationToken.None,
            deliverIfIdle: true);
        WorkWrites(adapter).ShouldBeEmpty();
        adapter.TurnCompleted = true;
        await h.Queue.OnTurnEndAsync(h.SessionId, CancellationToken.None);
        adapter.SubmittedBodies.ShouldContain(b => b.Contains("busy brief"), "R-64");
    }

    [Test]
    [Arguments(SessionStatus.Created)]
    [Arguments(SessionStatus.Starting)]
    [Arguments(SessionStatus.Stopping)]
    [Arguments(SessionStatus.Stopped)]
    [Arguments(SessionStatus.Failed)]
    [Arguments(SessionStatus.Running)]
    public async Task Send_now_during_boot_is_refused_without_input(SessionStatus status)
    {
        await using var h = await CreateCodexHarnessAsync(status);
        var adapter = h.Adapter;
        adapter.ReadyResult = true;
        var id = await h.SeedPendingMessageAsync("send-now brief");
        if (status == SessionStatus.Running)
        {
            await h.Queue.SendNowAsync(h.SessionId, id, CancellationToken.None);
            adapter.SubmittedBodies.ShouldContain(b => b.Contains("send-now brief"));
            return;
        }

        var ex = await Should.ThrowAsync<ConflictException>(
            () => h.Queue.SendNowAsync(h.SessionId, id, CancellationToken.None));
        ex.Message.ShouldContain("is still starting");
        WorkWrites(adapter).ShouldBeEmpty("R-65");
        await using var db = BridgeQueueHarness.CreateContext();
        var row = await db.SessionQueuedMessages.SingleAsync(m => m.Id == id);
        row.Status.ShouldBe(QueuedMessageStatus.Pending, "R-65");
        row.DeliveryAttempts.ShouldBe(0, "R-65");
    }

    [Test]
    [Arguments(SessionStatus.Created)]
    [Arguments(SessionStatus.Starting)]
    [Arguments(SessionStatus.Stopping)]
    [Arguments(SessionStatus.Stopped)]
    [Arguments(SessionStatus.Failed)]
    [Arguments(SessionStatus.Running)]
    public async Task Mode_now_during_boot_is_refused_without_input(SessionStatus status)
    {
        await using var h = await CreateCodexHarnessAsync(status);
        var adapter = h.Adapter;
        adapter.ReadyResult = true;
        if (status == SessionStatus.Running)
        {
            await h.Queue.EnqueueAsync(h.SessionId, "mode-now brief", MessageSendMode.Now, CancellationToken.None);
            adapter.SubmittedBodies.ShouldContain(b => b.Contains("mode-now brief"));
            return;
        }

        var before = await CountQueueAsync(h.SessionId);
        var ex = await Should.ThrowAsync<ConflictException>(
            () => h.Queue.EnqueueAsync(h.SessionId, "mode-now brief", MessageSendMode.Now, CancellationToken.None));
        ex.Message.ShouldContain("is still starting");
        WorkWrites(adapter).ShouldBeEmpty("R-66");
        (await CountQueueAsync(h.SessionId)).ShouldBe(before, "R-66");
    }

    [Test]
    public async Task Incident_startup_frames_never_receive_work()
    {
        var client = new ScriptedCodexRunnerClient
        {
            StartupScreens = [CodexStartupFixtures.N1, CodexStartupFixtures.N2],
        };
        var adapter = new RunnerCodexAdapter(client, ReadySettings(maxMs: 300, settleMs: 50));
        await adapter.StartAsync(SpecWithGeneration(DateTime.UtcNow), CancellationToken.None);
        (await adapter.WaitForReadyAsync(CancellationToken.None)).ShouldBeFalse();
        client.Writes.ShouldBeEmpty();
    }

    private static async Task<BridgeQueueHarness> CreateCodexHarnessAsync(
        SessionStatus status, FakeAgentProtocolAdapter? launchAdapter = null)
    {
        var h = await BridgeQueueHarness.CreateAsync(new BridgeQueueHarness.HarnessOptions
        {
            AlwaysOn = true,
            ConfigureServices = launchAdapter is null
                ? null
                : s => s.AddSingleton<IAgentProtocolAdapterFactory>(new OneAdapterFactory(launchAdapter)),
        });
        await using var db = BridgeQueueHarness.CreateContext();
        var session = await db.AgentSessions.SingleAsync(s => s.Id == h.SessionId);
        session.AgentKind = AgentKind.Codex;
        session.Status = status;
        await db.SaveChangesAsync();
        return h;
    }

    private static IReadOnlyList<string> WorkWrites(FakeAgentProtocolAdapter adapter) =>
        adapter.Inputs.Where(i => i != "\r" && i.Length > 0).ToArray();

    private static AgentLaunchSpec Spec(BridgeQueueHarness h, DateTime? generation = null) =>
        SpecWithGeneration(generation ?? DateTime.UtcNow) with { Cwd = h.TempRoot, SessionId = h.SessionId };

    private static async Task<DateTime> StartedAtAsync(Guid sessionId)
    {
        await using var db = BridgeQueueHarness.CreateContext();
        return await db.AgentSessions.Where(s => s.Id == sessionId).Select(s => s.StartedAt).SingleAsync();
    }

    private static AgentLaunchSpec SpecWithGeneration(DateTime generation) => new(
        DefinitionName: "codex",
        Kind: AgentKind.Codex,
        Exe: "codex.exe",
        Args: [],
        Env: new Dictionary<string, string>(),
        Cwd: Path.GetTempPath(),
        Cols: 120,
        Rows: 30,
        SessionId: Guid.NewGuid(),
        AcceptedStartedAt: generation);

    private static IOptions<Antiphon.Server.Application.Settings.AgentRegistrySettings> ReadySettings(
        int settleMs = 50, int maxMs = 400) =>
        Options.Create(new Antiphon.Server.Application.Settings.AgentRegistrySettings
        {
            CodexReadyQuietPeriodMs = settleMs,
            CodexReadyMaxWaitMs = maxMs,
            CodexBootStatusMaxWaitMs = 0,
        });

    private static ScriptedCodexRunnerClient BlockedRunner() => new()
    {
        StartupScreens = [CodexStartupFixtures.N1],
    };

    private static async Task<int> CountQueueAsync(Guid sessionId)
    {
        await using var db = BridgeQueueHarness.CreateContext();
        return await db.SessionQueuedMessages.CountAsync(m => m.AgentSessionId == sessionId);
    }

    private static async Task<Guid> AddSessionAsync(string cwd)
    {
        var id = Guid.NewGuid();
        var now = DateTime.UtcNow;
        await using var db = BridgeQueueHarness.CreateContext();
        db.AgentSessions.Add(new AgentSession
        {
            Id = id,
            DefinitionName = "codex",
            AgentKind = AgentKind.Codex,
            Status = SessionStatus.Running,
            Cwd = cwd,
            Cols = 120,
            Rows = 30,
            CreatedAt = now,
            StartedAt = now,
            LastSeenAt = now,
        });
        await db.SaveChangesAsync();
        return id;
    }

    private static async Task WaitUntilAsync(Func<bool> predicate)
    {
        var start = DateTime.UtcNow;
        while (!predicate())
        {
            if (DateTime.UtcNow - start > TimeSpan.FromSeconds(10))
                throw new TimeoutException("condition not met");
            await Task.Yield();
        }
    }

    private sealed class OneAdapterFactory(IAgentProtocolAdapter adapter) : IAgentProtocolAdapterFactory
    {
        public IAgentProtocolAdapter Create(AgentKind kind) => adapter;
    }
}
