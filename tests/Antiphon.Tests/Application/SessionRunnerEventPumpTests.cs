using System.Collections.Concurrent;
using System.Data.Common;
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
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Antiphon.Server.Infrastructure.Data;
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
        await using var store = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var h = await ClaudeDialogFixture.CreateBridgeAsync(new()
        {
            ConnectionString = store.ConnectionString,
            StartRuntimeActions = true,
            ConfigureServices = services =>
            {
                AddStateStore(services);
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
        await using (var db = h.CreateObserverContext())
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
                await using var db = h.CreateObserverContext();
                var rows = await db.TranscriptEntries.Where(e => e.AgentSessionId == second)
                    .OrderBy(e => e.Sequence).Select(e => e.Uuid).ToListAsync();
                return persistedWhileBlocked = rows.SequenceEqual(new[] { b.Uuid, bText.Uuid, bEnd.Uuid })
                    && await db.AgentTasks.AnyAsync(t => t.Id == taskId && t.Result != null);
            }); }
            catch (OperationCanceledException) { }
            release.Task.IsCompleted.ShouldBeFalse();
            persistedWhileBlocked.ShouldBeTrue("B's ordered transcript and marked task must progress before A's effect is released");
            if (!reconnect) runner.Received.ShouldContain(second);
        }
        finally
        {
            release.TrySetResult();
            await SpecialistTaskRunnerDeadlineTests.UntilAsync(async () =>
            {
                await using var db = h.CreateObserverContext();
                return await db.TranscriptEntries.AnyAsync(e => e.Uuid == bEnd.Uuid);
            });
            await SpecialistTaskRunnerDeadlineTests.UntilAsync(async () =>
            {
                await using var db = h.CreateObserverContext();
                return await db.AgentTasks.AnyAsync(t => t.Id == taskId && t.Result != null);
            });
            await pump.StopAsync(CancellationToken.None);
            var directory = Path.Combine("TestOutput", "card-0432", "s0");
            Directory.CreateDirectory(directory);
            await File.WriteAllLinesAsync(Path.Combine(directory, $"{operation}-{reconnect}-{diagnosticsFail}.log"),
                trace.Lines.Append($"B={second} uuid={b.Uuid} persistedWhileBlocked={persistedWhileBlocked}"));
        }
        await using var settledDb = h.CreateObserverContext();
        (await settledDb.AgentTasks.SingleAsync(t => t.Id == taskId)).Result.ShouldNotBeNull(
            string.Join("\n", trace.Lines.Where(s => s.Contains("settle") || s.Contains("Exception"))));
        trace.Lines.ShouldContain(s => s.Contains(operation == "signalr-publish"
            ? "transcript.signalr-publish start" : "queue.delivery-confirm start"));
        persistedWhileBlocked.ShouldBeTrue($"B Uuid {b.Uuid} must persist while {operation} is held");
    }

    private static void AddStateStore(IServiceCollection services)
    {
        services.AddSingleton(Options.Create(new SessionStateSettings()));
        services.AddSingleton<SessionStateCommandMetrics>();
        services.AddSingleton<ISessionStateLoader, SessionStateLoader>();
        services.AddSingleton<SessionStateStore>();
    }

    [Test]
    [Arguments("live")]
    [Arguments("startup")]
    [Arguments("subscription-ready")]
    public async Task C1259_confirming_prompt_and_other_session_progress_before_confirmation(string ingress)
    {
        await using var store = await TestDbFixture.CreateIsolatedSchemaAsync();
        var runner = new ScriptedSessionRunnerClient();
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 15, 12, 0, 0, TimeSpan.Zero));
        var read = new ConfirmReadBarrier();
        await using var h = await ClaudeDialogFixture.CreateBridgeAsync(new()
        {
            ConnectionString = store.ConnectionString,
            StartRuntimeActions = true,
            TimeProvider = clock,
            ConfigureDbContext = options => options.AddInterceptors(read),
            ConfigureDeliveryVerification = v =>
            {
                v.TranscriptConfirmTimeoutSeconds = 30;
                v.ReEnterIntervalSeconds = 7;
                v.PollIntervalMs = 250;
            },
            ConfigureServices = services =>
            {
                AddStateStore(services);
                services.AddSingleton<ISessionRunnerClient>(runner);
            }
        });
        read.SessionId = h.SessionId;
        await h.InsertTranscriptEntryAsync(TranscriptKinds.UserPrompt, "prior prompt");
        await h.InsertTranscriptEntryAsync(TranscriptKinds.TurnEnd, stopReason: "end_turn");
        var state = h.Provider.GetRequiredService<SessionStateStore>();
        var baseline = await state.ReadAsync(h.SessionId, CancellationToken.None);
        baseline.LastSequence.ShouldBe(2);
        baseline.Working.ShouldBeFalse();
        var second = Guid.NewGuid();
        await using (var db = h.CreateObserverContext())
        {
            db.AgentSessions.Add(new AgentSession { Id = second, Cwd = h.TempRoot, DefinitionName = "fake",
                Status = SessionStatus.Running, AgentKind = AgentKind.ClaudeCode, CreatedAt = h.Now });
            await db.SaveChangesAsync();
        }
        const string body = "C1259 complete ordinary first line\nC1259 distinctive second line";
        var queued = await h.Queue.EnqueueAsync(h.SessionId, body, MessageSendMode.WhenIdle,
            CancellationToken.None, deliverIfIdle: false);
        var queueId = queued.Messages.Single().Id;
        var boundary = Entry(h.SessionId, 3, TranscriptKinds.TurnEnd, null) with { Timestamp = clock.GetUtcNow() };
        var confirming = Entry(h.SessionId, 4, TranscriptKinds.UserPrompt, body) with { Timestamp = clock.GetUtcNow() };
        var b = new[] { Entry(second, 1, TranscriptKinds.UserPrompt, "other prompt"),
            Entry(second, 2, TranscriptKinds.AssistantText, "other answer"),
            Entry(second, 3, TranscriptKinds.TurnEnd, null) };
        var submitted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        h.Adapter.OnSubmitted = actual =>
        {
            actual.ShouldBe(body);
            read.Arm();
            runner.Produce(confirming);
            submitted.TrySetResult();
            return Task.CompletedTask;
        };
        void Snapshots()
        {
            runner.Sessions = [new(h.SessionId, null, h.Now, "Running", null, AgentExitReason.Unknown, 3),
                new(second, null, h.Now, "Running", null, AgentExitReason.Unknown, 3)];
            runner.SetTranscript(new(h.SessionId, [boundary], 3));
            runner.SetTranscript(new(second, b, 3));
        }
        if (ingress == "startup") Snapshots();
        using var pump = new SessionRunnerEventPump(h.Provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new SessionRunnerSettings { Enabled = true }),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<SessionRunnerEventPump>.Instance, clock);
        try
        {
            await pump.StartAsync(CancellationToken.None);
            if (ingress != "startup")
            {
                await runner.Streaming.Task.WaitAsync(TimeSpan.FromSeconds(5));
                if (ingress == "subscription-ready")
                {
                    Snapshots();
                    runner.ProduceSubscriptionReady(b[2]);
                }
                else
                {
                    runner.Produce(boundary);
                    foreach (var row in b) runner.Produce(row);
                }
            }
            // Drive only pre-submit composer settling. Confirmation time stays below its first retry.
            for (var i = 0; i < 100 && !submitted.Task.IsCompleted; i++)
            {
                clock.Advance(TimeSpan.FromMilliseconds(50));
                await Task.Delay(20);
            }
            await submitted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await read.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            read.Hits.ShouldBe(1);
            read.Baseline.ShouldBe(3);
            read.CommandId.ShouldNotBe(Guid.Empty);
            var persisted = false;
            using (var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(3)))
            {
                try
                {
                    while (!persisted)
                    {
                        await using var db = h.CreateObserverContext();
                        var rows = await db.TranscriptEntries.Where(e => e.AgentSessionId == second)
                            .OrderBy(e => e.Sequence).Select(e => e.Uuid).ToListAsync(deadline.Token);
                        persisted = rows.SequenceEqual(b.Select(e => e.Uuid))
                            && await db.TranscriptEntries.AnyAsync(e => e.AgentSessionId == h.SessionId
                                && e.Uuid == confirming.Uuid && e.Text == body, deadline.Token);
                        if (!persisted) await Task.Delay(10, deadline.Token);
                    }
                }
                catch (OperationCanceledException) when (deadline.IsCancellationRequested) { }
            }
            read.Release.Task.IsCompleted.ShouldBeFalse();
            persisted.ShouldBeTrue("A's complete prompt and B's ordered rows must commit while the real confirmation read is held");
            (await state.ReadAsync(h.SessionId, CancellationToken.None)).LastSequence.ShouldBe(4);
            (await state.ReadAsync(second, CancellationToken.None)).LastSequence.ShouldBe(3);
            await using (var db = h.CreateObserverContext())
                (await db.SessionQueuedMessages.SingleAsync(m => m.Id == queueId)).DeliveryVerdict.ShouldBeNull();
            h.Adapter.SubmittedBodies.ShouldBe(new[] { body });
            h.Adapter.Inputs.Count(i => i == "\r").ShouldBe(1);
            h.Adapter.Inputs.Count(i => i.Contains(body, StringComparison.Ordinal)).ShouldBe(1);
            h.Adapter.Inputs.ShouldContain("\u001b[200~" + body + "\u001b[201~");
            read.Release.TrySetResult();
            await SpecialistTaskRunnerDeadlineTests.UntilAsync(async () =>
            {
                await using var db = h.CreateObserverContext();
                return await db.SessionQueuedMessages.AnyAsync(m => m.Id == queueId
                    && m.DeliveryVerdict == DeliveryVerdict.Delivered);
            });
            await using (var db = h.CreateObserverContext())
            {
                var message = await db.SessionQueuedMessages.SingleAsync(m => m.Id == queueId);
                message.DeliveryAttempts.ShouldBe(1);
                message.LastDeliveryBaselineSequence.ShouldBe(3);
                (await db.TranscriptEntries.CountAsync(e => e.AgentSessionId == h.SessionId
                    && e.Sequence > 3 && e.Kind == TranscriptKinds.UserPrompt && e.Text == body)).ShouldBe(1);
            }
            runner.SnapshotReads.Count.ShouldBe(ingress == "live" ? 0 : 2);
            h.Adapter.Inputs.Count(i => i == "\r").ShouldBe(1);
            read.Hits.ShouldBe(1);
        }
        finally
        {
            read.Release.TrySetResult();
            await pump.StopAsync(CancellationToken.None);
        }
    }

    private sealed class ConfirmReadBarrier : DbCommandInterceptor
    {
        private int _armed;
        private int _hits;
        public Guid SessionId { get; set; }
        public Guid CommandId { get; private set; }
        public long Baseline { get; private set; }
        public int Hits => Volatile.Read(ref _hits);
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void Arm() => Volatile.Write(ref _armed, 1);
        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (Volatile.Read(ref _armed) != 1 || !command.CommandText.StartsWith("SELECT t.\"Text\"", StringComparison.Ordinal)
                || !command.CommandText.Contains("\"Sequence\" >", StringComparison.Ordinal)
                || !command.CommandText.Contains("'UserPrompt'", StringComparison.Ordinal)
                || !command.Parameters.Cast<DbParameter>().Any(p => p.Value is Guid id && id == SessionId)
                || Interlocked.CompareExchange(ref _armed, 2, 1) != 1)
                return result;
            CommandId = eventData.CommandId;
            Baseline = Convert.ToInt64(command.Parameters.Cast<DbParameter>()
                .Single(p => p.ParameterName.Contains("baselineSequence", StringComparison.Ordinal)).Value);
            Interlocked.Increment(ref _hits);
            Entered.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
            return result;
        }
    }

    [Test]
    public async Task C502_V36_pump_forwards_the_typed_exit_and_publishes_nothing_for_a_stale_disposition()
    {
        var runner = new ScriptedSessionRunnerClient();
        var bus = new GatedEventBus();
        await using var h = await ClaudeDialogFixture.CreateBridgeAsync(new()
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

        // Agent creation notifications belong to fixture setup. Measure the pump and
        // stale exit from a clean receiver, preserving the zero-notification oracle.
        bus.Events.Clear();
        using var pump = new SessionRunnerEventPump(
            h.Provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new SessionRunnerSettings { Enabled = true }),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<SessionRunnerEventPump>.Instance);
        await pump.StartAsync(CancellationToken.None);
        await runner.Streaming.Task.WaitAsync(TimeSpan.FromSeconds(5));
        runner.ProduceExit(new SessionRunnerExitedEvent(
            h.SessionId, 1, AgentExitReason.KilledByRequest, 0, generationA));
        await runner.ExitConsumed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await pump.StopAsync(CancellationToken.None);

        await using var verify = BridgeQueueHarness.CreateContext();
        (await verify.AgentSessions.SingleAsync(s => s.Id == h.SessionId)).Status.ShouldBe(SessionStatus.Running);
        bus.Events.ShouldBeEmpty("a consumed stale exit has no exit or status notification authority");
    }

    private static SessionRunnerTranscriptEvent Entry(Guid session, long sequence, string kind, string? text) =>
        new(session, sequence, kind, Guid.NewGuid().ToString("N"), null, DateTimeOffset.UtcNow,
            null, text, null, null, null, null, kind == TranscriptKinds.TurnEnd ? "end_turn" : null);

    private sealed class GatedEventBus : IEventBus
    {
        public ConcurrentQueue<string> Events { get; } = new();
        public string? Group;
        public TaskCompletionSource? Entered;
        public TaskCompletionSource? Release;
        public async Task PublishToGroupAsync(string group, string eventName, object payload, CancellationToken ct = default)
        {
            Events.Enqueue(eventName);
            if (group == Group && eventName == "SessionTranscript")
            { Entered!.TrySetResult(); await Release!.Task.WaitAsync(ct); }
        }
        public Task PublishToAllAsync(string eventName, object payload, CancellationToken ct = default)
        { Events.Enqueue(eventName); return Task.CompletedTask; }
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
