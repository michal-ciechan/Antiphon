using System.Data.Common;
using System.Net;
using System.Text;
using System.Text.Json;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Agents.SessionRunner;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[Category("Slow")]
public class WorkingTranscriptRecoveryTests
{
    [Test, Arguments(true), Arguments(false)]
    public async Task C1205_Complete_idle_snapshot_recovers_once(bool cache)
    {
        await using var h = await Harness.CreateAsync(cache);
        var result = await h.ProbeAsync();
        result.Outcome.ToString().ShouldBe("RecoveredIdle");
        result.AddedCount.ShouldBe(1);
        (await h.RowsAsync()).Count(r => r.Uuid == "end").ShouldBe(1);
        (await h.WorkingAsync()).ShouldBeFalse();
        if (cache) (await h.F.Store.ReadAsync(h.Id, default)).Working.ShouldBeFalse();
        var repeat = await h.ProbeAsync();
        repeat.Outcome.ToString().ShouldBe("RecoveredIdle");
        repeat.AddedCount.ShouldBe(0);
        (await h.RowsAsync()).Count(r => r.Uuid == "end").ShouldBe(1);
    }

    [Test]
    [Arguments("pull-fault"), Arguments("persist-fault"), Arguments("state-read-fault")]
    [Arguments("empty"), Arguments("wrong-generation"), Arguments("missing-generation")]
    [Arguments("wrong-session"), Arguments("mixed-entry-session"), Arguments("missing-frontier")]
    [Arguments("null-uuid-frontier-mismatch"), Arguments("retained-failure"), Arguments("needs-reload")]
    [Arguments("absent-store"), Arguments("canceled-pull"), Arguments("truncated-tail")]
    public async Task C1205_Uncertain_probe_does_not_certify_idle(string variant)
    {
        var fault = new Faults();
        await using var h = await Harness.CreateAsync(true, fault, variant == "null-uuid-frontier-mismatch");
        switch (variant)
        {
            case "pull-fault": h.Handler.Fail = true; break;
            case "persist-fault": fault.SaveFailure = true; break;
            case "state-read-fault": fault.ReadFailure = true; break;
            case "empty": h.Snapshot = h.Snapshot with { Entries = [], LastSequence = 0 }; break;
            case "wrong-generation": h.Snapshot = h.Snapshot with { AcceptedStartedAt = h.Generation.AddSeconds(1) }; break;
            case "missing-generation": h.Snapshot = h.Snapshot with { AcceptedStartedAt = null }; break;
            case "wrong-session": h.Snapshot = h.Snapshot with { SessionId = Guid.NewGuid() }; break;
            case "mixed-entry-session": h.SetEntries(h.Prompt, h.End with { SessionId = Guid.NewGuid() }); break;
            case "missing-frontier": h.SetEntries(h.End with { Sequence = 99 }); break;
            case "null-uuid-frontier-mismatch": h.SetEntries(h.Prompt with { Text = "different" }, h.End); break;
            case "retained-failure": fault.RejectUuid = "poison"; h.SetEntries(h.Prompt,
                h.F.Event(2, "ToolResult", 1, "bad", "poison"), h.End with { Sequence = 3 }); break;
            case "needs-reload": fault.Collision = true; break;
            case "absent-store": h.ReplaceRuntime(null); break;
            case "canceled-pull": h.Handler.Cancel = true; break;
            case "truncated-tail": h.Snapshot = h.Snapshot with { LastSequence = 99 }; break;
        }
        var result = await h.ProbeAsync();
        result.Outcome.ToString().ShouldBe("Unknown", variant);
        var rows = await h.RowsAsync();
        if (variant is "retained-failure" or "needs-reload")
        {
            (await h.WorkingAsync()).ShouldBeFalse("partial durable idle is insufficient certification");
            if (variant == "retained-failure")
            {
                result.AddedCount.ShouldBe(2);
                h.Runtime.TryGetTranscriptPersistFailure(h.Id, out _).ShouldBeTrue();
            }
            else
            {
                h.Runtime.TryGetTranscriptPersistFailure(h.Id, out _).ShouldBeFalse();
                fault.Hits.ShouldBe(1);
            }
        }
        else if (variant == "state-read-fault")
        {
            rows.Count(r => r.Uuid == "end").ShouldBe(1);
            result.AddedCount.ShouldBe(1);
            fault.Hits.ShouldBe(1);
        }
        else rows.Count(r => r.Uuid == "end").ShouldBe(0, variant);
        if (variant is "pull-fault" or "canceled-pull") h.Handler.Hits.ShouldBe(1);
        if (variant is "persist-fault" or "state-read-fault" or "retained-failure") fault.Hits.ShouldBeGreaterThan(0);
    }

