using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Orchestration;
using Antiphon.Tests.Agents;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[NotInParallel]
[ParallelLimiter<ProcessSpawnLimit>]
public class OutputDistillationProducerTests
{
    [Test]
    [Arguments("full")]
    [Arguments("closed")]
    [Arguments("missing")]
    public async Task Full_missing_and_closed_worker_keep_raw_fallback(string scenario)
    {
        using var scratch = new TempWorkspace();
        await using var provider = AgentTaskSettlementRaceTests.BuildHarness(s =>
        {
            s.AddSingleton(Options.Create(new DelegationSettings { OutputDistillerEnabled = true,
                OutputDistillerMode = OutputDistillerMode.Apply, OutputDistillerWorkingDirectory = scratch.Path,
                ReplyInlineMaxChars = 20_000 }));
            if (scenario != "missing") s.AddSingleton<OutputDistillationQueue>();
            s.AddScoped<OutputDistillerProvisioner>();
            s.AddScoped<OutputDistillationService>();
            s.AddSingleton<CompletionNoteFlushQueue>();
        });
        var queue = provider.GetService<OutputDistillationQueue>();
        if (scenario == "closed") queue!.Complete();
        if (scenario == "full")
            for (var n = 0; n < 3; n++) queue!.TryEnqueue(new(Guid.NewGuid(), null,
                DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddSeconds(45), OutputDistillerMode.Apply)).ShouldBeTrue();
        var parent = await AgentTaskSettlementRaceTests.SeedSessionAsync(scratch.Path);
        var (task, session) = await AgentTaskSettlementRaceTests.SeedSharedTaskAsync(scratch.Path, parent);
        var report = new string('z', 1400);
        await AgentTaskSettlementRaceTests.SeedMarkedTurnAsync(session, task.Id, report);
        await provider.GetRequiredService<AgentTaskReplyService>().OnTurnEndAsync(session, CancellationToken.None);
        await using var db = BridgeQueueHarness.CreateContext();
        var note = (await db.SessionQueuedMessages.Where(m => m.SourceTaskId == task.Id).ToListAsync()).ShouldHaveSingleItem();
        note.Body.ShouldContain(report);
        note.HoldUntil.ShouldBeNull();
        note.DeliveryAttempts.ShouldBe(0);
        var ledger = (await db.OutputDistillations.Where(r => r.TaskId == task.Id).ToListAsync()).ShouldHaveSingleItem();
        ledger.Outcome.ShouldBe(DistillationOutcome.DegradedBusy);
        ledger.Reason.ShouldBe(scenario == "full" ? "queue-full" : "worker-unavailable");
        ledger.DistillTaskId.ShouldBeNull();
    }

