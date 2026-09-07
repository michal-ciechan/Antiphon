using Antiphon.Server.Application.Services;
using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Antiphon.Tests.Agents;
using Antiphon.Server.Application.Settings;
using Microsoft.Extensions.Options;
using Antiphon.Server.Domain.Enums;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[NotInParallel]
[ParallelLimiter<ProcessSpawnLimit>]
public class OutputDistillationApplyRaceTests
{
    [Test]
    public async Task Source_row_lock_prevents_a_poll_commit_between_read_and_apply()
    {
        using var seedHarness = new OutputDistillationHarness();
        var seed = await seedHarness.SeedSourceAsync();
        var probe = new PollOrderingProbe();
        await using var provider = AgentTaskSettlementRaceTests.BuildHarness(s =>
            s.AddDbContext<Antiphon.Server.Infrastructure.Data.AppDbContext>(o => o.AddInterceptors(probe)));
        var queue = provider.GetRequiredService<SessionMessageQueueService>();
        Task? poll = null;
        var committedBeforeApply = false;
        queue.BeforeDistillationUpdateAsync = async ct =>
        {
            poll = Task.Run(async () => {
                await using var scope = provider.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<AgentTaskService>()
                    .GetAsync(seed.Task.Id, ct, seed.Task.ParentSessionId);
            }, ct);
            await probe.PollEntered.Task.WaitAsync(TimeSpan.FromSeconds(5), ct);
            // The mutation arm removes only FOR UPDATE. Force its now-unblocked real
            // parent poll to commit before Apply writes, exposing the stale snapshot.
            if (!probe.SourceReadWasLocked)
            {
                await poll.WaitAsync(TimeSpan.FromSeconds(5), ct);
                committedBeforeApply = true;
            }
            else poll.IsCompleted.ShouldBeFalse("the poll update must wait for the source row lock");
        };
        var now = DateTimeOffset.UtcNow;
        string? outcome;
        try
        {
            outcome = await queue.TryApplyDistillationAsync(new(seed.Task.Id, seed.QueuedMessageId,
                now, now.AddSeconds(45), OutputDistillerMode.Apply), seed.Digest,
                seedHarness.PassingDistillation(), CancellationToken.None);
        }
        finally { if (poll is not null) await poll.WaitAsync(TimeSpan.FromSeconds(5)); }
        if (committedBeforeApply)
            outcome.ShouldBe("full-report-read", "a committed full-report poll must never be followed by stale summary replacement");
        else outcome.ShouldBeNull();
        (await seedHarness.ReloadTaskAsync(seed.Task.Id)).LastPolledResultHash.ShouldBe(seed.Digest);
    }