    [Test, Arguments(true), Arguments(false)]
    public async Task C1205_Newer_runner_activity_keeps_working(bool cache)
    {
        await using var h = await Harness.CreateAsync(cache);
        var newer = h.F.Event(3, "UserPrompt", 3, "new turn", "new");
        await h.Runtime.PersistTranscriptAsync(h.Id, [newer]);
        h.SetEntries(h.Prompt, h.End, newer);
        var result = await h.ProbeAsync();
        result.Outcome.ToString().ShouldBe("CheckedWorking");
        result.AddedCount.ShouldBe(0);
        (await h.RowsAsync()).Count(r => r.Uuid == "end").ShouldBe(0);
        (await h.WorkingAsync()).ShouldBeTrue();
    }

    [Test]
    [Arguments("new-prompt", true), Arguments("new-prompt", false)]
    [Arguments("queue-annotation", true), Arguments("queue-annotation", false)]
    [Arguments("generation", true), Arguments("generation", false)]
    [Arguments("remote-ownership", true), Arguments("remote-ownership", false)]
    [Arguments("terminal-status", true), Arguments("terminal-status", false)]
    [Arguments("replace-frontier", true), Arguments("replace-frontier", false)]
    public async Task C1205_Concurrent_commit_invalidates_snapshot(string variant, bool cache)
    {
        await using var h = await Harness.CreateAsync(cache);
        h.Handler.Hold = true;
        var probe = h.ProbeAsync();
        try
        {
            await h.Handler.Reached.Task.WaitAsync(TimeSpan.FromSeconds(10));
            if (variant is "new-prompt" or "queue-annotation")
                await h.Runtime.PersistTranscriptAsync(h.Id, [h.F.Event(3,
                    variant == "new-prompt" ? "UserPrompt" : "QueueEnqueue", 3, "concurrent", "new")]);
            else if (variant == "replace-frontier")
            {
                await using var mutation = await h.F.Store.BeginMutationAsync([h.Id], default);
                await using var db = h.F.Db();
                // CARD-1440: since CARD-1259 S2 every ingested row is admitted as a SessionRuntimeAction whose
                // TranscriptEntryId FK is Restrict, so a legal replacement removes the session's actions first
                // (the order DataRetentionService requires before it deletes a session's transcript).
                await db.SessionRuntimeActions.Where(a => a.AgentSessionId == h.Id).ExecuteDeleteAsync();
                await db.TranscriptEntries.Where(r => r.AgentSessionId == h.Id).ExecuteDeleteAsync();
                db.TranscriptEntries.Add(new TranscriptEntry { Id = Guid.NewGuid(), AgentSessionId = h.Id,
                    Sequence = 1, Kind = "UserPrompt", Uuid = "replacement", Text = "other", CreatedAt = DateTime.UtcNow });
                await db.SaveChangesAsync();
                await mutation.PublishCommittedAsync(default);
            }
            else
            {
                await using var db = h.F.Db();
                var session = await db.AgentSessions.SingleAsync(s => s.Id == h.Id);
                if (variant == "generation") session.StartedAt = h.Generation.AddSeconds(1);
                if (variant == "remote-ownership")
                {
                    session.RunnerId = "foreign"; session.RunnerStoreId = Guid.NewGuid(); session.RunnerCwd = "/foreign";
                }
                if (variant == "terminal-status") session.Status = SessionStatus.Stopped;
                await db.SaveChangesAsync();
            }
            var before = (await h.RowsAsync()).Select(r => r.Id).ToArray();
            h.Handler.Release.TrySetResult();
            var result = await probe;
            result.Outcome.ToString().ShouldBe("Unknown", variant);
            result.AddedCount.ShouldBe(0);
            (await h.RowsAsync()).Select(r => r.Id).ShouldBe(before);
        }
        finally { h.Handler.Release.TrySetResult(); await probe; }
    }

