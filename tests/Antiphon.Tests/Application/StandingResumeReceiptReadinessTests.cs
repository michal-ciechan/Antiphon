using System.Data.Common;
using System.Net;
using System.Text;
using System.Text.Json;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Agents.SessionRunner;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
public partial class StandingResumeReceiptReadinessTests
{
    [Test]
    public async Task C1574_Transport_and_persistence_fence_readiness()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var commit = new CommitControl();
        var fence = new FenceControl();
        var storeId = Guid.NewGuid();
        await using var h = await ClaudeDialogFixture.CreateBridgeAsync(new()
        {
            ConnectionString = schema.ConnectionString,
            ConfigureDbContext = options => options.AddInterceptors(commit, fence)
        });
        await using (var db = h.CreateObserverContext())
        {
            await db.AgentSessions.Where(s => s.Id == h.SessionId)
                .ExecuteUpdateAsync(u => u.SetProperty(s => s.StandingAgentId, h.AgentId)
                    .SetProperty(s => s.RunnerId, "fixture-runner").SetProperty(s => s.RunnerStoreId, storeId)
                    .SetProperty(s => s.RunnerCwd, "/fixture"));
        }
        DateTime generation;
        await using (var db = h.CreateObserverContext())
            generation = (await db.AgentSessions.SingleAsync(s => s.Id == h.SessionId)).StartedAt;
        var ready = new SessionRunnerTranscriptReadiness("Ready", "opaque-native-binding", 100, 100);
        var snapshot = new SessionRunnerTranscriptDto(h.SessionId, [], 0,
            AcceptedStartedAt: generation, Readiness: ready);
        var handler = new TranscriptHandler { Snapshot = snapshot };
        using var http = new HttpClient(handler);
        var client = new SessionRunnerHttpClient(http, new HttpFactory(handler),
            Options.Create(new SessionRunnerSettings { BaseUrl = "http://runner.invalid" }));
        var mapper = new RunnerContractMapper();
        async Task CheckWireAsync(SessionRunnerTranscriptDto expected)
        {
            handler.Snapshot = expected;
            var wire = JsonSerializer.Deserialize<RunnerTranscriptDto>(
                JsonSerializer.Serialize(expected, RunnerContractMapper.Json), RunnerContractMapper.Json)!;
            var phoneHome = mapper.MapTranscript(wire);
            var pulled = await client.GetTranscriptAsync(h.SessionId, default);
            phoneHome.Readiness.ShouldBe(expected.Readiness, "phone-home mapper preserves all readiness fields");
            pulled.Readiness.ShouldBe(expected.Readiness, "HTTP deserialization preserves all readiness fields");
            phoneHome.AcceptedStartedAt.ShouldBe(generation);
            pulled.AcceptedStartedAt.ShouldBe(generation);
        }
        await CheckWireAsync(snapshot);
        // Old peers omit the member entirely, rather than explicitly sending null.
        handler.LegacyJson = JsonSerializer.Serialize(new { snapshot.SessionId, snapshot.Entries,
            snapshot.LastSequence, snapshot.AcceptedStartedAt }, RunnerContractMapper.Json);
        (await client.GetTranscriptAsync(h.SessionId, default)).Readiness.ShouldBeNull();
        mapper.MapTranscript(JsonSerializer.Deserialize<RunnerTranscriptDto>(handler.LegacyJson,
            RunnerContractMapper.Json)!).Readiness.ShouldBeNull();
        handler.LegacyJson = null;
        h.Runner.TranscriptPull = (id, ct) => client.GetTranscriptAsync(id, ct);
        Task<ReceiptReadinessObservation> Observe() =>
            h.Runtime.ObserveReceiptReadinessAsync(h.SessionId, generation, h.AgentId, default);

        // Empty committed history has an observable zero baseline, not a null/unknown floor.
        var first = await Observe();
        first.Status.ShouldBe(ReceiptReadinessStatus.Ready, "a caught-up, same-launch empty snapshot is Ready");
        first.BaselineSequence.ShouldBe(0);
        first.BindingId.ShouldBe(ready.BindingId);
        first.ParsedThrough.ShouldBe(100);
        first.ObservedLength.ShouldBe(100);

