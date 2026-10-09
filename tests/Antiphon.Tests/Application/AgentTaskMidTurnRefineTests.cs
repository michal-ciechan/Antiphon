using Antiphon.Agents.Pty;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0491 V-3 methods 1-4. The key is one conditional Ctrl+C after the row exists.
/// A refusal keeps today's refinement and names itself.
/// </summary>
[Category("Integration")]
[NotInParallel("AgentQueue")]
public sealed class AgentTaskMidTurnRefineTests
{
    private const string Message = "Skip the known-red rows and continue the slice.";

    [Test]
    [Arguments("working")]
    [Arguments("dispatched")]
    public async Task C0491_InterruptWritesOneConditionalCtrlCAfterTheMarkedRow(string arm)
    {
        using var workspace = new TempWorkspace();
        var status = arm == "dispatched" ? AgentTaskStatus.Dispatched : AgentTaskStatus.Working;
        var fx = await PrepareAsync(workspace.Path, status, AgentKind.Grok, SessionStatus.Running, openQuestion: false);
        var observed = 0;
        fx.Runner.OnConditional = async ct =>
        {
            await using var countDb = CreateContext();
            observed = await countDb.SessionQueuedMessages.CountAsync(m => m.AgentSessionId == fx.SessionId, ct);
        };
        var requestId = Guid.NewGuid();

        var summary = await CreateService(fx).RefineAsync(
            fx.Task.Id, Message, CancellationToken.None, interruptCurrentTurn: true, requestId: requestId);

        summary.Status.ShouldBe(status);
        summary.InterruptWritten.ShouldBe("written");
        summary.RefinementDelivered.ShouldBe("pending");
        observed.ShouldBe(1, "the row is committed before the key");
        fx.Runner.ConditionalCalls.Count.ShouldBe(1);
        var call = fx.Runner.ConditionalCalls[0];
        call.SessionId.ShouldBe(fx.SessionId);
        call.Request.Input.ShouldBe("\x03");
        call.Request.ExpectedLastSequence.ShouldBe(41);
        call.Request.ExpectedAcceptedStartedAt.ShouldBe(fx.Started);
        fx.Runner.RawInputs.ShouldBeEmpty();
        AssertNoKillOrBareEscape(fx.Runner);

        await using var verify = CreateContext();
        var row = await verify.SessionQueuedMessages.SingleAsync(m => m.AgentSessionId == fx.SessionId);
        row.Status.ShouldBe(QueuedMessageStatus.Pending);
        row.DeliveryAttempts.ShouldBe(0);
        row.ConversationKey.ShouldBe($"refine:{fx.Task.Id:N}:{requestId:N}");
        row.Body.ShouldContain(DelegationReportFormatter.TaskMarker(fx.Task.Id));
        var refined = await verify.AgentTaskEvents.SingleAsync(
            e => e.AgentTaskId == fx.Task.Id && e.Type == AgentTaskEventType.Refined);
        refined.InputBody.ShouldNotBeNull().ShouldContain(Message);
        refined.Detail.ShouldContain(Message);
        (await verify.AgentTasks.SingleAsync(t => t.Id == fx.Task.Id)).Status.ShouldBe(status);
    }

