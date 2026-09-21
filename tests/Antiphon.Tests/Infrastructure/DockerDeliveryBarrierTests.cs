using Antiphon.DockerStack.Fixture;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Infrastructure;

[Category("Unit")]
public sealed class DockerDeliveryBarrierTests
{
    [Test]
    public async Task Untargeted_runner_calls_are_forwarded()
    {
        var target = Guid.NewGuid();
        var foreign = Guid.NewGuid();
        var inner = new RecordingRunnerClient();
        var client = new OrdinaryDeliveryRunnerClient(inner, new DeliveryGate { TargetSession = target, ArmedCut = "body-before-enter" });
        await client.SendInputAsync(foreign, "hello", CancellationToken.None);
        inner.Inputs.ShouldBe([(foreign, "hello")]);
    }

    [Test]
    public async Task Target_sse_receipt_waits_for_release()
    {
        var session = Guid.NewGuid();
        var gate = new DeliveryGate { TargetSession = session, ArmedCut = "recipient-before-ingestion" };
        var inner = new RecordingRunnerClient { Events = { Event(session, "UserPrompt", "body") } };
        var client = new OrdinaryDeliveryRunnerClient(inner, gate);
        var move = client.StreamEventsAsync(CancellationToken.None).GetAsyncEnumerator().MoveNextAsync();
        await Task.Delay(40);
        move.IsCompleted.ShouldBeFalse();
        gate.Release();
        (await move).ShouldBeTrue();
    }

    [Test]
    public async Task Target_pull_receipt_waits_for_release()
    {
        var session = Guid.NewGuid();
        var foreign = Guid.NewGuid();
        var gate = new DeliveryGate { TargetSession = session, ArmedCut = "recipient-before-ingestion" };
        var inner = new RecordingRunnerClient();
        var client = new OrdinaryDeliveryRunnerClient(inner, gate);
        var pull = client.GetTranscriptAsync(session, CancellationToken.None);
        await Task.Delay(40);
        pull.IsCompleted.ShouldBeFalse();
        (await client.GetTranscriptAsync(foreign, CancellationToken.None)).SessionId.ShouldBe(Guid.Empty);
        gate.Release();
        await pull;
    }

    [Test]
    public async Task Body_is_forwarded_before_enter_cut()
    {
        var session = Guid.NewGuid();
        var gate = new DeliveryGate { TargetSession = session, ArmedCut = "body-before-enter" };
        var inner = new RecordingRunnerClient();
        var client = new OrdinaryDeliveryRunnerClient(inner, gate);
        await client.SendInputAsync(session, "body", CancellationToken.None);
        var enter = client.SendInputAsync(session, "\r", CancellationToken.None);
        await Task.Delay(40);
        inner.Inputs.Select(item => item.Input).ShouldBe(["body"]);
        enter.IsCompleted.ShouldBeFalse();
        gate.Release();
        await enter;
    }

    [Test]
    public async Task Multiline_bytes_are_unchanged()
    {
        var session = Guid.NewGuid();
        var inner = new RecordingRunnerClient();
        var client = new OrdinaryDeliveryRunnerClient(inner, new DeliveryGate());
        var body = "\u001b[200~line1\nline2\u001b[201~";
        await client.SendInputAsync(session, body, CancellationToken.None);
        await client.SendInputAsync(session, "\r", CancellationToken.None);
        inner.Inputs.Select(item => item.Input).ShouldBe([body, "\r"]);
    }

    [Test]
    public void Save_metadata_is_captured_before_accept()
    {
        using var db = NewDb();
        db.Rows.Add(new TinyRow { Name = "insert" });
        var interceptor = new OrdinaryDeliverySaveInterceptor();
        interceptor.Remember(db);
        db.ChangeTracker.AcceptAllChanges();
        interceptor.Publish(db);
        interceptor.Reached.Count.ShouldBe(1);
    }

    [Test]
    public void Failed_save_never_emits_commit_ready()
    {
        using var db = NewDb();
        db.Rows.Add(new TinyRow { Name = "insert" });
        var interceptor = new OrdinaryDeliverySaveInterceptor();
        interceptor.Remember(db);
        interceptor.OnSaveFailed(db);
        interceptor.Reached.ShouldBeEmpty();
    }