    private sealed class PollOrderingProbe : DbCommandInterceptor
    {
        public bool SourceReadWasLocked { get; private set; }
        public TaskCompletionSource PollEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
            DbDataReader result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("SELECT * FROM \"AgentTasks\" WHERE", StringComparison.Ordinal))
                SourceReadWasLocked = command.CommandText.Contains("FOR UPDATE", StringComparison.Ordinal);
            return ValueTask.FromResult(result);
        }
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.StartsWith("UPDATE \"AgentTasks\"", StringComparison.Ordinal)
                && command.CommandText.Contains("LastPolledResultHash", StringComparison.Ordinal)) PollEntered.TrySetResult();
            return ValueTask.FromResult(result);
        }
    }

    [Test]
    public async Task Equality_and_postlock_expiry_never_apply()
    {
        var clock = new OffsetClock(); var probe = new CountUpdates();
        using var h = new OutputDistillationHarness(servicesOverride: s => s.AddSingleton<TimeProvider>(clock),
            configureDb: o => o.AddInterceptors(probe));
        var seed = await h.SeedSourceAsync(); var queue = h.Provider.GetRequiredService<SessionMessageQueueService>();
        queue.BeforeDistillationUpdateAsync = _ => { clock.Offset = TimeSpan.FromMinutes(1); return Task.CompletedTask; };
        var now = clock.GetUtcNow();
        (await queue.TryApplyDistillationAsync(new(seed.Task.Id, seed.QueuedMessageId, now, now.AddSeconds(45), OutputDistillerMode.Apply),
            seed.Digest, h.PassingDistillation(), CancellationToken.None)).ShouldBe("deadline");
        probe.Count.ShouldBe(0); (await h.ReloadQueuedAsync(seed.QueuedMessageId)).Body.ShouldBe(seed.RawBody);
    }
    [Test]
    public async Task Database_clock_rejects_an_expired_body_update()
    {
        var probe = new ApplyWriteGate();
        using var h = new OutputDistillationHarness(configureDb: o => o.AddInterceptors(probe));
        var seed = await h.SeedSourceAsync();
        var now = h.Clock.GetUtcNow();
        var apply = h.Provider.GetRequiredService<SessionMessageQueueService>().TryApplyDistillationAsync(
            new(seed.Task.Id, seed.QueuedMessageId, now, now.AddSeconds(1), OutputDistillerMode.Apply),
            seed.Digest, h.PassingDistillation(), CancellationToken.None);
        try
        {
            await probe.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await Task.Delay(TimeSpan.FromMilliseconds(1200));
            h.Clock.GetUtcNow().ShouldBeLessThan(now.AddSeconds(1), "only the database clock expired");
        }
        finally { probe.Release.TrySetResult(); }
        (await apply).ShouldBe("deadline");
        (await h.ReloadQueuedAsync(seed.QueuedMessageId)).Body.ShouldBe(seed.RawBody);
    }
    private sealed class OffsetClock : TimeProvider
    {
        public TimeSpan Offset { get; set; }
        public DateTimeOffset? OverrideUtc { get; set; }
        public override DateTimeOffset GetUtcNow() => OverrideUtc ?? DateTimeOffset.UtcNow + Offset;
    }
    private sealed class CountUpdates : DbCommandInterceptor
    {
        public int Count { get; private set; }
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.StartsWith("UPDATE \"SessionQueuedMessages\" SET \"Body\"")) Count++;
            return ValueTask.FromResult(result);
        }
    }

    [Test]
    [Arguments(false)] [Arguments(true)]
    public async Task Parent_poll_and_apply_have_one_ordered_winner(bool applyFirst)
    {
        await using var f = await OutputDistillationDeliveryTests.DeliveryFixture.CreateAsync();
        var seed = await f.SettleAsync(OutputDistillationDeliveryTests.ReportOfLength(5500));
        var queue = f.Provider.GetRequiredService<SessionMessageQueueService>();
        var now = DateTimeOffset.UtcNow;
        Task<string?> Apply() => queue.TryApplyDistillationAsync(new(seed.Task.Id, seed.NoteId, now, now.AddSeconds(45), OutputDistillerMode.Apply),
            seed.Digest, OutputDistillationDeliveryTests.Summary, CancellationToken.None);
        async Task Poll()
        {
            await using var scope = f.Provider.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<AgentTaskService>().GetAsync(seed.Task.Id, CancellationToken.None, seed.Task.ParentSessionId);
        }
        if (applyFirst) { (await Apply()).ShouldBeNull(); await Poll(); }
        else { await Poll(); (await Apply()).ShouldBe("full-report-read"); }
        // The worker owns bounded hold cleanup when the matching poll makes model work redundant.
        await using (var scope = f.Provider.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<OutputDistillationService>()
                .RequestAsync(seed.Task.Id, seed.NoteId, CancellationToken.None);
        var note = await f.DeliverAsync(seed);
        note.Body.ShouldContain("Report withheld"); note.Body.ShouldNotContain(OutputDistillationDeliveryTests.Summary);
    }
    [Test]
    public async Task Path_changed_after_validation_cannot_publish_stale_pointer()
    {
        using var h = new OutputDistillationHarness(servicesOverride: s => s.AddGitWorkspaceService());
        var seed = await h.SeedSourceAsync();
        var path = Path.Combine(h.Scratch, "report.md"); await File.WriteAllTextAsync(path, seed.Report);
        await using (var db = OutputDistillationHarness.CreateContext())
            await db.AgentTasks.Where(t => t.Id == seed.Task.Id).ExecuteUpdateAsync(s => s.SetProperty(t => t.ResultFilePath, path));
        var queue = h.Provider.GetRequiredService<SessionMessageQueueService>();
        queue.AfterReportValidationAsync = async ct => {
            await using var db = OutputDistillationHarness.CreateContext();
            await db.AgentTasks.Where(t => t.Id == seed.Task.Id).ExecuteUpdateAsync(s => s.SetProperty(t => t.ResultFilePath, path + ".changed"), ct);
        };
        var now = h.Clock.GetUtcNow();
        (await queue.TryApplyDistillationAsync(new(seed.Task.Id, seed.QueuedMessageId, now, now.AddSeconds(45), OutputDistillerMode.Apply),
            seed.Digest, h.PassingDistillation(), CancellationToken.None)).ShouldBeNull();
        var note = await h.ReloadQueuedAsync(seed.QueuedMessageId);
        note.Body.ShouldNotContain(path); note.Body.ShouldContain("GET /api/agent-tasks/");
    }

    [Test]
    public async Task File_validation_does_not_own_delivery_lock()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var store = new ValidationGate(entered, release);
        using var h = new OutputDistillationHarness(servicesOverride: s => {
            s.AddSingleton<Antiphon.Server.Application.Interfaces.IAgentReportStore>(store);
            s.AddSingleton(TimeProvider.System);
            s.AddSingleton(Options.Create(new SupervisionSettings
            { DeliveryVerification = new DeliveryVerificationSettings { Enabled = false } }));
        });
        var seed = await h.SeedSourceAsync();
        var session = seed.Task.ParentSessionId!.Value;
        await using (var db = OutputDistillationHarness.CreateContext())
            await db.AgentSessions.Where(s => s.Id == session).ExecuteUpdateAsync(s => s.SetProperty(x => x.Cwd, h.Scratch));
        var submitted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var adapter = new FakeAgentProtocolAdapter();
        adapter.OnSubmitted = _ => { submitted.TrySetResult(); return Task.CompletedTask; };
        h.Provider.GetRequiredService<AgentSessionRuntime>().Register(session, adapter);
        var queue = h.Provider.GetRequiredService<SessionMessageQueueService>();
        var now = DateTimeOffset.UtcNow;
        var apply = queue.TryApplyDistillationAsync(new(seed.Task.Id, seed.QueuedMessageId, now, now.AddSeconds(45), OutputDistillerMode.Apply),
            seed.Digest, h.PassingDistillation(), CancellationToken.None);
        Task? send = null;
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            send = queue.SendNowAsync(session, seed.QueuedMessageId, CancellationToken.None);
            await Task.WhenAny(submitted.Task, Task.Delay(TimeSpan.FromSeconds(5)));
            submitted.Task.IsCompleted.ShouldBeTrue("SendNow must reach real protocol submission while only file validation is held");
        }
        finally
        {
            release.TrySetResult();
            if (send is not null) await send.WaitAsync(TimeSpan.FromSeconds(5));
            await apply.WaitAsync(TimeSpan.FromSeconds(5));
        }
        (await apply).ShouldBe("delivery-claimed");
        (await h.ReloadQueuedAsync(seed.QueuedMessageId)).DeliveryAttempts.ShouldBe(1);
    }

    [Test]
    [Arguments(false)] [Arguments(true)]
    public async Task File_validation_consumes_original_deadline(bool equality)
    {
        // Direct Apply only, no polling loops: UTC can hit equality while the real timer remains before D.
        var clock = new OffsetClock(); var probe = new CountUpdates();
        using var h = new OutputDistillationHarness(servicesOverride: s => s.AddSingleton<TimeProvider>(clock),
            configureDb: o => o.AddInterceptors(probe));
        var seed = await h.SeedSourceAsync();
        var queue = h.Provider.GetRequiredService<SessionMessageQueueService>();
        var now = clock.GetUtcNow();
        queue.AfterReportValidationAsync = _ => { clock.OverrideUtc = now.AddSeconds(equality ? 45 : 46); return Task.CompletedTask; };
        (await queue.TryApplyDistillationAsync(new(seed.Task.Id, seed.QueuedMessageId, now, now.AddSeconds(45), OutputDistillerMode.Apply),
            seed.Digest, h.PassingDistillation(), CancellationToken.None)).ShouldBe("deadline");
        (await h.ReloadQueuedAsync(seed.QueuedMessageId)).Body.ShouldBe(seed.RawBody);
        probe.Count.ShouldBe(0, "equality at the original deadline must refuse before attempting an UPDATE");
    }

    private sealed class ValidationGate(TaskCompletionSource entered, TaskCompletionSource release)
        : Antiphon.Server.Application.Interfaces.IAgentReportStore
    {
        public Task<Antiphon.Server.Application.Interfaces.AgentReportStorageResult> StoreAsync(
            Antiphon.Server.Domain.Entities.AgentTask task, CancellationToken ct) => throw new NotSupportedException();
        public async Task<bool> IsUsableAsync(string? path, string raw, CancellationToken ct)
        { entered.TrySetResult(); await release.Task.WaitAsync(ct); return false; }
    }
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Apply_and_SendNow_preserve_one_complete_body(bool applicationFirst)
    {
        var probe = new ApplyWriteGate();
        using var h = new OutputDistillationHarness(servicesOverride: s =>
        {
            s.AddSingleton(TimeProvider.System);
            s.AddSingleton(Options.Create(new SupervisionSettings
            { DeliveryVerification = new DeliveryVerificationSettings { Enabled = false } }));
        }, configureDb: o => o.AddInterceptors(probe));
        var seed = await h.SeedSourceAsync();
        var session = seed.Task.ParentSessionId!.Value;
        await using (var edit = OutputDistillationHarness.CreateContext())
            await edit.AgentSessions.Where(s => s.Id == session).ExecuteUpdateAsync(s => s.SetProperty(x => x.Cwd, h.Scratch));
        var queue = h.Provider.GetRequiredService<SessionMessageQueueService>();
        var adapter = new FakeAgentProtocolAdapter();
        h.Provider.GetRequiredService<AgentSessionRuntime>().Register(session, adapter);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        adapter.OnSubmitted = async _ => { entered.TrySetResult(); await release.Task; };
        var now = DateTimeOffset.UtcNow;
        Task<string?> Apply() => queue.TryApplyDistillationAsync(new(seed.Task.Id, seed.QueuedMessageId,
            now, now.AddSeconds(45), OutputDistillerMode.Apply), seed.Digest, h.PassingDistillation(), CancellationToken.None);
        Task<string?>? apply = null;
        Task? delivery = null;
        try
        {
            if (applicationFirst)
            {
                apply = Apply();
                await probe.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
                delivery = queue.SendNowAsync(session, seed.QueuedMessageId, CancellationToken.None);
                delivery.IsCompleted.ShouldBeFalse();
                probe.Release.TrySetResult();
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            }
            else
            {
                delivery = queue.SendNowAsync(session, seed.QueuedMessageId, CancellationToken.None);
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
                apply = Apply();
                probe.Release.TrySetResult();
            }
        }
        finally
        {
            probe.Release.TrySetResult();
            release.TrySetResult();
            if (delivery is not null) await delivery.WaitAsync(TimeSpan.FromSeconds(5));
            if (apply is not null) await apply.WaitAsync(TimeSpan.FromSeconds(5));
        }
        var result = await apply!;
        if (applicationFirst) result.ShouldBeNull(); else result.ShouldBe("delivery-claimed");
        var note = await h.ReloadQueuedAsync(seed.QueuedMessageId);
        note.Status.ShouldBe(QueuedMessageStatus.Sent);
        note.DeliveryAttempts.ShouldBe(1);
        if (applicationFirst) note.Body.ShouldContain(h.PassingDistillation());
        else
        {
            // The inbox backend's real size gate sends a pointer to the complete raw body.
            note.Body.ShouldContain(TypedBodySpill.PointerHeadline);
            (await File.ReadAllTextAsync(TypedBodySpill.InboxAbsolutePath(h.Scratch, seed.QueuedMessageId.ToString("D"))))
                .ShouldBe(seed.RawBody);
        }
        adapter.Inputs.Count(i => i == "\r").ShouldBe(1);
        adapter.KillCount.ShouldBe(0);
    }

    [Test]
    public async Task Apply_owns_the_delivery_session_lock_until_commit()
    {
        var probe = new ApplyWriteGate();
        using var h = new OutputDistillationHarness(configureDb: o => o.AddInterceptors(probe));
        var seed = await h.SeedSourceAsync();
        var queue = h.Provider.GetRequiredService<SessionMessageQueueService>();
        var now = h.Clock.GetUtcNow();
        var apply = queue.TryApplyDistillationAsync(new(seed.Task.Id, seed.QueuedMessageId,
            now, now.AddSeconds(45), OutputDistillerMode.Apply), seed.Digest, h.PassingDistillation(), CancellationToken.None);
        var sem = queue.GetLock(seed.Task.ParentSessionId!.Value);
        var bypassed = false;
        Exception? failure = null;
        try
        {
            await probe.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            bypassed = await sem.WaitAsync(0);
            if (bypassed) sem.Release();
        }
        finally
        {
            probe.Release.TrySetResult();
            try { await apply.WaitAsync(TimeSpan.FromSeconds(5)); } catch (Exception ex) { failure = ex; }
        }
        bypassed.ShouldBeFalse("delivery must not enter the queue critical section while application owns its read/write decision");
        failure.ShouldBeNull();
        (await h.ReloadQueuedAsync(seed.QueuedMessageId)).Body.ShouldContain(h.PassingDistillation());
    }

    private sealed class ApplyWriteGate : DbCommandInterceptor
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.StartsWith("UPDATE \"SessionQueuedMessages\" SET \"Body\""))
            { Entered.TrySetResult(); await Release.Task.WaitAsync(cancellationToken); }
            return result;
        }
    }

    [Test]
    [Arguments("eligible")]
    [Arguments("wrong-note")]
    [Arguments("wrong-source")]
    [Arguments("wrong-digest")]
    [Arguments("wrong-report")]
    [Arguments("wrong-origin")]
    [Arguments("canceled")]
    [Arguments("sent")]
    [Arguments("attempted")]
    [Arguments("polled")]
    [Arguments("shadow")]
    [Arguments("expired")]
    [Arguments("missing-source")]
    [Arguments("missing-note")]
    public async Task Apply_eligibility_matrix(string scenario)
    {
        using var h = new OutputDistillationHarness(s => s.OutputDistillerMode = OutputDistillerMode.Apply,
            servicesOverride: s => s.AddGitWorkspaceService());
        var seed = await h.SeedSourceAsync();
        var wrongNote = scenario == "wrong-note" ? (await h.SeedSourceAsync(report: "unrelated raw report")).QueuedMessageId : seed.QueuedMessageId;
        var unrelatedBody = (await h.ReloadQueuedAsync(wrongNote)).Body;
        var reportPath = Path.Combine(h.Scratch, "full-report.md");
        await File.WriteAllTextAsync(reportPath, seed.Report);
        await using (var db = OutputDistillationHarness.CreateContext())
        {
            (await db.AgentTasks.SingleAsync(t => t.Id == seed.Task.Id)).ResultFilePath = reportPath;
            var note = await db.SessionQueuedMessages.SingleAsync(m => m.Id == seed.QueuedMessageId);
            if (scenario == "wrong-source") note.SourceTaskId = null;
            if (scenario == "wrong-digest") note.ContentDigest = "different";
            if (scenario == "wrong-report")
                (await db.AgentTasks.SingleAsync(t => t.Id == seed.Task.Id)).Result = "different authoritative report";
            if (scenario == "wrong-origin") note.Origin = QueuedMessageOrigin.Ui;
            if (scenario == "canceled") note.Status = QueuedMessageStatus.Canceled;
            if (scenario == "sent") note.Status = QueuedMessageStatus.Sent;
            if (scenario == "attempted") note.DeliveryAttempts = 1;
            if (scenario == "polled")
                (await db.AgentTasks.SingleAsync(t => t.Id == seed.Task.Id)).LastPolledResultHash = seed.Digest;
            await db.SaveChangesAsync();
            if (scenario == "missing-note") await db.SessionQueuedMessages.Where(t => t.Id == seed.QueuedMessageId).ExecuteDeleteAsync();
        }
        var now = h.Clock.GetUtcNow();
        var request = new DistillRequest(scenario == "missing-source" ? Guid.NewGuid() : seed.Task.Id, wrongNote,
            now, now.AddSeconds(scenario == "expired" ? -1 : 45),
            scenario == "shadow" ? OutputDistillerMode.Shadow : OutputDistillerMode.Apply);
        var result = await h.Provider.GetRequiredService<SessionMessageQueueService>().TryApplyDistillationAsync(
            request, seed.Digest, h.PassingDistillation(), CancellationToken.None);
        if (scenario == "missing-note")
        {
            result.ShouldBe("note-missing");
            (await h.ReloadTaskAsync(seed.Task.Id)).Result.ShouldBe(seed.Report);
            return;
        }
        var persisted = await h.ReloadQueuedAsync(seed.QueuedMessageId);
        if (scenario == "eligible")
        {
            result.ShouldBeNull();
            persisted.Body.ShouldContain(h.PassingDistillation());
            persisted.Body.ShouldContain("Full report:");
            persisted.Body.ShouldContain(reportPath);
            persisted.NoteHeader.ShouldBe(seed.Header);
            persisted.ContentDigest.ShouldBe(seed.Digest);
        }
        else { result.ShouldNotBeNull(); persisted.Body.ShouldBe(seed.RawBody); }
        if (scenario == "wrong-note") (await h.ReloadQueuedAsync(wrongNote)).Body.ShouldBe(unrelatedBody);
        (await h.ReloadTaskAsync(seed.Task.Id)).Result.ShouldBe(scenario == "wrong-report" ? "different authoritative report" : seed.Report);
    }

    [Test]
    public async Task Expired_lock_wait_never_applies()
    {
        using var h = new OutputDistillationHarness();
        var seed = await h.SeedSourceAsync();
        var queue = h.Provider.GetRequiredService<SessionMessageQueueService>();
        var gate = queue.GetLock(seed.Task.ParentSessionId!.Value);
        await gate.WaitAsync();
        var now = h.Clock.GetUtcNow();
        var apply = queue.TryApplyDistillationAsync(new(seed.Task.Id, seed.QueuedMessageId,
            now, now.AddSeconds(45), OutputDistillerMode.Apply), seed.Digest, h.PassingDistillation(), CancellationToken.None);
        try
        {
            h.Clock.Advance(TimeSpan.FromSeconds(45));
        }
        finally { gate.Release(); }
        (await apply.WaitAsync(TimeSpan.FromSeconds(5))).ShouldBe("deadline");
        (await h.ReloadQueuedAsync(seed.QueuedMessageId)).Body.ShouldBe(seed.RawBody);
    }

    [Test]
    public async Task Expired_database_write_never_applies()
    {
        using var h = new OutputDistillationHarness(servicesOverride: s => s.AddSingleton(TimeProvider.System));
        var seed = await h.SeedSourceAsync();
        await using var blocker = OutputDistillationHarness.CreateContext();
        await using var transaction = await blocker.Database.BeginTransactionAsync();
        await blocker.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM \"SessionQueuedMessages\" WHERE \"Id\" = {seed.QueuedMessageId} FOR UPDATE");
        var now = DateTimeOffset.UtcNow;
        var apply = h.Provider.GetRequiredService<SessionMessageQueueService>().TryApplyDistillationAsync(
            new(seed.Task.Id, seed.QueuedMessageId, now, now.AddSeconds(1), OutputDistillerMode.Apply),
            seed.Digest, h.PassingDistillation(), CancellationToken.None);
        await Task.Delay(TimeSpan.FromMilliseconds(1100));
        await transaction.RollbackAsync();
        (await apply.WaitAsync(TimeSpan.FromSeconds(5))).ShouldBe("deadline");
        (await h.ReloadQueuedAsync(seed.QueuedMessageId)).Body.ShouldBe(seed.RawBody);
    }
}