    [Test]
    public async Task C1205_Overlap_preserves_uuid_kind_and_rebased_sequence()
    {
        await using var h = await Harness.CreateAsync(true);
        var high = h.F.Event(90, "AssistantText", 1, "prior", "high");
        await h.Runtime.PersistTranscriptAsync(h.Id, [high]);
        var shared = h.F.Event(2, "AssistantText", 2, "answer", "shared");
        await h.Runtime.PersistTranscriptAsync(h.Id, [shared]);
        var legacy = h.F.Event(92, "ToolResult", 2, "legacy") with { Uuid = null };
        await h.Runtime.PersistTranscriptAsync(h.Id, [legacy]);
        var end = h.End with { Sequence = 3, Uuid = "shared", Timestamp = h.End.Timestamp!.Value.AddSeconds(3) };
        h.SetEntries(h.Prompt, high with { Sequence = 1 }, shared, legacy, end);
        var result = await h.ProbeAsync();
        result.Outcome.ToString().ShouldBe("RecoveredIdle");
        result.AddedCount.ShouldBe(1);
        var rows = await h.RowsAsync();
        rows.Where(r => r.Uuid == "shared").Select(r => r.Kind).ShouldBe(new[] { "AssistantText", "TurnEnd" });
        rows.Select(r => r.Sequence).Distinct().Count().ShouldBe(rows.Count);
        rows.Single(r => r.Uuid == "shared" && r.Kind == "TurnEnd").Sequence.ShouldBeGreaterThan(92);
        rows.Single(r => r.Kind == "TurnEnd").Timestamp.ShouldBe(end.Timestamp.Value.UtcDateTime);
        (await h.ProbeAsync()).AddedCount.ShouldBe(0);
        (await h.RowsAsync()).Count.ShouldBe(rows.Count);
    }

    [Test, Arguments(true), Arguments(false)]
    public async Task C1205_Probe_holds_the_commit_gate_through_publication(bool cache)
    {
        var fault = new Faults();
        await using var h = await Harness.CreateAsync(cache, fault);
        fault.HoldSave = true;
        var probe = h.ProbeAsync();
        Task<AgentSessionRuntime.PersistResult>? competing = null;
        Task<SessionStateStore.Lease>? reader = null;
        try
        {
            await fault.Reached.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var gate = h.F.Store.NextGateWait = Signal();
            reader = h.F.Store.BeginWriteAsync(h.Id, default);
            await gate.Task.WaitAsync(TimeSpan.FromSeconds(10));
            reader.IsCompleted.ShouldBeFalse("a warmed gate acquisition completes synchronously when the probe releases its lease");
            gate = h.F.Store.NextGateWait = Signal();
            competing = h.Runtime.PersistTranscriptAsync(h.Id, [h.F.Event(3, "UserPrompt", 3, "new", "new")]);
            await gate.Task.WaitAsync(TimeSpan.FromSeconds(10));
            competing.IsCompleted.ShouldBeFalse("competing ingest has reached the held session gate");
            (await h.RowsAsync()).Count.ShouldBe(1);
            fault.Release.TrySetResult();
            using (var published = await reader)
            {
                published.Snapshot.Working.ShouldBeFalse();
                published.Snapshot.Count.ShouldBe(2);
            }
            reader = null;
            (await probe).Outcome.ToString().ShouldBe("RecoveredIdle");
            await competing;
            (await h.WorkingAsync()).ShouldBeTrue();
            if (cache) (await h.F.Store.ReadAsync(h.Id, default)).Working.ShouldBeTrue();
        }
        finally
        {
            fault.Release.TrySetResult();
            if (reader is not null) (await reader).Dispose();
            await probe;
            if (competing is not null) await competing;
        }
    }