    [Test]
    [Arguments("setting-disabled")]
    [Arguments("kind-claude")]
    [Arguments("session-stopping")]
    [Arguments("runner-missing")]
    [Arguments("runner-exited")]
    [Arguments("generation-mismatch")]
    [Arguments("generation-unproven")]
    [Arguments("already-sent-at-boundary")]
    [Arguments("open-question")]
    [Arguments("composer-draft")]
    [Arguments("composer-unreadable")]
    [Arguments("conditional-unsupported")]
    [Arguments("queued-status")]
    [Arguments("blocked-status")]
    public async Task C0491_RefusalKeepsTheRowPendingAndSendsNoKey(string flip)
    {
        using var workspace = new TempWorkspace();
        var fx = await PrepareAsync(workspace.Path, TaskStatus(flip), Kind(flip), SessionStatusOf(flip), flip == "open-question");
        ApplyRunnerFlip(fx, flip);
        if (flip == "setting-disabled")
            fx.Settings.GrokMidTurnInterruptEnabled = false;
        var requestId = Guid.NewGuid();
        var service = CreateService(fx);

        if (flip == "blocked-status")
        {
            var refused = await Should.ThrowAsync<ConflictException>(() => service.RefineAsync(
                fx.Task.Id, Message, CancellationToken.None, interruptCurrentTurn: true, requestId: requestId));
            refused.Message.ShouldContain("ANSWER");
            fx.Runner.ConditionalCalls.ShouldBeEmpty();
            AssertNoKillOrBareEscape(fx.Runner);
            await using var blocked = CreateContext();
            (await blocked.SessionQueuedMessages.CountAsync(m => m.AgentSessionId == fx.SessionId)).ShouldBe(0);
            (await blocked.AgentTaskEvents.CountAsync(
                e => e.AgentTaskId == fx.Task.Id && e.Type == AgentTaskEventType.Refined)).ShouldBe(0);
            (await blocked.AgentTasks.SingleAsync(t => t.Id == fx.Task.Id)).Status.ShouldBe(AgentTaskStatus.Blocked);
            return;
        }

        var summary = await service.RefineAsync(
            fx.Task.Id, Message, CancellationToken.None, interruptCurrentTurn: true, requestId: requestId);

        summary.Status.ShouldBe(TaskStatus(flip));
        summary.InterruptWritten.ShouldBe(NamedRefusal(flip), flip);
        fx.Runner.ConditionalCalls.ShouldBeEmpty();
        AssertNoKillOrBareEscape(fx.Runner);

        await using var verify = CreateContext();
        (await verify.AgentTasks.SingleAsync(t => t.Id == fx.Task.Id)).Status.ShouldBe(TaskStatus(flip));
        if (flip == "queued-status")
        {
            summary.RefinementDelivered.ShouldBeNull();
            (await verify.AgentTasks.SingleAsync(t => t.Id == fx.Task.Id)).Goal.ShouldContain(Message);
            (await verify.SessionQueuedMessages.CountAsync(m => m.AgentSessionId == fx.SessionId)).ShouldBe(0);
            (await verify.AgentTaskEvents.CountAsync(
                e => e.AgentTaskId == fx.Task.Id && e.Type == AgentTaskEventType.Refined)).ShouldBe(1);
            return;
        }

        var row = await verify.SessionQueuedMessages.SingleAsync(m => m.AgentSessionId == fx.SessionId);
        if (flip == "already-sent-at-boundary")
        {
            row.Status.ShouldBe(QueuedMessageStatus.Sent);
            summary.InterruptWritten.ShouldBe("already-sent");
            // The queue types the body, then a separate CR. A multiline body is bracketed paste.
            // Neither write is the interrupt key or a bare Esc.
            fx.Runner.RawInputs.Select(raw => raw.Input).ShouldBe(new[]
            {
                PtyInputEncoding.EncodeBody(row.Body),
                "\r",
            });
            return;
        }

        row.Status.ShouldBe(QueuedMessageStatus.Pending, flip);
        row.Body.ShouldContain(DelegationReportFormatter.TaskMarker(fx.Task.Id));
        fx.Runner.RawInputs.ShouldBeEmpty();
        if (flip == "conditional-unsupported")
            fx.Runner.RawInputs.ShouldBeEmpty("a missing capability has no raw fallback");
    }