        SessionRunnerTranscriptEvent Entry(string uuid, long sequence = 1) => new(h.SessionId, sequence,
            TranscriptKinds.UserPrompt, uuid, null, new DateTimeOffset(generation), "user",
            "complete restart obligation " + uuid, null, null, null, null, null);
        var receipt = Entry("stored-once");
        handler.Snapshot = snapshot = snapshot with { Entries = [receipt], LastSequence = 1 };
        (await Observe()).Status.ShouldBe(ReceiptReadinessStatus.Ready);
        var repeated = await Observe();
        repeated.Status.ShouldBe(ReceiptReadinessStatus.Ready, "all-duplicate successful persistence is Ready");
        repeated.BaselineSequence.ShouldBe(1);
        await using (var db = h.CreateObserverContext())
            (await db.TranscriptEntries.CountAsync(t => t.AgentSessionId == h.SessionId && t.Uuid == receipt.Uuid)).ShouldBe(1);

        var variants = new Dictionary<string, SessionRunnerTranscriptDto>
        {
            ["wrong-session"] = snapshot with { SessionId = Guid.NewGuid() },
            ["wrong-entry-session"] = snapshot with { Entries = [receipt with { SessionId = Guid.NewGuid() }] },
            ["wrong-generation"] = snapshot with { AcceptedStartedAt = generation.AddSeconds(1) },
            ["missing-generation"] = snapshot with { AcceptedStartedAt = null },
            ["incomplete-tail"] = snapshot with { LastSequence = 2 },
            ["bad-order"] = snapshot with { Entries = [receipt with { Sequence = 2 }, receipt], LastSequence = 1 },
            ["legacy-unknown"] = snapshot with { Readiness = null },
            ["unknown"] = snapshot with { Readiness = ready with { Status = "Unknown" } },
            ["missing-binding"] = snapshot with { Readiness = ready with { BindingId = "" } },
            ["bad-extent"] = snapshot with { Readiness = ready with { ParsedThrough = 99 } },
            ["negative-extent"] = snapshot with { Readiness = ready with { ParsedThrough = -1, ObservedLength = -1 } }
        };
        foreach (var (name, candidate) in variants)
        {
            handler.Snapshot = candidate;
            var result = await Observe();
            result.Status.ShouldBe(ReceiptReadinessStatus.Unknown, name);
            result.BaselineSequence.ShouldBeNull(name);
            Console.WriteLine($"C1574 readiness variant={name} outcome={result.Status} reason={result.Reason}");
        }
        // Behind still persists available receipt rows; it cannot certify absence.
        var behindEntry = Entry("behind-receipt", 2);
        handler.Snapshot = snapshot with { Entries = [receipt, behindEntry], LastSequence = 2,
            Readiness = ready with { Status = "Behind", ParsedThrough = 50 } };
        (await Observe()).Status.ShouldBe(ReceiptReadinessStatus.Behind);
        await using (var db = h.CreateObserverContext())
            (await db.TranscriptEntries.AnyAsync(t => t.AgentSessionId == h.SessionId && t.Uuid == behindEntry.Uuid)).ShouldBeTrue();
        snapshot = handler.Snapshot with { Readiness = ready };
        handler.Snapshot = snapshot;
        handler.Fail = true;
        (await Observe()).Status.ShouldBe(ReceiptReadinessStatus.Unknown, "failed pull");
        handler.Fail = false;