    [Test]
    public void Invisible_commit_cannot_reach_cut()
    {
        using var db = NewDb();
        db.Rows.Add(new TinyRow { Name = "insert" });
        var interceptor = new OrdinaryDeliverySaveInterceptor { Visible = () => false };
        interceptor.Remember(db);
        interceptor.Publish(db);
        interceptor.Reached.ShouldBeEmpty();
    }

    [Test]
    public void Open_transaction_cannot_reach_cut()
    {
        using var db = NewDb();
        using var tx = db.Database.BeginTransaction();
        var interceptor = new OrdinaryDeliverySaveInterceptor();
        Should.Throw<OpenTransactionException>(() => interceptor.Publish(db));
    }

    [Test]
    public void Save_metadata_cannot_cross_contexts()
    {
        using var selected = NewDb();
        using var foreign = NewDb();
        selected.Rows.Add(new TinyRow { Name = "selected" });
        var interceptor = new OrdinaryDeliverySaveInterceptor();
        interceptor.Remember(selected);
        interceptor.Publish(foreign);
        interceptor.Reached.ShouldBeEmpty();
    }

    [Test]
    public void Transcript_batch_is_refused()
    {
        var interceptor = new OrdinaryDeliverySaveInterceptor { FaultArmed = true, FaultUuid = "uuid-1" };
        Should.Throw<DbUpdateException>(() => interceptor.FaultSnapshots(
        [
            new EntityStateSnapshot("TranscriptEntry", EntityState.Added, "uuid-1", "UserPrompt"),
            new EntityStateSnapshot("TranscriptEntry", EntityState.Added, "other", "UserPrompt"),
        ]));
    }

    [Test]
    public void Transcript_individual_retry_is_refused()
    {
        var interceptor = new OrdinaryDeliverySaveInterceptor { FaultArmed = true, FaultUuid = "uuid-1" };
        var one = new[] { new EntityStateSnapshot("TranscriptEntry", EntityState.Added, "uuid-1", "UserPrompt") };
        Should.Throw<DbUpdateException>(() => interceptor.FaultSnapshots(one));
        Should.Throw<DbUpdateException>(() => interceptor.FaultSnapshots(one));
    }

    [Test]
    public void Transcript_stub_retry_is_refused()
    {
        var interceptor = new OrdinaryDeliverySaveInterceptor { FaultArmed = true, FaultUuid = "uuid-1" };
        Should.Throw<DbUpdateException>(() => interceptor.FaultSnapshots(
            [new EntityStateSnapshot("TranscriptEntry", EntityState.Added, "uuid-1", "stub")]));
        interceptor.FaultSnapshots([new EntityStateSnapshot("TranscriptEntry", EntityState.Added, "other", "stub")]);
    }

    [Test]
    public async Task Verdict_is_held_before_commit()
    {
        var gate = new DeliveryGate { ArmedCut = "receipt-before-verdict" };
        var interceptor = new OrdinaryDeliverySaveInterceptor { VerdictGate = gate };
        var held = interceptor.HoldVerdictAsync(() => Task.CompletedTask, CancellationToken.None);
        await Task.Delay(40);
        interceptor.SaveCalls.ShouldBe(0);
        gate.Release();
        await held;
    }

    [Test]
    public async Task Foreign_arm_is_not_activated()
    {
        var root = Temp();
        var barrier = new DeliveryFileBarrier(root) { Expected = Arm("case-a") };
        var result = await barrier.ArmAsync(Arm("case-b"), CancellationToken.None);
        result.Completed.ShouldBeFalse();
        Directory.GetFiles(root, "reached-*").ShouldBeEmpty();
    }

    [Test]
    public void Old_host_release_cannot_unblock()
    {
        var barrier = new DeliveryFileBarrier(Temp());
        barrier.ReleaseMatches(Arm("case", host: "new"), Arm("case", host: "old")).ShouldBeFalse();
    }