    [Test]
    [Arguments("stale-observation")]
    [Arguments("generation-mismatch")]
    [Arguments("missing")]
    [Arguments("exited")]
    [Arguments("unsupported")]
    [Arguments("unknown")]
    [Arguments("throws")]
    public async Task C0491_UncertainWriteLeavesTheRowPendingAndNeverRetries(string outcome)
    {
        using var workspace = new TempWorkspace();
        var fx = await PrepareAsync(workspace.Path, AgentTaskStatus.Working, AgentKind.Grok, SessionStatus.Running, false);
        if (outcome == "throws")
            fx.Runner.ThrowOnConditional = new HttpRequestException("conditional write failed");
        else
            fx.Runner.ConditionalOutcomeOverride = outcome;
        var requestId = Guid.NewGuid();

        var summary = await CreateService(fx).RefineAsync(
            fx.Task.Id, Message, CancellationToken.None, interruptCurrentTurn: true, requestId: requestId);

        var named = outcome == "throws" ? "unknown" : outcome;
        summary.InterruptWritten.ShouldBe($"not-confirmed:{named}", outcome);
        summary.Status.ShouldBe(AgentTaskStatus.Working);
        fx.Runner.ConditionalCalls.Count.ShouldBe(1);
        fx.Runner.KillCalls.ShouldBe(0);
        AssertNoKillOrBareEscape(fx.Runner);

        await using var verify = CreateContext();
        var row = await verify.SessionQueuedMessages.SingleAsync(m => m.AgentSessionId == fx.SessionId);
        row.Status.ShouldBe(QueuedMessageStatus.Pending);
        row.DeliveryAttempts.ShouldBe(0);
        (await verify.AgentTasks.SingleAsync(t => t.Id == fx.Task.Id)).Status.ShouldBe(AgentTaskStatus.Working);
    }

    [Test]
    [Arguments("after-written")]
    [Arguments("after-not-confirmed")]
    [Arguments("after-refused")]
    [Arguments("different-request-id")]
    public async Task C0491_ReplayOfTheSameRequestIsIdempotent(string arm)
    {
        using var workspace = new TempWorkspace();
        var kind = arm == "after-refused" ? AgentKind.ClaudeCode : AgentKind.Grok;
        var fx = await PrepareAsync(workspace.Path, AgentTaskStatus.Working, kind, SessionStatus.Running, false);
        if (arm == "after-not-confirmed")
            fx.Runner.ConditionalOutcomeOverride = "unknown";
        var service = CreateService(fx);
        var requestId = Guid.NewGuid();

        var first = await service.RefineAsync(
            fx.Task.Id, Message, CancellationToken.None, interruptCurrentTurn: true, requestId: requestId);
        var callsAfterFirst = fx.Runner.ConditionalCalls.Count;
        var secondId = arm == "different-request-id" ? Guid.NewGuid() : requestId;
        var second = await service.RefineAsync(
            fx.Task.Id, Message, CancellationToken.None, interruptCurrentTurn: true, requestId: secondId);

        AssertNoKillOrBareEscape(fx.Runner);
        await using var verify = CreateContext();
        var rows = await verify.SessionQueuedMessages.CountAsync(m => m.AgentSessionId == fx.SessionId);
        var events = await verify.AgentTaskEvents.CountAsync(
            e => e.AgentTaskId == fx.Task.Id && e.Type == AgentTaskEventType.Refined);
        if (arm == "different-request-id")
        {
            rows.ShouldBe(2);
            events.ShouldBe(2);
            fx.Runner.ConditionalCalls.Count.ShouldBe(2);
            return;
        }

        rows.ShouldBe(1);
        events.ShouldBe(1);
        fx.Runner.ConditionalCalls.Count.ShouldBe(callsAfterFirst);
        callsAfterFirst.ShouldBe(arm == "after-refused" ? 0 : 1);
        second.ShouldBe(first);
    }

    private static void AssertNoKillOrBareEscape(RecordingInterruptRunner runner)
    {
        runner.KillCalls.ShouldBe(0);
        foreach (var raw in runner.RawInputs)
        {
            raw.Input.ShouldNotBe("\x03");
            raw.Input.ShouldNotBe("\x1b");
        }
    }

    private static AgentTaskStatus TaskStatus(string flip) => flip switch
    {
        "queued-status" => AgentTaskStatus.Queued,
        "blocked-status" => AgentTaskStatus.Blocked,
        _ => AgentTaskStatus.Working,
    };

    private static AgentKind Kind(string flip) =>
        flip == "kind-claude" ? AgentKind.ClaudeCode : AgentKind.Grok;

    private static SessionStatus SessionStatusOf(string flip) =>
        flip == "session-stopping" ? SessionStatus.Stopping : SessionStatus.Running;