        // A valid all-present remote binding permits independent coordinate changes.
        // Independently change each launch/route coordinate while the pull is held.
        foreach (var variant in new[] { "generation", "route", "store", "cwd", "owner", "owner-pointer", "terminal" })
        {
            var entered = Signal();
            var release = Signal();
            var candidate = snapshot with { Entries = [receipt, behindEntry, Entry("raced-" + variant, 3)], LastSequence = 3 };
            h.Runner.TranscriptPull = async (_, ct) => { entered.TrySetResult(); await release.Task.WaitAsync(ct); return candidate; };
            var observation = Observe();
            try
            {
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
                await using (var db = h.CreateObserverContext())
                {
                    var session = await db.AgentSessions.SingleAsync(s => s.Id == h.SessionId);
                    var agent = await db.Agents.SingleAsync(a => a.Id == h.AgentId);
                    switch (variant)
                    {
                        case "generation": session.StartedAt = generation.AddSeconds(1); break;
                        case "route": session.RunnerId = "other-runner"; break;
                        case "store": session.RunnerStoreId = Guid.NewGuid(); break;
                        case "cwd": session.RunnerCwd = "/other-store"; break;
                        case "owner": session.StandingAgentId = Guid.NewGuid(); break;
                        case "owner-pointer": agent.PersistentSessionId = Guid.NewGuid().ToString("D"); break;
                        case "terminal": session.Status = SessionStatus.Stopped; break;
                    }
                    await db.SaveChangesAsync();
                }
                release.TrySetResult();
                (await observation).Status.ShouldBe(ReceiptReadinessStatus.Unknown, variant);
                await using var verify = h.CreateObserverContext();
                (await verify.TranscriptEntries.AnyAsync(t => t.AgentSessionId == h.SessionId && t.Uuid == "raced-" + variant)).ShouldBeFalse(variant);
            }
            finally
            {
                release.TrySetResult(); await observation;
                await using var db = h.CreateObserverContext();
                await db.AgentSessions.Where(s => s.Id == h.SessionId).ExecuteUpdateAsync(u => u
                    .SetProperty(s => s.StartedAt, generation).SetProperty(s => s.RunnerId, "fixture-runner")
                    .SetProperty(s => s.RunnerStoreId, storeId).SetProperty(s => s.RunnerCwd, "/fixture")
                    .SetProperty(s => s.StandingAgentId, h.AgentId).SetProperty(s => s.Status, SessionStatus.Running));
                await db.Agents.Where(a => a.Id == h.AgentId).ExecuteUpdateAsync(u => u
                    .SetProperty(a => a.PersistentSessionId, h.SessionId.ToString("D")));
            }
        }
        h.Runner.TranscriptPull = (id, ct) => client.GetTranscriptAsync(id, ct);
        // Change ownership after the outer recheck, while the SQL fence is about to lock.
        foreach (var variant in new[] { "generation", "store", "owner" })
        {
            fence.Arm();
            handler.Snapshot = snapshot with { Entries = [Entry("commit-race-" + variant, 3)], LastSequence = 3 };
            var racing = Observe();
            try
            {
                await fence.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
                fence.InTransaction.ShouldBeTrue();
                await using (var db = h.CreateObserverContext())
                {
                    var session = await db.AgentSessions.SingleAsync(s => s.Id == h.SessionId);
                    if (variant == "generation") session.StartedAt = generation.AddSeconds(1);
                    if (variant == "store") session.RunnerStoreId = Guid.NewGuid();
                    if (variant == "owner") session.StandingAgentId = Guid.NewGuid();
                    await db.SaveChangesAsync();
                }
                fence.Release.TrySetResult();
                (await racing).Status.ShouldBe(ReceiptReadinessStatus.Unknown, "commit-race-" + variant);
                await using var verify = h.CreateObserverContext();
                (await verify.TranscriptEntries.AnyAsync(t => t.AgentSessionId == h.SessionId
                    && t.Uuid == "commit-race-" + variant)).ShouldBeFalse("stale launch rows must not commit");
            }
            finally
            {
                fence.Release.TrySetResult(); await racing;
                await using var db = h.CreateObserverContext();
                await db.AgentSessions.Where(s => s.Id == h.SessionId).ExecuteUpdateAsync(u => u
                    .SetProperty(s => s.StartedAt, generation).SetProperty(s => s.RunnerStoreId, storeId)
                    .SetProperty(s => s.StandingAgentId, h.AgentId));
            }
        }
        handler.Snapshot = snapshot with { Entries = [receipt, behindEntry, Entry("held-commit", 3)], LastSequence = 3 };
        commit.Hold = true;
        var pending = Observe();
        try
        {
            await commit.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            pending.IsCompleted.ShouldBeFalse("Ready must wait for the real transaction commit");
            await using var db = h.CreateObserverContext();
            (await db.TranscriptEntries.AnyAsync(t => t.AgentSessionId == h.SessionId && t.Uuid == "held-commit")).ShouldBeFalse();
        }
        finally { commit.Release.TrySetResult(); await pending; }
        (await pending).Status.ShouldBe(ReceiptReadinessStatus.Ready);
        commit.Hold = false;
        commit.Fail = true;
        handler.Snapshot = snapshot with { Entries = [Entry("faulted-commit", 4)], LastSequence = 4 };
        (await Observe()).Status.ShouldBe(ReceiptReadinessStatus.Unknown, "commit fault cannot certify persistence");
        commit.Hits.ShouldBeGreaterThan(0);
        commit.Fail = false;
        // A retained persist failure cannot be erased by an all-duplicate snapshot.
        handler.Snapshot = snapshot;
        (await Observe()).Status.ShouldBe(ReceiptReadinessStatus.Unknown, "retained persistence fault");
        handler.Snapshot = snapshot with { Entries = [Entry("repair", 5)], LastSequence = 5 };
        (await Observe()).Status.ShouldBe(ReceiptReadinessStatus.Ready, "successful new commit clears the persistence fault");