    [Test]
    [Arguments(OutputDistillerMode.Shadow, true)]
    [Arguments(OutputDistillerMode.Apply, true)]
    [Arguments(OutputDistillerMode.Shadow, false)]
    public async Task Idle_parent_delivery_does_not_delay_admission(OutputDistillerMode mode, bool enabled)
    {
        using var scratch = new TempWorkspace();
        var settings = new DelegationSettings { OutputDistillerEnabled = enabled, OutputDistillerMode = mode,
            ReplyInlineMaxChars = 20_000, MaxConcurrentTasks = 512 };
        var clock = new ParentClock();
        await using var provider = AgentTaskSettlementRaceTests.BuildHarness(s =>
        {
            s.AddSingleton<TimeProvider>(clock);
            s.AddSingleton(Options.Create(settings));
            s.AddSingleton<OutputDistillationQueue>();
            s.AddSingleton<CompletionNoteFlushQueue>();
            s.AddSingleton<SpecialistFailureQueue>();
        });
        var parent = await AgentTaskSettlementRaceTests.SeedSessionAsync(scratch.Path);
        var (task, session) = await AgentTaskSettlementRaceTests.SeedSharedTaskAsync(scratch.Path, parent);
        await AgentTaskSettlementRaceTests.SeedMarkedTurnAsync(session, task.Id, new string('z', 1_400));
        var adapter = new FakeAgentProtocolAdapter();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        adapter.OnSubmitted = async body =>
        {
            entered.TrySetResult();
            await release.Task.WaitAsync(TimeSpan.FromSeconds(15));
            await BridgeQueueHarness.InsertEntryAsync(parent, Antiphon.SessionRunner.Contracts.TranscriptKinds.UserPrompt,
                body, timestamp: clock.GetUtcNow().UtcDateTime);
        };
        provider.GetRequiredService<AgentSessionRuntime>().Register(parent, adapter);
        using var worker = new CompletionNoteWorkHostedService(provider.GetRequiredService<IServiceScopeFactory>(),
            provider.GetRequiredService<CompletionNoteFlushQueue>(), provider.GetRequiredService<SpecialistFailureQueue>(),
            clock, NullLogger<CompletionNoteWorkHostedService>.Instance);
        Task? settle = null;
        try
        {
            // Producer must finish before the hosted owner is started. An inline send hangs here.
            settle = provider.GetRequiredService<AgentTaskReplyService>().OnTurnEndAsync(session, CancellationToken.None);
            await Task.WhenAny(settle, entered.Task).WaitAsync(TimeSpan.FromSeconds(5));
            settle.IsCompletedSuccessfully.ShouldBeTrue("admission must complete while inline parent verification is still held");
            await using var db = BridgeQueueHarness.CreateContext();
            var note = (await db.SessionQueuedMessages.Where(m => m.SourceTaskId == task.Id).ToListAsync()).ShouldHaveSingleItem();
            var requests = provider.GetRequiredService<OutputDistillationQueue>();
            requests.PendingCount.ShouldBe(enabled ? 1 : 0);
            if (enabled)
            {
                requests.TryDequeue(out var request).ShouldBeTrue();
                request.Mode.ShouldBe(mode);
                request.QueuedMessageId.ShouldBe(note.Id);
                request.DeadlineAt.ShouldBe(request.RequestedAt.AddSeconds(45));
                if (mode == OutputDistillerMode.Apply)
                    note.HoldUntil!.Value.ShouldBe(request.DeadlineAt.UtcDateTime, TimeSpan.FromMicroseconds(1));
                else note.HoldUntil.ShouldBeNull();
            }
            else note.HoldUntil.ShouldBeNull();
            await provider.GetRequiredService<AgentTaskReplyService>().OnTurnEndAsync(session, CancellationToken.None);
            (await db.SessionQueuedMessages.CountAsync(m => m.SourceTaskId == task.Id)).ShouldBe(1);
            requests.PendingCount.ShouldBe(0);
            await worker.StartAsync(CancellationToken.None);
            if (mode == OutputDistillerMode.Apply)
                clock.Offset = TimeSpan.FromSeconds(45);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            release.Task.IsCompleted.ShouldBeFalse();
            settle.IsCompletedSuccessfully.ShouldBeTrue();
        }
        finally
        {
            release.TrySetResult();
            if (settle is not null) await settle.WaitAsync(TimeSpan.FromSeconds(5));
            try
            {
                await SpecialistTaskRunnerDeadlineTests.UntilAsync(async () =>
                {
                    await using var db = BridgeQueueHarness.CreateContext();
                    return await db.SessionQueuedMessages.AnyAsync(m => m.SourceTaskId == task.Id
                        && m.Status == QueuedMessageStatus.Sent && m.DeliveryVerdict == DeliveryVerdict.Delivered);
                });
            }
            finally { await worker.StopAsync(CancellationToken.None); }
        }
        adapter.Inputs.Count(i => i == "\r").ShouldBe(1);
        adapter.KillCount.ShouldBe(0);
    }

    private sealed class TempWorkspace : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory("antiphon-c432-producer").FullName;
        public void Dispose() => Directory.Delete(Path, recursive: true);
    }

    private sealed class ParentClock : TimeProvider
    {
        public TimeSpan Offset;
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.UtcNow + Offset;
    }
}