    private static string NamedRefusal(string flip) => flip switch
    {
        "setting-disabled" => "refused:disabled",
        "kind-claude" => "refused:kind-not-grok",
        "session-stopping" => "refused:session-not-running",
        "runner-missing" => "refused:runner-missing",
        "runner-exited" => "refused:runner-exited",
        "generation-mismatch" => "refused:generation-mismatch",
        "generation-unproven" => "refused:generation-unproven",
        "already-sent-at-boundary" => "already-sent",
        "open-question" => "refused:question-open",
        "composer-draft" => "refused:composer-not-empty",
        "composer-unreadable" => "refused:composer-unreadable",
        "conditional-unsupported" => "refused:conditional-input-unsupported",
        "queued-status" => "refused:task-status",
        _ => throw new ArgumentOutOfRangeException(nameof(flip), flip, "unmapped refusal"),
    };

    private static void ApplyRunnerFlip(Prepared fx, string flip)
    {
        switch (flip)
        {
            case "runner-missing":
                fx.Runner.ThrowOnGet = new NotFoundException(nameof(AgentSession), fx.SessionId);
                break;
            case "runner-exited":
                fx.Runner.SessionStatusText = "Exited";
                break;
            case "generation-mismatch":
                fx.Runner.AcceptedStartedAt = fx.Started.AddHours(-1);
                fx.Runner.SnapshotAcceptedStartedAt = fx.Started.AddHours(-1);
                break;
            case "generation-unproven":
                fx.Runner.AcceptedStartedAt = null;
                fx.Runner.SnapshotAcceptedStartedAt = null;
                break;
            case "already-sent-at-boundary":
                fx.Runner.NextEntries[fx.SessionId] = [TurnEnd(fx.SessionId)];
                break;
            case "composer-draft":
                fx.Runner.RenderedScreen = Draft(fx.Runner.RenderedScreen!);
                break;
            case "composer-unreadable":
                fx.Runner.ThrowOnSnapshot = true;
                break;
            case "conditional-unsupported":
                fx.Runner.AdvertiseConditional = false;
                break;
        }
    }

    private static string Draft(string screen)
    {
        const string needle = "\u2502 >";
        var index = screen.IndexOf(needle, StringComparison.Ordinal);
        index.ShouldBeGreaterThanOrEqualTo(0, "the fixture has a composer box row");
        return screen[..index] + needle + " GK-DRAFT" + screen[(index + needle.Length)..];
    }

    private static SessionRunnerTranscriptEvent TurnEnd(Guid sessionId) =>
        new(sessionId, 42, TranscriptKinds.TurnEnd, Guid.NewGuid().ToString("D"),
            null, DateTimeOffset.UtcNow, "system", null, null, null, null, null,
            TranscriptKinds.StopReasons.EndTurn);

    private static async Task<Prepared> PrepareAsync(
        string workingDirectory, AgentTaskStatus status, AgentKind kind, SessionStatus sessionStatus, bool openQuestion)
    {
        var runner = new RecordingInterruptRunner();
        var settings = new DelegationSettings();
        var scope = new ScopeFactory(runner);
        var (task, sessionId, started) = await SeedTaskAsync(workingDirectory, status, kind, sessionStatus);
        if (status != AgentTaskStatus.Queued)
            await SeedWorkingAsync(scope.Runtime, sessionId, openQuestion);
        runner.SessionId = sessionId;
        runner.AcceptedStartedAt = started;
        runner.SnapshotAcceptedStartedAt = started;
        runner.LastSequence = 41;
        runner.RenderedScreen = File.ReadAllText(Path.Combine(
            DelegateScriptRunner.RepoRoot, "tests", "Antiphon.Tests", "Fixtures", "grok-empty-composer.txt"));
        return new Prepared(scope, runner, settings, task, sessionId, started);
    }

    private static AgentTaskReplyService CreateService(Prepared fx) =>
        new AgentTaskReplyService(
            fx.Scope,
            Options.Create(fx.Settings),
            new MockEventBus(),
            TimeProvider.System,
            NullLogger<AgentTaskReplyService>.Instance);

