using System.Collections.Concurrent;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Agents.SessionRunner;
using Antiphon.Tests.TestHelpers;
using Antiphon.SessionRunner.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[NotInParallel]
[ParallelLimiter<ProcessSpawnLimit>]
public class SessionRunnerEventPumpTests
{
    [Test]
    [Arguments("signalr-publish", false, false)]
    [Arguments("queue-confirm", false, false)]
    [Arguments("queue-confirm", true, false)]
    [Arguments("signalr-publish", false, true)]
    public async Task S0_blocked_hosted_pump_probe(string operation, bool reconnect, bool diagnosticsFail)
    {
        var runner = new ScriptedSessionRunnerClient();
        var bus = new GatedEventBus();
        var trace = new PhaseLogger { ThrowDiagnostics = diagnosticsFail };
        await using var h = await BridgeQueueHarness.CreateAsync(new()
        {
            ConfigureServices = services =>
            {
                services.AddSingleton<ISessionRunnerClient>(runner);
                services.AddSingleton<IEventBus>(bus);
                services.AddSingleton<AgentTaskReplyService>();
                services.AddDelegationWorktreeGraph(new GitSettings { WorktreeBasePath = Path.GetTempPath() });
                services.AddLogging(b => b.SetMinimumLevel(LogLevel.Debug).AddProvider(trace));
            },
        });
        var second = Guid.NewGuid();
        var taskId = Guid.NewGuid();
        var owner = Guid.NewGuid();
        await using (var db = BridgeQueueHarness.CreateContext())
        {
            db.AgentSessions.Add(new AgentSession { Id = second, Cwd = h.TempRoot, DefinitionName = "fake",
                Status = SessionStatus.Running, AgentKind = AgentKind.ClaudeCode, CreatedAt = DateTime.UtcNow });
            db.Agents.Add(new Agent { Id = owner, Name = "s0-" + owner, Slug = "s0-" + owner,
                WorkingDirectory = h.TempRoot, PersistentSessionId = second.ToString("D"), AlwaysOn = true,
                CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });
            db.AgentTasks.Add(new AgentTask { Id = taskId, RootTaskId = taskId, AgentId = owner,
                AgentSessionId = second, ReplyTo = AgentTaskReplyTo.None, Role = AgentTaskRole.Docs,
                Status = AgentTaskStatus.Dispatched, Workspace = WorkspaceMode.Shared,
                WorkingDirectory = h.TempRoot, Title = "S0 synthetic", Goal = "S0 synthetic",
                CreatedAt = DateTime.UtcNow, DispatchedAt = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }
        await h.InsertTranscriptEntryAsync(TranscriptKinds.UserPrompt, "prior prompt");
        await h.InsertTranscriptEntryAsync(TranscriptKinds.TurnEnd, stopReason: "end_turn");
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (operation == "signalr-publish")
        {
            bus.Group = AgentSessionGroups.Session(h.SessionId);
            bus.Entered = entered;
            bus.Release = release;
        }
        else
        {
            h.Adapter.OnSubmitted = async body =>
            {
                entered.TrySetResult();
                await release.Task.WaitAsync(TimeSpan.FromSeconds(15));
                await h.InsertTranscriptEntryAsync(TranscriptKinds.UserPrompt, body, timestamp: DateTime.UtcNow);
                await h.InsertTranscriptEntryAsync(TranscriptKinds.TurnEnd, stopReason: "end_turn");
            };
            await h.Queue.EnqueueAsync(h.SessionId, "synthetic complete queued body", MessageSendMode.WhenIdle,
                CancellationToken.None, deliverIfIdle: false);
        }
        var a = Entry(h.SessionId, 100, TranscriptKinds.TurnEnd, null);
        var b = Entry(second, 1, TranscriptKinds.UserPrompt, DelegationReportFormatter.TaskMarker(taskId) + " synthetic brief");
        var bText = Entry(second, 2, TranscriptKinds.AssistantText, "Synthetic evidence saved and verified.\n" + DelegationReportFormatter.ReportToken(taskId, "done"));
        var bEnd = Entry(second, 3, TranscriptKinds.TurnEnd, null);
        bText = bText with { ApiCallId = "synthetic-b" };
        bEnd = bEnd with { ApiCallId = "synthetic-b" };
        if (reconnect)
        {
            runner.Sessions = [new(h.SessionId, null, DateTime.UtcNow, "Running", null, AgentExitReason.Unknown, 100),
                new(second, null, DateTime.UtcNow, "Running", null, AgentExitReason.Unknown, 3)];
            runner.SetTranscript(new(h.SessionId, [a], 100));
            runner.SetTranscript(new(second, [b, bText, bEnd], 3));
        }
        using var pump = new SessionRunnerEventPump(h.Provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new SessionRunnerSettings { Enabled = true }), trace);
        var persistedWhileBlocked = false;
        try
        {
            await pump.StartAsync(CancellationToken.None);
            if (!reconnect)
            {
                await runner.Streaming.Task.WaitAsync(TimeSpan.FromSeconds(5));
                runner.Produce(a);
            }
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            if (!reconnect) { runner.Produce(b); runner.Produce(bText); runner.Produce(bEnd); }
            try { await SpecialistTaskRunnerDeadlineTests.UntilAsync(async () =>
            {
                await using var db = BridgeQueueHarness.CreateContext();
                return persistedWhileBlocked = await db.TranscriptEntries.AnyAsync(e => e.Uuid == b.Uuid);
            }); }
            catch (OperationCanceledException) { }
            release.Task.IsCompleted.ShouldBeFalse();
            if (!reconnect) runner.Received.ShouldNotContain(second);
        }
        finally
        {
            release.TrySetResult();
            await SpecialistTaskRunnerDeadlineTests.UntilAsync(async () =>
            {
                await using var db = BridgeQueueHarness.CreateContext();
                return await db.TranscriptEntries.AnyAsync(e => e.Uuid == bEnd.Uuid);
            });
            await SpecialistTaskRunnerDeadlineTests.UntilAsync(async () =>
            {
                await using var db = BridgeQueueHarness.CreateContext();
                return await db.AgentTasks.AnyAsync(t => t.Id == taskId && t.Result != null);
            });
            await pump.StopAsync(CancellationToken.None);
            var directory = Path.Combine("TestOutput", "card-0432", "s0");
            Directory.CreateDirectory(directory);
            await File.WriteAllLinesAsync(Path.Combine(directory, $"{operation}-{reconnect}-{diagnosticsFail}.log"),
                trace.Lines.Append($"B={second} uuid={b.Uuid} persistedWhileBlocked={persistedWhileBlocked}"));
        }
        await using var settledDb = BridgeQueueHarness.CreateContext();
        (await settledDb.AgentTasks.SingleAsync(t => t.Id == taskId)).Result.ShouldNotBeNull(
            string.Join("\n", trace.Lines.Where(s => s.Contains("settle") || s.Contains("Exception"))));
        trace.Lines.ShouldContain(s => s.Contains(operation == "signalr-publish"
            ? "transcript.signalr-publish start" : "queue.delivery-confirm start"));
        persistedWhileBlocked.ShouldBeFalse($"B Uuid {b.Uuid} missing while {operation} is held; original inline pump reproduction");
    }

    [Test]
    public async Task C502_V36_pump_forwards_the_typed_exit_and_publishes_nothing_for_a_stale_disposition()
    {
        var runner = new ScriptedSessionRunnerClient();
        var bus = new GatedEventBus();
        await using var h = await BridgeQueueHarness.CreateAsync(new()
        {
            ConfigureServices = services =>
            {
                services.AddSingleton<ISessionRunnerClient>(runner);
                services.AddSingleton<IEventBus>(bus);
            },
        });
        var generationA = SessionGeneration.Normalize(DateTime.UtcNow.AddMinutes(-5));
        var generationB = SessionGeneration.Next(generationA, DateTime.UtcNow);
        await using (var db = BridgeQueueHarness.CreateContext())
        {
            await db.AgentSessions.Where(s => s.Id == h.SessionId).ExecuteUpdateAsync(u => u
                .SetProperty(s => s.StartedAt, generationB)
                .SetProperty(s => s.Status, SessionStatus.Running));
        }

        using var pump = new SessionRunnerEventPump(
            h.Provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new SessionRunnerSettings { Enabled = true }),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<SessionRunnerEventPump>.Instance);
        await pump.StartAsync(CancellationToken.None);
        await runner.Streaming.Task.WaitAsync(TimeSpan.FromSeconds(5));
        runner.ProduceExit(new SessionRunnerExitedEvent(
            h.SessionId, 1, AgentExitReason.KilledByRequest, 0, generationA));
        await Task.Delay(500);
        await pump.StopAsync(CancellationToken.None);

        await using var verify = BridgeQueueHarness.CreateContext();
        (await verify.AgentSessions.SingleAsync(s => s.Id == h.SessionId)).Status.ShouldBe(SessionStatus.Running);
    }