    [Test]
    [Arguments("end", "RecoveredIdle"), Arguments("new-prompt", "CheckedWorking")]
    [Arguments("future-kind", "CheckedWorking"), Arguments("auto-compact", "CheckedWorking")]
    [Arguments("manual-compact", "RecoveredIdle"), Arguments("null-time", "CheckedWorking")]
    [Arguments("equal-time", "CheckedWorking"), Arguments("annotation", "RecoveredIdle")]
    public async Task C1205_Runner_tail_classification_is_conservative(string variant, string expected)
    {
        await using var h = await Harness.CreateAsync(true);
        var activity = h.F.Event(3, "UserPrompt", 3, "new", "new");
        switch (variant)
        {
            case "new-prompt": h.SetEntries(h.Prompt, h.End, activity); break;
            case "future-kind": h.SetEntries(h.Prompt, h.End, activity with { Kind = "FutureActivity" }); break;
            case "auto-compact": h.SetEntries(h.Prompt, h.End with { Kind = "CompactBoundary", Text = "(auto)" }); break;
            case "manual-compact": h.SetEntries(h.Prompt, h.End with { Kind = "CompactBoundary", Text = "(manual)" }); break;
            case "null-time": h.SetEntries(h.Prompt, h.End, activity with { Timestamp = null }); break;
            case "equal-time": h.SetEntries(h.Prompt, h.End, activity with { Timestamp = h.End.Timestamp }); break;
            case "annotation": await h.Runtime.PersistTranscriptAsync(h.Id,
                [h.F.Event(3, "QueueEnqueue", 3, "server only", "annotation")]); break;
        }
        var result = await h.ProbeAsync();
        result.Outcome.ToString().ShouldBe(expected, variant);
        if (expected == "CheckedWorking")
        {
            result.AddedCount.ShouldBe(0);
            (await h.RowsAsync()).Count.ShouldBe(1);
        }
        else (await h.WorkingAsync()).ShouldBeFalse();
    }

    [Test]
    public async Task C1205_Generation_changed_after_validation_must_not_append_old_end()
    {
        var fault = new Faults();
        await using var h = await Harness.CreateAsync(true, fault);
        fault.HoldIdentity = true;
        var probe = h.ProbeAsync();
        try
        {
            await fault.Reached.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await using (var db = h.F.Db())
            {
                var session = await db.AgentSessions.SingleAsync(s => s.Id == h.Id);
                session.StartedAt = h.Generation.AddSeconds(1);
                await db.SaveChangesAsync();
            }
            fault.Release.TrySetResult();
            var result = await probe;
            var rows = await h.RowsAsync();
            Console.WriteLine($"REVIEW_RACE outcome={result.Outcome} reason={result.Reason} added={result.AddedCount} sqlWorking={await h.WorkingAsync()} oldEnds={rows.Count(r => r.Uuid == "end")} accepted={result.AcceptedGeneration:O}");
            rows.Count(r => r.Uuid == "end").ShouldBe(0,
                "a changed accepted generation must invalidate the old snapshot before append, not only after publication");
            result.Outcome.ToString().ShouldBe("Unknown");
            result.AddedCount.ShouldBe(0);
            var published = await h.F.Store.ReadAsync(h.Id, default);
            published.Working.ShouldBeTrue();
            published.Count.ShouldBe(1);
        }
        finally { fault.Release.TrySetResult(); await probe; }
    }
    [Test]
    [Arguments("transaction-generation"), Arguments("owner"), Arguments("terminal"), Arguments("frontier")]
    public async Task C1205_Late_precondition_change_rejects_without_publication(string variant)
    {
        var fault = new Faults();
        await using var h = await Harness.CreateAsync(true, fault);
        var before = await h.F.Store.ReadAsync(h.Id, default);
        fault.HoldFence = variant == "transaction-generation";
        fault.HoldIdentity = !fault.HoldFence;
        var probe = h.ProbeAsync();
        try
        {
            await fault.Reached.Task.WaitAsync(TimeSpan.FromSeconds(10));
            if (variant == "transaction-generation") fault.TransactionAtFence.ShouldBeTrue();
            await using (var db = h.F.Db())
            {
                var session = await db.AgentSessions.SingleAsync(s => s.Id == h.Id);
                if (variant == "transaction-generation") session.StartedAt = h.Generation.AddSeconds(1);
                if (variant == "owner")
                {
                    session.RunnerId = "foreign";
                    session.RunnerStoreId = Guid.NewGuid();
                    session.RunnerCwd = "/foreign";
                }
                if (variant == "terminal") session.Status = SessionStatus.Stopped;
                if (variant == "frontier") db.TranscriptEntries.Add(new TranscriptEntry
                {
                    Id = Guid.NewGuid(), AgentSessionId = h.Id, Sequence = 3, Kind = "UserPrompt",
                    Uuid = "new", Text = "new turn", CreatedAt = DateTime.UtcNow
                });
                await db.SaveChangesAsync();
            }
            fault.Release.TrySetResult();
            var result = await probe;
            (await h.RowsAsync()).Count(r => r.Uuid == "end").ShouldBe(0);
            result.Outcome.ToString().ShouldBe("Unknown");
            result.AddedCount.ShouldBe(0);
            (await h.F.Store.ReadAsync(h.Id, default)).ShouldBe(before);
            h.Log.Entries.Count(e => e.Level == LogLevel.Warning).ShouldBe(1);
        }
        finally { fault.Release.TrySetResult(); await probe; }
    }