    [Test]
    public async Task Consumed_arm_cannot_rearm_after_restart()
    {
        var root = Temp();
        var arm = Arm("case");
        var first = new DeliveryFileBarrier(root) { Expected = arm };
        await File.WriteAllTextAsync(
            Path.Combine(root, DeliveryFileBarrier.ReleaseFileName(arm)),
            System.Text.Json.JsonSerializer.Serialize(arm));
        (await first.ArmAsync(arm, CancellationToken.None)).Completed.ShouldBeTrue();
        var second = new DeliveryFileBarrier(root) { Expected = Arm("case") };
        (await second.ArmAsync(Arm("case"), CancellationToken.None)).Code.ShouldBe("ConsumedArm");
    }

    [Test]
    public async Task Deadline_is_incomplete_not_release()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(30));
        var barrier = new DeliveryFileBarrier(Temp()) { Expected = Arm("case") };
        var result = await barrier.ArmAsync(Arm("case"), cts.Token);
        result.Code.ShouldBe("Incomplete");
        result.Completed.ShouldBeFalse();
    }

    [Test]
    public async Task Held_response_cannot_start_headers()
    {
        var barrier = new OrdinaryDeliveryResponseBarrier { Armed = true };
        var response = new HoldingResponse();
        var start = barrier.StartAsync(response, CancellationToken.None);
        await Task.Delay(30);
        response.HasStarted.ShouldBeFalse();
        barrier.Release();
        await start;
    }

    [Test]
    public async Task Held_response_cannot_flush_body()
    {
        var barrier = new OrdinaryDeliveryResponseBarrier { Armed = true };
        var response = new HoldingResponse();
        var flush = barrier.FlushAsync(response, CancellationToken.None);
        await Task.Delay(30);
        response.FlushCount.ShouldBe(0);
        response.Body.Count.ShouldBe(0);
        barrier.Release();
        await flush;
    }

    [Test]
    public async Task Unarmed_response_is_unchanged()
    {
        var barrier = new OrdinaryDeliveryResponseBarrier();
        var response = new HoldingResponse();
        var body = "ok"u8.ToArray();
        await barrier.WriteAsync(response, 200, "x", body, CancellationToken.None);
        response.Status.ShouldBe(200);
        response.Header.ShouldBe("x");
        response.Body.ShouldBe(body);
    }

    [Test]
    public void Runner_registration_is_decorated_once()
    {
        var inner = new RecordingRunnerClient();
        var services = new ServiceCollection();
        services.AddSingleton<ISessionRunnerClient>(inner);
        DeliveryFixtureHost.Install(services, new DeliveryGate());
        var resolved = services.BuildServiceProvider().GetRequiredService<ISessionRunnerClient>();
        var decorator = resolved.ShouldBeOfType<OrdinaryDeliveryRunnerClient>();
        decorator.Inner.ShouldBeSameAs(inner);
    }

    [Test]
    public void Torn_reached_record_is_refused()
    {
        DeliveryFileBarrier.DigestMatches("{\"arm\":{\"Cut\":\"x\"},\"digest\":\"00\"}").ShouldBeFalse();
    }

    [Test]
    public void Cross_attempt_release_cannot_unblock()
    {
        var barrier = new DeliveryFileBarrier(Temp());
        var arm = Arm("case", attempt: "1", host: "host");
        barrier.ReleaseMatches(arm, Arm("case", attempt: "2", host: "host")).ShouldBeFalse();
        barrier.ReleaseMatches(arm, arm).ShouldBeTrue();
    }

    private static SessionRunnerEvent Event(Guid session, string kind, string text) =>
        new("transcript", session, Transcript: new SessionRunnerTranscriptEvent(
            session, 1, kind, "uuid", null, null, null, text, null, null, null, null, null));

    private static TinyContext NewDb()
    {
        var options = new DbContextOptionsBuilder<TinyContext>().UseSqlite("Data Source=:memory:").Options;
        var db = new TinyContext(options);
        db.Database.OpenConnection();
        db.Database.EnsureCreated();
        return db;
    }

    private static string Temp() => Directory.CreateTempSubdirectory("c590-barrier-").FullName;

    private static BarrierArm Arm(string caseName, string attempt = "1", string host = "host") =>
        new(caseName, "insert-committed", "row", attempt, host);

    private sealed class TinyRow
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
    }

    private sealed class TinyContext : DbContext
    {
        public TinyContext(DbContextOptions<TinyContext> options) : base(options) { }
        public DbSet<TinyRow> Rows => Set<TinyRow>();
    }
}