        handler.Snapshot = snapshot with { Entries = [Entry("rebased", 1)], LastSequence = 1 };
        var rebased = await Observe();
        rebased.Status.ShouldBe(ReceiptReadinessStatus.Ready);
        rebased.BaselineSequence.ShouldBe(6, "the delivery floor is in stored, not runner, sequence space");
        await using (var db = h.CreateObserverContext())
            (await db.TranscriptEntries.SingleAsync(t => t.AgentSessionId == h.SessionId && t.Uuid == "rebased"))
                .Sequence.ShouldBe(6);

        // A healthy remote route is admissible; the separate local-only recovery probe is not reused.
        await using (var db = h.CreateObserverContext())
            await db.AgentSessions.Where(s => s.Id == h.SessionId).ExecuteUpdateAsync(u => u
                .SetProperty(s => s.RunnerId, "remote-fixture").SetProperty(s => s.RunnerStoreId, Guid.NewGuid())
                .SetProperty(s => s.RunnerCwd, "/fixture"));
        (await Observe()).Status.ShouldBe(ReceiptReadinessStatus.Ready, "stable remote ownership");
        var timeoutEntered = Signal();
        h.Runner.TranscriptPull = async (_, ct) => { timeoutEntered.TrySetResult(); await Task.Delay(Timeout.Infinite, ct); return snapshot; };
        var timedOut = await Observe();
        timeoutEntered.Task.IsCompleted.ShouldBeTrue();
        timedOut.Status.ShouldBe(ReceiptReadinessStatus.Unknown);
        timedOut.Reason.ShouldBe("observation_timeout");
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        await Should.ThrowAsync<OperationCanceledException>(() => h.Runtime.ObserveReceiptReadinessAsync(
            h.SessionId, generation, h.AgentId, canceled.Token));
    }

    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private sealed class HttpFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, false);
    }
    private sealed class TranscriptHandler : HttpMessageHandler
    {
        public required SessionRunnerTranscriptDto Snapshot { get; set; }
        public string? LegacyJson;
        public bool Fail;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (Fail) throw new HttpRequestException("fixture pull failure");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(
                LegacyJson ?? JsonSerializer.Serialize(Snapshot, RunnerContractMapper.Json), Encoding.UTF8, "application/json") });
        }
    }
    private sealed class FenceControl : DbCommandInterceptor
    {
        private bool _hold;
        public bool InTransaction;
        public TaskCompletionSource Entered { get; private set; } = Signal();
        public TaskCompletionSource Release { get; private set; } = Signal();
        public void Arm() { Entered = Signal(); Release = Signal(); _hold = true; }
        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command,
            CommandEventData data, InterceptionResult<int> result, CancellationToken ct = default)
        {
            if (_hold && command.CommandText.Contains("FOR UPDATE") && command.CommandText.Contains("AgentSessions"))
            {
                _hold = false;
                InTransaction = data.Context!.Database.CurrentTransaction is not null;
                Entered.TrySetResult();
                await Release.Task.WaitAsync(ct);
            }
            return result;
        }
    }
    private sealed class CommitControl : DbTransactionInterceptor
    {
        public bool Hold, Fail;
        public int Hits;
        public TaskCompletionSource Entered { get; } = Signal();
        public TaskCompletionSource Release { get; } = Signal();
        public override async ValueTask<InterceptionResult> TransactionCommittingAsync(DbTransaction transaction,
            TransactionEventData eventData, InterceptionResult result, CancellationToken ct = default)
        {
            if (Hold) { Entered.TrySetResult(); await Release.Task.WaitAsync(ct); }
            if (Fail) { Hits++; throw new InvalidOperationException("fixture commit failure"); }
            return result;
        }
    }
}