    private static async Task<(AgentTask Task, Guid SessionId, DateTime Started)> SeedTaskAsync(
        string workingDirectory, AgentTaskStatus status, AgentKind kind, SessionStatus sessionStatus)
    {
        var sessionId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var id = Guid.NewGuid();
        var task = new AgentTask
        {
            Id = id,
            RootTaskId = id,
            Title = "Seeded delegate",
            Goal = "Do the thing.",
            Kind = AgentTaskKind.Worker,
            Role = AgentTaskRole.Docs,
            ModelLevel = AgentModelLevel.Medium,
            Workspace = WorkspaceMode.Shared,
            WorkingDirectory = workingDirectory,
            AgentSessionId = status == AgentTaskStatus.Queued ? null : sessionId,
            Status = status,
            CreatedAt = now,
            DispatchedAt = status == AgentTaskStatus.Queued ? null : now,
        };

        await using var db = CreateContext();
        db.AgentSessions.Add(new AgentSession
        {
            Id = sessionId,
            CardId = null,
            DefinitionName = "fake",
            AgentKind = kind,
            Status = sessionStatus,
            Cwd = workingDirectory,
            Cols = 120,
            Rows = 30,
            CreatedAt = now,
            StartedAt = now,
            LastSeenAt = now,
        });
        db.AgentTasks.Add(task);
        await db.SaveChangesAsync();
        var started = await db.AgentSessions.Where(s => s.Id == sessionId).Select(s => s.StartedAt).SingleAsync();
        return (task, sessionId, started);
    }

    private static async Task SeedWorkingAsync(AgentSessionRuntime runtime, Guid sessionId, bool openQuestion)
    {
        var toolName = openQuestion ? GrokQuestionTool.AskUserQuestionName : "run_terminal_command";
        var toolUseId = openQuestion ? "q-1" : "tool-1";
        var now = DateTimeOffset.UtcNow;
        await runtime.PersistTranscriptAsync(sessionId,
        [
            new SessionRunnerTranscriptEvent(
                sessionId, 40, TranscriptKinds.UserPrompt, Guid.NewGuid().ToString("D"),
                null, now, "user", "Do the thing.", null, null, null, null, null),
            new SessionRunnerTranscriptEvent(
                sessionId, 41, TranscriptKinds.ToolCall, Guid.NewGuid().ToString("D"),
                null, now, "assistant", null, toolName, "{}", toolUseId, false, null),
        ]);
    }

    private static AppDbContext CreateContext() => new(TestDbFixture.CreateDbContextOptions());

    private sealed record Prepared(
        ScopeFactory Scope,
        RecordingInterruptRunner Runner,
        DelegationSettings Settings,
        AgentTask Task,
        Guid SessionId,
        DateTime Started);

    private sealed class ScopeFactory : IServiceScopeFactory, IServiceScope, IServiceProvider
    {
        private readonly ServiceProvider _provider;
        public AgentSessionRuntime Runtime => _provider.GetRequiredService<AgentSessionRuntime>();

        public ScopeFactory(RecordingInterruptRunner runner)
        {
            var supervision = new SupervisionSettings();
            supervision.DeliveryVerification.Enabled = false;
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddDbContext<AppDbContext>(o => o.UseNpgsql(TestDbFixture.ConnectionString));
            services.AddSingleton<IEventBus, MockEventBus>();
            services.AddSingleton(Options.Create(supervision));
            services.AddSingleton(Options.Create(new ChannelBridgeSettings()));
            services.AddSingleton(Options.Create(new DelegationSettings()));
            services.AddSingleton(TimeProvider.System);
            services.AddSingleton<ISessionRunnerClient>(runner);
            services.AddSingleton<AgentSessionRuntime>();
            services.AddSingleton<SessionMessageQueueService>();
            services.AddSingleton<IDelegateSessionStopper>(new RecordingSessionStopper());
            services.AddSingleton<DelegationWorkspaceResolver>();
            services.AddScoped<AgentTaskService>();
            _provider = services.BuildServiceProvider();
        }

        public IServiceScope CreateScope() => this;
        public IServiceProvider ServiceProvider => _provider;
        public object? GetService(Type serviceType) => _provider.GetService(serviceType);
        public void Dispose() { }
    }

    private sealed class TempWorkspace : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory("antiphon-c0491-").FullName;
        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); }
            catch (IOException) { }
        }
    }
}