    private static SessionRunnerTranscriptEvent Entry(Guid session, long sequence, string kind, string? text) =>
        new(session, sequence, kind, Guid.NewGuid().ToString("N"), null, DateTimeOffset.UtcNow,
            null, text, null, null, null, null, kind == TranscriptKinds.TurnEnd ? "end_turn" : null);

    private sealed class GatedEventBus : IEventBus
    {
        public string? Group;
        public TaskCompletionSource? Entered;
        public TaskCompletionSource? Release;
        public async Task PublishToGroupAsync(string group, string eventName, object payload, CancellationToken ct = default)
        {
            if (group == Group && eventName == "SessionTranscript")
            { Entered!.TrySetResult(); await Release!.Task.WaitAsync(ct); }
        }
        public Task PublishToAllAsync(string eventName, object payload, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class PhaseLogger : ILoggerProvider, ILogger<SessionRunnerEventPump>
    {
        public bool ThrowDiagnostics;
        public ConcurrentQueue<string> Lines { get; } = new();
        public ILogger CreateLogger(string categoryName) => this;
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var text = formatter(state, exception);
            if (text.StartsWith("Runtime phase") || level >= LogLevel.Warning)
                Lines.Enqueue(text + (exception is null ? "" : " " + exception));
            if (ThrowDiagnostics && text.StartsWith("Runtime phase")) throw new IOException("synthetic diagnostics-only sink failure");
        }
        public void Dispose() { }
    }
}