    [Test]
    public async Task C1205_Generation_changes_before_validation_are_rejected()
    {
        await using var h = await Harness.CreateAsync(true);
        h.Handler.Hold = true;
        var probe = h.ProbeAsync();
        try
        {
            await h.Handler.Reached.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await ChangeGenerationAsync(h);
            h.Handler.Release.TrySetResult();
            var result = await probe;
            result.Outcome.ToString().ShouldBe("Unknown");
            result.AddedCount.ShouldBe(0);
            (await h.RowsAsync()).Count(r => r.Uuid == "end").ShouldBe(0);
            (await h.F.Store.ReadAsync(h.Id, default)).Working.ShouldBeTrue();
        }
        finally { h.Handler.Release.TrySetResult(); await probe; }
    }

    [Test, Arguments(true), Arguments(false)]
    public async Task C1205_Unchanged_generation_publishes_only_committed_rows_once(bool cache)
    {
        var fault = new Faults();
        await using var h = await Harness.CreateAsync(cache, fault);
        fault.HoldSave = true;
        var probe = h.ProbeAsync();
        Task<SessionStateStore.Lease>? reader = null;
        try
        {
            await fault.Reached.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var waiting = h.F.Store.NextGateWait = Signal();
            reader = h.F.Store.BeginWriteAsync(h.Id, default);
            await waiting.Task.WaitAsync(TimeSpan.FromSeconds(10));
            reader.IsCompleted.ShouldBeFalse();
            (await h.RowsAsync()).Count(r => r.Uuid == "end").ShouldBe(0);
            fault.Release.TrySetResult();
            using (var published = await reader)
            {
                (await h.RowsAsync()).Count(r => r.Uuid == "end").ShouldBe(1);
                published.Snapshot.Count.ShouldBe(2);
                published.Snapshot.Working.ShouldBeFalse();
            }
            reader = null;
            var result = await probe;
            result.Outcome.ToString().ShouldBe("RecoveredIdle");
            result.AddedCount.ShouldBe(1);
            (await h.ProbeAsync()).AddedCount.ShouldBe(0);
            (await h.RowsAsync()).Count(r => r.Uuid == "end").ShouldBe(1);
        }
        finally { fault.Release.TrySetResult(); if (reader is not null) (await reader).Dispose(); await probe; }
    }

    [Test, Arguments(true), Arguments(false)]
    public async Task C1205_Concurrent_recoveries_commit_one_end_and_converge(bool cache)
    {
        await using var h = await Harness.CreateAsync(cache);
        h.Handler.Hold = true;
        var first = h.ProbeAsync();
        await h.Handler.Reached.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var second = h.ProbeAsync();
        try
        {
            await h.Handler.SecondReached.Task.WaitAsync(TimeSpan.FromSeconds(10));
            h.Handler.Release.TrySetResult();
            var results = await Task.WhenAll(first, second);
            results.Count(r => r.Outcome.ToString() == "RecoveredIdle").ShouldBe(1);
            results.Count(r => r.Outcome.ToString() == "Unknown").ShouldBe(1);
            results.Sum(r => r.AddedCount).ShouldBe(1);
            (await h.RowsAsync()).Count(r => r.Uuid == "end").ShouldBe(1);
            (await h.ProbeAsync()).Outcome.ToString().ShouldBe("RecoveredIdle");
        }
        finally { h.Handler.Release.TrySetResult(); await Task.WhenAll(first, second); }
    }

    [Test, Arguments(true), Arguments(false)]
    public async Task C1205_Recovery_commit_fences_lifecycle_writer_then_allows_new_generation(bool cache)
    {
        var fault = new Faults();
        await using var h = await Harness.CreateAsync(cache, fault);
        fault.HoldSave = true;
        var probe = h.ProbeAsync();
        try
        {
            await fault.Reached.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await using var db = h.F.Db();
            await using (var tx = await db.Database.BeginTransactionAsync())
            {
                var refusal = await Should.ThrowAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync(
                    $"""SELECT "Id" FROM "AgentSessions" WHERE "Id" = {h.Id} FOR UPDATE NOWAIT"""));
                refusal.SqlState.ShouldBe("55P03", "the lifecycle writer must contend on recovery's session row lock");
                await tx.RollbackAsync();
            }
            (await h.RowsAsync()).Count(r => r.Uuid == "end").ShouldBe(0);
            fault.Release.TrySetResult();
            (await probe).Outcome.ToString().ShouldBe("RecoveredIdle");
            await ChangeGenerationAsync(h);
            (await h.RowsAsync()).Count(r => r.Uuid == "end").ShouldBe(1);
            var rejected = await h.ProbeAsync();
            rejected.Outcome.ToString().ShouldBe("Unknown");
            rejected.AddedCount.ShouldBe(0);
        }
        finally { fault.Release.TrySetResult(); await probe; }
    }

    private static async Task ChangeGenerationAsync(Harness h)
    {
        await using var db = h.F.Db();
        var session = await db.AgentSessions.SingleAsync(s => s.Id == h.Id);
        session.StartedAt = h.Generation.AddSeconds(1);
        await db.SaveChangesAsync();
    }

    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private sealed class Harness : IAsyncDisposable
    {
        public required SessionStateTestFixture F { get; init; }
        public required Handler Handler { get; init; }
        public required HttpClient Http { get; init; }
        public required ISessionRunnerClient Client { get; init; }
        public AgentSessionRuntime Runtime { get; private set; } = null!;
        public RecordingLogger<AgentSessionRuntime> Log { get; } = new();
        public Guid Id => F.SessionId;
        public DateTime Generation { get; private set; }
        public SessionRunnerTranscriptEvent Prompt { get; private set; } = null!;
        public SessionRunnerTranscriptEvent End { get; private set; } = null!;
        public SessionRunnerTranscriptDto Snapshot { get => Handler.Snapshot; set => Handler.Snapshot = value; }
        public static async Task<Harness> CreateAsync(bool cache, Faults? fault = null, bool nullUuid = false)
        {
            var f = await SessionStateTestFixture.CreateAsync(fault, settings: new SessionStateSettings { Enabled = cache });
            try
            {
                var handler = new Handler();
                var http = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
                var client = new SessionRunnerHttpClient(http, new Factory(handler), Options.Create(new SessionRunnerSettings
                    { Enabled = true, BaseUrl = "http://runner.invalid" }));
                var h = new Harness { F = f, Handler = handler, Http = http, Client = client };
                h.ReplaceRuntime(f.Store);
                await using var db = f.Db();
                h.Generation = (await db.AgentSessions.SingleAsync(s => s.Id == h.Id)).StartedAt;
                h.Prompt = f.Event(1, "UserPrompt", 0, "working", "prompt") with { Uuid = nullUuid ? null : "prompt" };
                h.End = f.Event(2, "TurnEnd", 2, "done", "end");
                await h.Runtime.PersistTranscriptAsync(h.Id, [h.Prompt]);
                h.Snapshot = new(h.Id, [h.Prompt, h.End], 2, AcceptedStartedAt: h.Generation);
                return h;
            }
            catch { await f.DisposeAsync(); throw; }
        }
        public void ReplaceRuntime(SessionStateStore? store) => Runtime = new AgentSessionRuntime(Client,
            F.Services.GetRequiredService<IEventBus>(), F.Services.GetRequiredService<IOptions<AgentSessionSettings>>(),
            F.Services.GetRequiredService<IServiceScopeFactory>(), TimeProvider.System,
            Log, states: store);
        public void SetEntries(params SessionRunnerTranscriptEvent[] entries) => Snapshot = Snapshot with
            { Entries = entries, LastSequence = entries[^1].Sequence };
        public Task<TranscriptRecoveryResult> ProbeAsync() => Runtime.ProbeWorkingTranscriptAsync(Id, default);
        public Task<List<TranscriptEntry>> RowsAsync() => F.RowsAsync();
        public async Task<bool> WorkingAsync()
        {
            await using var db = F.Db();
            return await SessionMessageQueueService.IsWorkingAsync(db, Id, default);
        }
        public async ValueTask DisposeAsync() { Handler.Release.TrySetResult(); Http.Dispose(); await F.DisposeAsync(); }
    }

    private sealed class Factory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, false) { Timeout = Timeout.InfiniteTimeSpan };
    }
    private sealed class Handler : HttpMessageHandler
    {
        public SessionRunnerTranscriptDto Snapshot { get; set; } = null!;
        public bool Hold, Fail, Cancel;
        public int Hits;
        public TaskCompletionSource Reached { get; } = Signal();
        public TaskCompletionSource SecondReached { get; } = Signal();
        public TaskCompletionSource Release { get; } = Signal();
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (Interlocked.Increment(ref Hits) == 2) SecondReached.TrySetResult();
            var snapshot = Snapshot;
            Reached.TrySetResult();
            if (Hold) await Release.Task.WaitAsync(ct);
            if (Fail) throw new HttpRequestException("planned pull failure");
            if (Cancel) throw new OperationCanceledException(ct);
            return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(snapshot,
                new JsonSerializerOptions(JsonSerializerDefaults.Web)), Encoding.UTF8, "application/json") };
        }
    }
    private sealed class Faults : DbCommandInterceptor, ISaveChangesInterceptor
    {
        public bool SaveFailure, ReadFailure, Collision, HoldSave, HoldIdentity, HoldFence;
        public bool TransactionAtFence;
        public string? RejectUuid;
        public int Hits;
        public TaskCompletionSource Reached { get; } = Signal();
        public TaskCompletionSource Release { get; } = Signal();
        private bool _collided, _savedEnd;
        private void CheckRead(DbCommand command)
        {
            if (ReadFailure && _savedEnd && command.CommandText.Contains("session-state.fallback"))
            { Hits++; throw new InvalidOperationException("planned verdict read failure"); }
        }
        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData data, InterceptionResult<DbDataReader> result, CancellationToken ct = default)
        {
            if (HoldIdentity && command.CommandText.Contains("session-state.identity"))
            {
                HoldIdentity = false;
                Reached.TrySetResult();
                await Release.Task.WaitAsync(TimeSpan.FromSeconds(20), ct);
            }
            CheckRead(command);
            await InsertCollisionAsync(command, data.Context, ct);
            return result;
        }
        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command,
            CommandEventData data, InterceptionResult<int> result, CancellationToken ct = default)
        {
            if (HoldFence && command.CommandText.Contains("FOR UPDATE") && command.CommandText.Contains("AgentSessions"))
            {
                HoldFence = false;
                TransactionAtFence = data.Context!.Database.CurrentTransaction is not null;
                Reached.TrySetResult();
                await Release.Task.WaitAsync(TimeSpan.FromSeconds(20), ct);
            }
            await InsertCollisionAsync(command, data.Context, ct);
            return result;
        }
        private async Task InsertCollisionAsync(DbCommand command, DbContext? context, CancellationToken ct)
        {
            if (Collision && !_collided && command.CommandText.Contains("FOR UPDATE") && command.CommandText.Contains("AgentSessions"))
            {
                _collided = true; Hits++;
                await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
                    .UseNpgsql(context!.Database.GetConnectionString()).Options);
                var row = context.ChangeTracker.Entries<TranscriptEntry>().Single(e => e.Entity.Uuid == "end").Entity;
                db.TranscriptEntries.Add(new TranscriptEntry { Id = Guid.NewGuid(), AgentSessionId = row.AgentSessionId,
                    Sequence = row.Sequence, Kind = row.Kind, Uuid = row.Uuid, Timestamp = row.Timestamp, CreatedAt = DateTime.UtcNow });
                await db.SaveChangesAsync(ct);
            }
        }
        public ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData data, int result, CancellationToken ct = default)
        {
            _savedEnd |= data.Context!.ChangeTracker.Entries<TranscriptEntry>().Any(e => e.Entity.Kind == "TurnEnd");
            return ValueTask.FromResult(result);
        }
        public async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data,
            InterceptionResult<int> result, CancellationToken ct = default)
        {
            var rows = data.Context!.ChangeTracker.Entries<TranscriptEntry>().Where(e => e.State == EntityState.Added).ToArray();
            if (rows.Length == 0) return result;
            if (SaveFailure) { Hits++; throw new InvalidOperationException("planned save failure"); }
            if (RejectUuid is not null && rows.Any(e => e.Entity.Uuid == RejectUuid &&
                e.Entity.Text?.StartsWith(AgentSessionRuntime.TranscriptStubPrefix) != true))
            { Hits++; throw new DbUpdateException("planned row rejection", new PostgresException("planned", "ERROR", "ERROR", "XX000")); }
            if (HoldSave) { Reached.TrySetResult(); await Release.Task.WaitAsync(TimeSpan.FromSeconds(20), ct); }
            return result;
        }
    }
}
